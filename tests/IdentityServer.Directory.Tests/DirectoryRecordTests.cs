using System.Text.Json;

namespace IdentityServer.Directory.Tests;

/// <summary>The records' camelCase JSON contract, which directory APIs and their clients share.</summary>
public class DirectoryRecordTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private static readonly DirectoryUser Adams = new()
    {
        Sid = "S-1-5-21-1-2-3-1104",
        Guid = "00000000-0000-0000-0000-000000001104",
        AccountName = @"EXAMPLE\zadams",
        UserName = "zadams",
        DisplayName = "Zed Adams",
        GivenName = "Zed",
        Surname = "Adams",
        Email = "zadams@example.com",
        Telephone = "555-010-1104",
        Title = "Analyst",
        Department = "Operations",
        Enabled = true,
    };

    [Fact]
    public void UserDetail_RoundTripsThroughCamelCaseJson()
    {
        var detail = new DirectoryUserDetail(Adams, ["Directory Admins"]);

        string json = JsonSerializer.Serialize(detail, Web);
        DirectoryUserDetail? back = JsonSerializer.Deserialize<DirectoryUserDetail>(json, Web);

        using JsonDocument document = JsonDocument.Parse(json);
        string[] names = document.RootElement.EnumerateObject().Select(property => property.Name).ToArray();
        string[] expected =
        [
            "sid", "guid", "accountName", "userName", "displayName", "givenName", "surname", "email", "telephone",
            "mobile", "fax", "office", "title", "department", "employeeId", "enabled", "groups",
        ];
        Assert.Equal(expected.Order(), names.Order());
        Assert.NotNull(back);
        Assert.Equal(detail.AccountName, back.AccountName);
        Assert.Equal(detail.Groups, back.Groups);
    }

    [Fact]
    public void User_WithoutRequestedAttributes_IsUnchangedByAJsonRoundTrip()
    {
        // Attributes stays null and out of the JSON, so records still compare equal after a round trip.
        DirectoryUser? back = JsonSerializer.Deserialize<DirectoryUser>(JsonSerializer.Serialize(Adams, Web), Web);

        Assert.Equal(Adams, back);
        Assert.Null(back!.Attributes);
    }

    [Fact]
    public void User_RequestedAttributes_RoundTripAndReadIgnoringCase()
    {
        DirectoryUser user = Adams with
        {
            Attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["extensionAttribute1"] = "A-1" },
        };

        string json = JsonSerializer.Serialize(user, Web);
        DirectoryUser? back = JsonSerializer.Deserialize<DirectoryUser>(json, Web);

        Assert.Contains("\"attributes\":{\"extensionAttribute1\":\"A-1\"}", json);
        Assert.Equal("A-1", back?.Attributes?["extensionAttribute1"]);
        Assert.Equal("A-1", user.Attributes?.GetValueOrDefault("EXTENSIONATTRIBUTE1"));
    }

    [Fact]
    public void User_MissingARequiredProperty_DoesNotDeserialize()
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<DirectoryUser>("""{"sid":"S-1-5-21-1-2-3-4"}""", Web));
    }

    [Fact]
    public void GroupDetail_RoundTripsThroughCamelCaseJson()
    {
        var group = new DirectoryGroup { Sid = "S-1-5-21-1-2-3-2001", Guid = "2b7a5d3e-0f1c-4b8a-9d6e-5c4b3a291807", Name = "Help Desk" };
        var detail = new DirectoryGroupDetail(group)
        {
            MemberUsers = [new DirectoryMember { Guid = Adams.Guid, Sid = Adams.Sid, Name = Adams.DisplayName, Type = DirectoryMember.UserType }],
            MembersTruncated = true,
        };

        string json = JsonSerializer.Serialize(detail, Web);
        DirectoryGroupDetail? back = JsonSerializer.Deserialize<DirectoryGroupDetail>(json, Web);

        Assert.Contains("\"memberUsers\":[{", json);
        Assert.Contains("\"membersTruncated\":true", json);
        Assert.NotNull(back);
        Assert.Equal("Help Desk", back.Name);
        Assert.Equal(DirectoryMember.UserType, Assert.Single(back.MemberUsers).Type);
    }
}
