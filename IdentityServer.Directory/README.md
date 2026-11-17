# AurorisNet.IdentityServer.Directory

Searches Active Directory for people and groups: the lookup behind an address book or a people
picker, for .NET 10.

The package adds the `IDirectoryLookup` interface, its implementation `ActiveDirectoryLookup`,
and the records they return.

- **People search.** A query splits into terms on spaces and commas. An account matches when
  every term matches its display name, given name, surname, user name, email, telephone number or
  employee ID. Results are sorted by display name.
- **Two search roots.** A local root for everyday searches, and a wider global root whose
  searches require terms of a minimum length.
- **Account lookup.** One account by SID, objectGUID or `DOMAIN\user`, with its direct groups.
- **Groups.** Search groups by name, read one group with its direct members, or list the people
  in a group.
- **Application rules.** An `IDirectoryUserFilter` adds LDAP assertions, fetches extra
  attributes, and accepts or rejects each result.

The lookup escapes every value it puts in an LDAP filter and limits every result set. It runs on
Windows, through the `AurorisNet.ActiveDirectory` package. Queries run as the process account,
which is the application pool identity under IIS, and that account needs read access to the
configured roots.

## Install

```shell
dotnet add package AurorisNet.IdentityServer.Directory
```

The namespace is `IdentityServer.Directory`. The package installs `AurorisNet.ActiveDirectory`.
It needs neither `AurorisNet.IdentityServer` nor ASP.NET Core.

## Use

Bind the settings and register the lookup as a singleton, because it caches domain names:

```csharp
using IdentityServer.Directory;

services.AddOptions<DirectoryOptions>().BindConfiguration(DirectoryOptions.SectionName);
services.AddSingleton<IDirectoryLookup, ActiveDirectoryLookup>();
```

```json
{
  "Directory": {
    "UserRoot": "LDAP://OU=Staff,DC=example,DC=com",
    "UserRootGlobal": "LDAP://DC=example,DC=com",
    "GroupRoot": "LDAP://OU=Groups,DC=example,DC=com"
  }
}
```

A root that is not set covers the whole domain. Then search:

```csharp
IReadOnlyList<DirectoryUser> people = await lookup.SearchUsersAsync("smith j", take: 10, scope: DirectoryScope.Global);
DirectoryUserDetail? user = await lookup.FindUserAsync(@"EXAMPLE\jsmith");
DirectoryGroupListing? team = await lookup.ListGroupMembersAsync("Help Desk");
```

| `Directory` setting | Default | What it does |
|---|---|---|
| `UserRoot` | The whole domain | The LDAP root of local-scope people searches. |
| `UserRootGlobal` | The whole domain | The LDAP root of global-scope people searches and of account lookups. |
| `GroupRoot` | The whole domain | The LDAP root of group searches and lookups. |
| `GlobalSearchMinLength` | `2` | The shortest term a global-scope search accepts. |
| `GlobalSearchSizeLimitBuffer` | `20` | Extra entries a people search fetches, so that accounts dropped after the query are less likely to leave the result short. |
| `DefaultTake` | `20` | The number of results a search returns when the caller names none. |
| `MaxTake` | `50` | The most results a search returns. |
| `MaxGroupMembers` | `500` | The most members a group lookup or a group listing returns. |

A request that cannot run as asked, such as a global term that is too short or a malformed id,
throws `DirectoryQueryException` with a message for the caller. A directory that cannot be
queried throws `DirectoryUnavailableException`. The lookup never reports an unreachable directory
as an empty result.

Searches return enabled accounts only, unless the call sets `includeDisabled`. `FindUserAsync`
returns an account whatever its state.

## Application rules

Implement `IDirectoryUserFilter` to choose which accounts a search lists:

```csharp
using ActiveDirectory;
using IdentityServer.Directory;

sealed class StaffOnly : IDirectoryUserFilter
{
    public IReadOnlyList<string> AdditionalProperties { get; } = ["employeeType"];
    public string LdapFilter => "(employeeType=staff)";
    public bool IsIncluded(ADUser user) => user.GetProperty("employeeType") == "staff";
}

var staff = await lookup.SearchUsersAsync("smith", filter: new StaffOnly());
string? type = staff.FirstOrDefault()?.Attributes?.GetValueOrDefault("employeeType");
```

- A filter registered with the lookup, in the container or as a constructor argument, applies to
  every people search and group listing. A call with `includeDisabled` bypasses it.
- A filter passed to one `SearchUsersAsync` call replaces the registered filter for that call
  and always applies. Its `AdditionalProperties` come back in `DirectoryUser.Attributes`.
- `FindUserAsync` takes the names of further attributes to return.

Filters and attribute names are trusted application code. The process account reads them whoever
the caller is, so validate anything that comes from a request.

## Documentation

- [Using the people lookup](https://github.com/auroris/AurorisNet.IdentityServer/blob/main/docs/embedding.md#using-the-people-lookup-alone)
- [Source and issues](https://github.com/auroris/AurorisNet.IdentityServer)

## License

[MIT](https://github.com/auroris/AurorisNet.IdentityServer/blob/main/LICENSE)
