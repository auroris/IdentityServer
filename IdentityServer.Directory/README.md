# AurorisNet.IdentityServer.Directory

An Active Directory people and group lookup for .NET 10: the searches behind an address book
or a people picker, single-account lookups and group listings, with the records they return.
Every value is escaped before it reaches an LDAP filter and every result set is bounded.

## Features

- **Address-book search.** A query splits into terms on spaces and commas; an account matches
  when every term matches its display name, given name, surname, user name, email, telephone
  number or employee ID, or, in display-name mode, its display name alone. Results are sorted
  by display name.
- **Two search roots.** A local root for everyday searches and a broader global root that
  requires a minimum term length, so a one-letter query cannot sweep the directory.
- **Account lookups.** Find one account by SID, objectGUID or `DOMAIN\user`, with its direct
  groups, wherever it now lives under the global root.
- **Groups.** Search groups by name, look one up with its direct members, or list the people
  in a group.
- **Application rules.** An `IDirectoryUserFilter` adds LDAP assertions, fetches extra
  attributes and accepts or rejects each result, either for every search or for one call.
- **Plain records.** `DirectoryUser`, `DirectoryGroup` and their detail records serialize to
  camelCase JSON, so a directory HTTP API and its .NET clients can share them.

The lookup uses `System.DirectoryServices` through the `AurorisNet.ActiveDirectory` package,
so it runs on Windows, normally on a domain-joined server. Queries run as the process
account (the application pool identity under IIS), which needs read access to the
configured roots.

## Install

```shell
dotnet add package AurorisNet.IdentityServer.Directory
```

The library assembly is `IdentityServer.Directory.dll` and its namespace is
`IdentityServer.Directory`. It installs `AurorisNet.ActiveDirectory` and does not need
`AurorisNet.IdentityServer` or ASP.NET Core.

## Register and search

Bind the settings and register the lookup as a singleton (it caches domain names):

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
    "GroupRoot": "LDAP://OU=Groups,DC=example,DC=com",
    "GlobalSearchMinLength": 2,
    "DefaultTake": 20,
    "MaxTake": 50,
    "MaxGroupMembers": 500
  }
}
```

An empty root searches the whole domain. Then search and look people up:

```csharp
IReadOnlyList<DirectoryUser> people = await lookup.SearchUsersAsync("smith j", take: 10, scope: DirectoryScope.Global);
DirectoryUserDetail? user = await lookup.FindUserAsync(@"EXAMPLE\jsmith");
DirectoryGroupListing? team = await lookup.ListGroupMembersAsync("Help Desk");
```

A request that cannot be run as asked (a global term that is too short, a malformed id)
throws `DirectoryQueryException`, whose message is written for the caller. A directory that
cannot be reached throws `DirectoryUnavailableException`, never an empty result.

## Application rules and extra attributes

Implement `IDirectoryUserFilter` to choose which accounts are listed. Register one with the
lookup to apply it to every people search and group listing; `includeDisabled` requests
bypass it. Or pass one to a single `SearchUsersAsync` call, where it replaces the registered
filter, always applies, and returns its extra attributes in `DirectoryUser.Attributes`:

```csharp
using ActiveDirectory;
using IdentityServer.Directory;

sealed class StaffOnly : IDirectoryUserFilter
{
    public IReadOnlyList<string> AdditionalProperties { get; } = ["employeeType"];
    public string LdapFilter => "(employeeType=staff)";
    public bool IsIncluded(ADUser user) => user.GetProperty("employeeType") == "staff";
}

var staff = await lookup.SearchUsersAsync("smith", filter: new StaffOnly(), mode: DirectorySearchMode.DisplayName);
string? type = staff.FirstOrDefault()?.Attributes?.GetValueOrDefault("employeeType");
```

`FindUserAsync` takes the attribute names to return directly. Filters and attribute names
are trusted application code: never build them from request input without validating it.

## Documentation and support

- [Using the lookup on its own](https://github.com/auroris/IdentityServer/blob/main/docs/embedding.md#using-the-people-lookup-alone)
- [Source code](https://github.com/auroris/IdentityServer/tree/main/IdentityServer.Directory)
- [Report an issue](https://github.com/auroris/IdentityServer/issues)

## License

[MIT](https://github.com/auroris/IdentityServer/blob/main/LICENSE)
