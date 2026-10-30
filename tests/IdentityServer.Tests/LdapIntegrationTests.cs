using Microsoft.Extensions.Configuration;

namespace IdentityServer.Tests;

/// <summary>Opt-in, read-only tests against the configured AD, Samba AD or FreeIPA directory.</summary>
[Trait("Category", "LiveDirectory")]
public sealed class LdapIntegrationTests
{
    [Fact]
    public void ConfiguredDirectory_ResolvesStableSubjectAndDirectGroups()
    {
        string? settings = Environment.GetEnvironmentVariable("IDENTITYSERVER_LDAP_TEST_CONFIG");
        string? principalName = Environment.GetEnvironmentVariable("IDENTITYSERVER_LDAP_TEST_PRINCIPAL");
        string? expectedSubject = Environment.GetEnvironmentVariable("IDENTITYSERVER_LDAP_TEST_SUBJECT");
        if (string.IsNullOrEmpty(settings)) Assert.Skip("Set IDENTITYSERVER_LDAP_TEST_CONFIG to opt into portable live LDAP tests.");
        Assert.False(string.IsNullOrWhiteSpace(principalName), "Set IDENTITYSERVER_LDAP_TEST_PRINCIPAL to the test user's realm-qualified principal.");
        Assert.False(string.IsNullOrWhiteSpace(expectedSubject), "Set IDENTITYSERVER_LDAP_TEST_SUBJECT to the user's independently verified SID or ipaUniqueID.");
        var configuration = new ConfigurationBuilder().AddJsonFile(Path.GetFullPath(settings), optional: false)
            .AddEnvironmentVariables("IDENTITYSERVER_LDAP_TEST_").Build();
        var options = IdentityProviderOptions.Read(configuration);
        Assert.NotEqual(DirectoryProvider.Windows, options.Provider);
        var provider = new LdapUserProfileProvider(options);
        // This fixture supplies the already-authenticated principal. It checks real LDAP reads,
        // not the browser's Kerberos handshake, which requires the deployment checks in the docs.
        var principal = IdentityProviderTests.Principal(principalName!);
        var profile = provider.GetProfile(principal, includeGroups: true);
        Assert.Equal(expectedSubject, profile.Subject);
        Assert.NotEmpty(profile.Username);
        Assert.Equal(profile.Subject, provider.GetProfile(principal, includeGroups: false).Subject);
        string? expectedGroup = Environment.GetEnvironmentVariable("IDENTITYSERVER_LDAP_TEST_GROUP");
        if (!string.IsNullOrEmpty(expectedGroup)) Assert.Contains(expectedGroup, profile.Groups);
    }
}
