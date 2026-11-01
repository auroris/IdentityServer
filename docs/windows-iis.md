# Windows and IIS

A domain-joined Windows Server can use IIS Windows Authentication for browser sign-in and
the application pool's account to read Active Directory. IdentityServer uses the server's
domain membership to discover the directory and authenticate those reads.

This deployment requires administrative access to IIS and maybe Active Directory.

## Contents

- [Prerequisites](#prerequisites)
- [Publish the application](#publish-the-application)
- [Folders and permissions](#folders-and-permissions)
- [Settings](#settings)
- [Create the IIS site](#create-the-iis-site)
- [Kerberos and the service principal name](#kerberos-and-the-service-principal-name)
- [Browser trust](#browser-trust)
- [Hosting as a sub-application](#hosting-as-a-sub-application)
- [Check the deployment](#check-the-deployment)
- [Updating](#updating)
- [Hosting with Kestrel](#hosting-with-kestrel)

## Prerequisites

| Requirement | Notes |
|---|---|
| Windows Server, domain-joined | Validated on Windows Server 2022. Any version that runs .NET 10 and IIS 10 works. |
| IIS with **Windows Authentication** | Add the role service under *Web Server > Security* to supply the Negotiate handler. |
| **.NET 10 Hosting Bundle** | Supplies the ASP.NET Core module for IIS. Install it after IIS, or repair it afterwards. [Hosting Bundle](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/iis/hosting-bundle?view=aspnetcore-10.0). |
| A DNS name and HTTPS certificate | For example `identity.example.com`. Browsers and applications must trust the certificate. |
| An application pool identity that can read AD | The default `ApplicationPoolIdentity` runs as the computer account, which can read the directory. A dedicated domain service account also works; see the [SPN section](#kerberos-and-the-service-principal-name). |
| Browser trust for the hostname | Integrated authentication only happens for sites the browser considers intranet or allowlisted. See [Browser trust](#browser-trust). |

Build on a workstation with the .NET SDK and copy the published folder to the server.

## Publish the application

On a machine with the .NET 10 SDK, from the repository root:

```powershell
dotnet publish IdentityServer.csproj -c Release -o artifacts/publish
```

The output contains the application, its dependencies, `appsettings.json` and a `web.config`
for IIS. It is framework-dependent, so the Hosting Bundle on the server supplies the runtime.
Deploy `appsettings.Local.json` separately if you use it; it is excluded from publish output.

## Folders and permissions

Give the pool read access to the application and write access to the log and key folders
listed below. Store keys outside the application folder so they survive application updates.

| Path (example) | Application pool identity | Purpose |
|---|---|---|
| `C:\Sites\IdentityServer` | Read and execute | The published application |
| `C:\Sites\IdentityServer\logs` | Modify | Daily log files. The server writes to `logs\` under its working directory, which IIS sets to the application folder. Create the folder. |
| `C:\IdentityServerData\keys` | Modify | The two persisted token-key files. Keep this outside the application folder. |

Grant permissions to the pool identity, for example `IIS AppPool\IdentityServer` for a pool
named `IdentityServer`, or the dedicated service account.

## Settings

Edit `appsettings.json` in the application folder. This is a complete production example
for a server at `https://identity.example.com` serving an application at
`https://app.example.com`:

```json
{
  "Serilog": {
    "MinimumLevel": {
      "Default": "Information",
      "Override": { "Microsoft": "Warning", "Microsoft.Hosting.Lifetime": "Information" }
    }
  },
  "AllowedHosts": "identity.example.com",
  "IdentityServer": {
    "ServerUri": "https://identity.example.com",
    "PersistKeys": true,
    "DataPath": "C:\\IdentityServerData\\keys",
    "AccessTokenLifetime": "01:00:00",
    "IdentityTokenLifetime": "01:00:00",
    "AuthorizationCodeLifetime": "00:05:00",
    "Hosts": [ "https://app.example.com" ],
    "Clients": [
      { "ClientId": "example-app" }
    ],
    "Groups": [ "^Example App Users$" ]
  }
}
```

What each part does:

- **`ServerUri`** is the issuer that applications will see in tokens. Use the public HTTPS
  address.
- **`PersistKeys` and `DataPath`** preserve the token keys across application pool recycles,
  keeping existing tokens and unexpired codes usable after a recycle.
  On Windows the files are protected with machine-scoped DPAPI, which binds them to this
  server.
- **`Hosts`** lists where sign-in results may be sent. Only the hostname is compared.
- **`Clients`** lists the applications allowed to sign people in. Add a `ClientSecret` for a
  server-side application that can keep one.
- **`Groups`** is a list of regular expressions. A person's direct AD group becomes a `role`
  claim when its name matches one of them and the application asks for the `roles` scope.
- **`Authentication:Mode`** and **`Directory:Provider`** are left at their defaults: on
  Windows with no `Directory:Server` setting, the server authenticates with Negotiate and
  reads AD as the pool identity. This also accepts NTLM and local machine accounts, which
  receive fewer claims. Set `"Authentication": { "Mode": "Kerberos" }` to refuse NTLM.

Replace the repository's demonstration `appsettings.json` with your deployment settings.
The demonstration file accepts a client named `optional` and allows results to be sent to
`oidcdebugger.com`.

The [Configuration guide](configuration.md) describes every key, and how environment
variables and `appsettings.Local.json` override the file.

## Create the IIS site

1. **Application pool.** Create a pool named `IdentityServer` with **No Managed Code** and
   the identity you chose above. One pool per IdentityServer instance.
2. **Site or application.** Create a site bound to `https://identity.example.com:443` with
   your certificate, physical path `C:\Sites\IdentityServer`, using that pool. To host under
   an existing site instead, see [Hosting as a sub-application](#hosting-as-a-sub-application).
3. **Authentication.** In *IIS Manager > the site > Authentication*, enable **both**
   *Windows Authentication* and *Anonymous Authentication*, and disable the others.
   - Windows Authentication supplies the identity when the authorization endpoint asks for it.
   - Anonymous Authentication lets discovery, JWKS and token requests reach the application
     anonymously. IdentityServer applies authentication and authorization to protected
     routes inside the application.
4. **Providers.** Under *Windows Authentication > Providers*, keep `Negotiate` above `NTLM`.
5. **Start the pool and site**, then open
   `https://identity.example.com/.well-known/openid-configuration` from another machine.
   You should get JSON without a credential prompt.

Use IIS Manager to configure the deployed site. The repository's
`Properties/launchSettings.json` supplies the local IIS Express development settings.

## Kerberos and the service principal name

Kerberos works when the browser can get a ticket for `HTTP/identity.example.com` and IIS can
decrypt it. Which account must own that service principal name depends on one IIS setting:

| Pool identity | Kernel-mode authentication (default: on) | Register the SPN on |
|---|---|---|
| `ApplicationPoolIdentity`, `NetworkService` or `LocalSystem` | On | The **computer** account |
| A domain service account | On, with `useAppPoolCredentials` **true** | The **service account** |
| A domain service account | Off | The **service account** |

For the server's own hostname, use the computer account's existing SPN. Register an alias
such as `identity.example.com` with:

```powershell
setspn -Q HTTP/identity.example.com
setspn -S HTTP/identity.example.com EXAMPLE\SERVER01$
```

Use the service account in place of `SERVER01$` for the second and third rows. `setspn -Q`
checks the name's current owner; a duplicate SPN breaks Kerberos authentication for that
service. The `useAppPoolCredentials` switch is under
*Configuration Editor > system.webServer/security/authentication/windowsAuthentication*.

When Kerberos is not available, Negotiate falls back to NTLM, and sign-in still succeeds in
the default mode. The server log shows which protocol was used. Set
`Authentication:Mode` to `Kerberos` once Kerberos works and you want NTLM refused.

See Microsoft's [Windows Authentication in ASP.NET Core](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/windowsauth?view=aspnetcore-10.0)
for the full IIS reference.

## Browser trust

Browsers send a Kerberos ticket only to sites they trust for integrated authentication.

- **Edge, Chrome and Internet Explorer mode** use the Windows *Local intranet* zone. A plain
  hostname is trusted automatically; a fully qualified name such as `identity.example.com`
  usually must be added, by Group Policy (*Site to Zone Assignment List*) or the
  `AuthServerAllowlist` browser policy.
- **Firefox** has its own list. Set `network.negotiate-auth.trusted-uris` to the identity
  hostname in `about:config`, or deploy the
  [Authentication enterprise policy](https://firefox-admin-docs.mozilla.org/reference/policies/authentication/).

A credential prompt indicates that the browser needs the hostname added to its trust
settings. Entering domain credentials also completes sign-in; configuring trust enables
automatic sign-in.

## Hosting as a sub-application

To serve IdentityServer at `https://apps.example.com/IdentityServer` inside an existing
site, right-click the site in IIS Manager and use **Add Application**. Give it its own
application pool with No Managed Code, point it at the published folder, and enable Windows
and Anonymous Authentication on the application.

IIS passes the `/IdentityServer` path to the server, so discovery is at
`https://apps.example.com/IdentityServer/.well-known/openid-configuration` and every
endpoint carries the prefix. Set `ServerUri` to the full
`https://apps.example.com/IdentityServer` and configure applications with that authority.
Leave `PathBase` unset so IIS supplies the application path.
See [IIS sub-applications](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/iis/?view=aspnetcore-10.0#sub-applications).

## Check the deployment

Run the checks in [Verification](verification.md) from a domain-joined workstation:

1. Discovery loads anonymously and every URL in it uses the public HTTPS address.
2. A sign-in with a registered client completes without a prompt and the log shows
   `Authorization claims prepared`.
3. The application receives the subject, name, email and expected roles.
4. The two key files exist under `DataPath`, and a code issued before an application pool
   recycle can still be redeemed after it.

## Updating

1. Stop the application pool.
2. Replace the application folder with the new publish output, keeping your edited
   `appsettings.json` (and `appsettings.Local.json` if you use one). The key files live
   outside the folder and are untouched.
3. Start the pool and repeat the discovery and sign-in checks.

If the new `web.config` replaced one you had customised, re-check the site's authentication
settings in IIS Manager. Keep a copy of the previous folder until the new one is verified.

<a id="hosting-with-kestrel-instead-of-iis"></a>

## Hosting with Kestrel

IdentityServer can also run as its own process on Windows. The service then performs the
Negotiate exchange itself, under the account that runs it.

```powershell
Set-Location C:\Sites\IdentityServer
.\IdentityServer.exe
```

Configure the listener and certificate in `appsettings.json`:

```json
{
  "Kestrel": {
    "Endpoints": {
      "Https": { "Url": "https://0.0.0.0:443" }
    },
    "Certificates": {
      "Default": { "Path": "C:\\certificates\\identity.example.com.pfx" }
    }
  }
}
```

Supply the PFX password through the environment variable
`Kestrel__Certificates__Default__Password`, allow the port through the firewall, and set
`ServerUri` to the public URL. The log files go to `logs\` under the working directory, so
start the process from the application folder.

Differences from IIS to plan for:

- **Service hosting.** Run the host under a service wrapper such as NSSM or WinSW, or add
  the `Microsoft.Extensions.Hosting.WindowsServices` package to an [embedded host](embedding.md).
- **SPN.** Register `HTTP/identity.example.com` on the account that runs the process.
- **Proxy connections.** Negotiate requires one backend connection per client connection.
  Use IIS for proxy hosting on Windows; a generic proxy that pools connections breaks the
  exchange.

The authentication and directory defaults are the same as under IIS. Other Kestrel options
are documented in [Kestrel endpoint configuration](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/servers/kestrel/endpoints?view=aspnetcore-10.0).
