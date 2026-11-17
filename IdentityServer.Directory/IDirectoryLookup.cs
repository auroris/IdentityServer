using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace IdentityServer.Directory;

/// <summary>
/// Read-only queries against the organization's directory: the people search behind address
/// books and people pickers, single-account lookups, and groups.
/// </summary>
/// <remarks>
/// Implementations escape every value they put in a query and bound every result set. A request
/// that cannot be run as asked throws <see cref="DirectoryQueryException"/>; an unreachable
/// directory throws <see cref="DirectoryUnavailableException"/>.
/// </remarks>
public interface IDirectoryLookup
{
    /// <summary>
    /// Address-book search for people. The query splits into terms on spaces and commas, and an
    /// account matches when every term matches one of its display name, given name, surname, user
    /// name, email, telephone number or employee ID (or, with
    /// <see cref="DirectorySearchMode.DisplayName"/>, its display name alone). Unless
    /// <paramref name="includeDisabled"/>, only enabled accounts are returned. Results are sorted by
    /// display name.
    /// </summary>
    /// <remarks>
    /// Without <paramref name="filter"/>, the application's registered <see cref="IDirectoryUserFilter"/>,
    /// if any, also applies unless <paramref name="includeDisabled"/>. A <paramref name="filter"/>
    /// replaces it for this call and always applies.
    /// </remarks>
    /// <param name="query">The search terms. No terms lists the local scope; global scope requires one.</param>
    /// <param name="take">How many results to return; null or less than 1 means <see cref="DirectoryOptions.DefaultTake"/>, at most <see cref="DirectoryOptions.MaxTake"/>.</param>
    /// <param name="scope">The local organizational unit or the whole directory.</param>
    /// <param name="includeDisabled">Include disabled accounts and bypass the registered filter.</param>
    /// <param name="cancellationToken">Cancels the request before it reaches the directory.</param>
    /// <param name="filter">
    /// This caller's trusted LDAP assertions, extra attributes and result predicate; its
    /// <see cref="IDirectoryUserFilter.AdditionalProperties"/> are returned in <see cref="DirectoryUser.Attributes"/>.
    /// Null uses the registered filter.
    /// </param>
    /// <param name="mode">Which attributes the terms are matched against.</param>
    Task<IReadOnlyList<DirectoryUser>> SearchUsersAsync(string? query, int? take = null, DirectoryScope scope = DirectoryScope.Local, bool includeDisabled = false, CancellationToken cancellationToken = default,
        IDirectoryUserFilter? filter = null, DirectorySearchMode mode = DirectorySearchMode.AddressBook);

    /// <summary>
    /// Looks up one account, regardless of its enabled state or application-specific filters, with the
    /// groups it belongs to directly. Searches <see cref="DirectoryOptions.UserRootGlobal"/>.
    /// </summary>
    /// <param name="id">A SID, an objectGUID (36 characters), or <c>DOMAIN\user</c>.</param>
    /// <param name="cancellationToken">Cancels the request before it reaches the directory.</param>
    /// <param name="additionalAttributes">
    /// Further attributes to return in <see cref="DirectoryUser.Attributes"/>, by LDAP name. Trusted
    /// application input: the process account reads them, whoever the caller is.
    /// </param>
    /// <returns>The account, or null when there is none.</returns>
    Task<DirectoryUserDetail?> FindUserAsync(string id, CancellationToken cancellationToken = default, IReadOnlyList<string>? additionalAttributes = null);

    /// <summary>The common names of the groups an account belongs to directly, sorted.</summary>
    /// <param name="id">A SID, an objectGUID (36 characters), or <c>DOMAIN\user</c>.</param>
    /// <param name="cancellationToken">Cancels the request before it reaches the directory.</param>
    /// <returns>The group names, or null when there is no such account.</returns>
    Task<IReadOnlyList<string>?> GetUserGroupsAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Searches groups under <see cref="DirectoryOptions.GroupRoot"/> whose common name contains
    /// every term of the query, sorted by name.
    /// </summary>
    /// <param name="query">The search terms; at least one is required.</param>
    /// <param name="take">How many results to return, as for <see cref="SearchUsersAsync"/>.</param>
    /// <param name="cancellationToken">Cancels the request before it reaches the directory.</param>
    Task<IReadOnlyList<DirectoryGroup>> SearchGroupsAsync(string? query, int? take = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Looks up one group under <see cref="DirectoryOptions.GroupRoot"/> with its direct members,
    /// at most <see cref="DirectoryOptions.MaxGroupMembers"/> of them.
    /// </summary>
    /// <param name="id">A SID or an objectGUID (36 characters).</param>
    /// <param name="cancellationToken">Cancels the request before it reaches the directory.</param>
    /// <returns>The group, or null when there is none.</returns>
    Task<DirectoryGroupDetail?> FindGroupAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>
    /// The people who belong directly to a group and pass the enabled-account check and the
    /// registered <see cref="IDirectoryUserFilter"/>, if any, optionally narrowed by search terms,
    /// sorted by display name.
    /// </summary>
    /// <param name="group">The group's common name (exact), SID or objectGUID.</param>
    /// <param name="query">Optional terms every listed member must match, as for <see cref="SearchUsersAsync"/>.</param>
    /// <param name="take">How many members to return; null or less than 1 means <see cref="DirectoryOptions.MaxGroupMembers"/>, which is also the maximum.</param>
    /// <param name="scope">Whether members are looked for in the local organizational unit or the whole directory.</param>
    /// <param name="includeDisabled">Lift the listing rules: include disabled accounts and bypass the registered filter.</param>
    /// <param name="cancellationToken">Cancels the request before it reaches the directory.</param>
    /// <returns>The group and its members, or null when there is no such group.</returns>
    Task<DirectoryGroupListing?> ListGroupMembersAsync(string group, string? query = null, int? take = null, DirectoryScope scope = DirectoryScope.Local, bool includeDisabled = false, CancellationToken cancellationToken = default);
}
