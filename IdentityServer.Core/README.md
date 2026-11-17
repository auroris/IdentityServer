# AurorisNet.IdentityServer

Adds OpenID Connect sign-in with Active Directory, Samba AD or FreeIPA accounts to your own
ASP.NET Core 10 application, built on [OpenIddict](https://documentation.openiddict.com/).

The package serves the discovery, signing-key, authorization and token endpoints beside your own
routes. Negotiate, IIS Windows Authentication or a Kerberos-authenticating Apache or Nginx proxy
verifies the browser's Kerberos ticket. The server then reads the person's profile and direct
group memberships from the directory and issues an ID token and an access token to the
application. It accepts the authorization code flow, with PKCE when the client sends it, and the
implicit and hybrid flows. It issues no refresh tokens: the application starts a new sign-in to
get fresh tokens.

It runs on Windows and Linux. The same server is available as a standalone application in the
[repository](https://github.com/auroris/AurorisNet.IdentityServer).

## Install

```shell
dotnet add package AurorisNet.IdentityServer
```

The package installs `IdentityServer.Core.dll` and the `AurorisNet.ActiveDirectory` package. The
namespace is `IdentityServer`.

## Use

`Program.cs`:

```csharp
using IdentityServer;

IdentityServerHost.Run<Startup>(args);
```

`Startup.cs`:

```csharp
using IdentityServer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

public class Startup(IConfiguration configuration)
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddIdentityServer(configuration);
        services.AddControllers();
    }

    public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
    {
        app.UseIdentityServer(env);
        app.UseEndpoints(endpoints => endpoints.MapControllers());
    }
}
```

`IdentityServerHost.Run` reads `appsettings.json`, an optional `appsettings.Local.json`,
environment variables and command-line arguments, in that order, and logs to the console and to
daily files. It reads no `appsettings.{Environment}.json` file and no user secrets.

`UseIdentityServer` adds routing, CORS, authentication and authorization. Map your endpoints
after it. Every routed endpoint requires sign-in unless it carries `[AllowAnonymous]` or names
its own authentication scheme.

## Settings

Put the settings in the `IdentityServer` section of your application's `appsettings.json`:

```json
{
  "IdentityServer": {
    "ServerUri": "https://identity.example.com",
    "PersistKeys": true,
    "DataPath": "C:\\IdentityServerData\\keys",
    "Hosts": [ "https://app.example.com" ],
    "Clients": [ { "ClientId": "example-app" } ],
    "Groups": [ "^Example App Users$" ]
  }
}
```

- `Hosts` lists where sign-in results may be sent. The redirect check compares the hostname
  only, and with no entries every authorization request is rejected.
- `Clients` lists the applications that may request sign-in. An empty list accepts any client
  ID.
- `PersistKeys` keeps the token keys across restarts. On Linux it needs
  `KeyProtectionPassword`.
- On a domain-joined Windows server the example is complete. On Linux, add a `Directory` section
  for Active Directory, Samba AD or FreeIPA.

After the application starts, open `/.well-known/openid-configuration` to see the discovery
document.

## Documentation

- [Embedding the core](https://github.com/auroris/AurorisNet.IdentityServer/blob/main/docs/embedding.md)
- [Configuration](https://github.com/auroris/AurorisNet.IdentityServer/blob/main/docs/configuration.md)
- [Directory providers](https://github.com/auroris/AurorisNet.IdentityServer/blob/main/docs/kerberos-and-ldap.md)
- [Windows and IIS](https://github.com/auroris/AurorisNet.IdentityServer/blob/main/docs/windows-iis.md)
- [Linux](https://github.com/auroris/AurorisNet.IdentityServer/blob/main/docs/linux-deployment.md)
- [Apache and Nginx](https://github.com/auroris/AurorisNet.IdentityServer/blob/main/docs/apache-and-nginx.md)
- [Verification](https://github.com/auroris/AurorisNet.IdentityServer/blob/main/docs/verification.md)
- [Troubleshooting](https://github.com/auroris/AurorisNet.IdentityServer/blob/main/docs/troubleshooting.md)
- [All documentation, source and issues](https://github.com/auroris/AurorisNet.IdentityServer)

## License

[MIT](https://github.com/auroris/AurorisNet.IdentityServer/blob/main/LICENSE)
