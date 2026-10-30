using System;
using System.Security.Claims;
using System.Linq;
using Microsoft.Extensions.Configuration;

namespace IdentityServer;

internal enum BrowserAuthenticationMode { Auto, Negotiate, Kerberos, ProxyKerberos }
internal enum DirectoryProvider { Auto, Windows, ActiveDirectory, SambaAd, FreeIpa }

/// <summary>Resolved once per host. Invalid configuration never selects a weaker fallback.</summary>
internal sealed class IdentityProviderOptions
{
    public BrowserAuthenticationMode Authentication { get; private init; }
    public DirectoryProvider Provider { get; private init; }
    public Uri? Server { get; private init; }
    public string BaseDn { get; private init; } = "";
    public string UserBaseDn { get; private init; } = "";
    public string GroupBaseDn { get; private init; } = "";
    public string Realm { get; private init; } = "";
    public string? NetbiosDomain { get; private init; }
    public string? BindDn { get; private init; }
    public string? BindPassword { get; private init; }
    public int TimeoutSeconds { get; private init; }
    internal string AuthenticationScheme => Authentication == BrowserAuthenticationMode.ProxyKerberos
        ? ProxyKerberosOptions.Scheme : Microsoft.AspNetCore.Authentication.Negotiate.NegotiateDefaults.AuthenticationScheme;

    public bool Allows(ClaimsPrincipal? principal) =>
        principal?.Identity?.IsAuthenticated == true &&
        (Authentication switch
        {
            BrowserAuthenticationMode.Kerberos => string.Equals(principal.Identity.AuthenticationType, "Kerberos", StringComparison.OrdinalIgnoreCase),
            BrowserAuthenticationMode.ProxyKerberos => principal.Identity.AuthenticationType == ProxyKerberosOptions.Scheme,
            _ => true
        });

    public static IdentityProviderOptions Read(IConfiguration configuration, bool? isWindows = null)
    {
        bool windows = isWindows ?? OperatingSystem.IsWindows();
        var directory = configuration.GetSection("IdentityServer:Directory");
        var provider = ReadEnum<DirectoryProvider>(directory["Provider"], "IdentityServer:Directory:Provider");
        var authentication = ReadEnum<BrowserAuthenticationMode>(configuration["IdentityServer:Authentication:Mode"], "IdentityServer:Authentication:Mode");
        string? server = directory["Server"];
        if (provider == DirectoryProvider.Auto)
            provider = windows && string.IsNullOrWhiteSpace(server) ? DirectoryProvider.Windows : DirectoryProvider.ActiveDirectory;
        if (authentication == BrowserAuthenticationMode.Auto)
            authentication = windows && provider != DirectoryProvider.FreeIpa ? BrowserAuthenticationMode.Negotiate : BrowserAuthenticationMode.Kerberos;

        if (provider == DirectoryProvider.FreeIpa && authentication is not (BrowserAuthenticationMode.Kerberos or BrowserAuthenticationMode.ProxyKerberos))
            throw new InvalidOperationException("FreeIPA requires IdentityServer:Authentication:Mode Kerberos, ProxyKerberos or Auto.");

        if (provider == DirectoryProvider.Windows)
        {
            if (authentication == BrowserAuthenticationMode.ProxyKerberos)
                throw new InvalidOperationException("ProxyKerberos requires an explicit LDAP directory provider (ActiveDirectory, SambaAd or FreeIpa); forwarded principals have no Windows token/SID claims.");
            if (!windows)
                throw new InvalidOperationException("The Windows directory provider requires Windows. Configure IdentityServer:Directory for ActiveDirectory, SambaAd or FreeIpa on Linux.");
            if (directory.GetChildren().Any(section => !section.Key.Equals("Provider", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("The Windows directory provider uses the process account and automatic domain discovery. Remove LDAP settings or select ActiveDirectory, SambaAd or FreeIpa.");
            return new() { Provider = provider, Authentication = authentication };
        }

        if (!Uri.TryCreate(server, UriKind.Absolute, out var uri) ||
            (uri.Scheme != "ldap" && uri.Scheme != "ldaps") || string.IsNullOrEmpty(uri.Host) ||
            !string.IsNullOrEmpty(uri.UserInfo) || uri.AbsolutePath is not ("" or "/") ||
            !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment) || uri.Port == 0)
            throw new InvalidOperationException("IdentityServer:Directory:Server must be an ldaps://hostname[:port] or ldap://hostname[:port] URL. ldap:// always uses StartTLS.");

        string baseDn = Required(directory, "BaseDn");
        string realm = Required(directory, "Realm");
        if (realm.IndexOfAny(['@', '\\', '/', ' ', '\t', '\r', '\n']) >= 0)
            throw new InvalidOperationException("IdentityServer:Directory:Realm must be a Kerberos realm, for example EXAMPLE.COM.");
        string? bindDn = directory["BindDn"];
        string? bindPassword = directory["BindPassword"];
        if (string.IsNullOrWhiteSpace(bindDn) != string.IsNullOrEmpty(bindPassword))
            throw new InvalidOperationException("Set both IdentityServer:Directory:BindDn and BindPassword, or neither to use the process credentials.");
        int timeout = directory.GetValue("TimeoutSeconds", 10);
        if (timeout < 1 || timeout > 120)
            throw new InvalidOperationException("IdentityServer:Directory:TimeoutSeconds must be between 1 and 120.");
        foreach (string key in new[] { "UserBaseDn", "GroupBaseDn", "NetbiosDomain" })
            if (directory[key] is not null && string.IsNullOrWhiteSpace(directory[key]))
                throw new InvalidOperationException($"IdentityServer:Directory:{key} must be nonempty when configured.");

        return new()
        {
            Authentication = authentication, Provider = provider, Server = uri, BaseDn = baseDn,
            Realm = realm, NetbiosDomain = directory["NetbiosDomain"],
            UserBaseDn = directory["UserBaseDn"] ?? (provider == DirectoryProvider.FreeIpa ? "cn=users,cn=accounts," + baseDn : baseDn),
            GroupBaseDn = directory["GroupBaseDn"] ?? (provider == DirectoryProvider.FreeIpa ? "cn=groups,cn=accounts," + baseDn : baseDn),
            BindDn = bindDn, BindPassword = bindPassword, TimeoutSeconds = timeout
        };
    }

    private static string Required(IConfigurationSection section, string key) =>
        !string.IsNullOrWhiteSpace(section[key]) ? section[key]!.Trim() :
        throw new InvalidOperationException($"IdentityServer:Directory:{key} is required for an LDAP directory provider.");

    private static T ReadEnum<T>(string? value, string key) where T : struct, Enum
    {
        if (string.IsNullOrWhiteSpace(value)) return default;
        // Do not accept enum numbers: misspelled settings must fail at startup.
        foreach (string name in Enum.GetNames<T>())
            if (string.Equals(name, value, StringComparison.OrdinalIgnoreCase)) return Enum.Parse<T>(name);
        throw new InvalidOperationException($"{key} must be one of: {string.Join(", ", Enum.GetNames<T>())}.");
    }
}
