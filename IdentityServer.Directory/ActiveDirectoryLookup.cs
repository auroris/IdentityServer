using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using ActiveDirectory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using static ActiveDirectory.ADObject;

namespace IdentityServer.Directory;

/// <summary>
/// <see cref="IDirectoryLookup"/> over Active Directory, through the shared <c>ActiveDirectory</c>
/// library. Queries run as the process account (the application pool identity under IIS), which
/// needs read access to the configured roots, on a domain-joined server.
/// </summary>
/// <remarks>
/// System.DirectoryServices is synchronous, so each method has done its work by the time it
/// returns; the methods are asynchronous in shape so that callers and test doubles do not depend
/// on that. Register it as a singleton: it caches domain names.
/// </remarks>
public sealed class ActiveDirectoryLookup : IDirectoryLookup
{
    private static readonly StringComparer NameComparer = StringComparer.InvariantCultureIgnoreCase;

    // Fetch the fields needed to build DirectoryUser in one search. Reading a cached field that
    // was not requested looks like a missing value; binding every result to fetch it later would
    // add a network connection per entry. Filters and callers name any extra fields they need.
    private static readonly string[] UserProperties =
    [
        ADProperties.ObjectSid,
        ADProperties.CommonName,
        ADProperties.DistinguishedName,
        ADProperties.UserAccountControl,
        ADProperties.User.SamAccountName,
        ADProperties.User.DisplayName,
        ADProperties.User.GivenName,
        ADProperties.User.Surname,
        ADProperties.User.Email,
        ADProperties.User.TelephoneNumber,
        ADProperties.User.MobilePhone,
        ADProperties.User.FaxNumber,
        ADProperties.User.Office,
        ADProperties.User.Title,
        ADProperties.User.Department,
        ADProperties.User.EmployeeID,
    ];

    private static readonly string[] GroupProperties =
    [
        ADProperties.ObjectSid,
        ADProperties.CommonName,
        ADProperties.Group.Description,
    ];

    private static readonly string[] MemberProperties =
    [
        ADProperties.ObjectSid,
        ADProperties.CommonName,
        ADProperties.User.DisplayName,
        ADProperties.User.SamAccountName,
    ];

    // An unpaged search stops at the domain controller's MaxPageSize, 1000 unless changed.
    private const int ServerPageSize = 1000;

    private readonly IOptionsMonitor<DirectoryOptions> _options;
    private readonly ILogger<ActiveDirectoryLookup> _logger;
    private readonly IDirectoryUserFilter? _userFilter;

