using System.DirectoryServices;
using System.Runtime.InteropServices;
using ActiveDirectory;
using static ActiveDirectory.ADObject;

namespace IdentityServer.Tests;

/// <summary>
/// Whether this machine can run the live-directory tests: it needs a domain account and a
/// reachable domain controller. Everything is read-only.
/// </summary>
internal static class LiveDirectory
{
    private static readonly Lazy<string?> UnavailableReason = new(Probe);

    /// <summary>Skips the calling test with the reason when the directory is not available.</summary>
    public static void SkipUnlessAvailable()
    {
        if (UnavailableReason.Value is { } reason)
            Assert.Skip(reason);
    }

    /// <summary>The SAM account name of the signed-in user.</summary>
    public static string CurrentUserName => Environment.UserName;

    private static string? Probe()
    {
        if (!OperatingSystem.IsWindows())
            return "Active Directory tests need Windows.";
        if (string.Equals(Environment.UserDomainName, Environment.MachineName, StringComparison.OrdinalIgnoreCase))
            return $"Signed in with the local account {Environment.MachineName}\\{Environment.UserName}, not a domain account; the live directory tests need a domain-joined machine.";
        try
        {
            using var rootDse = new DirectoryEntry("LDAP://RootDSE");
            return rootDse.Properties["defaultNamingContext"].Value is string ? null : "The directory returned no default naming context.";
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException)
        {
            return "Active Directory is unreachable: " + ex.Message;
        }
    }
}

/// <summary>
/// Integration tests of the ActiveDirectory library against the live
/// domain. They use the signed-in account instead of a fixed user name and skip themselves on a
/// machine that is not signed in to a reachable domain. All tests are read-only.
/// </summary>
/// <remarks>
/// Test sequence:
///   1. Find the currently signed-in user account.
///   2. Retrieve the groups that user belongs to.
///   3. Pick the first group and enumerate its members.
///   4. Pick a member from that group and search for it via DirectorySearch.
///   5. A property left out of the search reads as empty until Bind() (the relaxed contract).
/// </remarks>
[Trait("Category", "LiveDirectory")]
public class ActiveDirectoryIntegrationTests(ITestOutputHelper output)
{
    // ── Test 1: find the current user ────────────────────────────────────────

    [Fact]
    public void FindUser_ByCurrentUsername_ReturnsMatchingAccount()
    {
        LiveDirectory.SkipUnlessAvailable();
        using var user = new ADUser(LiveDirectory.CurrentUserName);

        Assert.NotNull(user);
        Assert.Equal(LiveDirectory.CurrentUserName, user.Username, ignoreCase: true);

        output.WriteLine($"Found user: {user.Username} ({user.DisplayName})");
    }

    // ── Test 2: the user belongs to at least one group ───────────────────────

    [Fact]
    public void CurrentUser_Groups_AreNonEmpty()
    {
        LiveDirectory.SkipUnlessAvailable();
        using var user = new ADUser(LiveDirectory.CurrentUserName);

        List<string> groups = user.Groups;

        Assert.NotNull(groups);
        Assert.NotEmpty(groups);

        output.WriteLine($"User belongs to {groups.Count} group(s).");
        output.WriteLine($"First group DN: {groups[0]}");
    }

    // ── Test 3: the first group has at least one member ──────────────────────

    [Fact]
    public void FirstGroup_Members_AreNonEmpty()
    {
        LiveDirectory.SkipUnlessAvailable();
        using var user = new ADUser(LiveDirectory.CurrentUserName);
        List<string> groupDns = user.Groups;
        Assert.NotEmpty(groupDns);

        string groupDn = groupDns[0];
        output.WriteLine($"Testing group: {groupDn}");

        using var group = new ADGroup(groupDn);
        List<ADGroup.Membership> members = group.Members;

        Assert.NotNull(members);
        Assert.NotEmpty(members);

        output.WriteLine($"Group has {members.Count} member(s). First CN: {members[0].DisplayName}");
    }

    // ── Test 4: a member of that group can be found via DirectorySearch ───────

    [Fact]
    public void FirstGroupUserMember_CanBeFoundByDirectorySearch()
    {
        LiveDirectory.SkipUnlessAvailable();

        // Walk the user's groups until we find one that contains at least one user member.
        using var user = new ADUser(LiveDirectory.CurrentUserName);
        List<string> groupDns = user.Groups;
        Assert.NotEmpty(groupDns);

        string? memberCn = null;
        string? chosenGroup = null;

        foreach (string dn in groupDns)
        {
            using var group = new ADGroup(dn);
            List<ADGroup.Membership> members = group.Members;

            var userMemberInfo = members.FirstOrDefault(m => m.ObjectType == ObjectClass.User);
            if (userMemberInfo != null)
            {
                using var userMember = new ADUser($"LDAP://{userMemberInfo.DistinguishedName}");
                memberCn = userMember.Name;
                chosenGroup = dn;
                break;
            }
        }

        Assert.NotNull(memberCn);
        output.WriteLine($"Selected user member '{memberCn}' from group '{chosenGroup}'.");

        // Search for the member by CN using DirectorySearch.
        var search = new DirectorySearch(ObjectClass.User);
        search.PropertiesToSearch.Add(ADProperties.CommonName, memberCn);
        search.PropertiesToLoad.Add(ADProperties.CommonName);

        using var found = search.FindOne<ADUser>();

        Assert.NotNull(found);
        Assert.NotEmpty(found.Name);
        output.WriteLine($"DirectorySearch located member as: {found.Name}");
    }

    // ── Test 5: a property left out of the search reads as empty until Bind() ─

    [Fact]
    public void SearchResult_UnloadedProperty_IsEmptyUntilBind()
    {
        LiveDirectory.SkipUnlessAvailable();

        // Search for the current user, pre-loading only cn (objectguid and objectClass are always
        // added by DirectorySearch). sAMAccountName is intentionally omitted.
        var search = new DirectorySearch(ObjectClass.User);
        search.PropertiesToSearch.Add(ADProperties.User.SamAccountName, LiveDirectory.CurrentUserName);
        search.PropertiesToLoad.Add(ADProperties.CommonName);

        using var user = search.FindOne<ADUser>();
        Assert.NotNull(user);

        // Name (cn) is served from the search-result cache — no bind required.
        string cachedName = user.Name;
        Assert.NotEmpty(cachedName);
        output.WriteLine($"Cached property (cn): {cachedName}");

        // Reading a search result must not bind; the shared library reads an attribute
        // that was not requested as absent instead of throwing or binding behind the caller's back.
        Assert.Equal("", user.Username);

        user.Bind();
        string boundUsername = user.Username;
        Assert.Equal(LiveDirectory.CurrentUserName, boundUsername, ignoreCase: true);
        output.WriteLine($"Property after Bind() (sAMAccountName): {boundUsername}");

        // DisplayName may legitimately be empty, but reading it must not throw.
        Assert.NotNull(user.DisplayName);
    }
}
