using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ActiveDirectory;
using static ActiveDirectory.DirectorySearch;

namespace IdentityServer.Directory;

/// <summary>
/// The rules behind directory searches, kept apart from Active Directory so they can be tested
/// and reused: how a query splits into terms, the global minimum term length, result limits, the
/// enabled-account selection, and the LDAP filters built from them.
/// </summary>
public static class DirectoryQuery
{
    /// <summary>The longest query accepted, in characters.</summary>
    public const int MaxQueryLength = 256;

    /// <summary>The most terms a query may have; each adds a clause every candidate entry is tested against.</summary>
    public const int MaxTerms = 8;

    // Commas separate terms too, so "Smith, John" finds the same people as "Smith John".
    private static readonly char[] TermSeparators = [' ', '\t', '\r', '\n', ','];

    /// <summary>
    /// The attributes a people search matches each term against, and how. Contains matching is
    /// used for display name, given name and telephone number, so
    /// <c>8000</c> finds <c>555-010-8000</c>; the other attributes match from the start, which the
    /// directory can answer from its indexes.
    /// </summary>
    internal static readonly (string Attribute, MatchType Match)[] PeopleAttributes =
    [
        (ADProperties.User.DisplayName, MatchType.Contains),
        (ADProperties.User.GivenName, MatchType.Contains),
        (ADProperties.User.Surname, MatchType.StartsWith),
        (ADProperties.User.SamAccountName, MatchType.StartsWith),
        (ADProperties.User.Email, MatchType.StartsWith),
        (ADProperties.User.TelephoneNumber, MatchType.Contains),
        (ADProperties.User.EmployeeID, MatchType.StartsWith),
    ];

    /// <summary>The attribute a <see cref="DirectorySearchMode.DisplayName"/> search matches each term against.</summary>
    internal static readonly (string Attribute, MatchType Match)[] DisplayNameAttributes =
    [
        (ADProperties.User.DisplayName, MatchType.Contains),
    ];

    /// <summary>
    /// Excludes disabled accounts using the standard userAccountControl attribute.
    /// The numeric LDAP rule performs a bitwise AND: flag value 2 means disabled, and the outer NOT
    /// excludes it even when the account has other flags set. Comparing the whole value to 2
    /// would miss those combinations.
    /// </summary>
    internal const string EnabledUserFilter = "(!(userAccountControl:1.2.840.113556.1.4.803:=2))";

