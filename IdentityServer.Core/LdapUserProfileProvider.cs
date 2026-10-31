using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.DirectoryServices.Protocols;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Security.Claims;
using System.Text;

namespace IdentityServer;

/// <summary>Portable LDAP reads with explicit schema and realm boundaries.</summary>
internal sealed class LdapUserProfileProvider : IUserProfileProvider
{
    private readonly IdentityProviderOptions _options;
    private readonly Func<ILdapSearch> _connect;

    internal LdapUserProfileProvider(IdentityProviderOptions options, Func<ILdapSearch>? connect = null)
    {
        _options = options;
        _connect = connect ?? (() => new LdapSearch(options));
    }

    public UserProfile GetProfile(ClaimsPrincipal principal, bool includeGroups)
    {
        string filter = UserFilter(principal); // Reject ambiguous/untrusted names before connecting.
        using var ldap = _connect();
        var users = ldap.Search(_options.UserBaseDn, filter,
            ["objectSid", "ipaUniqueID", "uid", "sAMAccountName", "displayName", "cn", "mail", "givenName", "sn", "telephoneNumber"], 2);
        if (users.Count != 1)
            throw new InvalidOperationException("The authenticated identity must resolve to exactly one directory user.");
        var user = users[0];
        bool ipa = _options.Provider == DirectoryProvider.FreeIpa;
        string subject = ipa ? user.RequiredString("ipaUniqueID") : SidToString(user.Bytes("objectSid"));
        string username = user.RequiredString(ipa ? "uid" : "sAMAccountName");
        string account = !ipa && !string.IsNullOrWhiteSpace(_options.NetbiosDomain)
            ? _options.NetbiosDomain + "\\" + username : principal.Identity!.Name!;
        var groups = includeGroups
            ? ldap.Search(_options.GroupBaseDn,
                $"(&(objectClass={(ipa ? "groupOfNames" : "group")})(member={Escape(user.DistinguishedName)}))", ["cn"])
                .Select(group => group.RequiredString("cn")).Distinct(StringComparer.Ordinal).ToArray()
            : Array.Empty<string>();
        // Search group.member directly: memberOf may include indirect memberships on FreeIPA.
        // This also avoids AD's ranged memberOf attributes and excludes the AD primary group.
        return new()
        {
            Subject = subject, Username = username, AccountName = account,
            DisplayName = user.String("displayName") is { Length: > 0 } display ? display : user.String("cn") is { Length: > 0 } cn ? cn : username,
            Email = user.String("mail"), GivenName = user.String("givenName"), Surname = user.String("sn"),
            TelephoneNumber = user.String("telephoneNumber"), Groups = groups
        };
    }

    internal string UserFilter(ClaimsPrincipal principal)
    {
        if (principal.Identity?.IsAuthenticated != true || string.IsNullOrWhiteSpace(principal.Identity.Name))
            throw new InvalidOperationException("A named, authenticated identity is required for directory lookup.");
        bool ipa = _options.Provider == DirectoryProvider.FreeIpa;
        string name = principal.Identity.Name;
        string match;
        if (!ipa && principal.FindFirst(ClaimTypes.PrimarySid)?.Value is { } sid)
        {
            // A Windows-authenticated SID is unambiguous even across domain trusts.
            match = "(objectSid=" + EscapeBytes(SidFromString(sid)) + ")";
        }
        else
        {
            string username;
            int separator = name.LastIndexOf('@');
            if (separator > 0 && separator < name.Length - 1)
            {
                if (!name[(separator + 1)..].Equals(_options.Realm, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("The authenticated Kerberos realm does not match IdentityServer:Directory:Realm.");
                username = name[..separator];
            }
            else if (!ipa && name.IndexOf('\\') is int slash && slash > 0 &&
                     !string.IsNullOrWhiteSpace(_options.NetbiosDomain) &&
                     name[..slash].Equals(_options.NetbiosDomain, StringComparison.OrdinalIgnoreCase))
            {
                username = name[(slash + 1)..];
            }
            else
                throw new InvalidOperationException("Use a realm-qualified Kerberos identity, or configure NetbiosDomain for an AD DOMAIN\\user identity.");

            if (string.IsNullOrWhiteSpace(username) || username.IndexOfAny(['@', '/', '\\']) >= 0)
                throw new InvalidOperationException("The authenticated principal is not a supported user principal.");
            // Resolve canonical principals first. A different UPN suffix in AD is supported via
            // sAMAccountName only inside the explicitly configured realm and directory base.
            match = ipa
                ? $"(krbPrincipalName={Escape(name)})"
                : $"(|(userPrincipalName={Escape(name)})(sAMAccountName={Escape(username)}))";
        }
        return ipa ? $"(&(objectClass=posixAccount){match})" : $"(&(objectCategory=person)(objectClass=user){match})";
    }

    // Escape UTF-8 octets, including non-ASCII characters, without trimming account names.
    internal static string Escape(string value)
    {
        var output = new StringBuilder();
        foreach (byte b in Encoding.UTF8.GetBytes(value))
            if (b is 0 or (byte)'*' or (byte)'(' or (byte)')' or (byte)'\\' || b >= 0x80)
                output.Append('\\').Append(b.ToString("x2", CultureInfo.InvariantCulture));
            else output.Append((char)b);
        return output.ToString();
    }

    private static string EscapeBytes(byte[] value) => string.Concat(value.Select(b => "\\" + b.ToString("x2", CultureInfo.InvariantCulture)));

    // SecurityIdentifier itself is Windows-only. Decode the standard binary SID on either OS.
    internal static string SidToString(byte[] bytes)
    {
        if (bytes.Length < 8 || bytes[0] != 1 || bytes[1] > 15 || bytes.Length != 8 + bytes[1] * 4)
            throw new InvalidOperationException("The directory returned an invalid objectSid.");
        ulong authority = 0;
        for (int i = 2; i < 8; i++) authority = (authority << 8) | bytes[i];
        string value = "S-1-" + authority.ToString(CultureInfo.InvariantCulture);
        for (int i = 8; i < bytes.Length; i += 4)
            value += "-" + BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(i, 4)).ToString(CultureInfo.InvariantCulture);
        return value;
    }

    internal static byte[] SidFromString(string value)
    {
        string[] parts = value.Split('-');
        if (parts.Length < 3 || parts.Length > 18 || parts[0] != "S" || parts[1] != "1" ||
            !ulong.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out ulong authority) || authority > 0xffffffffffff)
            throw new InvalidOperationException("The authenticated identity has an invalid primary SID.");
        byte[] bytes = new byte[8 + (parts.Length - 3) * 4];
        bytes[0] = 1;
        bytes[1] = (byte)(parts.Length - 3);
        for (int i = 7; i >= 2; i--) { bytes[i] = (byte)authority; authority >>= 8; }
        for (int i = 3; i < parts.Length; i++)
        {
            if (!uint.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out uint subAuthority))
                throw new InvalidOperationException("The authenticated identity has an invalid primary SID.");
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8 + (i - 3) * 4, 4), subAuthority);
        }
        return bytes;
    }
}

