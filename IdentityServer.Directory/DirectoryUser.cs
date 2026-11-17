using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace IdentityServer.Directory;

/// <summary>
/// A person in Active Directory as the lookup returns them: what an address book or a people
/// picker needs. Directory HTTP APIs serialize it camelCase and other applications deserialize it
/// into this same record, so the property names and types are a contract; add to it, do not rename.
/// </summary>
/// <remarks>
/// Optional attributes are <see langword="null"/> when the account has no value, never empty
/// strings, so a consumer can use <c>??</c> fallbacks directly.
/// </remarks>
public record DirectoryUser
{
    /// <summary>
    /// The account's security identifier (<c>objectSid</c>), e.g. <c>S-1-5-21-…</c>. It is the
    /// stable key: the identity server issues it as the <c>sub</c> claim.
    /// </summary>
    public required string Sid { get; init; }

    /// <summary>
    /// The account's <c>objectGUID</c> in the standard 36-character form, as PowerShell shows it.
    /// <c>Guid.Parse(value).ToByteArray()</c> gives the raw attribute bytes.
    /// </summary>
    public required string Guid { get; init; }

    /// <summary>The Windows account name, <c>DOMAIN\user</c>, as it appears in a logon token.</summary>
    public required string AccountName { get; init; }

    /// <summary>The pre-Windows 2000 logon name (<c>sAMAccountName</c>), without the domain.</summary>
    public required string UserName { get; init; }

    /// <summary>The display name (<c>displayName</c>), falling back to the common name, then the user name.</summary>
    public required string DisplayName { get; init; }

    /// <summary>The given name (<c>givenName</c>).</summary>
    public string? GivenName { get; init; }

    /// <summary>The surname (<c>sn</c>).</summary>
    public string? Surname { get; init; }

    /// <summary>The email address (<c>mail</c>).</summary>
    public string? Email { get; init; }

    /// <summary>The work telephone number (<c>telephoneNumber</c>).</summary>
    public string? Telephone { get; init; }

    /// <summary>The mobile number (<c>mobile</c>).</summary>
    public string? Mobile { get; init; }

    /// <summary>The fax number (<c>facsimileTelephoneNumber</c>).</summary>
    public string? Fax { get; init; }

    /// <summary>The office (<c>physicalDeliveryOfficeName</c>).</summary>
    public string? Office { get; init; }

    /// <summary>The job title (<c>title</c>).</summary>
    public string? Title { get; init; }

    /// <summary>The department (<c>department</c>).</summary>
    public string? Department { get; init; }

    /// <summary>The employee ID (<c>employeeID</c>).</summary>
    public string? EmployeeId { get; init; }

    /// <summary>Whether the account is enabled: the <c>ACCOUNTDISABLE</c> bit of <c>userAccountControl</c> is clear.</summary>
    public bool Enabled { get; init; }

    /// <summary>
    /// Further attributes the caller asked for by name (the <see cref="IDirectoryUserFilter.AdditionalProperties"/>
    /// of a filter passed to a search, or the attributes passed to a single-account lookup), keyed by
    /// the requested name; the lookup's dictionaries compare keys case-insensitively, though one
    /// deserialized from JSON does not. Values are trimmed; an attribute the account has no
    /// value for is left out. <see langword="null"/> when the call asked for none, so records from
    /// callers that do not use it serialize, deserialize and compare as before.
    /// </summary>
    /// <remarks>
    /// A multi-valued attribute gives its first value; a value the directory does not return as a
    /// string (a number, a date, binary data) is left out. The attributes are read as the process
    /// account sees them, so request only attributes the caller is entitled to show.
    /// </remarks>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyDictionary<string, string>? Attributes { get; init; }
}

/// <summary>A <see cref="DirectoryUser"/> with the groups the account belongs to directly.</summary>
public sealed record DirectoryUserDetail : DirectoryUser
{
    /// <summary>Creates an empty detail record; used by serializers and object initializers.</summary>
    public DirectoryUserDetail()
    {
    }

    /// <summary>Creates a detail record from a summary and its groups.</summary>
    /// <param name="user">The account's summary record; every property is copied.</param>
    /// <param name="groups">The account's direct groups, bare common names.</param>
    [SetsRequiredMembers]
    public DirectoryUserDetail(DirectoryUser user, IReadOnlyList<string> groups)
        : base(user)
    {
        Groups = groups;
    }

    /// <summary>
    /// The common names of the groups in the account's <c>memberOf</c>, sorted, without a domain
    /// prefix (<c>Directory Admins</c>, not <c>EXAMPLE\Directory Admins</c>). Nested memberships and the
    /// primary group (normally Domain Users) are not listed, matching the identity server's
    /// <c>role</c> claims.
    /// </summary>
    public IReadOnlyList<string> Groups { get; init; } = [];
}
