# Embedding the core

Reference `IdentityServer.Core` from your own ASP.NET Core application to serve the OpenID
Connect endpoints beside your own controllers, such as an administration page behind the same
Kerberos sign-in. This page also covers the two library packages, which work without the server.

| Project | Package | Contents |
|---|---|---|
| `IdentityServer.Core` | `AurorisNet.IdentityServer` | Authentication, directory providers, OpenIddict setup and the host builder. |
| `ActiveDirectory` | `AurorisNet.ActiveDirectory` | Wrappers over `System.DirectoryServices`. Windows only. |
| `IdentityServer.Directory` | `AurorisNet.IdentityServer.Directory` | People and group lookup over Active Directory. Windows only. Published in releases after 1.1.1. |

The standalone `IdentityServer` project is a host of a few lines over `IdentityServer.Core`.

## Reference the core

Add the package to a .NET 10 ASP.NET Core application:

```shell
dotnet add package AurorisNet.IdentityServer
```

It installs `IdentityServer.Core.dll` and the `AurorisNet.ActiveDirectory` package. The
namespace is `IdentityServer`.

To build against a checkout, use a project reference. This project file expects the checkout in
a folder named `AurorisNet.IdentityServer` beside the project's folder:

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <IdentityServerRoot Condition="'$(IdentityServerRoot)' == ''">$(MSBuildThisFileDirectory)..\AurorisNet.IdentityServer</IdentityServerRoot>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="$(IdentityServerRoot)\IdentityServer.Core\IdentityServer.Core.csproj" />
    <Content Update="appsettings.Local.json" CopyToPublishDirectory="Never" />
  </ItemGroup>
</Project>
```

Pass `-p:IdentityServerRoot=<path>` to build with the checkout somewhere else. The `Content`
line keeps `appsettings.Local.json` out of the publish output.

## Program and Startup

`Program.cs`:

```csharp
using IdentityServer;
using Microsoft.Extensions.Hosting;

namespace MyIdentityHost;

public class Program
{
    public static void Main(string[] args) => IdentityServerHost.Run<Startup>(args);

    public static IHostBuilder CreateHostBuilder(string[] args) =>
        IdentityServerHost.CreateHostBuilder<Startup>(args);
}
```

`IdentityServerHost.Run` loads the settings sources described in
[Configuration](configuration.md#where-settings-come-from) and sets up the console and file
logs. A public `CreateHostBuilder` lets `WebApplicationFactory` build the host in integration
tests.

`Startup.cs`:

```csharp
using IdentityServer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MyIdentityHost;

public class Startup(IConfiguration configuration)
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddIdentityServer(configuration);
        services.AddControllers();
        // Register your own services here.
    }

    public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
    {
        app.UseIdentityServer(env);
        app.UseEndpoints(endpoints => endpoints.MapControllers());
    }
}
```

`AddIdentityServer` registers authentication, authorization, CORS and OpenIddict. It stops
startup when the authentication, directory, proxy or key settings are invalid.
`UseIdentityServer` adds routing, CORS, authentication and authorization to the pipeline, and
forwarded-header handling when `UseForwardedHeaders` is on. The authentication middleware serves
the OpenID Connect endpoints. Map your endpoints after it.

Put hosts, clients, groups, keys and the directory connection in your application's
`appsettings.json`, as [Configuration](configuration.md) describes.

## What your routes inherit

- Every routed endpoint requires sign-in unless it carries `[AllowAnonymous]` or names its own
  authentication scheme. An unauthenticated request gets the Negotiate challenge that the
  authorization endpoint uses, or `403` in `ProxyKerberos` mode.
- A request that matches no endpoint returns 404.
- With `Authentication:Mode` set to `Kerberos`, a protected route answers `403` to an identity
  that arrived by NTLM, including one that IIS authenticated.
- Behind Apache or Nginx, each protected route needs its own Kerberos-authenticated location in
  the proxy, and anonymous locations must clear the identity header.
  [Root hosting and stripped prefixes](apache-and-nginx.md#root-hosting-and-stripped-prefixes)
  shows the pattern.
- The server reads profiles through `IUserProfileProvider`. Register your own implementation as a
  singleton to supply profiles from another source.

## Using the Active Directory library alone

`AurorisNet.ActiveDirectory` wraps `System.DirectoryServices` with `ADUser`, `ADGroup`,
`ADComputer`, `ADPrinter` and the `DirectorySearch` helper, in the `ActiveDirectory` namespace.
It runs on Windows, and the server's `Windows` directory provider uses it.

```shell
dotnet add package AurorisNet.ActiveDirectory
```

Or reference the checkout:

```xml
<PropertyGroup>
  <IdentityServerRoot Condition="'$(IdentityServerRoot)' == ''">$(MSBuildThisFileDirectory)..\AurorisNet.IdentityServer</IdentityServerRoot>
</PropertyGroup>
<ItemGroup>
  <ProjectReference Include="$(IdentityServerRoot)\ActiveDirectory\ActiveDirectory.csproj" />
</ItemGroup>
```

Notes for callers:

- Scope a search with an LDAP root such as `LDAP://OU=People,DC=example,DC=com`. With no root,
  the search covers the domain.
- A `DirectorySearch` result holds only the attributes named in `PropertiesToLoad`. Any other
  attribute reads as empty until you call `Bind()`, which opens a connection for that object.
  Name the attributes you need before the search, and dispose each wrapper when you are done.
- `Find<T>(howMany)` limits the results, and the directory chooses which matches fall inside the
  limit. Sorting the returned list sorts that subset only. Set `PageSize` to read more results
  than the domain controller returns in one page.
- `PropertiesToSearch` entries are combined with OR, and their values are escaped.
  `AdditionalFilter` is raw LDAP: build it from trusted values only.
