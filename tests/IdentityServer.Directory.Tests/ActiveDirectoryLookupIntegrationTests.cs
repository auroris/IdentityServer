using System.DirectoryServices;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Extensions.Logging.Abstractions;

namespace IdentityServer.Directory.Tests;

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
/// <see cref="ActiveDirectoryLookup"/> against the live domain, searching the whole domain
/// (no configured roots) for the signed-in account. Skipped off the domain; read-only.
/// </summary>
[Trait("Category", "LiveDirectory")]
public class ActiveDirectoryLookupIntegrationTests(ITestOutputHelper output)
{
    private readonly ActiveDirectoryLookup _lookup = new(
        new StaticOptionsMonitor<DirectoryOptions>(new DirectoryOptions()),
        NullLogger<ActiveDirectoryLookup>.Instance);

    [Fact]
    public async Task CurrentUser_IsFoundBySidGuidAndAccountName()
    {
        LiveDirectory.SkipUnlessAvailable();
        using WindowsIdentity current = WindowsIdentity.GetCurrent();
        string sid = current.User!.Value;

        DirectoryUserDetail? bySid = await _lookup.FindUserAsync(sid, TestContext.Current.CancellationToken);

        Assert.NotNull(bySid);
        Assert.Equal(sid, bySid.Sid);
        Assert.Equal(current.Name, bySid.AccountName, ignoreCase: true);
        output.WriteLine($"{bySid.AccountName}: {bySid.DisplayName}, {bySid.Groups.Count} group(s)");

        DirectoryUserDetail? byGuid = await _lookup.FindUserAsync(bySid.Guid, TestContext.Current.CancellationToken);
        DirectoryUserDetail? byAccount = await _lookup.FindUserAsync(bySid.AccountName, TestContext.Current.CancellationToken);
        Assert.Equal(sid, byGuid?.Sid);
        Assert.Equal(sid, byAccount?.Sid);
        Assert.Equal(bySid.Groups, await _lookup.GetUserGroupsAsync(sid, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CurrentUser_IsFoundByAPeopleSearch()
    {
        LiveDirectory.SkipUnlessAvailable();
        string sid = WindowsIdentity.GetCurrent().User!.Value;
        DirectoryUserDetail? me = await _lookup.FindUserAsync(sid, TestContext.Current.CancellationToken);
        Assert.NotNull(me);

        // The user name and, when known, the surname narrow the search to this account.
        string query = me.Surname is { Length: >= 2 } surname ? $"{me.UserName} {surname}" : me.UserName;
        IReadOnlyList<DirectoryUser> found = await _lookup.SearchUsersAsync(query, take: 50, includeDisabled: true, cancellationToken: TestContext.Current.CancellationToken);

        output.WriteLine($"'{query}' found {found.Count} account(s).");
        Assert.Contains(found, user => user.Sid == sid);
        Assert.Equal(found.OrderBy(user => user.DisplayName, StringComparer.InvariantCultureIgnoreCase).Select(user => user.Sid), found.Select(user => user.Sid));
        Assert.All(found, user => Assert.Null(user.Attributes));
    }

    [Fact]
    public async Task CurrentUser_ReturnsTheAttributesACallerAsksFor()
    {
        LiveDirectory.SkipUnlessAvailable();
        string sid = WindowsIdentity.GetCurrent().User!.Value;

        DirectoryUserDetail? me = await _lookup.FindUserAsync(sid, TestContext.Current.CancellationToken, ["sAMAccountName", "distinguishedName"]);
        Assert.NotNull(me);
        Assert.Equal(me.UserName, me.Attributes?["samaccountname"]);
        Assert.NotNull(me.Attributes?.GetValueOrDefault("distinguishedName"));

        // A caller's filter applies even to an includeDisabled search and returns its attributes.
        IReadOnlyList<DirectoryUser> found = await _lookup.SearchUsersAsync(me.UserName, take: 50, includeDisabled: true,
            cancellationToken: TestContext.Current.CancellationToken, filter: new SidOnly(sid));
        DirectoryUser only = Assert.Single(found);
        Assert.Equal(sid, only.Sid);
        Assert.Equal(me.UserName, only.Attributes?.GetValueOrDefault(ActiveDirectory.ADProperties.User.SamAccountName));
    }

    private sealed class SidOnly(string sid) : IDirectoryUserFilter
    {
        public IReadOnlyList<string> AdditionalProperties => [ActiveDirectory.ADProperties.User.SamAccountName];
        public string LdapFilter => "(sAMAccountName=*)";
        public bool IsIncluded(ActiveDirectory.ADUser user) => user.Sid == sid;
    }

    [Fact]
    public async Task CurrentUsersFirstGroup_IsListedSearchedAndFoundWithMembers()
    {
        LiveDirectory.SkipUnlessAvailable();
        DirectoryUserDetail? me = await _lookup.FindUserAsync(WindowsIdentity.GetCurrent().User!.Value, TestContext.Current.CancellationToken);
        Assert.NotNull(me);
        Assert.SkipWhen(me.Groups.Count == 0, "The signed-in account has no direct group memberships to look up.");
        string groupName = me.Groups[0];

        // A group listing finds the group by its exact common name.
        DirectoryGroupListing? listing = await _lookup.ListGroupMembersAsync(groupName, includeDisabled: true, cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(listing);
        Assert.Equal(groupName, listing.Group.Name, ignoreCase: true);
        if (listing.Members.Count < new DirectoryOptions().MaxGroupMembers)
            Assert.Contains(listing.Members, member => member.Sid == me.Sid);
        output.WriteLine($"{listing.Group.Name}: {listing.Members.Count} listed member(s), modified {listing.Modified:u}");

        IReadOnlyList<DirectoryGroup> groups = await _lookup.SearchGroupsAsync(groupName, take: 50, cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotEmpty(groups);

        DirectoryGroupDetail? detail = await _lookup.FindGroupAsync(listing.Group.Sid, TestContext.Current.CancellationToken);
        Assert.NotNull(detail);
        Assert.Equal(listing.Group.Guid, detail.Guid);
        Assert.True(detail.MembersTruncated || detail.MemberUsers.Count + detail.MemberGroups.Count + detail.MemberComputers.Count > 0);
        output.WriteLine($"{detail.Name}: {detail.MemberUsers.Count} user(s), {detail.MemberGroups.Count} group(s), {detail.MemberComputers.Count} computer(s)");
    }
}
