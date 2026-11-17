using MatchType = ActiveDirectory.DirectorySearch.MatchType;

namespace IdentityServer.Directory.Tests;

/// <summary>Term splitting, the global minimum length, result limits, listing rules and LDAP filters.</summary>
public class DirectoryQueryTests
{
    [Fact]
    public void Tokenize_SplitsOnSpacesAndCommas_AndDropsDuplicatesIgnoringCase()
    {
        IReadOnlyList<string> terms = DirectoryQuery.Tokenize("  Smith,  John\tsmith JOHN ,, 840-8000 ");

        Assert.Equal(["Smith", "John", "840-8000"], terms);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(" , ,")]
    public void Tokenize_BlankQuery_HasNoTerms(string? query)
    {
        Assert.Empty(DirectoryQuery.Tokenize(query));
    }

    [Fact]
    public void Tokenize_TooManyTerms_IsRefused()
    {
        string query = string.Join(' ', Enumerable.Range(1, DirectoryQuery.MaxTerms + 1).Select(i => "t" + i));

        var refused = Assert.Throws<DirectoryQueryException>(() => DirectoryQuery.Tokenize(query));
        Assert.Contains(DirectoryQuery.MaxTerms.ToString(), refused.Message);
    }

    [Fact]
    public void Tokenize_TooLongQuery_IsRefused()
    {
        Assert.Throws<DirectoryQueryException>(() => DirectoryQuery.Tokenize(new string('a', DirectoryQuery.MaxQueryLength + 1)));
    }

    [Fact]
    public void GlobalTerms_ShorterThanMinimum_AreRefusedWithReason()
    {
        var refused = Assert.Throws<DirectoryQueryException>(() => DirectoryQuery.EnsureGlobalTerms(["smith", "j"], 2));

        Assert.Equal("Each search term must be at least 2 characters when searching the global directory.", refused.Message);
    }

    [Fact]
    public void GlobalTerms_AtMinimum_AreAccepted()
    {
        DirectoryQuery.EnsureGlobalTerms(["jo", "smith"], 2);
    }

