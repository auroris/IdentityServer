# AurorisNet.IdentityServer

An embeddable OpenID Connect server for the directory you already run, built on ASP.NET
Core 10 and [OpenIddict](https://documentation.openiddict.com/). Host sign-in endpoints
beside your own application endpoints and let people use their existing directory accounts.

This project is independent of Duende IdentityServer.

## Features

- **Directory sign-in.** Authenticate browsers through Negotiate/Kerberos, IIS Windows
  Authentication, or a trusted Kerberos-aware Apache or Nginx proxy.
- **OpenID Connect.** Discovery, JWKS, authorization and token endpoints. Authorization
  code with PKCE is the recommended flow; implicit and hybrid support is available for
  older clients.
- **Directory claims.** Read a stable subject, profile attributes and direct group
  memberships from Active Directory, Samba AD or FreeIPA. Map allowlisted groups to roles.
- **Windows and Linux.** Host with Kestrel or behind IIS, Apache or Nginx. The Windows
  directory provider uses Windows-only APIs; the LDAP providers support both platforms.
- **Deployment settings.** Configure clients, callback hosts, directory access and
  persistent signing and encryption keys in your application.

## How sign-in works

1. Your client application redirects the browser to the authorization endpoint.
2. The host or authenticating proxy verifies the user's directory credentials. A browser
   that trusts the identity hostname and holds a Kerberos ticket signs in automatically.
3. IdentityServer reads the user's directory profile and allowed direct groups, then
   redirects the browser to the application's callback with an authorization code.
4. The application exchanges the code and PKCE verifier for tokens at the token endpoint.

The service is intended for intranets where browsers can obtain directory Kerberos
tickets. Password sign-in, multi-factor authentication and account self-service require
a separate solution.

## Install

Add the package to a .NET 10 ASP.NET Core web application:

```shell
dotnet add package AurorisNet.IdentityServer
```

The package includes `IdentityServer.Core.dll` and automatically installs its
`AurorisNet.ActiveDirectory` dependency. Namespaces and extension methods are under
`IdentityServer`. Supply your own application settings; the package does not install
demonstration settings or a standalone host.

## Embed in your application

Use the shared host builder in `Program.cs`:

```csharp
using IdentityServer;

IdentityServerHost.Run<Startup>(args);
```

Register the server and your controllers in `Startup.cs`:

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

`UseIdentityServer` adds routing, CORS, authentication and authorization. Map your
endpoints after it. Application routes require sign-in by default; use `[AllowAnonymous]`
for routes intended to be public. The
[embedding guide](https://github.com/auroris/IdentityServer/blob/main/docs/embedding.md)
describes route authentication, proxy requirements and custom profile providers.

## Configure and deploy

Create `appsettings.json` in your host application using the
[configuration example](https://github.com/auroris/IdentityServer/blob/main/docs/configuration.md#a-complete-example)
and the guide for your environment:

- Set the public HTTPS issuer, allowed hosts, clients and callback hosts.
- Choose browser authentication and directory settings. On Linux, configure Kerberos
  and an LDAP provider for Active Directory, Samba AD or FreeIPA.
- Set the group-name allowlist if applications need role claims.
- Persist token keys outside the deployment folder. On Linux, also supply a
  key-protection password through your deployment's secret configuration.
- Configure explicit proxy trust when hosting behind Apache or Nginx.

The shared host reads `appsettings.json`, optional `appsettings.Local.json`, environment
variables and command-line arguments, in that order. It does not load environment-specific
JSON files or .NET user secrets. It logs to the console and daily files.

After starting the configured host, check `/.well-known/openid-configuration` for its
discovery document, then follow the
[sign-in verification guide](https://github.com/auroris/IdentityServer/blob/main/docs/verification.md).

## Documentation and support

- [How it works](https://github.com/auroris/IdentityServer/blob/main/docs/how-it-works.md)
- [Embedding the core](https://github.com/auroris/IdentityServer/blob/main/docs/embedding.md)
- [Configuration](https://github.com/auroris/IdentityServer/blob/main/docs/configuration.md)
- [Directory providers](https://github.com/auroris/IdentityServer/blob/main/docs/kerberos-and-ldap.md)
- [Windows and IIS deployment](https://github.com/auroris/IdentityServer/blob/main/docs/windows-iis.md)
- [Linux deployment](https://github.com/auroris/IdentityServer/blob/main/docs/linux-deployment.md)
- [Apache and Nginx](https://github.com/auroris/IdentityServer/blob/main/docs/apache-and-nginx.md)
- [Troubleshooting](https://github.com/auroris/IdentityServer/blob/main/docs/troubleshooting.md)
- [Source code and standalone server](https://github.com/auroris/IdentityServer)
- [Report an issue](https://github.com/auroris/IdentityServer/issues)

## License

[MIT](https://github.com/auroris/IdentityServer/blob/main/LICENSE)
