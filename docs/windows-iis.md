# Windows and IIS

Follow these steps to run IdentityServer on a domain-joined Windows Server. IIS Windows
Authentication signs the browser in, and the application pool's account reads Active Directory.
The server finds the directory through the machine's domain membership, so it needs no
`Directory` settings. You need administrative access to IIS, and to Active Directory when you
register a service principal name.

## Prerequisites

| Requirement | Notes |
|---|---|
| Windows Server, joined to the domain | A version that runs .NET 10 and IIS. |
| IIS with **Windows Authentication** | Add the role service under *Web Server > Security*. |
| **.NET 10 Hosting Bundle** | Supplies the ASP.NET Core module for IIS. Install it after IIS, or repair it afterwards. See [Hosting Bundle](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/iis/hosting-bundle?view=aspnetcore-10.0). |
| A DNS name and an HTTPS certificate | For example `identity.example.com`. Browsers and applications must trust the certificate. |
| An application pool identity that can read Active Directory | The default `ApplicationPoolIdentity` reaches the directory as the computer account. A dedicated domain service account also works: see [Kerberos and the service principal name](#kerberos-and-the-service-principal-name). |
| Browser trust for the hostname | Browsers use integrated authentication only for sites they trust for it. See [Browser trust](#browser-trust). |

## Publish the application

On a machine with the .NET 10 SDK, from the root of the repository:

```powershell
dotnet publish IdentityServer.csproj -c Release -o artifacts/publish
```

The output holds the application, its dependencies, `appsettings.json` and a `web.config` for
IIS. It uses the runtime that the Hosting Bundle installed. Copy the folder to the server.
`appsettings.Local.json` is left out of the output, so deploy it separately if you use one.

A [release archive](releasing.md#run-a-self-contained-archive) holds the same application with
its own .NET runtime.

## Folders and permissions

| Path (example) | Access for the pool identity | Purpose |
|---|---|---|
| `C:\Sites\IdentityServer` | Read and execute | The published application. |
| `C:\Sites\IdentityServer\logs` | Modify | The daily log files. The server writes to `logs\` under its working directory, which under IIS is the application folder. Create the folder. |
| `C:\IdentityServerData\keys` | Modify | The two token-key files. A folder outside the application folder keeps them through an update. |

Grant the access to the pool identity, which is `IIS AppPool\IdentityServer` for a pool named
`IdentityServer`, or to the dedicated service account.

## Settings

Replace the published `appsettings.json`, which holds the repository's demonstration settings: a
client named `optional` and the callback host `oidcdebugger.com`. This example is complete for a
server at `https://identity.example.com` and an application at `https://app.example.com`:

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

- **`ServerUri`** is the issuer that applications see in tokens. Use the public HTTPS address.
- **`PersistKeys` and `DataPath`** keep the token keys through an application pool recycle, so
  tokens and unexpired codes stay valid. The files are protected with machine-scoped DPAPI: only
  this server can read them.
- **`Hosts`** lists where sign-in results may be sent. **The redirect check compares the hostname
  only.**
- **`Clients`** lists the applications that may request sign-in. Add a `ClientSecret` for a
  server-side application. **An empty list accepts any client ID.**
- **`Groups`** is a list of regular expressions. A person's direct group becomes a `role` claim
  when its name matches one of them and the application requests the `roles` scope.
- **`Authentication:Mode`** is left at its default. The server then accepts Kerberos and NTLM,
  including local Windows accounts, which get fewer claims. Add
  `"Authentication": { "Mode": "Kerberos" }` to refuse NTLM.

[Configuration](configuration.md) describes every setting, and how environment variables and
`appsettings.Local.json` override the file.

## Create the IIS site

1. **Application pool.** Create a pool named `IdentityServer` with **No Managed Code** and the
   identity you chose. Use one pool for each IdentityServer instance.
2. **Site.** Create a site bound to `https://identity.example.com:443` with your certificate,
   the physical path `C:\Sites\IdentityServer` and that pool. To host under an existing site,
   see [Hosting as a sub-application](#hosting-as-a-sub-application).
3. **Authentication.** In *IIS Manager > the site > Authentication*, enable **both** *Windows
   Authentication* and *Anonymous Authentication*, and disable the others.
   - Windows Authentication supplies the identity when the authorization endpoint asks for it.
   - Anonymous Authentication lets discovery, the signing keys and token requests reach the
     server without credentials. The server itself requires sign-in where it is needed.
4. **Providers.** Under *Windows Authentication > Providers*, keep `Negotiate` above `NTLM`.
5. **Start the pool and the site.** Open
   `https://identity.example.com/.well-known/openid-configuration` from another machine. The
   browser shows JSON and no credential prompt.

`Properties/launchSettings.json` in the repository configures IIS Express for development only.
Configure the deployed site in IIS Manager.

## Kerberos and the service principal name

Kerberos works when the browser can get a ticket for `HTTP/identity.example.com` and IIS can
decrypt it. The account that must own that service principal name (SPN) depends on the pool
identity and on kernel-mode authentication, which is on by default:

| Pool identity | Kernel-mode authentication | Register the SPN on |
|---|---|---|
| `ApplicationPoolIdentity`, `NetworkService` or `LocalSystem` | On | The **computer** account |
| A domain service account | On, with `useAppPoolCredentials` set to **true** | The **service account** |
| A domain service account | Off | The **service account** |

The computer account already has an SPN for the server's own hostname. For an alias such as
`identity.example.com`, register one:

```powershell
setspn -Q HTTP/identity.example.com
setspn -S HTTP/identity.example.com EXAMPLE\SERVER01$
```

`setspn -Q` shows the current owner of the name. **Check it first: a duplicate SPN breaks
Kerberos for the service.** For the second and third rows, name the service account in place of
`SERVER01$`. The `useAppPoolCredentials` switch is under *Configuration Editor >
system.webServer/security/authentication/windowsAuthentication*.

When the browser has no usable ticket, Negotiate falls back to NTLM and the sign-in still
succeeds in the default mode. The `Authorization claims prepared` log line names the protocol.
Set `Authentication:Mode` to `Kerberos` once Kerberos works and you want NTLM refused.

Microsoft's
[Windows Authentication in ASP.NET Core](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/windowsauth?view=aspnetcore-10.0)
is the full IIS reference.

## Browser trust

A browser sends a Kerberos ticket only to a site it trusts for integrated authentication.

- **Edge and Chrome** use the Windows *Local intranet* zone. A single-label hostname is in the
  zone automatically. Add a fully qualified name such as `identity.example.com` with Group
  Policy (*Site to Zone Assignment List*) or with the `AuthServerAllowlist` browser policy.
- **Firefox** has its own list. Set `network.negotiate-auth.trusted-uris` to the identity
  hostname in `about:config`, or deploy the
  [Authentication policy](https://firefox-admin-docs.mozilla.org/reference/policies/authentication/).

A credential prompt means the browser does not trust the hostname yet. Domain credentials typed
into the prompt also complete the sign-in.

## Hosting as a sub-application

To serve IdentityServer at `https://apps.example.com/IdentityServer` inside an existing site:

1. Right-click the site in IIS Manager and choose **Add Application**.
2. Give the application its own pool with No Managed Code, and point it at the published folder.
3. Enable Windows Authentication and Anonymous Authentication on the application.
4. Set `ServerUri` to `https://apps.example.com/IdentityServer`, and configure applications with
   that authority.
5. Leave `PathBase` unset. IIS supplies the application path.

Discovery is then at `https://apps.example.com/IdentityServer/.well-known/openid-configuration`,
and every endpoint carries the prefix. See
[IIS sub-applications](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/iis/?view=aspnetcore-10.0#sub-applications).

## Check the deployment

Run the checks in [Verification](verification.md#checks-for-a-new-deployment) from a
domain-joined workstation:

1. Discovery loads without credentials, and every URL in it carries the public HTTPS address.
2. A sign-in with a registered client completes without a prompt, and the log shows
   `Authorization claims prepared`.
3. The application receives the subject, name, email and the expected roles.
4. The two key files exist under `DataPath`, and a code issued before an application pool
   recycle can be redeemed after it.

## Updating

1. Stop the application pool.
2. Replace the application folder with the new publish output. Keep your `appsettings.json`, and
   `appsettings.Local.json` if you use one. The key files are outside the folder.
3. Start the pool and repeat the discovery and sign-in checks.

When the new `web.config` replaces one you had changed, check the site's authentication settings
in IIS Manager again. Keep a copy of the previous folder until the new version is verified.

## Hosting with Kestrel

IdentityServer also runs as its own process on Windows. It then performs the Negotiate exchange
itself, as the account that runs it.

```powershell
Set-Location C:\Sites\IdentityServer
.\IdentityServer.exe
```

Start the process from the application folder: it reads `appsettings.json` from its working
directory and writes its log files to `logs\` there. Configure the listener and the certificate
in `appsettings.json`:

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

Supply the PFX password in the environment variable `Kestrel__Certificates__Default__Password`,
allow the port through the firewall, and set `ServerUri` to the public URL.

Differences from IIS:

- **Running as a service.** Use a service wrapper such as NSSM or WinSW, or add the
  `Microsoft.Extensions.Hosting.WindowsServices` package to an [embedded host](embedding.md).
- **SPN.** Register `HTTP/identity.example.com` on the account that runs the process.
- **Proxies.** A Negotiate exchange belongs to one connection, so a proxy that pools backend
  connections breaks it. To put a proxy in front of the server on Windows, use IIS.

The authentication and directory defaults are the same as under IIS. For other listener options,
see
[Kestrel endpoint configuration](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/servers/kestrel/endpoints?view=aspnetcore-10.0).
