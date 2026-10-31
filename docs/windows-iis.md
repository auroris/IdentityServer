# Windows and IIS

This is the simplest deployment. A domain-joined Windows Server runs IdentityServer in IIS,
IIS Windows Authentication verifies the browser, and the application pool's own account reads
Active Directory. No LDAP settings, keytabs or read accounts are needed.

The guide assumes you administer IIS and Active Directory already. It ends with the checks to
run before connecting a real application.

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
- [Hosting with Kestrel instead of IIS](#hosting-with-kestrel-instead-of-iis)

## Prerequisites

| Requirement | Notes |
|---|---|
| Windows Server, domain-joined | Validated on Windows Server 2022. Any version that runs .NET 10 and IIS 10 works. |
| IIS with **Windows Authentication** | Add the role service under *Web Server > Security*. Without it there is no Negotiate handler. |
| **.NET 10 Hosting Bundle** | Supplies the ASP.NET Core module for IIS. Install it after IIS, or repair it afterwards. [Hosting Bundle](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/iis/hosting-bundle?view=aspnetcore-10.0). |
| A DNS name and HTTPS certificate | For example `identity.example.com`. Browsers and applications must trust the certificate. |
| An application pool identity that can read AD | The default `ApplicationPoolIdentity` runs as the computer account, which can read the directory. A dedicated domain service account also works; see the [SPN section](#kerberos-and-the-service-principal-name). |
| Browser trust for the hostname | Integrated authentication only happens for sites the browser considers intranet or allowlisted. See [Browser trust](#browser-trust). |

The server does not need the .NET SDK. Build on a workstation and copy the published folder.

## Publish the application

On a machine with the .NET 10 SDK, from the repository root:

```powershell
dotnet publish IdentityServer.csproj -c Release -o artifacts/publish
```

The output contains the application, its dependencies, `appsettings.json` and a `web.config`
for IIS. It is framework-dependent, so the Hosting Bundle on the server supplies the runtime.
`appsettings.Local.json` is never published; if you use one, deploy it separately.

## Folders and permissions

Keep the application folder read-only for the pool and put anything that changes at run time
beside it, so an update is a folder swap.

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
- **`PersistKeys` and `DataPath`** keep the token keys across application pool recycles.
  Without them every recycle generates new keys, and tokens or codes issued before the
  recycle stop working. On Windows the files are protected with machine-scoped DPAPI, so
  they can only be read on this server.
- **`Hosts`** lists where sign-in results may be sent. Only the hostname is compared.
- **`Clients`** lists the applications allowed to sign people in. Add a `ClientSecret` for a
  server-side application that can keep one.
- **`Groups`** is a list of regular expressions. A person's direct AD group becomes a `role`
  claim when its name matches one of them and the application asks for the `roles` scope.
  An empty list means no roles at all.
- **`Authentication:Mode`** and **`Directory:Provider`** are left at their defaults: on
  Windows with no `Directory:Server` setting, the server authenticates with Negotiate and
  reads AD as the pool identity. This also accepts NTLM and local machine accounts, which
  receive fewer claims. Set `"Authentication": { "Mode": "Kerberos" }` to refuse NTLM.

The repository's own `appsettings.json` is a demonstration file: it accepts a test client
named `optional` and allows results to be sent to `oidcdebugger.com`. Replace it; do not
ship it.

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
     without an IIS challenge. The application still protects its own routes; enabling
     anonymous at IIS does not make anything public.
4. **Providers.** Under *Windows Authentication > Providers*, keep `Negotiate` above `NTLM`.
5. **Start the pool and site**, then open
   `https://identity.example.com/.well-known/openid-configuration` from another machine.
   You should get JSON without a credential prompt.

The `Properties/launchSettings.json` file in the repository configures IIS Express for
development only; it has no effect on this deployment.

## Kerberos and the service principal name

Kerberos works when the browser can get a ticket for `HTTP/identity.example.com` and IIS can
decrypt it. Which account must own that service principal name depends on one IIS setting:

| Pool identity | Kernel-mode authentication (default: on) | Register the SPN on |
|---|---|---|
| `ApplicationPoolIdentity`, `NetworkService` or `LocalSystem` | On | The **computer** account |
| A domain service account | On, with `useAppPoolCredentials` **true** | The **service account** |
| A domain service account | Off | The **service account** |

If the hostname is the server's own name, the computer account already has a matching SPN
and nothing needs registering. For an alias such as `identity.example.com`:

```powershell
setspn -Q HTTP/identity.example.com
setspn -S HTTP/identity.example.com EXAMPLE\SERVER01$
```

Use the service account instead of `SERVER01$` for the second and third rows. `setspn -Q`
first checks that no other account already owns the name; a duplicate SPN breaks Kerberos
for everyone. The `useAppPoolCredentials` switch lives under
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

A browser that does not trust the hostname shows a credential prompt instead of signing in
silently. Entering domain credentials there still works, but it is a sign the trust setting
is missing.

## Hosting as a sub-application

To serve IdentityServer at `https://apps.example.com/IdentityServer` inside an existing
site, right-click the site in IIS Manager and use **Add Application** (not a virtual
directory). Give it its own application pool with No Managed Code, point it at the
published folder, and enable Windows and Anonymous Authentication on the application.

IIS passes the `/IdentityServer` path to the server, so discovery is at
`https://apps.example.com/IdentityServer/.well-known/openid-configuration` and every
endpoint carries the prefix. Set `ServerUri` to the full
`https://apps.example.com/IdentityServer` and configure applications with that authority.
Do not set `PathBase`; that setting is for Linux proxies, and IIS already supplies the path.
See [IIS sub-applications](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/iis/?view=aspnetcore-10.0#sub-applications).

## Check the deployment

Run the checks in [Verification](verification.md) from a domain-joined workstation. In short:

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

## Hosting with Kestrel instead of IIS

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

- **Service hosting.** The host does not call `UseWindowsService`. Run it under a service
  wrapper such as NSSM or WinSW, or add the `Microsoft.Extensions.Hosting.WindowsServices`
  package to an [embedded host](embedding.md).
- **SPN.** Register `HTTP/identity.example.com` on the account that runs the process. There
  is no kernel-mode option here.
- **No reverse proxy for Negotiate.** A generic proxy in front of Kestrel breaks the
  Negotiate exchange unless it keeps one backend connection per client connection. If you
  need a proxy on Windows, use IIS as that proxy instead.

The authentication and directory defaults are the same as under IIS. Other Kestrel options
are documented in [Kestrel endpoint configuration](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/servers/kestrel/endpoints?view=aspnetcore-10.0).