    /// <summary>
    /// Splits a query into distinct search terms on whitespace and commas, ignoring case when
    /// removing duplicates. An empty or blank query has no terms.
    /// </summary>
    /// <param name="query">The query as the caller typed it.</param>
    /// <exception cref="DirectoryQueryException">The query is longer than <see cref="MaxQueryLength"/> or has more than <see cref="MaxTerms"/> terms.</exception>
    public static IReadOnlyList<string> Tokenize(string? query)
    {
        if (string.IsNullOrWhiteSpace(query)) return [];
        if (query.Length > MaxQueryLength)
            throw new DirectoryQueryException($"The query is longer than {MaxQueryLength} characters.");

        List<string> terms = query
            .Split(TermSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (terms.Count > MaxTerms)
            throw new DirectoryQueryException($"A query can have at most {MaxTerms} terms.");
        return terms;
    }

    /// <summary>
    /// Applies the global-scope rule: every term must be at least <paramref name="minLength"/>
    /// characters, and, unless <paramref name="allowEmpty"/>, there must be a term at all.
    /// </summary>
    /// <param name="terms">The terms from <see cref="Tokenize"/>.</param>
    /// <param name="minLength">The configured minimum; values below 1 mean 2.</param>
    /// <param name="allowEmpty">Accept no terms, for searches something else already narrows (a group listing).</param>
    /// <exception cref="DirectoryQueryException">A term is too short, or there is none.</exception>
    public static void EnsureGlobalTerms(IReadOnlyList<string> terms, int minLength, bool allowEmpty = false)
    {
        if (minLength < 1) minLength = 2;
        if (terms.Count == 0 && !allowEmpty)
            throw new DirectoryQueryException("A search term is required when searching the global directory.");
        if (terms.Any(term => term.Length < minLength))
            throw new DirectoryQueryException($"Each search term must be at least {minLength} characters when searching the global directory.");
    }

    /// <summary>
    /// The number of results to return: <paramref name="take"/> when it is positive, otherwise
    /// <paramref name="defaultTake"/>, never more than <paramref name="maxTake"/>.
    /// </summary>
    /// <param name="take">What the caller asked for, if anything.</param>
    /// <param name="defaultTake">The configured default.</param>
    /// <param name="maxTake">The configured maximum; values below 1 mean 1.</param>
    public static int ClampTake(int? take, int defaultTake, int maxTake)
    {
        int max = Math.Max(1, maxTake);
        return take is > 0 ? Math.Min(take.Value, max) : Math.Clamp(defaultTake, 1, max);
    }

    /// <summary>Builds one escaped LDAP assertion, e.g. <c>(sn=smith*)</c>.</summary>
    internal static string Assertion(string attribute, string value, MatchType match)
    {
        // Escape the value before adding our own wildcards. A '*' typed by the caller must stay
        // literal data, otherwise a narrow search could become an unintended directory-wide query.
        string escaped = LdapHelper.CleanLDAPString(value);
        return match switch
        {
            MatchType.Contains => $"({attribute}=*{escaped}*)",
            MatchType.StartsWith => $"({attribute}={escaped}*)",
            _ => $"({attribute}={escaped})",
        };
    }

    /// <summary>
    /// The AND of the given assertions, in the form <see cref="DirectorySearch.AdditionalFilter"/>
    /// expects: without the outer parentheses, which DirectorySearch adds.
    /// </summary>
    internal static string All(IEnumerable<string> assertions) => "&" + string.Concat(assertions);

    /// <summary>
    /// The filter for a people search: person accounts (not the computer accounts that share
    /// <c>objectClass=user</c>), optionally only members of one group, optionally only enabled
    /// accounts, optionally only accounts an application filter selects, where every term matches
    /// at least one of the attributes <paramref name="mode"/> searches.
    /// </summary>
    /// <param name="terms">The search terms; each is escaped.</param>
    /// <param name="includeDisabled">Leave out the enabled-account filter.</param>
    /// <param name="memberOfGuid">A group's objectGUID as 32 hex digits in directory byte order, or null.</param>
    /// <param name="additionalFilter">
    /// Trusted application LDAP assertions, added as given. The lookup decides whether they apply;
    /// see <see cref="IDirectoryUserFilter"/>.
    /// </param>
    /// <param name="mode">The address-book fields (<see cref="PeopleAttributes"/>) or the display name alone.</param>
    internal static string PeopleFilter(IReadOnlyList<string> terms, bool includeDisabled, string? memberOfGuid = null, string? additionalFilter = null,
        DirectorySearchMode mode = DirectorySearchMode.AddressBook)
    {
        var filter = new StringBuilder("&(").Append(ADProperties.ObjectCategory).Append("=person)");
        if (memberOfGuid != null)
        {
            if (memberOfGuid.Length != 32 || !memberOfGuid.All(char.IsAsciiHexDigit))
                throw new ArgumentException("Expected an objectGUID as 32 hexadecimal digits.", nameof(memberOfGuid));
            filter.Append("(").Append(ADProperties.MemberOf).Append("=<GUID=").Append(memberOfGuid).Append(">)");
        }
        if (!includeDisabled)
            filter.Append(EnabledUserFilter);
        filter.Append(additionalFilter);
        // AND across words, OR across fields: "Jane Smith" can match givenName=Jane and sn=Smith.
        // OR across words instead would return everyone called Jane plus everyone called Smith.
        (string Attribute, MatchType Match)[] attributes = mode == DirectorySearchMode.DisplayName ? DisplayNameAttributes : PeopleAttributes;
        foreach (string term in terms)
        {
            filter.Append("(|");
            foreach ((string attribute, MatchType match) in attributes)
                filter.Append(Assertion(attribute, term, match));
            filter.Append(')');
        }
        return filter.ToString();
    }

    /// <summary>The filter for a group search: every term appears somewhere in the group's common name.</summary>
    /// <param name="terms">The search terms; each is escaped.</param>
    internal static string GroupNameFilter(IReadOnlyList<string> terms) =>
        All(terms.Select(term => Assertion(ADProperties.CommonName, term, MatchType.Contains)));

    /// <summary>Escapes binary data for an LDAP assertion value, two hex digits per byte: <c>\01\05\00...</c>.</summary>
    internal static string EscapeBinary(byte[] bytes) =>
        string.Concat(bytes.Select(b => "\\" + b.ToString("x2")));

    /// <summary>
    /// The common name in the first component of a distinguished name, unescaped:
    /// <c>CN=Smith\, John,OU=Staff,DC=example,DC=com</c> gives <c>Smith, John</c>. Returns null
    /// when the first component is not a CN.
    /// </summary>
    /// <param name="distinguishedName">A distinguished name as Active Directory returns it.</param>
    internal static string? CommonName(string? distinguishedName)
    {
        if (string.IsNullOrEmpty(distinguishedName)) return null;
        int equals = distinguishedName.IndexOf('=');
        if (equals < 0 || !distinguishedName.AsSpan(0, equals).Trim().Equals("CN", StringComparison.OrdinalIgnoreCase))
            return null;

        // RFC 4514: a backslash escapes the next character, or introduces two hex digits of a
        // UTF-8 byte; an unescaped comma or plus ends the component.
        var value = new StringBuilder();
        var utf8 = new List<byte>();
        for (int i = equals + 1; i < distinguishedName.Length; i++)
        {
            char c = distinguishedName[i];
            if (c == '\\' && i + 2 < distinguishedName.Length
                && char.IsAsciiHexDigit(distinguishedName[i + 1]) && char.IsAsciiHexDigit(distinguishedName[i + 2]))
            {
                utf8.Add(Convert.ToByte(distinguishedName.Substring(i + 1, 2), 16));
                i += 2;
                continue;
            }

            FlushUtf8(value, utf8);
            if (c == '\\' && i + 1 < distinguishedName.Length)
            {
                value.Append(distinguishedName[++i]);
                continue;
            }
            if (c == ',' || c == '+') break;
            value.Append(c);
        }
        FlushUtf8(value, utf8);

        string name = value.ToString().Trim();
        return name.Length > 0 ? name : null;
    }

    /// <summary>
    /// The first <c>DC=</c> label of a distinguished name in upper case, which is the NetBIOS
    /// domain name in most domains: <c>...,DC=EXAMPLE,DC=COM</c> gives <c>EXAMPLE</c>.
    /// </summary>
    /// <param name="distinguishedName">A distinguished name.</param>
    internal static string? DomainLabel(string? distinguishedName)
    {
        if (string.IsNullOrEmpty(distinguishedName)) return null;
        foreach (string component in distinguishedName.Split(','))
        {
            string part = component.Trim();
            if (part.StartsWith("DC=", StringComparison.OrdinalIgnoreCase) && part.Length > 3)
                return part[3..].ToUpperInvariant();
        }
        return null;
    }

    private static void FlushUtf8(StringBuilder value, List<byte> utf8)
    {
        if (utf8.Count == 0) return;
        value.Append(Encoding.UTF8.GetString(utf8.ToArray()));
        utf8.Clear();
    }
}
