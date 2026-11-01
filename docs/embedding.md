# Embedding the core

The standalone `IdentityServer` project is a thin host over `IdentityServer.Core`. You can
reference the core from your own ASP.NET Core application to serve the OpenID Connect
endpoints beside your own controllers, for example an administration page that uses the
same Kerberos sign-in. The `ActiveDirectory` wrapper library can also be used on its own.

| Project | Responsibility |
|---|---|
| `IdentityServer.Core` | Authentication, directory providers, OpenIddict setup, the shared host builder |
| `IdentityServer` | The standalone web host and demonstration settings |
| `ActiveDirectory` | Windows-only wrappers over `System.DirectoryServices` |

## Reference the core

For a .NET 10 web project in a folder beside the IdentityServer checkout:

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
is what the Windows directory provider uses. Reference it on its own with:

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