    [Fact]
    public void GlobalTerms_None_AreRefusedUnlessAllowed()
    {
        Assert.Throws<DirectoryQueryException>(() => DirectoryQuery.EnsureGlobalTerms([], 2));
        DirectoryQuery.EnsureGlobalTerms([], 2, allowEmpty: true);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void GlobalTerms_MisconfiguredMinimum_FallsBackToTwo(int minLength)
    {
        Assert.Throws<DirectoryQueryException>(() => DirectoryQuery.EnsureGlobalTerms(["a"], minLength));
        DirectoryQuery.EnsureGlobalTerms(["ab"], minLength);
    }

    [Theory]
    [InlineData(null, 20, 50, 20)]
    [InlineData(0, 20, 50, 20)]
    [InlineData(-1, 20, 50, 20)]
    [InlineData(7, 20, 50, 7)]
    [InlineData(500, 20, 50, 50)]
    [InlineData(null, 80, 50, 50)]
    [InlineData(null, 0, 50, 1)]
    [InlineData(10, 20, 0, 1)]
    public void ClampTake_UsesDefaultAndCapsAtMaximum(int? take, int defaultTake, int maxTake, int expected)
    {
        Assert.Equal(expected, DirectoryQuery.ClampTake(take, defaultTake, maxTake));
    }

    [Theory]
    [InlineData(MatchType.Contains, "(cn=*a\\2ab\\28c\\29d\\5ce*)")]
    [InlineData(MatchType.StartsWith, "(cn=a\\2ab\\28c\\29d\\5ce*)")]
    [InlineData(MatchType.Exact, "(cn=a\\2ab\\28c\\29d\\5ce)")]
    public void Assertion_EscapesFilterMetacharacters(MatchType match, string expected)
    {
        Assert.Equal(expected, DirectoryQuery.Assertion("cn", "a*b(c)d\\e", match));
    }

    [Fact]
    public void PeopleFilter_RequiresEveryTermInSomeAttribute()
    {
        string filter = DirectoryQuery.PeopleFilter(["smi", "84"], includeDisabled: true);

        Assert.Equal(
            "&(objectCategory=person)" +
            "(|(displayName=*smi*)(givenName=*smi*)(sn=smi*)(sAMAccountName=smi*)(mail=smi*)(telephoneNumber=*smi*)(employeeID=smi*))" +
            "(|(displayName=*84*)(givenName=*84*)(sn=84*)(sAMAccountName=84*)(mail=84*)(telephoneNumber=*84*)(employeeID=84*))",
            filter);
    }

    [Fact]
    public void PeopleFilter_OnlyRequiresEnabledAccountsUnlessIncludeDisabled()
    {
        string listedOnly = DirectoryQuery.PeopleFilter([], includeDisabled: false);
        string everyone = DirectoryQuery.PeopleFilter([], includeDisabled: true);

        Assert.Equal("&(objectCategory=person)" + DirectoryQuery.EnabledUserFilter, listedOnly);
        Assert.Contains("(!(userAccountControl:1.2.840.113556.1.4.803:=2))", listedOnly);
        Assert.Equal("&(objectCategory=person)", everyone);
    }

    [Fact]
    public void PeopleFilter_InjectionAttempt_StaysOneEscapedValue()
    {
        string filter = DirectoryQuery.PeopleFilter(["*)(objectClass=*"], includeDisabled: true);

        Assert.Contains("(displayName=*\\2a\\29\\28objectClass=\\2a*)", filter);
        // Only the parentheses the builder writes itself: the category, the OR group, seven assertions.
        Assert.Equal(9, filter.Count(c => c == '('));
        Assert.Equal(9, filter.Count(c => c == ')'));
    }

    [Fact]
    public void PeopleFilter_ForGroupListing_AddsMembershipByGuid()
    {
        string filter = DirectoryQuery.PeopleFilter([], includeDisabled: true, memberOfGuid: "0123456789abcdef0123456789abcdef");

        Assert.Equal("&(objectCategory=person)(memberOf=<GUID=0123456789abcdef0123456789abcdef>)", filter);
        Assert.Throws<ArgumentException>(() => DirectoryQuery.PeopleFilter([], true, "0123)(cn=*"));
    }

    [Fact]
    public void PeopleFilter_DisplayNameMode_MatchesEveryTermInTheDisplayNameOnly()
    {
        string filter = DirectoryQuery.PeopleFilter(["Smith*", "jo"], includeDisabled: true, mode: DirectorySearchMode.DisplayName);

        Assert.Equal("&(objectCategory=person)(|(displayName=*Smith\\2a*))(|(displayName=*jo*))", filter);
    }

    [Theory]
    [InlineData(false, "&(objectCategory=person)(!(userAccountControl:1.2.840.113556.1.4.803:=2))(givenName=*)")]
    [InlineData(true, "&(objectCategory=person)(givenName=*)")]
    public void PeopleFilter_AddsTheApplicationFilterAsGiven(bool includeDisabled, string expected)
    {
        // Whether a filter applies to an includeDisabled search is the lookup's decision, not the builder's.
        Assert.Equal(expected, DirectoryQuery.PeopleFilter([], includeDisabled, additionalFilter: "(givenName=*)"));
    }

    [Fact]
    public void GroupNameFilter_RequiresEveryTermInTheName()
    {
        Assert.Equal("&(cn=*EXAMPLE*)(cn=*Staff\\2a*)", DirectoryQuery.GroupNameFilter(["EXAMPLE", "Staff*"]));
    }

    [Fact]
    public void EscapeBinary_WritesTwoHexDigitsPerByte()
    {
        Assert.Equal("\\01\\05\\00\\ff", DirectoryQuery.EscapeBinary([0x01, 0x05, 0x00, 0xff]));
    }

    [Theory]
    [InlineData("CN=Directory Admins,OU=Groups,DC=example,DC=com", "Directory Admins")]
    [InlineData("CN=Domain Admins,CN=Users,DC=example,DC=com", "Domain Admins")]
    [InlineData("CN=Smith\\, John,OU=Staff,DC=example,DC=com", "Smith, John")]
    [InlineData("CN=Ren\\C3\\A9 Smith,OU=Staff,DC=example,DC=com", "René Smith")]
    [InlineData("cn=lower,DC=example", "lower")]
    [InlineData("OU=Staff,DC=example,DC=com", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void CommonName_ReadsTheFirstComponentUnescaped(string? distinguishedName, string? expected)
    {
        Assert.Equal(expected, DirectoryQuery.CommonName(distinguishedName));
    }

    [Theory]
    [InlineData("CN=jsmith,OU=People,OU=ACCOUNTS,DC=example,DC=com", "EXAMPLE")]
    [InlineData("CN=jsmith,CN=Users", null)]
    [InlineData(null, null)]
    public void DomainLabel_IsTheFirstDcComponent(string? distinguishedName, string? expected)
    {
        Assert.Equal(expected, DirectoryQuery.DomainLabel(distinguishedName));
    }
}
