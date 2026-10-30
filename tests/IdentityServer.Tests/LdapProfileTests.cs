using System.Security.Claims;
using static IdentityServer.Tests.IdentityProviderTests;

namespace IdentityServer.Tests;

public sealed class LdapProfileTests
{
    [Theory]
    [InlineData("alice@OTHER.COM")]
    [InlineData("alice")]
    [InlineData("OTHER\\alice")]
    [InlineData("host/server@EXAMPLE.COM")]
    public void UntrustedOrAmbiguousPrincipal_NeverOpensLdap(string name)
    {
        var options = IdentityProviderOptions.Read(LdapConfiguration());
        var provider = new LdapUserProfileProvider(options, () => throw new Exception("LDAP must not be opened"));
        Assert.Throws<InvalidOperationException>(() => provider.GetProfile(Principal(name), true));
    }

    [Fact]
    public void LdapFilterEscapesUserInputWithoutTrimming()
    {
        var provider = new LdapUserProfileProvider(IdentityProviderOptions.Read(LdapConfiguration()));
        string filter = provider.UserFilter(Principal("a*)(uid=*)@EXAMPLE.COM"));
        Assert.Equal("(&(objectClass=posixAccount)(krbPrincipalName=a\\2a\\29\\28uid=\\2a\\29@EXAMPLE.COM))", filter);
        Assert.Equal(" a\\00\\5c\\c3\\a9 ", LdapUserProfileProvider.Escape(" a\0\\é "));
    }

    [Theory]
    [InlineData("ActiveDirectory")]
    [InlineData("SambaAd")]
    public void AdProvidersPreserveSid_AndQueryDirectGroupMembers(string providerName)
    {
        var options = IdentityProviderOptions.Read(LdapConfiguration(providerName));
        const string sid = "S-1-5-21-123-456-789-1001";
        var ldap = new FakeLdap([
            new("CN=Alice,OU=People,DC=example,DC=com", new Dictionary<string, object[]>
            {
                ["objectSid"] = [LdapUserProfileProvider.SidFromString(sid)],
                ["sAMAccountName"] = ["alice"], ["displayName"] = ["Alice"], ["mail"] = ["alice@example.com"]
            })], [new("CN=App Readers,DC=example,DC=com", new Dictionary<string, object[]> { ["cn"] = ["App Readers"] })]);
        var user = new LdapUserProfileProvider(options, () => ldap).GetProfile(Principal(), true);
        Assert.Equal(sid, user.Subject);
        Assert.Equal("alice@example.com", user.Email);
        Assert.Equal(["App Readers"], user.Groups);
        Assert.Contains("(userPrincipalName=alice@EXAMPLE.COM)", ldap.Filters[0]);
        Assert.Equal("(&(objectClass=group)(member=CN=Alice,OU=People,DC=example,DC=com))", ldap.Filters[1]);
        Assert.True(ldap.Disposed);
    }

    [Fact]
    public void FreeIpaUsesUuid_AndDoesNotQueryGroupsWithoutRolesScope()
    {
        var options = IdentityProviderOptions.Read(LdapConfiguration());
        var ldap = new FakeLdap([IpaUser()], []);
        var user = new LdapUserProfileProvider(options, () => ldap).GetProfile(Principal(), false);
        Assert.Equal("81f8a447-5bdd-4f57-9e35-678912302922", user.Subject);
        Assert.Equal("alice", user.Username);
        Assert.Equal("alice@EXAMPLE.COM", user.AccountName);
        Assert.Single(ldap.Filters);
        Assert.Equal("cn=users,cn=accounts,dc=example,dc=com", ldap.Bases[0]);
    }

    [Fact]
    public void FreeIpaQueriesOnlyDirectGroupsInItsGroupContainer()
    {
        var options = IdentityProviderOptions.Read(LdapConfiguration());
        var ldap = new FakeLdap([IpaUser()], [new("cn=app,cn=groups,cn=accounts,dc=example,dc=com", new Dictionary<string, object[]> { ["cn"] = ["app"] })]);
        var user = new LdapUserProfileProvider(options, () => ldap).GetProfile(Principal(), true);
        Assert.Equal(["app"], user.Groups);
        Assert.Equal("cn=groups,cn=accounts,dc=example,dc=com", ldap.Bases[1]);
        Assert.Equal("(&(objectClass=groupOfNames)(member=uid=alice,cn=users,cn=accounts,dc=example,dc=com))", ldap.Filters[1]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void MissingOrDuplicateUsers_FailClosed(int count)
    {
        var ldap = new FakeLdap(Enumerable.Repeat(IpaUser(), count).ToArray(), []);
        var provider = new LdapUserProfileProvider(IdentityProviderOptions.Read(LdapConfiguration()), () => ldap);
        Assert.Throws<InvalidOperationException>(() => provider.GetProfile(Principal(), true));
        Assert.Single(ldap.Filters);
        Assert.True(ldap.Disposed);
    }

    [Fact]
    public void WindowsPrimarySid_IsUsedInsteadOfAnAmbiguousAccountName()
    {
        var principal = Principal("OTHER\\alice");
        ((ClaimsIdentity)principal.Identity!).AddClaim(new(ClaimTypes.PrimarySid, "S-1-5-21-1-2-3-1001"));
        var provider = new LdapUserProfileProvider(IdentityProviderOptions.Read(LdapConfiguration("SambaAd")));
        string filter = provider.UserFilter(principal);
        Assert.Contains("(objectSid=\\01\\05", filter);
        Assert.DoesNotContain("alice", filter);
    }

    [Fact]
    public void SidEncodingMatchesKnownWireFormat_AndRejectsTruncatedInput()
    {
        byte[] sid = [1, 2, 0, 0, 0, 0, 0, 5, 32, 0, 0, 0, 32, 2, 0, 0];
        Assert.Equal("S-1-5-32-544", LdapUserProfileProvider.SidToString(sid));
        Assert.Equal(sid, LdapUserProfileProvider.SidFromString("S-1-5-32-544"));
        Assert.Throws<InvalidOperationException>(() => LdapUserProfileProvider.SidToString(sid[..^1]));
        Assert.Throws<InvalidOperationException>(() => LdapUserProfileProvider.SidFromString("S-1-5-4294967296"));
    }

    private static LdapEntry IpaUser() => new("uid=alice,cn=users,cn=accounts,dc=example,dc=com", new Dictionary<string, object[]>
    {
        ["ipaUniqueID"] = ["81f8a447-5bdd-4f57-9e35-678912302922"], ["uid"] = ["alice"], ["cn"] = ["Alice"]
    });

    private sealed class FakeLdap(IReadOnlyList<LdapEntry> users, IReadOnlyList<LdapEntry> groups) : ILdapSearch
    {
        internal List<string> Filters { get; } = [];
        internal List<string> Bases { get; } = [];
        internal bool Disposed { get; private set; }
        public IReadOnlyList<LdapEntry> Search(string searchBase, string filter, string[] attributes, int sizeLimit = 0)
        {
            Filters.Add(filter); Bases.Add(searchBase);
            return Filters.Count == 1 ? users : groups;
        }
        public void Dispose() => Disposed = true;
    }
}
