using System.Security.Claims;
using System.Security.Principal;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace IdentityServer.Tests;

public sealed class IdentityProviderTests
{
    internal static IConfiguration Configuration(params (string Key, string? Value)[] values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values.ToDictionary(p => p.Key, p => p.Value)).Build();

    internal static IConfiguration LdapConfiguration(string provider = "FreeIpa", params (string Key, string? Value)[] overrides)
    {
        var values = new Dictionary<string, string?>
        {
            ["IdentityServer:Directory:Provider"] = provider,
            ["IdentityServer:Directory:Server"] = "ldaps://directory.example.com",
            ["IdentityServer:Directory:BaseDn"] = "dc=example,dc=com",
            ["IdentityServer:Directory:Realm"] = "EXAMPLE.COM"
        };
        foreach (var (key, value) in overrides) values[key] = value;
        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    internal static ClaimsPrincipal Principal(string name = "alice@EXAMPLE.COM", string protocol = "Kerberos") =>
        new(new GenericIdentity(name, protocol));

    [Fact]
    public void Windows_DefaultsNeedNoNewSettings_AndContinueAcceptingNtlm()
    {
        var options = IdentityProviderOptions.Read(Configuration(), isWindows: true);
        Assert.Equal(DirectoryProvider.Windows, options.Provider);
        Assert.Equal(BrowserAuthenticationMode.Negotiate, options.Authentication);
        Assert.True(options.Allows(Principal("MACHINE\\alice", "NTLM")));
    }

    [Fact]
    public void LinuxWithoutDirectory_FailsWithActionableConfigurationError()
    {
        var error = Assert.Throws<InvalidOperationException>(() => IdentityProviderOptions.Read(Configuration(), isWindows: false));
        Assert.Contains("IdentityServer:Directory:Server", error.Message);
    }

    [Theory]
    [InlineData("ActiveDirectory", false, "Kerberos")]
    [InlineData("SambaAd", false, "Kerberos")]
    [InlineData("ActiveDirectory", true, "Negotiate")]
    [InlineData("SambaAd", true, "Negotiate")]
    [InlineData("FreeIpa", true, "Kerberos")]
    [InlineData("FreeIpa", false, "Kerberos")]
    public void AutoSelectsAuthenticationFromPlatformAndProvider(string provider, bool windows, string expected)
    {
        var options = IdentityProviderOptions.Read(LdapConfiguration(provider), windows);
        Assert.Equal(expected, options.Authentication.ToString());
    }

    [Fact]
    public void ServerWithAutoProvider_SelectsPortableAdEvenOnWindows()
    {
        var options = IdentityProviderOptions.Read(LdapConfiguration("Auto"), isWindows: true);
        Assert.Equal(DirectoryProvider.ActiveDirectory, options.Provider);
    }

    [Theory]
    [InlineData("IdentityServer:Directory:Provider", "FreeIpaa")]
    [InlineData("IdentityServer:Directory:Provider", "3")]
    [InlineData("IdentityServer:Directory:Provider", "Windows")]
    [InlineData("IdentityServer:Authentication:Mode", "Kerbeross")]
    [InlineData("IdentityServer:Authentication:Mode", "Negotiate")]
    [InlineData("IdentityServer:Directory:Server", "ldap://user:password@host")]
    [InlineData("IdentityServer:Directory:Server", "https://host")]
    [InlineData("IdentityServer:Directory:Server", "ldaps://host/dc=example")]
    [InlineData("IdentityServer:Directory:BaseDn", "")]
    [InlineData("IdentityServer:Directory:Realm", "")]
    [InlineData("IdentityServer:Directory:BindDn", "uid=reader,dc=example")]
    [InlineData("IdentityServer:Directory:BindPassword", "secret")]
    [InlineData("IdentityServer:Directory:TimeoutSeconds", "0")]
    [InlineData("IdentityServer:Directory:UserBaseDn", "")]
    [InlineData("IdentityServer:Directory:GroupBaseDn", " ")]
    public void InvalidConfiguration_FailsInsteadOfFallingBack(string key, string value)
    {
        Assert.Throws<InvalidOperationException>(() => IdentityProviderOptions.Read(LdapConfiguration("FreeIpa", (key, value)), isWindows: true));
    }

    [Theory]
    [InlineData("Kerberos", true)]
    [InlineData("NTLM", false)]
    [InlineData("Negotiate", false)]
    [InlineData("", false)]
    public void RequiredKerberos_RejectsOtherAndUnspecifiedProtocols(string protocol, bool accepted)
    {
        var options = IdentityProviderOptions.Read(LdapConfiguration());
        Assert.Equal(accepted, options.Allows(Principal(protocol: protocol)));
        Assert.False(options.Allows(new ClaimsPrincipal(new ClaimsIdentity())));
    }

    [Fact]
    public void WindowsLocalAccount_StillUsesItsSid_AndSkipsDirectoryReads()
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("Windows provider requires Windows.");
        var identity = new ClaimsIdentity([
            new Claim(ClaimTypes.Name, "MACHINE\\alice"),
            new Claim(ClaimTypes.PrimarySid, "S-1-5-21-1-2-3-1001"),
            new Claim(ClaimTypes.GroupSid, "S-1-5-113")], "NTLM");
        var user = new WindowsUserProfileProvider().GetProfile(new(identity), includeGroups: true);
        Assert.True(user.IsLocal);
        Assert.Equal("S-1-5-21-1-2-3-1001", user.Subject);
        Assert.Equal("alice", user.Username);
        Assert.Empty(user.Groups);
        var claims = ProfileClaims.Create(user, [Scopes.OpenId, Scopes.Email, Scopes.Profile, Scopes.Roles], []);
        Assert.Equal("alice@localhost", claims.FindFirst(ClaimTypes.Email)?.Value);
        Assert.Equal("MACHINE\\alice", claims.FindFirst(ClaimTypes.WindowsAccountName)?.Value);
    }

    [Fact]
    public void Claims_RespectScopesAndGroupAllowlist_WithoutChangingClaimNames()
    {
        var profile = new UserProfile
        {
            Subject = "stable-id", Username = "alice", AccountName = "alice@EXAMPLE.COM", DisplayName = "Alice",
            Email = "alice@example.com", Groups = ["App Readers", "Domain Admins", "App Readers"], GivenName = "Alice"
        };
        var minimal = ProfileClaims.Create(profile, [Scopes.OpenId], [new Regex("^App ")]);
        Assert.Equal(2, minimal.Claims.Count());
        var full = ProfileClaims.Create(profile, [Scopes.OpenId, Scopes.Email, Scopes.Profile, Scopes.Roles], [new Regex("^App ")]);
        Assert.Equal("stable-id", full.FindFirst(Claims.Subject)?.Value);
        Assert.Equal("alice@example.com", full.FindFirst(ClaimTypes.Email)?.Value);
        Assert.Equal("App Readers", Assert.Single(full.FindAll(ClaimTypes.Role)).Value);
    }
}