    // NetBIOS domain names by domain SID; see DomainOf.
    private readonly ConcurrentDictionary<string, string> _domainNames = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Creates the lookup.</summary>
    /// <param name="options">The <c>Directory</c> settings; changes apply to the next query.</param>
    /// <param name="logger">Receives query counts/timings at Debug level and directory failures at Error level.</param>
    /// <param name="userFilter">
    /// Optional application rules for people searches and group listings, in addition to the
    /// enabled-account check; see <see cref="IDirectoryUserFilter"/>.
    /// </param>
    public ActiveDirectoryLookup(IOptionsMonitor<DirectoryOptions> options, ILogger<ActiveDirectoryLookup>? logger = null, IDirectoryUserFilter? userFilter = null)
    {
        _options = options;
        _logger = logger ?? NullLogger<ActiveDirectoryLookup>.Instance;
        _userFilter = userFilter;
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<DirectoryUser>> SearchUsersAsync(string? query, int? take = null, DirectoryScope scope = DirectoryScope.Local, bool includeDisabled = false, CancellationToken cancellationToken = default,
        IDirectoryUserFilter? filter = null, DirectorySearchMode mode = DirectorySearchMode.AddressBook)
    {
        cancellationToken.ThrowIfCancellationRequested();
        DirectoryOptions options = _options.CurrentValue;
        IReadOnlyList<string> terms = DirectoryQuery.Tokenize(query);
        if (scope == DirectoryScope.Global)
            DirectoryQuery.EnsureGlobalTerms(terms, options.GlobalSearchMinLength);
        int limit = DirectoryQuery.ClampTake(take, options.DefaultTake, options.MaxTake);

        return Task.FromResult(Run(() => SearchPeople(options, terms, limit, scope, includeDisabled, memberOfGuid: null, filter, mode)));
    }

    /// <inheritdoc/>
    public Task<DirectoryUserDetail?> FindUserAsync(string id, CancellationToken cancellationToken = default, IReadOnlyList<string>? additionalAttributes = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        DirectoryId key = DirectoryId.Parse(id, allowAccountName: true);
        DirectoryOptions options = _options.CurrentValue;

        return Task.FromResult(Run(() =>
        {
            using ADUser? user = FindPerson(options, key, additionalAttributes);
            return user == null ? null : new DirectoryUserDetail(ToUser(user, additionalAttributes), GroupNames(user.Groups));
        }));
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<string>?> GetUserGroupsAsync(string id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        DirectoryId key = DirectoryId.Parse(id, allowAccountName: true);
        DirectoryOptions options = _options.CurrentValue;

        return Task.FromResult(Run(() =>
        {
            using ADUser? user = FindPerson(options, key, additionalAttributes: null);
            return user == null ? null : GroupNames(user.Groups);
        }));
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<DirectoryGroup>> SearchGroupsAsync(string? query, int? take = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        DirectoryOptions options = _options.CurrentValue;
        IReadOnlyList<string> terms = DirectoryQuery.Tokenize(query);
        if (terms.Count == 0)
            throw new DirectoryQueryException("A search term is required to search groups.");
        int limit = DirectoryQuery.ClampTake(take, options.DefaultTake, options.MaxTake);

        return Task.FromResult(Run<IReadOnlyList<DirectoryGroup>>(() =>
        {
            var search = new DirectorySearch(Root(options.GroupRoot), ObjectClass.Group)
            {
                AdditionalFilter = DirectoryQuery.GroupNameFilter(terms),
            };
            search.PropertiesToLoad.AddRange(GroupProperties);
            List<ADGroup> found = Find<ADGroup>(search, limit, "group search", "Directory:GroupRoot");
            try
            {
                return found.Select(ToGroup).OrderBy(group => group.Name, NameComparer).ToList();
            }
            finally
            {
                DisposeAll(found);
            }
        }));
    }

    /// <inheritdoc/>
    public Task<DirectoryGroupDetail?> FindGroupAsync(string id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        DirectoryId key = DirectoryId.Parse(id, allowAccountName: false);
        DirectoryOptions options = _options.CurrentValue;

        return Task.FromResult(Run(() =>
        {
            using ADGroup? group = FindGroup(options, key.Assertion(), withModified: false);
            return group == null ? null : WithMembers(options, group);
        }));
    }

    /// <inheritdoc/>
    public Task<DirectoryGroupListing?> ListGroupMembersAsync(string group, string? query = null, int? take = null, DirectoryScope scope = DirectoryScope.Local, bool includeDisabled = false, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        DirectoryOptions options = _options.CurrentValue;
        string name = group?.Trim() ?? "";
        if (name.Length == 0)
            throw new DirectoryQueryException("A group is required.");
        if (name.Length > DirectoryQuery.MaxQueryLength)
            throw new DirectoryQueryException($"The group name is longer than {DirectoryQuery.MaxQueryLength} characters.");
        IReadOnlyList<string> terms = DirectoryQuery.Tokenize(query);
        if (scope == DirectoryScope.Global)
            DirectoryQuery.EnsureGlobalTerms(terms, options.GlobalSearchMinLength, allowEmpty: true);
        int max = Math.Max(1, options.MaxGroupMembers);
        int limit = DirectoryQuery.ClampTake(take, max, max);

        // Accept the group's common name as well as a SID or GUID.
        string assertion = DirectoryId.TryParse(name, allowAccountName: false, out DirectoryId key)
            ? key.Assertion()
            : DirectoryQuery.Assertion(ADProperties.CommonName, name, DirectorySearch.MatchType.Exact);

        return Task.FromResult(Run(() =>
        {
            using ADGroup? found = FindGroup(options, assertion, withModified: true);
            if (found == null) return null;
            return new DirectoryGroupListing
            {
                Group = ToGroup(found),
                Modified = ModifiedOf(found),
                Members = SearchPeople(options, terms, limit, scope, includeDisabled, memberOfGuid: found.ADGuid.ToString(), filter: null, DirectorySearchMode.AddressBook),
            };
        }));
    }

    /// <summary>
    /// The rules a people search applies: the caller's own filter, which always applies, or else the
    /// registered one, which <paramref name="includeDisabled"/> lifts.
    /// </summary>
    private IDirectoryUserFilter? RulesFor(IDirectoryUserFilter? filter, bool includeDisabled) =>
        filter ?? (includeDisabled ? null : _userFilter);

    internal DirectorySearch CreatePeopleSearch(DirectoryOptions options, IReadOnlyList<string> terms, DirectoryScope scope, bool includeDisabled, string? memberOfGuid = null,
        IDirectoryUserFilter? filter = null, DirectorySearchMode mode = DirectorySearchMode.AddressBook)
    {
        IDirectoryUserFilter? rules = RulesFor(filter, includeDisabled);
        string? root = Root(scope == DirectoryScope.Global ? options.UserRootGlobal : options.UserRoot);
        var search = new DirectorySearch(root, ObjectClass.User)
        {
            AdditionalFilter = DirectoryQuery.PeopleFilter(terms, includeDisabled, memberOfGuid, rules?.LdapFilter, mode),
        };
        search.PropertiesToLoad.AddRange(UserProperties);
        if (rules != null)
            search.PropertiesToLoad.AddRange(rules.AdditionalProperties);
        return search;
    }

    private IReadOnlyList<DirectoryUser> SearchPeople(DirectoryOptions options, IReadOnlyList<string> terms, int limit, DirectoryScope scope, bool includeDisabled, string? memberOfGuid,
        IDirectoryUserFilter? filter, DirectorySearchMode mode)
    {
        IDirectoryUserFilter? rules = RulesFor(filter, includeDisabled);
        var search = CreatePeopleSearch(options, terms, scope, includeDisabled, memberOfGuid, filter, mode);

        // Fetch a bounded buffer because application rules can discard entries after the LDAP query.
        // This is not pagination: if many entries are excluded, fewer than limit may still remain.
        int sizeLimit = limit + Math.Max(0, options.GlobalSearchSizeLimitBuffer);
        if (sizeLimit > ServerPageSize)
            search.PageSize = ServerPageSize;

        List<ADUser> found = scope == DirectoryScope.Global
            ? Find<ADUser>(search, sizeLimit, "global people search", "Directory:UserRootGlobal")
            : Find<ADUser>(search, sizeLimit, "local people search", "Directory:UserRoot");
        try
        {
            // Only a caller's own filter names attributes to return; a registered filter's are for its
            // decision alone, so they never reach callers that did not ask for them.
            IReadOnlyList<string>? returned = filter?.AdditionalProperties;
            var users = found
                .Where(user => (includeDisabled || !user.IsDisabled) && (rules?.IsIncluded(user) ?? true))
                .Select(user => ToUser(user, returned))
                .OrderBy(user => user.DisplayName, NameComparer)
                .Take(limit)
                .ToList();
            _logger.LogDebug("Directory people search in {Scope} scope returned {Count} users from {Fetched} fetched entries after filtering and applying the result limit {Limit} (includeDisabled: {IncludeDisabled}). Scope selects Directory:UserRoot or UserRootGlobal; excluded accounts and result limits can shorten the list.",
                scope, users.Count, found.Count, limit, includeDisabled);
            return users;
        }
        finally
        {
            DisposeAll(found);
        }
    }

    private ADUser? FindPerson(DirectoryOptions options, DirectoryId key, IReadOnlyList<string>? additionalAttributes)
    {
        // A picker may be limited to one organizational unit, but resolving a saved identity must
        // also find an account that has moved outside that unit. Use the broader configured root.
        var search = new DirectorySearch(Root(options.UserRootGlobal), ObjectClass.User)
        {
            AdditionalFilter = DirectoryQuery.All(["(" + ADProperties.ObjectCategory + "=person)", key.Assertion()]),
        };
        search.PropertiesToLoad.AddRange(UserProperties);
        search.PropertiesToLoad.Add(ADProperties.MemberOf);
        if (additionalAttributes != null)
            search.PropertiesToLoad.AddRange(additionalAttributes);

        ADUser? user = FindOne<ADUser>(search, "user lookup", "Directory:UserRootGlobal");
        if (user != null && key.Kind == DirectoryIdKind.AccountName
            && !string.Equals(DomainOf(user), key.Domain, StringComparison.OrdinalIgnoreCase))
        {
            // The same user name in another domain is another account.
            user.Dispose();
            return null;
        }
        return user;
    }

    private ADGroup? FindGroup(DirectoryOptions options, string assertion, bool withModified)
    {
        var search = new DirectorySearch(Root(options.GroupRoot), ObjectClass.Group)
        {
            AdditionalFilter = DirectoryQuery.All([assertion]),
        };
        search.PropertiesToLoad.AddRange(GroupProperties);
        if (withModified)
            search.PropertiesToLoad.Add(ADProperties.WhenChanged);
        return FindOne<ADGroup>(search, "group lookup", "Directory:GroupRoot");
    }

    private DirectoryGroupDetail WithMembers(DirectoryOptions options, ADGroup group)
    {
        int max = Math.Max(1, options.MaxGroupMembers);

        // Members can live anywhere in the domain, so this search is not held to a root. Asking
        // for one more than the maximum tells a complete list from a cut one.
        var search = new DirectorySearch(null, ObjectClass.Unknown)
        {
            AdditionalFilter = ADProperties.MemberOf + "=<GUID=" + group.ADGuid + ">",
        };
        search.PropertiesToLoad.AddRange(MemberProperties);
        if (max + 1 > ServerPageSize)
            search.PageSize = ServerPageSize;

        List<ADObject> found = Find<ADObject>(search, max + 1, "group member search", "the server's default domain");
        try
        {
            var users = new List<DirectoryMember>();
            var groups = new List<DirectoryMember>();
            var computers = new List<DirectoryMember>();
            foreach (ADObject member in found.Take(max))
            {
                switch (member)
                {
                    case ADComputer computer:
                        computers.Add(ToMember(computer, DirectoryMember.ComputerType, FirstNonEmpty(computer.Name, computer.GetProperty(ADProperties.User.SamAccountName))));
                        break;
                    case ADUser user:
                        users.Add(ToMember(user, DirectoryMember.UserType, FirstNonEmpty(user.DisplayName, user.Name, user.Username)));
                        break;
                    case ADGroup memberGroup:
                        groups.Add(ToMember(memberGroup, DirectoryMember.GroupType, FirstNonEmpty(memberGroup.Name, memberGroup.Windows2000Name)));
                        break;
                    // Contacts and foreign security principals are neither users, groups nor computers here.
                }
            }

            if (found.Count > max)
                _logger.LogDebug("Directory group details exceeded Directory:MaxGroupMembers ({MaxGroupMembers}). MembersTruncated is true because only part of the direct membership is returned; increase the setting if callers need a larger list.", max);

            return new DirectoryGroupDetail(ToGroup(group))
            {
                MemberUsers = Sorted(users),
                MemberGroups = Sorted(groups),
                MemberComputers = Sorted(computers),
                MembersTruncated = found.Count > max,
            };
        }
        finally
        {
            DisposeAll(found);
        }
    }

    private DirectoryUser ToUser(ADUser user, IReadOnlyList<string>? attributes)
    {
        string userName = user.Username;
        string? domain = DomainOf(user);
        return new DirectoryUser
        {
            Sid = user.Sid,
            Guid = GuidOf(user),
            AccountName = domain != null ? domain + "\\" + userName : userName,
            UserName = userName,
            DisplayName = FirstNonEmpty(user.DisplayName, user.Name, userName),
            GivenName = NonEmpty(user.GivenName),
            Surname = NonEmpty(user.Surname),
            Email = NonEmpty(user.Email),
            Telephone = NonEmpty(user.TelephoneNumber),
            Mobile = NonEmpty(user.MobilePhone),
            Fax = NonEmpty(user.FaxNumber),
            Office = NonEmpty(user.Office),
            Title = NonEmpty(user.Title),
            Department = NonEmpty(user.Department),
            EmployeeId = NonEmpty(user.EmployeeID),
            Enabled = !user.IsDisabled,
            Attributes = AttributesOf(user, attributes),
        };
    }

    /// <summary>
    /// The requested attributes that have a value, keyed by the requested name; null when none
    /// were requested, so <see cref="DirectoryUser.Attributes"/> stays out of the JSON.
    /// </summary>
    internal static IReadOnlyDictionary<string, string>? AttributesOf(ADObject entry, IReadOnlyList<string>? names)
    {
        if (names is null or { Count: 0 }) return null;
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string name in names)
        {
            if (string.IsNullOrWhiteSpace(name) || values.ContainsKey(name)) continue;
            if (NonEmpty(entry.GetProperty(name)) is { } value)
                values[name] = value;
        }
        return values;
    }

    private static DirectoryGroup ToGroup(ADGroup group) => new()
    {
        Sid = group.Sid,
        Guid = GuidOf(group),
        Name = group.Name,
        Description = NonEmpty(group.Description),
    };

    private static DirectoryMember ToMember(ADObject member, string type, string name) => new()
    {
        Guid = GuidOf(member),
        Sid = NonEmpty(member.Sid),
        Name = name,
        Type = type,
    };

    /// <summary>
    /// The NetBIOS name of an account's domain, the <c>DOMAIN</c> of <c>DOMAIN\user</c> in a logon
    /// token. The directory does not store it on the account, so Windows translates the SID of the
    /// first account seen from each domain and the answer is cached by domain SID. When that fails,
    /// the first <c>DC=</c> label of the distinguished name stands in; in most domains it is the
    /// same name.
    /// </summary>
    private string? DomainOf(ADObject account)
    {
        string? fallback = DirectoryQuery.DomainLabel(account.QualifiedName);
        string sid = account.Sid;
        if (sid.Length == 0) return fallback;

        string? domainSid;
        SecurityIdentifier identifier;
        try
        {
            identifier = new SecurityIdentifier(sid);
            domainSid = identifier.AccountDomainSid?.Value;
        }
        catch (ArgumentException)
        {
            return fallback;
        }
        if (domainSid == null) return fallback;
        if (_domainNames.TryGetValue(domainSid, out string? cached)) return cached;

        string? name = null;
        try
        {
            string accountName = identifier.Translate(typeof(NTAccount)).Value;
            int separator = accountName.IndexOf('\\');
            if (separator > 0) name = accountName[..separator];
        }
        catch (SystemException ex)
        {
            _logger.LogDebug(ex, "Could not translate Windows security identifier {Sid} to an account name; using the directory's domain label instead. This fallback can differ from the DOMAIN part of a Windows logon name, so check domain connectivity if account names appear incorrect.", sid);
        }

        name ??= fallback;
        if (name != null) _domainNames.TryAdd(domainSid, name);
        return name;
    }

    private static IReadOnlyList<string> GroupNames(IEnumerable<string> distinguishedNames) =>
        distinguishedNames
            .Select(DirectoryQuery.CommonName)
            .OfType<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, NameComparer)
            .ToList();

    private static IReadOnlyList<DirectoryMember> Sorted(List<DirectoryMember> members) =>
        members.OrderBy(member => member.Name, NameComparer).ToList();

    private static DateTimeOffset? ModifiedOf(ADObject entry)
    {
        try
        {
            // ADSI returns generalized times in UTC but leaves DateTime.Kind unspecified.
            return entry.WhenChanged is DateTime changed
                ? new DateTimeOffset(DateTime.SpecifyKind(changed, DateTimeKind.Utc))
                : null;
        }
        catch (InvalidOperationException)
        {
            // whenChanged was not in the result (the account cannot read it); the listing does not need it.
            return null;
        }
    }

    private List<T> Find<T>(DirectorySearch search, int sizeLimit, string purpose, string searchBase) where T : ADObject, new()
    {
        var stopwatch = Stopwatch.StartNew();
        // A group listing can query two different roots. Attribute a failed search to the one it actually used.
        List<T> found = Run(() => search.Find<T>(sizeLimit), purpose, searchBase);
        // Counts and timings explain query cost without recording people's search text or LDAP
        // filters, which can include names, email addresses and employee identifiers.
        _logger.LogDebug("Directory {Purpose} returned {Count} entries with size limit {SizeLimit} in {Elapsed} ms. This count is before application filtering; reaching the limit does not mean the entire directory was returned.",
            purpose, found.Count, sizeLimit, stopwatch.ElapsedMilliseconds);
        if (found.Count == 0)
            _logger.LogDebug("Directory {Purpose} completed with no matches. Check the search scope, configured Directory roots and filters; an empty result is not a connection failure.", purpose);
        return found;
    }

    private T? FindOne<T>(DirectorySearch search, string purpose, string searchBase) where T : ADObject, new()
    {
        List<T> found = Find<T>(search, 1, purpose, searchBase);
        return found.Count > 0 ? found[0] : null;
    }

    /// <summary>
    /// Runs a directory query, turning the failures System.DirectoryServices and the
    /// ActiveDirectory library report into <see cref="DirectoryUnavailableException"/>.
    /// </summary>
    private T Run<T>(Func<T> query, string purpose = "directory lookup", string? searchBase = null)
    {
        try
        {
            return query();
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or PlatformNotSupportedException or UnauthorizedAccessException)
        {
            // COMException includes DirectoryServicesCOMException: no domain controller, a bad
            // LDAP root, access denied. InvalidOperationException comes from the ActiveDirectory
            // library when the directory returns something it cannot type.
            // Keep a failed lookup distinct from a successful empty result: returning an empty
            // list here would make a directory outage look as though all matching people disappeared.
            var failure = DescribeFailure(ex, purpose, searchBase);
            _logger.LogError(ex, "{DirectoryFailure}", failure.Message);
            throw failure;
        }
    }

    internal static DirectoryUnavailableException DescribeFailure(Exception cause, string purpose, string? searchBase)
    {
        var context = searchBase is null ? purpose : $"{purpose} using {searchBase}";
        var hint = cause.HResult switch
        {
            unchecked((int)0x8007202B) => "Verify that the LDAP search base belongs to the intended domain and that the server can reach the referred domain.",
            unchecked((int)0x80072030) => "Verify that the configured LDAP search base exists in the intended domain.",
            unchecked((int)0x80070005) => "Verify that the application's process account (not the signed-in user) has read access to the directory objects being queried.",
            _ => "Check domain controller connectivity and the read access of the application's process account (not the signed-in user).",
        };
        return new DirectoryUnavailableException(
            $"Active Directory could not be queried during {context} (0x{cause.HResult:X8}). {cause.Message.TrimEnd()} {hint}", cause);
    }

    private static string GuidOf(ADObject entry) =>
        entry.ADGuid.Bytes is { Length: 16 } bytes ? new Guid(bytes).ToString("D") : "";

    private static string? Root(string? ldapPath) =>
        string.IsNullOrWhiteSpace(ldapPath) ? null : ldapPath.Trim();

    private static string? NonEmpty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? "";

    private static void DisposeAll<T>(IEnumerable<T> entries) where T : IDisposable
    {
        foreach (T entry in entries)
            entry.Dispose();
    }
}
