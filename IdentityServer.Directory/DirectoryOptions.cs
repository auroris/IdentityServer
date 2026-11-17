namespace IdentityServer.Directory;

/// <summary>
/// Settings for directory lookups, bound from the <c>Directory</c> configuration section.
/// A root is the starting location of a search in the directory tree. These roots control which
/// records the lookup can find; they do not grant the process account permission to read them.
/// </summary>
public sealed class DirectoryOptions
{
    /// <summary>
    /// The configuration section applications conventionally bind these options from. It is a
    /// top-level section, separate from the identity server's <c>IdentityServer:Directory</c>.
    /// </summary>
    public const string SectionName = "Directory";

    /// <summary>
    /// The LDAP path searched for people in local scope,
    /// e.g. <c>LDAP://OU=People,DC=EXAMPLE,DC=COM</c>. Empty searches the whole domain.
    /// </summary>
    public string? UserRoot { get; set; }

    /// <summary>
    /// The LDAP path searched for people in global scope and for single-account lookups
    /// (across organizational units). Empty searches the whole domain.
    /// </summary>
    public string? UserRootGlobal { get; set; }

    /// <summary>The LDAP path searched for groups. Empty searches the whole domain.</summary>
    public string? GroupRoot { get; set; }

    /// <summary>
    /// The shortest term a global people search accepts, so a one-letter query cannot sweep the
    /// whole directory. Values below 1 mean 2.
    /// </summary>
    public int GlobalSearchMinLength { get; set; } = 2;

    /// <summary>
    /// How many more entries than requested a people search asks the domain controller for, so
    /// accounts the listing rules drop after the query are less likely to leave the page short.
    /// Despite the name, this buffer applies to both local and global searches. It is bounded
    /// over-fetching, not pagination, so enough excluded accounts can still leave fewer results.
    /// </summary>
    public int GlobalSearchSizeLimitBuffer { get; set; } = 20;

    /// <summary>Results a search returns when the caller does not say how many it wants.</summary>
    public int DefaultTake { get; set; } = 20;

    /// <summary>The most results a search returns, whatever the caller asks for.</summary>
    public int MaxTake { get; set; } = 50;

    /// <summary>
    /// The most members a group lookup or a group listing returns. A group listing shows the
    /// whole group rather than a page of search results, so this is also its default size.
    /// </summary>
    public int MaxGroupMembers { get; set; } = 500;
}

/// <summary>
/// Which configured root a people search uses. Local/global describe search coverage, not whether
/// a user is a local Windows account or a domain account; both scopes query Active Directory.
/// </summary>
public enum DirectoryScope
{
    /// <summary>The local organizational unit, <see cref="DirectoryOptions.UserRoot"/>.</summary>
    Local,

    /// <summary>
    /// The broader configured root, <see cref="DirectoryOptions.UserRootGlobal"/>; this need not
    /// cover the whole domain or forest. Every term must meet
    /// <see cref="DirectoryOptions.GlobalSearchMinLength"/>.
    /// </summary>
    Global,
}

/// <summary>Which attributes the terms of a people search are matched against.</summary>
public enum DirectorySearchMode
{
    /// <summary>
    /// The address-book fields: display name, given name, surname, user name, email, telephone
    /// number and employee ID. Every term must match at least one of them.
    /// </summary>
    AddressBook,

    /// <summary>
    /// The display name alone: every term must appear somewhere in <c>displayName</c>. For pickers
    /// whose people should be found only by the name shown to the user.
    /// </summary>
    DisplayName,
}