internal sealed record LdapEntry(string DistinguishedName, IReadOnlyDictionary<string, object[]> Attributes)
{
    internal string String(string name)
    {
        if (!Attributes.TryGetValue(name, out var values) || values.Length == 0) return "";
        return values[0] is byte[] bytes ? Encoding.UTF8.GetString(bytes) : (string)values[0];
    }
    internal string RequiredString(string name) => String(name) is { Length: > 0 } value ? value :
        throw new InvalidOperationException($"The directory entry is missing {name}.");
    internal byte[] Bytes(string name) => Attributes.TryGetValue(name, out var values) && values.Length > 0 && values[0] is byte[] bytes
        ? bytes : throw new InvalidOperationException($"The directory entry is missing binary {name}.");
}

internal interface ILdapSearch : IDisposable
{
    IReadOnlyList<LdapEntry> Search(string searchBase, string filter, string[] attributes, int sizeLimit = 0);
}

internal sealed class LdapSearch : ILdapSearch
{
    private readonly LdapConnection _connection;

    internal LdapSearch(IdentityProviderOptions options)
    {
        Uri uri = options.Server!;
        bool tls = uri.Scheme == "ldaps";
        int port = uri.Port > 0 ? uri.Port : tls ? 636 : 389;
        _connection = new LdapConnection(new LdapDirectoryIdentifier(uri.Host, port))
        {
            AuthType = string.IsNullOrWhiteSpace(options.BindDn) ? AuthType.Negotiate : AuthType.Basic,
            Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds)
        };
        try
        {
            _connection.SessionOptions.ProtocolVersion = 3;
            _connection.SessionOptions.ReferralChasing = ReferralChasingOptions.None;
            _connection.SessionOptions.SecureSocketLayer = tls;
            if (!tls) _connection.SessionOptions.StartTransportLayerSecurity(null);
            // Use OS certificate validation. Never install an accept-all certificate callback.
            // On Linux Negotiate binds using GSSAPI and the process's Kerberos credential cache.
            if (!string.IsNullOrWhiteSpace(options.BindDn))
                _connection.Credential = new NetworkCredential(options.BindDn, options.BindPassword);
            _connection.Bind();
        }
        catch { _connection.Dispose(); throw; }
    }

    public IReadOnlyList<LdapEntry> Search(string searchBase, string filter, string[] attributes, int sizeLimit = 0)
    {
        var request = new SearchRequest(searchBase, filter, SearchScope.Subtree, attributes) { SizeLimit = sizeLimit };
        var page = new PageResultRequestControl(200);
        if (sizeLimit == 0) request.Controls.Add(page);
        var entries = new List<LdapEntry>();
        do
        {
            var response = (SearchResponse)_connection.SendRequest(request);
            foreach (SearchResultEntry entry in response.Entries)
            {
                var values = new Dictionary<string, object[]>(StringComparer.OrdinalIgnoreCase);
                foreach (string attribute in entry.Attributes.AttributeNames)
                    values[attribute] = entry.Attributes[attribute].GetValues(attribute.Equals("objectSid", StringComparison.OrdinalIgnoreCase) ? typeof(byte[]) : typeof(string));
                entries.Add(new(entry.DistinguishedName, values));
            }
            if (sizeLimit != 0) break;
            var responsePage = response.Controls.OfType<PageResultResponseControl>().SingleOrDefault()
                ?? throw new InvalidOperationException("The directory did not honor the paged group search.");
            page.Cookie = responsePage.Cookie;
        } while (page.Cookie.Length > 0);
        return entries;
    }

    public void Dispose() => _connection.Dispose();
}
