using System;
using System.Security.Principal;
using System.Text.RegularExpressions;
using ActiveDirectory;

namespace IdentityServer.Directory;

/// <summary>The kinds of identifier a directory lookup accepts.</summary>
internal enum DirectoryIdKind
{
    /// <summary>A security identifier, <c>S-1-5-21-…</c>.</summary>
    Sid,

    /// <summary>An objectGUID in its 36-character form.</summary>
    Guid,

    /// <summary>A Windows account name, <c>DOMAIN\user</c>.</summary>
    AccountName,
}

/// <summary>
/// A lookup identifier, parsed and validated before it gets anywhere near an LDAP filter: a SID,
/// an objectGUID (36 characters, braces optional) or, for people, <c>DOMAIN\user</c>.
/// </summary>
/// <param name="Kind">What the identifier is.</param>
/// <param name="Value">The normalized SID or GUID, or the user part of an account name.</param>
/// <param name="Domain">The domain part of an account name.</param>
internal readonly record struct DirectoryId(DirectoryIdKind Kind, string Value, string? Domain = null)
{
    private static readonly Regex SidPattern = new(
        @"^S-1-\d{1,15}(-\d{1,10}){1,15}$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>Parses an identifier, refusing anything that is not one of the accepted forms.</summary>
    /// <param name="id">The identifier the caller sent.</param>
    /// <param name="allowAccountName">Accept <c>DOMAIN\user</c> (people only).</param>
    /// <exception cref="DirectoryQueryException">The identifier is empty or malformed.</exception>
    public static DirectoryId Parse(string? id, bool allowAccountName)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new DirectoryQueryException("An id is required.");
        if (TryParse(id, allowAccountName, out DirectoryId result))
            return result;
        throw new DirectoryQueryException(allowAccountName
            ? @"The id must be a SID (S-1-5-21-...), a GUID (xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx) or an account name (DOMAIN\user)."
            : "The id must be a SID (S-1-5-21-...) or a GUID (xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx).");
    }

    /// <summary>Parses an identifier, returning false for anything that is not one of the accepted forms.</summary>
    /// <param name="id">The identifier the caller sent.</param>
    /// <param name="allowAccountName">Accept <c>DOMAIN\user</c> (people only).</param>
    /// <param name="result">The parsed identifier.</param>
    public static bool TryParse(string? id, bool allowAccountName, out DirectoryId result)
    {
        result = default;
        string value = id?.Trim() ?? "";
        if (value.Length == 0 || value.Length > DirectoryQuery.MaxQueryLength)
            return false;

        if (SidPattern.IsMatch(value))
        {
            try
            {
                result = new DirectoryId(DirectoryIdKind.Sid, new SecurityIdentifier("S" + value[1..]).Value);
                return true;
            }
            catch (ArgumentException)
            {
                return false;
            }
        }

        // Only the dashed forms: 32 bare hex digits would be ambiguous between .NET's byte order
        // and the directory's raw byte order.
        if (Guid.TryParseExact(value, "D", out Guid guid) || Guid.TryParseExact(value, "B", out guid))
        {
            result = new DirectoryId(DirectoryIdKind.Guid, guid.ToString("D"));
            return true;
        }

        if (allowAccountName)
        {
            // A forward slash is accepted as well as the Windows backslash, because some servers
            // turn an encoded backslash in a URL path into a slash before routing sees it.
            int separator = value.IndexOfAny(['\\', '/']);
            if (separator > 0 && separator < value.Length - 1 && value.IndexOfAny(['\\', '/'], separator + 1) < 0)
            {
                result = new DirectoryId(DirectoryIdKind.AccountName, value[(separator + 1)..], value[..separator]);
                return true;
            }
        }

        return false;
    }

    /// <summary>The LDAP assertion that selects the object this identifier names, with its value escaped.</summary>
    public string Assertion()
    {
        switch (Kind)
        {
            case DirectoryIdKind.Sid:
                var sid = new SecurityIdentifier(Value);
                byte[] bytes = new byte[sid.BinaryLength];
                sid.GetBinaryForm(bytes, 0);
                return "(" + ADProperties.ObjectSid + "=" + DirectoryQuery.EscapeBinary(bytes) + ")";
            case DirectoryIdKind.Guid:
                return "(" + ADProperties.ObjectGuid + "=" + DirectoryQuery.EscapeBinary(Guid.Parse(Value).ToByteArray()) + ")";
            default:
                return "(" + ADProperties.User.SamAccountName + "=" + LdapHelper.CleanLDAPString(Value) + ")";
        }
    }
}
