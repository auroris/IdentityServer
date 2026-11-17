using System.Security.Principal;

namespace IdentityServer.Directory.Tests;

/// <summary>Parsing lookup identifiers and turning them into escaped LDAP assertions.</summary>
public class DirectoryIdTests
{
    [Fact]
    public void Sid_IsNormalizedAndMatchedAsBinary()
    {
        Platform.SkipUnlessWindows();
        DirectoryId id = DirectoryId.Parse("s-1-5-21-1004336348-1177238915-682003330-512", allowAccountName: true);

        Assert.Equal(DirectoryIdKind.Sid, id.Kind);
        Assert.Equal("S-1-5-21-1004336348-1177238915-682003330-512", id.Value);

        var sid = new SecurityIdentifier(id.Value);
        byte[] bytes = new byte[sid.BinaryLength];
        sid.GetBinaryForm(bytes, 0);
        Assert.Equal("(objectSid=" + DirectoryQuery.EscapeBinary(bytes) + ")", id.Assertion());
        Assert.StartsWith("(objectSid=\\01\\05\\00\\00\\00\\00\\00\\05\\15\\00\\00\\00", id.Assertion());
    }

    [Fact]
    public void Sid_AssertionMatchesTheDirectoryWireFormat()
    {
        Platform.SkipUnlessWindows();
        DirectoryId id = DirectoryId.Parse("s-1-5-21-1-2-3-1001", allowAccountName: true);

        Assert.Equal("S-1-5-21-1-2-3-1001", id.Value);
        // Revision 1, five sub-authorities, authority 5, then each sub-authority little-endian.
        Assert.Equal(@"(objectSid=\01\05\00\00\00\00\00\05\15\00\00\00\01\00\00\00\02\00\00\00\03\00\00\00\e9\03\00\00)", id.Assertion());
    }

    [Theory]
    [InlineData("d0b6f8d2-3c4e-4f5a-8b9c-0d1e2f3a4b5c")]
    [InlineData("{D0B6F8D2-3C4E-4F5A-8B9C-0D1E2F3A4B5C}")]
    public void Guid_IsNormalizedAndMatchedAsDirectoryBytes(string value)
    {
        DirectoryId id = DirectoryId.Parse(value, allowAccountName: false);

        Assert.Equal(DirectoryIdKind.Guid, id.Kind);
        Assert.Equal("d0b6f8d2-3c4e-4f5a-8b9c-0d1e2f3a4b5c", id.Value);
        // objectGUID stores the first three fields little-endian, as Guid.ToByteArray does.
        Assert.Equal("(objectguid=\\d2\\f8\\b6\\d0\\4e\\3c\\5a\\4f\\8b\\9c\\0d\\1e\\2f\\3a\\4b\\5c)", id.Assertion());
    }

    [Fact]
    public void Guid_WithoutDashes_IsRefusedAsAmbiguous()
    {
        // The previous directory implementation returned 32 hex digits in directory byte order; .NET reads them in another.
        Assert.False(DirectoryId.TryParse("d0b6f8d23c4e4f5a8b9c0d1e2f3a4b5c", allowAccountName: true, out _));
    }

    [Theory]
    [InlineData(@"EXAMPLE\jsmith")]
    [InlineData("EXAMPLE/jsmith")]
    public void AccountName_SplitsDomainAndUser(string value)
    {
        DirectoryId id = DirectoryId.Parse(value, allowAccountName: true);

        Assert.Equal(DirectoryIdKind.AccountName, id.Kind);
        Assert.Equal("EXAMPLE", id.Domain);
        Assert.Equal("jsmith", id.Value);
        Assert.Equal("(sAMAccountName=jsmith)", id.Assertion());
    }

    [Fact]
    public void AccountName_UserPartIsEscaped()
    {
        Assert.Equal("(sAMAccountName=j\\2a\\29smith)", DirectoryId.Parse(@"EXAMPLE\j*)smith", allowAccountName: true).Assertion());
    }

    [Theory]
    [InlineData(@"EXAMPLE\jsmith")]
    [InlineData("Directory Admins")]
    public void Groups_AcceptOnlySidsAndGuids(string value)
    {
        Assert.Throws<DirectoryQueryException>(() => DirectoryId.Parse(value, allowAccountName: false));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("jsmith")]
    [InlineData(@"\jsmith")]
    [InlineData(@"EXAMPLE\")]
    [InlineData(@"A\B\C")]
    [InlineData("S-1-5-21-x")]
    [InlineData("*")]
    public void Malformed_IsRefused(string value)
    {
        Assert.Throws<DirectoryQueryException>(() => DirectoryId.Parse(value, allowAccountName: true));
    }
}
