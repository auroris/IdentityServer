# Embedding the core

The standalone `IdentityServer` project is a thin host over `IdentityServer.Core`. You can
reference the core from your own ASP.NET Core application to serve the OpenID Connect
endpoints beside your own controllers, for example an administration page that uses the
same Kerberos sign-in. The `ActiveDirectory` wrapper library and the `IdentityServer.Directory`
people lookup can also be used on their own.

| Project | Responsibility |
|---|---|
| `IdentityServer.Core` | Authentication, directory providers, OpenIddict setup, the shared host builder |
| `IdentityServer` | The standalone web host and demonstration settings |
| `ActiveDirectory` | Windows-only wrappers over `System.DirectoryServices` |
| `IdentityServer.Directory` | Active Directory people and group lookup for address books and people pickers |

## Reference the core

Install the NuGet package in your .NET 10 ASP.NET Core application:

```shell
dotnet add package AurorisNet.IdentityServer
```

This installs `IdentityServer.Core.dll` and its `AurorisNet.ActiveDirectory` dependency.
The namespace remains `IdentityServer`; the Program and Startup examples below work with
either a package reference or a project reference. Package versions follow the repository's
release versions. See [Creating a release](releasing.md#nugetorg-trusted-publishing) for publishing.

For development against a checkout, use a project reference instead. For a web project
in a folder beside the IdentityServer checkout:

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <IdentityServerRoot Condition="'$(IdentityServerRoot)' == ''">$(MSBuildThisFileDirectory)..\IdentityServer</IdentityServerRoot>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="$(IdentityServerRoot)\IdentityServer.Core\IdentityServer.Core.csproj" />
    <Content Update="appsettings.Local.json" CopyToPublishDirectory="Never" />
  </ItemGroup>
</Project>
```

Pass `-p:IdentityServerRoot=C:\src\IdentityServer` to build from another layout.

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

`IdentityServerHost.Run` applies the shared configuration and logging setup described in
[Configuration](configuration.md#where-settings-come-from). Keeping `CreateHostBuilder`
public lets `WebApplicationFactory` build the host in integration tests.

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

`UseIdentityServer` adds routing, CORS, authentication and authorization; map your endpoints
after it. The middleware serves the OpenID Connect endpoints.

## What your routes inherit

- Every routed endpoint requires sign-in unless it has `[AllowAnonymous]` or names its own
  authentication scheme. An unauthenticated request receives the same Negotiate challenge
  the authorization endpoint uses, or a 403 under `ProxyKerberos`.
- With `Authentication:Mode` set to `Kerberos`, routes also refuse identities that arrived
  by NTLM, including ones IIS authenticated.
- Behind Apache or Nginx, each protected route needs its own Kerberos-authenticated location
  in the proxy, and anonymous routes must clear the identity header. The
  [proxy guide](apache-and-nginx.md#root-hosting-and-stripped-prefixes) explains the pattern.
- `IUserProfileProvider` is registered as a singleton and can be replaced to supply profiles
  from another source, as the integration tests do.

Configure hosts, clients, groups, keys and directory access in your application's own
`appsettings.json`. Keep organisation-specific rules in your application.

## Using the Active Directory library alone

`ActiveDirectory/ActiveDirectory.csproj` wraps `System.DirectoryServices` with `ADUser`,
`ADGroup`, `ADComputer`, `ADPrinter` and a `DirectorySearch` helper. It is Windows-only and
is what the Windows directory provider uses. Install it independently with
`dotnet add package AurorisNet.ActiveDirectory`, or reference the checkout with:

```xml
<PropertyGroup>
  <IdentityServerRoot Condition="'$(IdentityServerRoot)' == ''">$(MSBuildThisFileDirectory)..\IdentityServer</IdentityServerRoot>
</PropertyGroup>
<ItemGroup>
  <ProjectReference Include="$(IdentityServerRoot)\ActiveDirectory\ActiveDirectory.csproj" />
</ItemGroup>
```

Notes for callers:

- Scope searches with an LDAP root such as `LDAP://OU=People,DC=example,DC=com`.
- A `DirectorySearch` result carries only the attributes you asked for in `PropertiesToLoad`.
  Anything else reads as empty until you call `Bind()`, which opens a live connection for
  that object. Ask for what you need up front and dispose wrappers when done.
- `Find<T>(howMany)` caps results and `PageSize` enables paging past the domain controller's
  limit. Sorting a capped result sorts only that subset.
- `PropertiesToSearch` entries are OR-ed. `AdditionalFilter` is raw LDAP for trusted input
  only.
- `ADUser`'s string constructor takes a SAM account name, `DOMAIN\user` or a full `LDAP://`
  path; a bare distinguished name is searched as an account name and fails. The other
  wrappers accept a bare distinguished name and prefix `LDAP://` themselves.

The portable LDAP providers in the core use
`System.DirectoryServices.Protocols`, which runs on both platforms.

## Using the people lookup alone

`IdentityServer.Directory/IdentityServer.Directory.csproj` is the search behind an address
book or a people picker: `IDirectoryLookup` and its Active Directory implementation,
`ActiveDirectoryLookup`, with the `DirectoryUser` and `DirectoryGroup` records they return. It
builds on the `ActiveDirectory` library, so it is Windows-only, and it needs neither the core
nor ASP.NET Core. Install it with `dotnet add package AurorisNet.IdentityServer.Directory`, or
reference the checkout:

```xml
<PropertyGroup>
  <IdentityServerRoot Condition="'$(IdentityServerRoot)' == ''">$(MSBuildThisFileDirectory)..\IdentityServer</IdentityServerRoot>
</PropertyGroup>
<ItemGroup>
  <ProjectReference Include="$(IdentityServerRoot)\IdentityServer.Directory\IdentityServer.Directory.csproj" />
</ItemGroup>
```

Register it as a singleton, with its settings bound from a top-level `Directory` section
(separate from the server's `IdentityServer:Directory`):

```csharp
using IdentityServer.Directory;

services.AddOptions<DirectoryOptions>().BindConfiguration(DirectoryOptions.SectionName);
services.AddSingleton<IDirectoryLookup, ActiveDirectoryLookup>();
```

| `Directory` setting | Default | Meaning |
|---|---|---|
| `UserRoot` | whole domain | LDAP root of local-scope people searches, e.g. `LDAP://OU=Staff,DC=example,DC=com` |
| `UserRootGlobal` | whole domain | LDAP root of global-scope searches and of single-account lookups |
| `GroupRoot` | whole domain | LDAP root of group searches and lookups |
| `GlobalSearchMinLength` | `2` | Shortest term a global search accepts |
| `GlobalSearchSizeLimitBuffer` | `20` | Extra entries fetched so filtered-out accounts do not leave a page short |
| `DefaultTake` / `MaxTake` | `20` / `50` | Results returned when the caller does not say, and at most |
| `MaxGroupMembers` | `500` | Most members a group lookup or listing returns |

Notes for callers:

- Queries run as the process account (the application pool identity under IIS), not as the
  signed-in user. That account needs read access to the roots.
- A query splits into at most eight terms on spaces and commas. In the default
  `DirectorySearchMode.AddressBook`, each term must match the display name, given name,
  surname, user name, email, telephone number or employee ID; `DisplayName` mode matches the
  display name alone. Every value is escaped, so a `*` typed by a user stays literal.
- Only enabled accounts are returned unless the call sets `includeDisabled`. Single-account
  lookups (`FindUserAsync`, by SID, objectGUID or `DOMAIN\user`) ignore the enabled state and
  search `UserRootGlobal`, so a saved identity still resolves after the account moves.
- An `IDirectoryUserFilter` adds trusted LDAP assertions, fetches extra attributes and accepts
  or rejects each result. Pass one to the `ActiveDirectoryLookup` constructor (or register it in
  the container) to apply it to every people search and group listing, except
  `includeDisabled` requests. Pass one to a `SearchUsersAsync` call to replace it for that call;
  that filter always applies and its `AdditionalProperties` come back in
  `DirectoryUser.Attributes`. `FindUserAsync` takes the attribute names to return directly.
  `Attributes` is `null` when nothing was asked for, and is then left out of the JSON.
- A request that cannot be run as asked throws `DirectoryQueryException` with a message for
  the caller (an HTTP API can return it as a 400); an unreachable directory throws
  `DirectoryUnavailableException` (a 503 from an HTTP API), never an empty list.
- The records serialize to camelCase JSON with `JsonSerializerDefaults.Web`, and a .NET client
  of such an API can deserialize them into the same types. Their property names are a contract:
  new versions add properties and do not rename them.
