# IdentityServer

An OpenID Connect (OIDC) server using Negotiate/Kerberos authentication, built on ASP.NET Core and [OpenIddict](https://documentation.openiddict.com/). It reads profile details and direct group memberships from Active Directory (AD), Samba AD or FreeIPA.

Run it standalone or embed its core library in your own ASP.NET Core application. The shared Active Directory library can also be used independently.

> **Note:** "IdentityServer" in this project refers to the assembly name and configuration section — it is **not** related to Duende IdentityServer.

## Contents

- [Quick start](#quick-start)
- [How the pieces fit together](#how-the-pieces-fit-together)
- [Rationale](#rationale)
- [Prerequisites](#prerequisites)
- [Installation](#installation)
  - [Hosting on Linux](#hosting-on-linux)
  - [Hosting with Kestrel](#hosting-with-kestrel)
  - [Hosting in IIS](#hosting-in-iis)
  - [Hosting behind Apache or Nginx](#hosting-behind-apache-or-nginx)
  - [Checking a deployment](#checking-a-deployment)
  - [How authentication is wired](#how-authentication-is-wired)
- [Configuration](#configuration)
  - [Authentication and directory providers](#authentication-and-directory-providers)
  - [Example appsettings.json](#example-appsettingsjson)
  - [Configuration keys](#configuration-keys)
    - [Serilog:MinimumLevel](#serilogminimumlevel)
    - [IdentityServer:ServerUri](#identityserverserveruri)
    - [IdentityServer:UseForwardedHeaders](#identityserveruseforwardedheaders)
    - [IdentityServer:PersistKeys](#identityserverpersistkeys)
    - [IdentityServer:DataPath](#identityserverdatapath)
    - [IdentityServer:AccessTokenLifetime](#identityserveraccesstokenlifetime)
    - [IdentityServer:IdentityTokenLifetime](#identityserveridentitytokenlifetime)
    - [IdentityServer:AuthorizationCodeLifetime](#identityserverauthorizationcodelifetime)
    - [IdentityServer:Hosts](#identityserverhosts)
    - [IdentityServer:Clients](#identityserverclients)
    - [IdentityServer:Groups](#identityservergroups)
- [Supported flows, scopes, and claims](#supported-flows-scopes-and-claims)
- [Using the core from another repository](#using-the-core-from-another-repository)
- [Shared libraries](#shared-libraries)
- [Testing](#testing)
  - [Testing with oidcdebugger.com](#testing-with-oidcdebuggercom)
- [Reading the logs](#reading-the-logs)
- [Troubleshooting](#troubleshooting)
- [License](#license)

## Quick start

For a Linux installation, start with the [end-to-end Linux guide](docs/linux-deployment.md), including native Kestrel, Apache/Nginx, AD/Samba/FreeIPA and browser SSO checks. The quick check below uses Windows defaults.

On Windows with the .NET 10 SDK installed, run from the repository root:

```powershell
dotnet restore IdentityServer.sln
dotnet test IdentityServer.sln
dotnet run --project IdentityServer.csproj --no-launch-profile -- --urls http://localhost:5000
```

`--no-launch-profile` avoids the development HTTPS configuration for this first check. Tests requiring a real domain skip themselves when one is unavailable.

Open [the discovery document](http://localhost:5000/.well-known/openid-configuration) and check its `issuer`, `authorization_endpoint` and `token_endpoint`. Discovery is deliberately anonymous so clients can configure themselves before sign-in. The root URL has no landing page and returns 404.

Discovery verifies hosting, not Windows sign-in. Continue to [Testing with oidcdebugger.com](#testing-with-oidcdebuggercom) for that. No database or custom directory schema is required. Local accounts work with reduced claims; domain accounts need a reachable AD domain.

The supplied settings recognize a test application called `optional`, allow sign-in results to return to localhost or `oidcdebugger.com`, and include no group roles. These are demonstration settings. For your own application, configure [Clients](#identityserverclients), [Hosts](#identityserverhosts) and, if it uses roles to decide access, [Groups](#identityservergroups).

Publish a deployable application with:

```powershell
dotnet publish IdentityServer.csproj -c Release -o artifacts/publish
```

The output includes dependencies, settings and an IIS `web.config`. It is framework-dependent and requires the ASP.NET Core 10 runtime on the destination. See [Installation](#installation) for host configuration.

## How the pieces fit together

The operating system's Negotiate/Kerberos handler, or an explicitly trusted authenticating proxy, verifies the user. IdentityServer separately reads the directory and token-key files using its service credentials. Successful browser authentication therefore does not prove the service has the access needed to build claims.

| Term | Meaning in this project |
|---|---|
| Client / relying party | The application requesting sign-in, identified by `client_id`. |
| Issuer / authority | IdentityServer's public base URL. The server reports it as the issuer; clients commonly configure it as their authority. |
| Redirect URI / callback | The client's URL for receiving the sign-in result. |
| Claim / scope | Claims are token fields such as name or role. Scopes select categories of claims, such as `profile`, `email` or `roles`. |
| ID token / access token | An ID token identifies the user to the client. An access token is presented to an API. They are not interchangeable. |

In the authorization code flow, the normal sequence is:

1. The client sends the browser to IdentityServer's authorization endpoint.
2. The configured authentication handler or proxy verifies the person with Negotiate/Kerberos. A trusted browser can often do this without a password prompt.
3. IdentityServer reads the requested account details and returns an **authorization code**, a short-lived intermediate value, to the client's callback.
4. The client exchanges that code at the token endpoint and validates the returned tokens. It can then establish its own application session.

The client application or API enforces its own access rules using the issued claims.

## Rationale

This project bridges integrated directory sign-in and OIDC without a separate user database. It generates token signing and encryption keys automatically; HTTPS certificates are configured separately by the host.

**Tradeoffs to be aware of:**

- By default, keys are **ephemeral**: new keys are generated at startup. A restart can therefore disrupt existing tokens and outstanding codes. Enable [key persistence](#identityserverpersistkeys) to reuse keys on the same machine.
- There is no refresh token flow; clients start sign-in again when they need fresh tokens.
- The server uses OpenIddict's **degraded mode**, which means this application performs client checks and identity lookup without OpenIddict's database stores. It has no persistent token/revocation registry. Choose token lifetimes with that limitation in mind; retaining key files does not add token storage.
- Intended for intranets with existing AD, Samba AD or FreeIPA identities. Browser trust and directory/Kerberos connectivity are part of the setup.

## Prerequisites

- Windows or Linux compatible with .NET 10. Windows defaults to automatic domain discovery and process-account directory access; Linux requires the [Linux deployment setup](docs/linux-deployment.md). The standalone `ActiveDirectory` wrapper library is Windows-only.
- .NET 10 SDK for building; ASP.NET Core 10 runtime for Kestrel deployment, or the .NET 10 Hosting Bundle for IIS.
- For default Windows domain sign-in, a domain-joined server with directory read access under its process account. Portable LDAP providers use explicit directory settings and process credentials or a dedicated read account.
- Browser intranet/trust settings permitting integrated authentication to the server's hostname.

The Negotiate handler uses Kerberos or NTLM. Kerberos requires an appropriate SPN (Service Principal Name); its ownership depends on the hosting setup. See [Troubleshooting](#troubleshooting) and [Microsoft's Windows authentication guide](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/windowsauth?view=aspnetcore-10.0) before changing SPN registration.

## Installation

Publish as shown in [Quick start](#quick-start), then configure DNS and HTTPS for a stable address such as `https://identity.example.com`.

Copy the publish output to the deployment folder and configure that deployment's settings. `appsettings.Local.json` is excluded from the standalone publish output, so create or deploy that file separately if you use it. Keep test clients and redirect hosts only where they are needed; [Hosts](#identityserverhosts) controls where sign-in results can be sent.

The process account needs read/execute access to the application and write access to the log and persistent key directories. Directory lookups need read access under the configured process or bind credentials. Choose one of the following hosts.

### Hosting on Linux

Follow [Running IdentityServer on Linux](docs/linux-deployment.md) for packages, DNS/time, AD/Samba/FreeIPA service keytabs, LDAP/HTTPS trust, read-account configuration, publishing, systemd, browser SSO, upgrades and troubleshooting. It includes commands for native Kestrel and for either authenticating proxy.

Use [appsettings.Kerberos.json](deploy/appsettings.Kerberos.json) with the [native systemd unit](deploy/identityserver-native.service) for direct HTTPS on port 8443, or the [proxy settings](deploy/appsettings.Proxy.json) and [proxy unit](deploy/identityserver.service) for a loopback backend. Install the selected template as the deployment's complete `appsettings.json`; it is not loaded automatically by its repository filename. Supply secrets separately and adapt all example domains, clients and certificate paths.

The [Kerberos/LDAP reference](docs/kerberos-and-ldap.md) describes provider selection, identities and key persistence. The [dated validation record](docs/validation-2026-10-31.md) documents the live interoperability results and resulting fixes.

### Hosting with Kestrel

For a local check of the published files, run this from the repository folder:

```powershell
Set-Location artifacts/publish
.\IdentityServer.exe --urls http://localhost:5000
```

Run from the publish folder so the host finds `appsettings.json`; `dotnet IdentityServer.dll` also works. The listening address and `IdentityServer:ServerUri` are configured separately.

For HTTPS, configure a listening endpoint and its certificate. For example, add this top-level section to the deployment's settings, replacing the certificate path:

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

Grant the process account access to the certificate and supply its password, if required, through `Kestrel__Certificates__Default__Password`. Run without the earlier HTTP `--urls` argument, allow the port through the firewall, and set `IdentityServer:ServerUri` to the public URL. See [Kestrel endpoint configuration](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/servers/kestrel/endpoints?view=aspnetcore-10.0) for other certificate and binding options.

The host does not call `UseWindowsService`. Windows service hosting requires that addition with `Microsoft.Extensions.Hosting.WindowsServices`, or a service wrapper. Configure the service's working directory and process account accordingly.

The application permits HTTP, but HTTPS protects tokens in transit and avoids common client/browser restrictions. In particular, clients often use a **correlation cookie** to remember which browser started sign-in. A cross-site POST return can require `SameSite=None; Secure`, which requires HTTPS. These are cookies of the client application; this server's sign-in handler does not create a login cookie. See [SameSite cookies](https://learn.microsoft.com/en-us/aspnet/core/security/samesite?view=aspnetcore-10.0).

### Hosting in IIS

1. **Install IIS, Windows Authentication and the .NET 10 Hosting Bundle.** If the bundle was installed before IIS, repair it afterward to register the integration module. See [Hosting Bundle installation](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/iis/hosting-bundle?view=aspnetcore-10.0).
2. **Create a site/application pointing at the publish folder**, with a dedicated pool, **No Managed Code**, and an HTTPS binding. See [IIS hosting](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/iis/?view=aspnetcore-10.0#create-the-iis-site).
3. **Grant the pool identity the access described above.** An interactive run under your account does not verify IIS's AD or file permissions.
4. **Enable both Windows Authentication and Anonymous Authentication for this application.** Windows Authentication provides the identity when sign-in is needed. Anonymous Authentication lets discovery and token requests reach the application's own checks without an IIS Windows challenge. It does not make protected application routes public: the application decides which routes require sign-in. The settings in `Properties/launchSettings.json` configure IIS Express during development, not this IIS deployment.
5. **Choose how keys survive a pool recycle.** Set `IdentityServer:PersistKeys` to `true` and grant read/write access to [DataPath](#identityserverdatapath). Otherwise, idle shutdowns and recycles generate new keys. Idle suspension can reduce disruption, but does not preserve keys through every restart.
6. **Check the deployment through its public URL.** Follow the checks below before connecting a real application. After publishing updates, preserve deployment settings and key files and recheck IIS authentication settings if `web.config` was replaced.

For a **sub-application**, such as `https://apps.example.com/IdentityServer`, use IIS **Add Application** or **Convert to Application**, not just a virtual directory. Assign its own app pool for in-process hosting. It shares the parent site's HTTPS binding, while IIS passes the `/IdentityServer` path to the app. See [IIS sub-applications](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/iis/?view=aspnetcore-10.0#sub-applications).

With automatic issuer detection, discovery and endpoints include that application path. Configure the client's authority as `https://apps.example.com/IdentityServer`; discovery is then under `/IdentityServer/.well-known/openid-configuration`. A URL rewrite exposing the endpoints at the site root is unnecessary for a client using this authority. Setting `ServerUri` alone does not create a sub-application or change routing.

### Hosting behind Apache or Nginx

Use a separate Kestrel service behind the proxy, optionally under a path such as `/IdentityServer`. The [Apache and Nginx guide](docs/apache-and-nginx.md) includes proxy configurations, a systemd unit, application settings and tests. Apache uses mod_auth_gssapi; Nginx requires its SPNEGO authentication module.

Select `Authentication:Mode: ProxyKerberos` for these examples: the front server verifies Kerberos and supplies a principal over an explicitly trusted, secret-protected connection. Set `PathBase` to the public prefix and configure `ReverseProxy:KnownProxies` with the actual backend peer addresses. IIS supplies authentication and the application path through its own integration.

### Checking a deployment

| Check | What success tells you |
|---|---|
| Open `<public base URL>/.well-known/openid-configuration` without signing in | A JSON response shows that hosting and anonymous discovery work. No password prompt should be needed. |
| Read the returned `issuer`, `authorization_endpoint` and `token_endpoint` | They should reflect the public address and any application path. An internal hostname, HTTP scheme or missing path points to hosting/proxy configuration. |
| Run a sign-in request from a registered client with an allowed callback | This exercises browser authentication, account lookup and return to the application, which discovery alone does not test. |
| Inspect the application's received claims | Requested fields and roles should match the account type, directory data and group patterns. Missing roles do not necessarily mean authentication failed. |
| If persistence is enabled, verify the two key files are present under `DataPath` after startup | The process can create/read the persisted keys. Preserve these files during later deployments so a restart can reuse them. |

### How authentication is wired

Protocol requests and application routes are handled at different points in the middleware pipeline:

- **The authorization endpoint** (`/connect/authorize`, under any configured application path) is processed by OpenIddict inside ASP.NET Core's authentication middleware. With native authentication, if there is no Windows/Kerberos identity yet, its handler issues a `401 Negotiate` **challenge**, asking the browser to authenticate and retry. A first 401 is part of the exchange, not proof of failure. The native exchange needs HTTP/1.1 even if the initial request used HTTP/2. Under IIS an identity may already be available, avoiding another challenge. With ProxyKerberos, Apache/Nginx performs that exchange and supplies a verified principal; a missing proxy identity produces 403 at the backend instead of starting another Negotiate exchange.
- **Routed application endpoints** go through the `Authentication` middleware, which runs after ASP.NET Core's authentication and challenges unauthenticated requests unless the endpoint allows anonymous access. OpenIddict handles discovery and token requests before this middleware. Unknown paths, including `/favicon.ico`, return 404 without triggering a Windows sign-in prompt.

Negotiate can retain authentication state on a connection. A proxy forwarding native Negotiate to Kestrel must maintain one-to-one connection affinity; forwarded headers alone do not address this. The [Apache/Nginx templates](docs/apache-and-nginx.md) use ProxyKerberos instead, authenticating each backend request independently. See the [Kestrel Windows authentication guidance](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/windowsauth?view=aspnetcore-10.0#kestrel).

## Configuration

### Authentication and directory providers

`IdentityServer:Authentication:Mode` accepts `Auto` (default), `Negotiate`, `Kerberos`, or explicit `ProxyKerberos`. `IdentityServer:Directory:Provider` accepts `Auto` (default), `Windows`, `ActiveDirectory`, `SambaAd`, or `FreeIpa` (case-insensitive). ProxyKerberos is for an authenticating Apache/Nginx front server and requires the [proxy trust settings](docs/apache-and-nginx.md#application-configuration); Auto never enables header authentication.

| Setup | Authentication selected by Auto | Directory lookup |
|---|---|---|
| Windows, default settings | Negotiate, including NTLM/local accounts | Windows automatic discovery |
| Windows, explicit AD/Samba LDAP settings | Negotiate | Portable LDAP with AD schema |
| Linux, AD/Samba LDAP settings | Kerberos required | Portable LDAP with AD schema |
| FreeIPA on either OS | Kerberos required | Portable LDAP with FreeIPA schema |

An explicit `Kerberos` mode rejects NTLM and identities whose actual protocol cannot be established. Both modes use the standard HTTP Negotiate exchange. For directory `Auto`, Windows without `Directory:Server` uses the Windows provider; otherwise Auto selects portable AD. FreeIPA must be selected explicitly. Invalid/incomplete settings fail at startup, and failed authentication never switches providers. Restart after changing provider settings.

For example, merge this into deployment settings for FreeIPA:

```json
{
  "IdentityServer": {
    "Directory": {
      "Provider": "FreeIpa",
      "Server": "ldaps://ipa.example.com",
      "Realm": "EXAMPLE.COM",
      "BaseDn": "dc=example,dc=com"
    }
  }
}
```

Use `SambaAd` or `ActiveDirectory` with the appropriate server/realm/base for those directories. See [Kerberos and LDAP deployment](docs/kerberos-and-ldap.md) for bind credentials, certificates, service principals/keytabs, Linux hosting and validation. Configuring a provider does not provision a Kerberos environment.

Settings are read in this order, with later sources overriding earlier values for the same key:

1. Required `appsettings.json` in the host's content root.
2. Optional `appsettings.Local.json`, ignored by Git and excluded from the standalone publish output.
3. Environment variables.
4. Command-line arguments.

The shared host deliberately does **not** load `appsettings.{Environment}.json` or .NET user secrets. Setting `ASPNETCORE_ENVIRONMENT=Development` changes development behavior, but does not make `appsettings.Development.json` load. This differs from a default ASP.NET Core template.

For example, override `IdentityServer:PersistKeys` with the environment variable `IdentityServer__PersistKeys=true` or the argument `--IdentityServer:PersistKeys=true`. The second client's secret can be supplied as `IdentityServer__Clients__1__ClientSecret`. See [ASP.NET Core configuration](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/configuration/?view=aspnetcore-10.0) for provider syntax.

Overrides merge individual keys, including array indexes; they do not replace a whole file or list. A shorter `Hosts` or `Clients` array in the local file can leave later entries from the base file active, and `[]` does not clear those entries. To remove demonstration clients or hosts, edit the complete lists in the deployment's base `appsettings.json` and check for other overrides.

Restart after changing server settings. Some values are read per request, but keys, issuer options and cached group patterns are initialized at startup or first use; JSON file reload does not guarantee every setting has taken effect. Applications using the core library supply their own configuration files and deployment rules.

### Example appsettings.json

This illustrates a deployment at `https://identity.example.com` used by applications at `https://app.example.com`. Replace those example names, the key path and the secret placeholder. `PersistKeys` is enabled here so routine restarts can reuse keys; the repository's quick-start file leaves it disabled. Supply a real client secret through your deployment's protected settings rather than committing it to source control.

```json
{
  "Serilog": {
    "MinimumLevel": {
      "Default": "Information",
      "Override": {
        "Microsoft": "Warning",
        "Microsoft.Hosting.Lifetime": "Information"
      }
    }
  },
  "IdentityServer": {
    "ServerUri": "https://identity.example.com",
    "UseForwardedHeaders": false,
    "PersistKeys": true,
    "DataPath": "C:\\IdentityServerData\\keys",
    "AccessTokenLifetime": "01:00:00",
    "IdentityTokenLifetime": "01:00:00",
    "AuthorizationCodeLifetime": "00:05:00",
    "Hosts": [
      "https://app.example.com"
    ],
    "Clients": [
      { "ClientId": "my-public-app" },
      { "ClientId": "my-server-app", "ClientSecret": "replace-with-a-generated-secret" }
    ],
    "Groups": [
      "^MyApp Readers$",
      "^MyApp Editors$"
    ]
  }
}
```

### Configuration keys

#### Serilog:MinimumLevel

Serilog writes to the console and daily rolling files under `logs/`, retaining up to 14 files. This is a file-count limit, not a strict 14-day expiry.

The namespace overrides below reduce framework chatter while retaining host start/stop messages:

```json
{
  "Serilog": {
    "MinimumLevel": {
      "Default": "Information",
      "Override": {
        "Microsoft": "Warning",
        "Microsoft.Hosting.Lifetime": "Information"
      }
    }
  }
}
```

Available levels, from most to least verbose:

| Level | When to use |
|---|---|
| `Verbose` | Fine-grained tracing; very high volume — development only |
| `Debug` | Diagnostic detail useful during development and troubleshooting |
| `Information` | Normal operational events (startup, requests, configuration) — recommended default |
| `Warning` | Unexpected conditions that the application handled but that warrant attention |
| `Error` | Failures that prevented an operation from completing |
| `Fatal` | Unrecoverable failures that force the application to shut down |

Setting `Default` to `Warning` or higher reduces volume but hides normal startup and authorization progress. For troubleshooting, prefer an `IdentityServer` namespace override at `Debug` so unrelated framework logs keep their existing levels. See [Reading the logs](#reading-the-logs) for a command and an explanation of the sign-in stages.

#### IdentityServer:ServerUri

Sets the **issuer**, the server identity clients expect in discovery and tokens. Use the public base URL, including an application path if one exists, such as `https://apps.example.com/IdentityServer`. Clients must agree on this address; reaching the same machine through a different hostname does not make the issuer strings interchangeable.

`"*"`, empty or absent means the issuer is derived from the request. This is convenient locally, but different hostnames or a proxy's internal address can produce an unexpected issuer. An explicit value gives the server a stable identity. It does not open a listening port, configure HTTPS or move endpoint routes: configure hosting separately and check the URLs returned by discovery.

#### IdentityServer:UseForwardedHeaders

Defaults to `false`. A reverse proxy can receive `https://identity.example.com` and forward it internally as HTTP to a different address. **Forwarded headers** describe the original request: `X-Forwarded-Proto` carries the scheme (`http`/`https`), and `X-Forwarded-Host` carries the hostname. The middleware can use them so issuer and endpoint generation see the public address.

Set this switch to `true` when your hosting arrangement needs that middleware. Add `IdentityServer:ReverseProxy:KnownProxies` with explicit proxy IPs and set an absolute `ServerUri` (or `ReverseProxy:AllowedHosts` with exact public hostnames). This configures one trusted hop for forwarded client address, scheme and host. Without a `ReverseProxy` section, the middleware uses host-supplied `ForwardedHeadersOptions`. The `ReverseProxy` configuration does not accept `X-Forwarded-Prefix`; use a fixed `IdentityServer:PathBase` and optional `PathBaseMode: Strip` instead. See [proxy/path configuration](docs/apache-and-nginx.md#application-configuration). Forwarded headers do not authenticate users or fix native Negotiate connection affinity.

#### IdentityServer:PersistKeys

Controls whether the server reuses its token keys after a restart. The **signing key** lets recipients check that a token was issued by this server and was not changed. The **encryption key** protects values such as authorization codes that this server later needs to read. These keys are separate from the HTTPS certificate.

- `false` (default): startup generates new keys. Outstanding codes can no longer be decrypted after a restart; existing tokens may fail validation as clients refresh cached signing keys. Some clients may continue accepting a signed token while its old public key remains cached, so a restart is not a reliable immediate revocation mechanism.
- `true`: load the existing files under `DataPath`, or create them if missing. Reusing keys prevents this restart-related disruption; tokens still expire according to their lifetimes.

On Windows, without `IdentityServer:KeyProtectionPassword`, keys are stored in **DPAPI**-protected `.bin` files with machine scope. These files can only be decrypted on the Windows machine that created them. Restrict file permissions to the service account and administrators.

With `IdentityServer:KeyProtectionPassword`, both operating systems use password-encrypted PKCS#8 `.pem` files. Linux requires this setting when persistence is enabled. Supply a strong password through a deployment secret such as `IdentityServer__KeyProtectionPassword`, separate from the key directory, and retain it across restarts. Startup fails without replacing keys if the password is wrong, a key file is damaged, or a key exists only in the other protection format. See [persistent keys](docs/kerberos-and-ldap.md#persistent-token-keys).

#### IdentityServer:DataPath

Used only when `PersistKeys` is `true`. It contains `signing-key.bin` and `encryption-key.bin` for DPAPI, or `signing-key.pem` and `encryption-key.pem` with `KeyProtectionPassword`. The default is a `keys` folder under the application's base directory. An absolute path outside the deployment folder makes it easier to replace application files without deleting the keys.

For example, merge this into the settings and grant the server process account read/write access to that folder:

```json
{
  "IdentityServer": {
    "PersistKeys": true,
    "DataPath": "C:\\IdentityServerData\\keys"
  }
}
```

#### IdentityServer:AccessTokenLifetime

How long an access token is valid after issuance, written as hours:minutes:seconds. The supplied settings use `"01:00:00"` (one hour). If the setting is omitted, OpenIddict supplies its library default.

A shorter lifetime limits exposure from a lost token or outdated role claim, at the cost of more frequent sign-in. Windows authentication may be silent, but the client still needs to handle the round trip.

#### IdentityServer:IdentityTokenLifetime

How long an ID token is valid, in hours:minutes:seconds. The supplied settings use `"01:00:00"` (one hour); omitting it leaves OpenIddict's default in effect.

The client uses this token to validate the sign-in result. Its own login session or cookie can have a separate lifetime, so changing this value does not automatically sign a person out of every application at that time. Configure the client's session behavior as well if that is the intended result.

#### IdentityServer:AuthorizationCodeLifetime

How long the intermediate authorization code can be exchanged at `/connect/token`. The supplied settings use `"00:05:00"` (five minutes); omitting it leaves OpenIddict's default in effect.

Clients should exchange codes immediately. This time allows for the browser's return trip and the client's token request; it is not the duration of the person's session. Lengthening it also extends the time an exposed code might be usable. This store-free configuration does not maintain a database of redeemed codes, so do not rely on it for durable single-use tracking; use PKCE and a short code lifetime.

#### IdentityServer:Hosts

Controls two related checks, using URLs such as `https://app.example.com` as entries:

| Check | What is compared | Why it exists |
|---|---|---|
| Authorization redirect | Hostname only, ignoring scheme, port and path | Keeps sign-in results from being returned to an unlisted host. An entry for `https://app.example.com` allows that hostname on other ports/paths too; it is not an exact callback registration. |
| CORS | Origin: scheme, hostname and port | Allows browser clients such as SPAs to read responses from the token endpoint. |

For a callback `https://app.example.com:8443/signin-oidc`, use `https://app.example.com:8443` as the origin entry, without a path or trailing slash. The application still configures its full callback URL. The allowlist is shared by all configured clients; it does not assign separate redirect hosts to each client. With no hosts configured, authorization redirects are rejected.

The top-level `AllowedHosts` setting in the supplied file has a different job: ASP.NET Core uses it to filter incoming HTTP hostnames. Its `"*"` value does not override `IdentityServer:Hosts`. CORS also does not replace client validation or Windows authentication.

#### IdentityServer:Clients

Lists the applications that can request tokens. A **public client**, such as browser JavaScript, cannot keep a shared secret confidential, so its entry omits `ClientSecret`. A **confidential client** runs in a place that can protect a secret, typically a server; it supplies that secret during token exchange. “Public” describes its ability to keep credentials, not whether anyone may use the application.

```json
{
  "IdentityServer": {
    "Clients": [
      { "ClientId": "my-public-app" },
      { "ClientId": "my-server-app", "ClientSecret": "replace-with-a-generated-secret" }
    ]
  }
}
```

Client IDs are matched without regard to case; configured secrets are case-sensitive. The secret belongs to the application and is unrelated to a person's Windows password. Use PKCE for code flows, especially for public clients; it serves a different purpose from a shared client secret.

A missing or empty client list accepts arbitrary client IDs. A `ClientId` of `"*"` also permits unmatched IDs, and a `ClientSecret` of `"*"` bypasses secret validation. Use explicit entries to restrict applications. The supplied ID `optional` is just a registered test name, not a special keyword.

#### IdentityServer:Groups

Case-insensitive .NET regular expressions selecting which directory groups become role claims. Applied to directory-backed identities when the client requests `roles`; the receiving application decides what those roles permit.

```json
{
  "IdentityServer": {
    "Groups": [
      "^MyApp Readers$",
      "^MyApp Editors$"
    ]
  }
}
```

These patterns match exact names; `^MyApp .*$` would match all names starting with `MyApp `. Use regex syntax, not filename wildcards. Narrow patterns limit both exposed group information and token size.

The code reads direct memberships and matches the group's **common name**, its name in the selected directory rather than its full directory path. Nested memberships are not expanded. An empty list emits no roles. Changing a group or pattern affects newly issued claims; it does not rewrite tokens or application sessions that already exist. Restart after changing patterns because they are cached.

## Supported flows, scopes, and claims

OAuth 2.0 defines token-based access to resources; OIDC adds a standard sign-in result, the ID token. A **flow** describes how the client receives its result. `response_type` chooses the result requested at the authorization endpoint:

| Flow | `response_type` values | What happens |
|---|---|---|
| Authorization Code | `code` | The browser returns a code; the client exchanges it at the token endpoint. |
| Implicit | `id_token`, `token`, `id_token token` | Tokens return directly through the browser, without a separate exchange for those tokens. |
| Hybrid | `code id_token`, `code token`, `code id_token token` | The browser returns both a code and one or more tokens. |

For a new client, start with authorization code flow and **PKCE (Proof Key for Code Exchange)**. The client generates a random **verifier**, sends a **challenge** derived from it with SHA-256 (a one-way fingerprint) at authorization time, then supplies the original verifier at token exchange. This ties redemption to the client that started the request, so an intercepted code alone is insufficient. The server supports PKCE but does not call `RequireProofKeyForCodeExchange` to require it globally; configure the client to use it. See [OpenIddict's PKCE documentation](https://documentation.openiddict.com/configuration/proof-key-for-code-exchange).

Include `openid` for sign-in: this implementation adds the required **subject** (`sub`, the person's identifier) in that scope's branch. Add other scopes only for information the client needs. The labels below describe the claims; their actual names are explained afterward.

| Scope | Claims |
|---|---|
| `openid` | `sub` (Windows/AD/Samba SID or FreeIPA `ipaUniqueID`), `name` |
| `profile` | `windowsaccountname` (account name, including the Kerberos principal for FreeIPA); plus `givenname`, `surname`, `homephone` when available |
| `email` | `email` (from directory `mail`, falling back to `username@localhost`) |
| `roles` | `role` (one per direct directory group matching `IdentityServer:Groups`) |

For a local machine account, the available claims are `sub`, the account name without its machine prefix, the Windows account name and a placeholder email, according to the requested scopes. There are no AD profile fields or group roles. A `username@localhost` email is a placeholder, not proof of a deliverable or verified mailbox.

Except for `sub`, this implementation uses .NET `ClaimTypes` identifiers. These look like web addresses but act as names; the client does not fetch them. For example, the name claim is `http://schemas.xmlsoap.org/ws/2005/05/identity/claims/name`, email is `http://schemas.xmlsoap.org/ws/2005/05/identity/claims/emailaddress`, and role is `http://schemas.microsoft.com/ws/2008/06/identity/claims/role`. In .NET, use the corresponding `ClaimTypes` constants. Other clients must read/map these full identifiers instead of assuming short names such as `name` or `email`.

The server marks these claims for inclusion in both access and ID tokens. It also retains OpenIddict's default access-token encryption: a separate API needs suitable token validation and decryption configuration; the public signing keys in discovery are not decryption keys. An **ID token** is not a substitute for that API's access token. See [OpenIddict token formats](https://documentation.openiddict.com/configuration/token-formats).

## Using the core from another repository

Reference `IdentityServer.Core` to host the sign-in endpoints alongside your own features. The root `IdentityServer.csproj` is the standalone host.

| Project | Responsibility |
|---|---|
| `IdentityServer.Core` | Reusable authentication and hosting; references `ActiveDirectory` |
| `IdentityServer` | Standalone base web host and generic settings |
| `ActiveDirectory` | Shared AD wrappers, also usable independently |

For a .NET 10 web project in a sibling `MyIdentityHost` folder, use:

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

Override the sibling layout with `-p:IdentityServerRoot=C:\src\IdentityServer` when building elsewhere.

Replace the new project's `Program.cs` with:

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

`Run` uses the shared configuration and logging setup. Exposing `CreateHostBuilder` also lets ASP.NET Core integration tests construct the host. Add `Startup.cs`:

```csharp
using IdentityServer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MyIdentityHost;

public class Startup
{
    private readonly IConfiguration configuration;

    public Startup(IConfiguration configuration) => this.configuration = configuration;

    public void ConfigureServices(IServiceCollection services)
    {
        services.AddIdentityServer(configuration);
        services.AddControllers();
        // Register this application's services here.
    }

    public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
    {
        app.UseIdentityServer(env);
        app.UseEndpoints(endpoints => endpoints.MapControllers());
    }
}
```

`UseIdentityServer` includes routing, CORS, authentication and authorization; map controllers afterward. Application routes require authentication unless explicitly anonymous or configured with their own authentication scheme. OpenIddict handles the sign-in endpoints through middleware, so no sign-in controller is needed.

Supply the consuming application's own `appsettings.json`: the standalone host's settings are **not inherited**. Configure its hosts, clients, groups and keys. The example excludes local settings from publish; also ignore that file in the consuming repository.

Rebuild and test consumers when updating the source reference. Keep custom controllers, data providers and organization-specific directory rules in the consuming application.

## Shared libraries

`ActiveDirectory` (`ActiveDirectory/ActiveDirectory.csproj`) provides `ADUser`, `ADGroup`, `DirectorySearch` and related wrappers used for sign-in claims. It can be referenced independently of the core server.

Scope searches with an LDAP root such as `LDAP://OU=People,DC=example,DC=com`, using paths and permissions appropriate to your directory.

`DirectorySearch` results contain only the loaded attributes; unrequested fields read as empty/default until `Bind()` opens a live connection. Prefer `PropertiesToLoad` to avoid per-result connections, and dispose wrappers when finished. Use `ADObject.GetProperty` / `SetProperty` for extra attributes.

`Find<T>(howMany)` caps results; `PageSize` enables paging beyond the domain controller's unpaged limit. Sorting a capped result sorts only that subset. `PropertiesToSearch` ORs its entries; `AdditionalFilter` accepts trusted raw LDAP and must not contain unescaped input.

Both libraries target .NET 10. The `ActiveDirectory` wrappers require Windows; IdentityServer's portable LDAP providers use `System.DirectoryServices.Protocols` separately. The core project keeps platform-compatibility analysis enabled and guards its Windows provider. To reference only the wrappers, add:

```xml
<PropertyGroup>
  <IdentityServerRoot Condition="'$(IdentityServerRoot)' == ''">$(MSBuildThisFileDirectory)..\IdentityServer</IdentityServerRoot>
</PropertyGroup>
<ItemGroup>
  <ProjectReference Include="$(IdentityServerRoot)\ActiveDirectory\ActiveDirectory.csproj" />
</ItemGroup>
```

## Testing

Run the automated checks from the repository folder:

```powershell
dotnet test IdentityServer.sln
```

Host tests cover anonymous discovery, configuration isolation, route handling and authorization-code/PKCE/token flows with simulated identities and profiles. Provider tests check defaults, realm boundaries, LDAP mapping, scoped claims and key persistence. Windows live-directory tests skip when no domain is reachable; portable live-LDAP tests are opt-in. See [deployment verification](docs/kerberos-and-ldap.md#verification) for live-test configuration and the remaining Kerberos/browser checks.

The [2026-10-31 live validation](docs/validation-2026-10-31.md) records 20 successful Chromium SSO flows across AD, Samba and FreeIPA, 10 no-ticket challenges, and live LDAPS/StartTLS checks. It also documents the Linux StartTLS initialization fix, the Nginx response-buffer/realm updates, and their scope. Use the [Linux guide](docs/linux-deployment.md#9-verify-kerberos-and-browser-sso) to verify a new deployment; isolated tests alone do not prove browser or directory interoperability.

Select live tests with `dotnet test IdentityServer.sln --filter Category=LiveDirectory`, or omit them with `--filter "Category!=LiveDirectory"`. Tests use xUnit with the VSTest adapter for CLI and Visual Studio support.

For manual browser testing with the Kestrel development profile, run:

```powershell
dotnet dev-certs https --trust
dotnet run --project IdentityServer.csproj --launch-profile IdentityServer
```

This uses the development certificate and URLs in `Properties/launchSettings.json`. Alternatively, select **IIS Express** in Visual Studio; the supplied profile enables both Windows and anonymous authentication. See [`dotnet dev-certs`](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-dev-certs) for certificate setup.

| How the server was started | Discovery address |
|---|---|
| Quick start with `--no-launch-profile` | [HTTP on port 5000](http://localhost:5000/.well-known/openid-configuration) |
| Kestrel `IdentityServer` profile | [HTTPS on port 5001](https://localhost:5001/.well-known/openid-configuration), or [HTTP on port 5000](http://localhost:5000/.well-known/openid-configuration) |
| Visual Studio IIS Express profile | [HTTPS on port 44353](https://localhost:44353/.well-known/openid-configuration), or [HTTP on port 5000](http://localhost:5000/.well-known/openid-configuration) |

For command-line Windows authentication, use PowerShell's `Invoke-WebRequest -UseDefaultCredentials`, or a curl build with SSPI/SPNEGO support (`curl --version`).

### Testing with oidcdebugger.com

[oidcdebugger.com](https://oidcdebugger.com/) can construct authorization requests and display their results. Use a test account and minimal scopes because the results are sent to that external site. Fill in the fields below and select **Send Request**.

#### Common fields (all flows)

| Field | Value | Why |
|---|---|---|
| Authorize URI | `http://localhost:5000/connect/authorize` | Starts sign-in on the quick-start server. Substitute the active HTTPS port or deployment base URL when appropriate. |
| Redirect URI | `https://oidcdebugger.com/debug` | Returns the result to the debugger, which is acting as the client. |
| Client ID | `optional` | Matches the test application registered in the supplied settings. |
| Scope | `openid` for the first check; optionally add `profile email roles` | Starts with identity only; additional scopes request more account details. |
| State | A fresh random value | Lets a client relate the callback to the request it started. Real applications must check it. |
| Nonce | Keep a fresh auto-generated value | Ties an ID token to this sign-in attempt and helps detect replay of an older result. |

The supplied file allows `https://oidcdebugger.com` in `IdentityServer:Hosts` and registers `optional` without a secret. If you replaced those settings with your deployment's values, this test client/host must be deliberately added there to use the debugger. The IIS Express launch profile opens an implicit-flow test with these values; the generic example deployment configuration above does not include them.

#### Implicit flow — a quick sign-in check

| Field | Value |
|---|---|
| Response type | `id_token` |
| Response mode | `form_post` |

`form_post` means the browser posts the result as form fields to the callback. Success shows an ID token in the debugger and an `Authorization claims prepared` message in the server log. This checks the browser sign-in and claim-building path without a separate token exchange. It is useful for diagnosis; use the code flow below when configuring a new application. Request `token` instead, or as well, only if you are testing access-token issuance.

#### Authorization code flow — test with PKCE

| Field | Value |
|---|---|
| Response type | `code` |
| Response mode | `query` |
| Use PKCE? | Enabled, with SHA-256 |
| Token URI | `http://localhost:5000/connect/token`, or the matching HTTPS address |

`query` puts the code in the callback URL's query string. The debugger supplies the PKCE verifier/challenge and uses the Token URI for exchange. An HTTPS debugger page may be blocked from calling a local HTTP endpoint by browser restrictions; use the HTTPS development profile with a trusted certificate, or perform the exchange from a local tool. A successful return with a code proves authorization worked even if the subsequent exchange fails.

To exchange a fresh code manually in PowerShell, copy the verifier from the **same** debugger request:

```powershell
$tokenRequest = @{
    grant_type = "authorization_code"
    code = "<code returned by the server>"
    redirect_uri = "https://oidcdebugger.com/debug"
    client_id = "optional"
    code_verifier = "<verifier used for that request>"
}
Invoke-RestMethod -Method Post -Uri "http://localhost:5000/connect/token" -ContentType "application/x-www-form-urlencoded" -Body $tokenRequest
```

Use the original client ID, full redirect URI and PKCE verifier, and redeem the code before expiry. For a confidential client, also supply its configured `client_secret`. This request authenticates the application; Windows credentials are not required.

#### Hybrid flow — returns a code and tokens together

| Field | Value |
|---|---|
| Response type | `code` + `id_token` (check both) |
| Response mode | `form_post` |
| Token URI | `http://localhost:5000/connect/token` |

Hybrid combines a direct browser result with a code that still needs an exchange. Use it when testing an existing client that needs that combination. `code token` and `code id_token token` are supported too. Keep PKCE enabled when testing the code exchange; receiving a direct token does not prove that exchange succeeded.


## Reading the logs

Logs appear in the console and in `logs/identityserver-YYYYMMDD.log`, relative to the process working directory. Startup messages explain the consequences of the configured issuer, redirect hosts, key persistence and role patterns. For example, an empty `IdentityServer:Groups` is valid, but it means applications will receive no AD group roles.

For a local troubleshooting run, enable this project's Debug messages:

```powershell
dotnet run --project IdentityServer.csproj --no-launch-profile -- --urls http://localhost:5000 --Serilog:MinimumLevel:Override:IdentityServer=Debug
```

For an existing deployment, add `"IdentityServer": "Debug"` to `Serilog:MinimumLevel:Override` in its settings. Restore the usual level after troubleshooting. The custom token-size diagnostics record lengths rather than token values; account names, network addresses and exception details can still appear in logs, so review them before sharing.

Use the stages below to locate where sign-in stopped:

| Log message or stage | Why it happens and what to check |
|---|---|
| `Authorization request rejected` | The application ID or return destination was not accepted. Check `IdentityServer:Clients` or `IdentityServer:Hosts`, as named in the message. A Windows password change will not fix application configuration. Redirect rejection logs include the host, not the full callback URL or query string. |
| `Issuing Negotiate challenge` | The server is asking the browser to prove the Windows identity. An initial HTTP 401 is normal. If the exchange repeats without progressing, check browser trust, client support for Windows authentication and the offered credentials. |
| `Reading directory profile` | Browser sign-in has succeeded. Check the selected provider, realm mapping, directory connectivity, TLS trust and process/bind credentials if profile lookup fails. The Windows provider skips AD for local accounts. |
| Direct group counts and pattern matches | Only direct group memberships matching `IdentityServer:Groups` become role claims, and only when `roles` was requested. Nested memberships are not expanded. Zero matches can be a valid result. |
| `Authorization claims prepared` | Identity lookup and claim preparation succeeded. OpenIddict still has to build the protocol response; this message does not prove that the application has received or accepted a token. |
| `Token request rejected` | In code flow, the application makes a separate call to exchange its code for tokens. The logged client ID/secret failure concerns the application's credentials, not the person's Windows password. |
| `Token endpoint response sizes` | Lengths help diagnose oversized request headers without recording token credentials. Zero means that token was absent, not necessarily an error. This diagnostic covers `/connect/token`; implicit/hybrid tokens returned directly by `/connect/authorize` do not pass through it. |
| `Error building authorization claims ... during ...` | The stage identifies where preparation stopped. Read the attached exception for the actual cause; successful Windows authentication does not guarantee successful directory lookup or role-pattern processing. |
| `Authorization denied ... during ...` | A warning, returned to the application as `access_denied`. The person authenticated, but directory policy refused to map them to exactly one user: for example an unknown realm, an unqualified name, no or several matching accounts, or a missing SID/`ipaUniqueID`. The message states the reason. Connectivity, TLS and bind failures remain `server_error`. |

## Troubleshooting

For Linux, start with the [Linux troubleshooting table](docs/linux-deployment.md#troubleshooting), including Kerberos/keytab, LDAP TLS, systemd and proxy failures. The Windows/IIS-specific cases below apply to those hosts.

Start with the first failed stage in [Reading the logs](#reading-the-logs). A browser password prompt, a rejected application request and an AD read failure involve different accounts/settings; changing a later stage will not fix an earlier failure.

**The server starts, but its home page returns 404.**
There is no home page in the standalone host. Open `/.well-known/openid-configuration` to check discovery, including the application path if hosted under one. If discovery also returns 404, check the running port, IIS application path and published folder.

**The published application fails before discovery can load.**
Read the console/startup exception. Confirm the required ASP.NET Core 10 runtime or IIS Hosting Bundle is installed, the content root contains `appsettings.json`, and the process account can read application files and access the configured key directory. Publishing did not copy `appsettings.Local.json`; a setting that worked in development may be absent on the server. If IIS cannot start the .NET process at all, also check the Windows Application event log.

**SPA client receives a 431 (Request Header Fields Too Large) error.**
This can happen when a user belongs to many AD groups that match `IdentityServer:Groups` — each matching group becomes a `role` claim and the resulting token can exceed the web server's default header size limit. Tighten the regex patterns in `IdentityServer:Groups` to emit only the groups the relying party actually needs, and check the receiving server or proxy's header limits. The log warns while building claims above 4,096 role-name characters (an estimate before token encoding), and when an access token returned by `/connect/token` exceeds 8,192 characters. These thresholds are diagnostic hints, not guaranteed limits for every application.

**Every request to the authorization endpoint gets a 401 with `WWW-Authenticate: Negotiate` and never completes.**
The Windows exchange is not completing. Check whether the browser trusts the hostname, whether the offered account can authenticate to the server, and whether a command-line client supports Negotiate. A first 401 is expected; repeated 401s without later claim-building logs indicate a problem. The message `Negotiate is not supported with HTTP/2` means the client did not switch to HTTP/1.1 for the exchange. Changing a client secret will not repair this Windows-authentication step.

**Firefox prompts for Windows credentials instead of signing in automatically.**
The server address may not be trusted for automatic Windows sign-in. For the local IIS Express profile, open `about:config` and add `https://localhost:44353` to `network.negotiate-auth.trusted-uris` and `network.automatic-ntlm-auth.trusted-uris`, preserving existing comma-separated entries. Use the actual origin for another host/port, then restart Firefox and retry in a normal window. Limit this trust to servers where automatic Windows sign-in is intended. Managed devices can use Firefox's [Authentication enterprise policy](https://firefox-admin-docs.mozilla.org/reference/policies/authentication/) instead of individual preferences.

**Manually entered Windows credentials succeed, but the debugger returns `server_error`.**
Find `Error building authorization claims ... during ...` and read its exception. Windows can verify the person while the server's separate AD read fails, or a group pattern is invalid. An account the directory cannot map to one user returns `access_denied` instead, with an `Authorization denied` warning. Check the account and stage named in the log. Local accounts skip AD; classification uses Windows' `Local account` SID (`S-1-5-113`).

**Browser prompts for Windows credentials repeatedly (401 loop).**
Check the logs before entering credentials repeatedly. Confirm both IIS authentication features are enabled and the browser trusts the hostname and is offering valid credentials. An unregistered client or rejected redirect returns HTTP 400 (`invalid_request`); correct the client/redirect configuration if it is named in the log.

**Tokens issued before a restart fail validation afterwards.**
Fresh keys after a restart can no longer support earlier tokens/codes in the same way. Enable `IdentityServer:PersistKeys`, retain the same `DataPath` and key files, and check that the process account can read them. If persistence is already enabled, check whether deployment replaced the key directory or moved to another Windows machine. Changing the setting now preserves future keys; it cannot recover keys already lost. Tokens also expire normally, independently of restarts.

**A relying party rejects the discovery document or the tokens over plain HTTP.**
Allowing HTTP on this server does not override the client's transport rules. ASP.NET Core OIDC clients normally require HTTPS discovery (`RequireHttpsMetadata`), and cross-site sign-in cookies may require HTTPS as well. Configure HTTPS for the server and client. If deliberately testing over HTTP, any relaxation belongs in the test client's configuration; changing token keys or redirect hosts does not remove an HTTPS requirement.

**Issuer in tokens doesn't match the URL clients use.**
Either set `IdentityServer:ServerUri` to the canonical public URL explicitly, or — if behind a reverse proxy — enable `IdentityServer:UseForwardedHeaders` and configure the host to accept the scheme and host headers from that trusted proxy. Check the resulting discovery document; enabling the middleware alone does not configure the accepted headers or proxy addresses.

**Authorization request rejected with `invalid_request` for `client_id` or `redirect_uri`.**
Either the `client_id` is not in `IdentityServer:Clients`, or the `redirect_uri` host is not in `IdentityServer:Hosts`. The application log records which check failed, the client ID and, for a rejected redirect, the host name. Token endpoint client authentication failures still return `invalid_client`.

**Expected role claims are missing.**
Check that the signed-in identity is a domain account, the request includes `roles`, the user belongs directly to the expected AD group, and its name matches `IdentityServer:Groups`. Use the Debug group counts to see whether memberships were found but filtered out. Patterns are regular expressions: use `^MyApp .*$` for names starting with `MyApp `, not `MyApp *`. Restart after editing patterns and obtain a new token; previously issued tokens and a client's existing session will not acquire new roles automatically.

**An API cannot read an access token even though sign-in succeeded.**
This server keeps OpenIddict's access-token encryption enabled. A generic JWT signature validator without the required decryption configuration cannot read that token. Check the API's OpenIddict/token validation setup and intended issuer. An ID token serves the client's sign-in and should not be sent to the API as a workaround. See [token formats](https://documentation.openiddict.com/configuration/token-formats).

**The debugger returns a code, but token exchange fails.**
Check the token request's original client ID, full callback URL, PKCE verifier and, for a confidential client, secret. A code from another request or a verifier generated afterward will not match. Start a fresh sign-in if the code expired or keys changed during a restart. If no token request appears in the server log, inspect the browser's network errors for HTTPS/CORS restrictions; authorization through a browser redirect and a JavaScript token request are separate operations.

**Logs show a domain user authenticated via NTLM instead of Kerberos.**
NTLM is expected for local accounts. Where Kerberos is expected, check the client-facing hostname and SPN registration for missing, duplicate or incorrectly owned entries. Kestrel Negotiate uses the service account; IIS ownership depends on its authentication configuration. Do not assume the app pool account is always the correct SPN owner. See [Windows authentication configuration](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/windowsauth?view=aspnetcore-10.0).

**Windows authentication succeeds sometimes but fails intermittently with Kerberos.**
Check time synchronization with `w32tm /query /status` and compare with the domain controller, especially after restoring or resuming a VM. Clock skew beyond the domain's tolerance can invalidate Kerberos tickets. Use the authentication error to confirm the cause.

## License

[MIT](LICENSE).
