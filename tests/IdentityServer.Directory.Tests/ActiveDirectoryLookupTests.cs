using ActiveDirectory;
using Microsoft.Extensions.Logging.Abstractions;
using System.Runtime.InteropServices;

namespace IdentityServer.Directory.Tests;

/// <summary>
/// <see cref="ActiveDirectoryLookup"/> without a domain: requests are validated before anything is
/// sent to the directory, and a directory that cannot be reached surfaces as
/// <see cref="DirectoryUnavailableException"/> rather than a COM error.
/// </summary>
public class ActiveDirectoryLookupTests
{
    private static ActiveDirectoryLookup Lookup(DirectoryOptions? options = null) =>
        new(new StaticOptionsMonitor<DirectoryOptions>(options ?? new DirectoryOptions()), NullLogger<ActiveDirectoryLookup>.Instance);

    [Fact]
    public void DefaultPeopleSearch_OnlyRequiresAnEnabledPersonAccount()
    {
        DirectorySearch search = Lookup().CreatePeopleSearch(new DirectoryOptions(), [], DirectoryScope.Local, includeDisabled: false);

        Assert.Equal("&(objectCategory=person)(!(userAccountControl:1.2.840.113556.1.4.803:=2))", search.AdditionalFilter);
        Assert.Contains(ADProperties.User.EmployeeID, search.PropertiesToLoad);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ApplicationFilter_IsAppliedAndPrefetchedOnlyForFilteredSearches(bool includeDisabled)
    {
        var lookup = new ActiveDirectoryLookup(new StaticOptionsMonitor<DirectoryOptions>(new()), userFilter: new StaffFilter());

        DirectorySearch search = lookup.CreatePeopleSearch(new DirectoryOptions(), [], DirectoryScope.Local, includeDisabled);

        Assert.Equal(!includeDisabled, search.PropertiesToLoad.Contains("employeeType"));
        Assert.Equal(includeDisabled
            ? "&(objectCategory=person)"
            : "&(objectCategory=person)(!(userAccountControl:1.2.840.113556.1.4.803:=2))(employeeType=staff)", search.AdditionalFilter);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CallerFilter_ReplacesTheRegisteredOne_AndAppliesEvenWithIncludeDisabled(bool includeDisabled)
    {
        var lookup = new ActiveDirectoryLookup(new StaticOptionsMonitor<DirectoryOptions>(new()), userFilter: new StaffFilter());

        DirectorySearch search = lookup.CreatePeopleSearch(new DirectoryOptions(), [], DirectoryScope.Local, includeDisabled, filter: new NamedFilter());

        Assert.Contains("extensionAttribute1", search.PropertiesToLoad);
        Assert.DoesNotContain("employeeType", search.PropertiesToLoad);
        Assert.Equal(includeDisabled
            ? "&(objectCategory=person)(givenName=*)"
            : "&(objectCategory=person)(!(userAccountControl:1.2.840.113556.1.4.803:=2))(givenName=*)", search.AdditionalFilter);
    }

    [Fact]
    public void CallerFilter_WithDisplayNameMode_SearchesOnlyTheDisplayName()
    {
        DirectorySearch search = Lookup().CreatePeopleSearch(new DirectoryOptions(), ["Smith*"], DirectoryScope.Global, includeDisabled: false,
            filter: new NamedFilter(), mode: DirectorySearchMode.DisplayName);

        Assert.Contains("extensionAttribute1", search.PropertiesToLoad);
        Assert.Contains("(givenName=*)", search.AdditionalFilter);
        Assert.Contains(@"(displayName=*Smith\2a*)", search.AdditionalFilter);
        Assert.DoesNotContain(ADProperties.User.SamAccountName, search.AdditionalFilter);
        Assert.DoesNotContain(ADProperties.User.EmployeeID, search.AdditionalFilter);

        DirectorySearch normal = Lookup().CreatePeopleSearch(new DirectoryOptions(), ["Smith"], DirectoryScope.Local, includeDisabled: false);
        Assert.DoesNotContain("(givenName=*)", normal.AdditionalFilter);
        Assert.DoesNotContain("extensionAttribute1", normal.PropertiesToLoad);
        Assert.Contains(ADProperties.User.SamAccountName, normal.AdditionalFilter);
    }

    [Fact]
    public void DefaultPeopleSearch_FetchesTheFieldsOfDirectoryUser()
    {
        DirectorySearch search = Lookup().CreatePeopleSearch(new DirectoryOptions(), [], DirectoryScope.Global, includeDisabled: false);

        Assert.Contains(ADProperties.ObjectSid, search.PropertiesToLoad);
        Assert.Contains(ADProperties.User.Email, search.PropertiesToLoad);
        Assert.Contains(ADProperties.UserAccountControl, search.PropertiesToLoad);
    }

    [Fact]
    public void Attributes_AreNullWhenNoneWereRequested()
    {
        using var user = new ADUser();

        Assert.Null(ActiveDirectoryLookup.AttributesOf(user, null));
        Assert.Null(ActiveDirectoryLookup.AttributesOf(user, []));
    }

    private sealed class StaffFilter : IDirectoryUserFilter
    {
        public IReadOnlyList<string> AdditionalProperties => ["employeeType"];
        public string LdapFilter => "(employeeType=staff)";
        public bool IsIncluded(ADUser user) => user.GetProperty("employeeType") == "staff";
    }

    private sealed class NamedFilter : IDirectoryUserFilter
    {
        public IReadOnlyList<string> AdditionalProperties => ["extensionAttribute1"];
        public string LdapFilter => "(givenName=*)";
        public bool IsIncluded(ADUser user) => true;
    }

    [Theory]
    [InlineData("j smith")]
    [InlineData("")]
    [InlineData("  ")]
    public async Task GlobalSearch_IsValidatedBeforeTheDirectoryIsAsked(string query)
    {
        await Assert.ThrowsAsync<DirectoryQueryException>(() => Lookup().SearchUsersAsync(query, scope: DirectoryScope.Global, cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GroupSearch_NeedsATerm()
    {
        await Assert.ThrowsAsync<DirectoryQueryException>(() => Lookup().SearchGroupsAsync("  ", cancellationToken: TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("not an id")]
    [InlineData("*")]
    [InlineData("S-1-5-21-x")]
    public async Task UserLookup_RefusesMalformedIds(string id)
    {
        await Assert.ThrowsAsync<DirectoryQueryException>(() => Lookup().FindUserAsync(id, TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<DirectoryQueryException>(() => Lookup().GetUserGroupsAsync(id, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GroupLookup_RefusesAccountNames()
    {
        await Assert.ThrowsAsync<DirectoryQueryException>(() => Lookup().FindGroupAsync(@"EXAMPLE\Directory Admins", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GroupListing_NeedsAGroup()
    {
        await Assert.ThrowsAsync<DirectoryQueryException>(() => Lookup().ListGroupMembersAsync(" ", cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Cancellation_IsHonouredBeforeTheQuery()
    {
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Lookup().SearchUsersAsync("smith", cancellationToken: cancelled.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Lookup().FindUserAsync("S-1-5-21-1-2-3-1001", cancelled.Token));
    }

    [Theory]
    [InlineData(DirectoryScope.Local, "local people search using Directory:UserRoot (")]
    [InlineData(DirectoryScope.Global, "global people search using Directory:UserRootGlobal (")]
    public async Task UnreachableDirectory_ReportsTheSearchAndItsSetting(DirectoryScope scope, string expectedContext)
    {
        // .invalid never resolves (RFC 2606), so this fails the same way on and off a domain.
        const string root = "LDAP://directory.invalid/DC=directory,DC=invalid";
        var options = new DirectoryOptions { UserRoot = root, UserRootGlobal = root };

        var failure = await Assert.ThrowsAsync<DirectoryUnavailableException>(() => Lookup(options).SearchUsersAsync("smith", scope: scope, cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains(expectedContext, failure.Message);
        Assert.Contains($"0x{failure.InnerException!.HResult:X8}", failure.Message);
        Assert.DoesNotContain("smith", failure.Message);
    }

    [Fact]
    public void ReferralDetailPreservesTheWindowsErrorAndExplainsWhatToCheck()
    {
        var referral = new COMException("A referral was returned from the server.", unchecked((int)0x8007202B));
        var failure = ActiveDirectoryLookup.DescribeFailure(referral, "global people search", "Directory:UserRootGlobal");

        Assert.Same(referral, failure.InnerException);
        Assert.Contains("global people search using Directory:UserRootGlobal (0x8007202B)", failure.Message);
        Assert.Contains(referral.Message, failure.Message);
        Assert.Contains("intended domain", failure.Message);
    }
}
