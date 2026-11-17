using System.Collections.Generic;
using ActiveDirectory;

namespace IdentityServer.Directory;

/// <summary>
/// Application-specific rules for people searches: which accounts an address book or a people
/// picker lists, decided from standard or custom attributes. A filter is used in one of two ways.
/// </summary>
/// <remarks>
/// <para>
/// <b>Registered with the lookup</b> (the <c>userFilter</c> argument of
/// <see cref="ActiveDirectoryLookup"/>'s constructor), it is the application's default listing
/// rule for people searches and group listings. Requests with <c>includeDisabled</c> set bypass it,
/// as they bypass the enabled-account check, and its <see cref="AdditionalProperties"/> are fetched
/// for <see cref="IsIncluded"/> only: they are not copied into <see cref="DirectoryUser.Attributes"/>.
/// </para>
/// <para>
/// <b>Passed to one search</b> (the <c>filter</c> argument of
/// <see cref="IDirectoryLookup.SearchUsersAsync"/>), it replaces the registered filter for that
/// call and always applies, whatever <c>includeDisabled</c> says; its
/// <see cref="AdditionalProperties"/> are returned in <see cref="DirectoryUser.Attributes"/>.
/// </para>
/// <para>
/// Single-account lookups never apply a filter. Filters are trusted application code, never request
/// input, and an instance shared between requests must be safe to use concurrently.
/// </para>
/// </remarks>
public interface IDirectoryUserFilter
{
    /// <summary>
    /// Extra attributes the search must fetch, by LDAP name, for <see cref="IsIncluded"/> to read with
    /// <see cref="ADObject.GetProperty(string)"/> (for example a custom <c>extensionAttribute</c>). An
    /// attribute that is not fetched reads as empty.
    /// </summary>
    IReadOnlyList<string> AdditionalProperties { get; }

    /// <summary>
    /// Zero or more parenthesized LDAP assertions combined with the search's own filter, such as
    /// <c>(givenName=*)(!(description=service*))</c>. This is trusted application code, not request
    /// input; escape any dynamic assertion values. Filtering at the directory keeps excluded accounts
    /// from filling the result limit.
    /// </summary>
    string LdapFilter { get; }

    /// <summary>Applies any remaining rules to a search result, using its prefetched attributes.</summary>
    /// <param name="user">An account returned by the directory search.</param>
    bool IsIncluded(ADUser user);
}