- The `ADUser` string constructor takes a SAM account name, `DOMAIN\user` or a full `LDAP://`
  path. It searches for a bare distinguished name as an account name and throws when it finds
  none. The other wrappers accept a bare distinguished name and add `LDAP://` themselves.

The server's LDAP providers for Active Directory, Samba AD and FreeIPA use
`System.DirectoryServices.Protocols`, which runs on Windows and Linux.

## Using the people lookup alone

`AurorisNet.IdentityServer.Directory` is the search behind an address book or a people picker:
the `IDirectoryLookup` interface, its Active Directory implementation `ActiveDirectoryLookup`,
and the `DirectoryUser` and `DirectoryGroup` records they return. It builds on the
`ActiveDirectory` library, so it runs on Windows. It needs neither the core nor ASP.NET Core.

```shell
dotnet add package AurorisNet.IdentityServer.Directory
```

Or reference the checkout:

```xml
<PropertyGroup>
  <IdentityServerRoot Condition="'$(IdentityServerRoot)' == ''">$(MSBuildThisFileDirectory)..\AurorisNet.IdentityServer</IdentityServerRoot>
</PropertyGroup>
<ItemGroup>
  <ProjectReference Include="$(IdentityServerRoot)\IdentityServer.Directory\IdentityServer.Directory.csproj" />
</ItemGroup>
```

Register the lookup as a singleton, because it caches domain names. Bind its settings from the
top-level `Directory` section, which is separate from the server's `IdentityServer:Directory`:

```csharp
using IdentityServer.Directory;

services.AddOptions<DirectoryOptions>().BindConfiguration(DirectoryOptions.SectionName);
services.AddSingleton<IDirectoryLookup, ActiveDirectoryLookup>();
```

| `Directory` setting | Default | What it does |
|---|---|---|
| `UserRoot` | The whole domain | The LDAP root of local-scope people searches, such as `LDAP://OU=Staff,DC=example,DC=com`. |
| `UserRootGlobal` | The whole domain | The LDAP root of global-scope people searches and of single-account lookups. |
| `GroupRoot` | The whole domain | The LDAP root of group searches and lookups. |
| `GlobalSearchMinLength` | `2` | The shortest term a global-scope search accepts. A value below 1 means 2. |
| `GlobalSearchSizeLimitBuffer` | `20` | Extra entries a people search fetches, in either scope, so that accounts dropped after the query are less likely to leave the result short. |
| `DefaultTake` | `20` | The number of results a search returns when the caller names none. |
| `MaxTake` | `50` | The most results a search returns. |
| `MaxGroupMembers` | `500` | The most members a group lookup or a group listing returns. |

The lookup reads the settings again for each query, so a change applies without a restart.

### The methods

| Method | Returns |
|---|---|
| `SearchUsersAsync` | People who match the query, sorted by display name. |
| `FindUserAsync` | One account by SID, objectGUID or `DOMAIN\user`, with its direct groups, or `null`. |
| `GetUserGroupsAsync` | The common names of an account's direct groups, or `null` when there is no such account. |
| `SearchGroupsAsync` | Groups whose common name contains every term. At least one term is required. |
| `FindGroupAsync` | One group by SID or objectGUID, with its direct members split into users, groups and computers. `MembersTruncated` is `true` when the group has more than `MaxGroupMembers` members. |
| `ListGroupMembersAsync` | A group, named by common name, SID or objectGUID, and the people who are its direct members. |

### How searches behave

- **Process account.** Queries run as the process account, which is the application pool
  identity under IIS. That account, and never the signed-in user, needs read access to the
  roots.
- **Terms.** A query of at most 256 characters splits on spaces and commas into at most eight
  terms. In `DirectorySearchMode.AddressBook`, the default, every term must match one of the
  display name, given name, surname, user name, email, telephone number or employee ID.
  `DirectorySearchMode.DisplayName` matches the display name alone. Every value is escaped, so a
  `*` that a user types stays a literal character.
- **Scope.** `DirectoryScope.Local`, the default, searches `UserRoot`. `DirectoryScope.Global`
  searches `UserRootGlobal` and requires at least one term, each of `GlobalSearchMinLength`
  characters or more.
- **Enabled accounts.** Searches and group listings return enabled accounts only, unless the call
  sets `includeDisabled`. `FindUserAsync` returns an account whatever its state and searches
  `UserRootGlobal`, so a saved identity still resolves after the account moves.
- **Application rules.** An `IDirectoryUserFilter` adds LDAP assertions, names extra attributes
  to fetch and accepts or rejects each result. Pass one to the `ActiveDirectoryLookup`
  constructor, or register it in the container, to apply it to every people search and group
  listing. A call with `includeDisabled` bypasses that registered filter. Pass one to a
  `SearchUsersAsync` call to replace the registered filter for that call. That filter always
  applies, and its `AdditionalProperties` come back in `DirectoryUser.Attributes`.
- **Extra attributes.** `FindUserAsync` takes the names of further attributes to return.
  `Attributes` is `null` when the call asked for none, and is then left out of the JSON.
  **Filters and attribute names are trusted application code.** The process account reads them
  whoever the caller is, so validate anything that comes from a request.
- **Errors.** A request that cannot run as asked, such as a global term that is too short or a
  malformed id, throws `DirectoryQueryException`. Its message is written for the caller, so an
  HTTP API can return it with a 400. A directory that cannot be queried throws
  `DirectoryUnavailableException`, which an HTTP API can answer with a 503. The lookup never
  reports an unreachable directory as an empty list.
- **JSON.** Serialize the records with `JsonSerializerDefaults.Web` for camelCase JSON. A .NET
  client can deserialize that JSON into the same types. The property names are a contract: new
  versions add properties and keep the existing names.
