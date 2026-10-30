# IdentityServer

IdentityServer lets applications sign people in with their existing Windows accounts. Windows verifies the account, and this server issues tokens that the application can use to identify the person. Domain accounts can also supply profile details and group memberships from **Active Directory (AD)**, an organization's directory of accounts and other resources.

The sign-in protocol is **OpenID Connect (OIDC)**, implemented with [OpenIddict](https://documentation.openiddict.com/). The web application runs on **ASP.NET Core**, Microsoft's .NET web framework. Run this repository as a standalone server, or use its core library inside your own ASP.NET Core application. The shared Active Directory library can also be used independently to read directory data.

> **Note:** "IdentityServer" in this project refers to the assembly name and configuration section — it is **not** related to Duende IdentityServer.

## Contents

- [Quick start](#quick-start)
- [How the pieces fit together](#how-the-pieces-fit-together)
- [Rationale](#rationale)
- [Prerequisites](#prerequisites)
- [Installation](#installation)
  - [Hosting with Kestrel](#hosting-with-kestrel)
  - [Hosting in IIS](#hosting-in-iis)
  - [Checking a deployment](#checking-a-deployment)
  - [How authentication is wired](#how-authentication-is-wired)
- [Configuration](#configuration)
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

Use a Windows machine with the **.NET 10 SDK** installed. The SDK includes the tools needed to build and test the source; a runtime alone can only run an already-built application. Open PowerShell in the repository folder containing `IdentityServer.sln` and run:

```powershell
dotnet restore IdentityServer.sln
dotnet test IdentityServer.sln
dotnet run --project IdentityServer.csproj --no-launch-profile -- --urls http://localhost:5000
```

`restore` downloads the project's library dependencies. `test` builds the solution and runs the automated checks; tests that need a real domain skip themselves when it is unavailable. `run` starts the web server and keeps it running until you press **Ctrl+C**. The `--no-launch-profile` option avoids the development HTTPS settings for this first check; arguments after `--` are passed to the application. `localhost` means this computer, so this address is for local testing.

Open [the discovery document](http://localhost:5000/.well-known/openid-configuration). You should see JSON containing an `issuer`, `authorization_endpoint` and `token_endpoint`. This document tells client applications where the sign-in endpoints are. It is deliberately public: an application needs it before a user has signed in. The bare root URL has no landing page and returns **404 Not Found**; that is expected.

This check proves the server can start and describe itself. To test Windows sign-in as well, continue to [Testing with oidcdebugger.com](#testing-with-oidcdebuggercom). No database, other repository or custom directory fields are required. A local Windows account works with fewer identity details; a domain account also needs access to its Active Directory domain.

The supplied settings recognize a test application called `optional`, allow sign-in results to return to localhost or `oidcdebugger.com`, and include no group roles. These are demonstration settings. For your own application, configure [Clients](#identityserverclients), [Hosts](#identityserverhosts) and, if it uses roles to decide access, [Groups](#identityservergroups).

Publish a deployable application with:

```powershell
dotnet publish IdentityServer.csproj -c Release -o artifacts/publish
```

`-c Release` selects the deployment build configuration; `-o` selects the output folder. Publish collects the application, dependencies and settings into `artifacts/publish`, including an IIS `web.config` that tells IIS how to start it. This is a **framework-dependent** deployment: the destination still needs the .NET/ASP.NET Core runtime. Publishing creates files; it does not install a service or configure a website. See [Installation](#installation).

## How the pieces fit together

Three identities appear in this setup: the **person** signing in, the **client application** requesting that sign-in, and the **process account** running IdentityServer. The process account reads Active Directory and local key files; giving the person access does not automatically give the process account access.

| Term | Meaning in this project |
|---|---|
| Client / relying party | The application the person wants to use. Its `client_id` is an application identifier, not a Windows username. |
| Issuer / authority | The public base URL of IdentityServer. The server calls it the issuer; a client often calls the same address its authority. Clients use it to identify the server they trust. |
| Endpoint | A URL with a particular job, such as `/connect/authorize` to begin sign-in or `/connect/token` to exchange a code for tokens. |
| Redirect URI / callback | A URL owned by the client application where the browser returns after sign-in. It is different from IdentityServer's own URL. |
| Claim | A named fact in a token, such as an account identifier, name or role. The receiving application decides what access those facts permit. |
| Scope | A category of information requested by the client, such as `profile`, `email` or `roles`. Requesting a scope does not create missing directory data. |
| Token | Data issued as evidence of identity or access. The recipient checks who issued it, whether it was altered and whether it has expired before accepting it. |
| ID token / access token | An ID token tells the client who signed in. An access token is presented to an API, a service called by application code, to request access. They have different purposes and should not be substituted for one another. |
| Role | A label, such as `MyApp Readers`, that an application uses in access decisions. Sending an AD group name as a role does not itself enforce the application's permissions. |

In the authorization code flow, the normal sequence is:

1. The client sends the browser to IdentityServer's authorization endpoint.
2. IdentityServer asks Windows to authenticate the person. A trusted browser can often do this without a password prompt.
3. IdentityServer reads the requested account details and returns an **authorization code**, a short-lived intermediate value, to the client's callback.
4. The client exchanges that code at the token endpoint and validates the returned tokens. It can then establish its own application session.

**Authentication** answers “who is this person?” **Authorization** answers “what may this person do?” This server supplies identity information; the client application or API enforces its own access rules.

## Rationale

This project bridges Windows sign-in and applications that understand OIDC, without maintaining a second user/password database. It generates its own token signing and encryption keys, so you do not need to provision token certificates to try it. **HTTPS certificates are separate:** they protect network traffic and still need to be configured for an HTTPS deployment.

**Tradeoffs to be aware of:**

- By default, keys are **ephemeral**: new keys are generated at startup. A restart can therefore disrupt existing tokens and outstanding codes. Enable [key persistence](#identityserverpersistkeys) to reuse keys on the same machine.
- There is no refresh token flow. A refresh token would let a client obtain new tokens without a new sign-in round trip; here the client starts sign-in again when it needs fresh tokens.
- The server uses OpenIddict's **degraded mode**, which means this application performs client checks and identity lookup without OpenIddict's database stores. It has no persistent token/revocation registry. Choose token lifetimes with that limitation in mind; retaining key files does not add token storage.
- The intended setting is an **intranet**, an organization's internal network with existing Windows accounts and directory access. Browser trust and domain connectivity are part of the setup.

## Prerequisites

| Requirement | Why it is needed |
|---|---|
| Windows compatible with .NET 10 | This implementation uses Windows identities, Windows key protection and Windows-only directory APIs. Installing .NET on Linux does not make this server portable. |
| .NET 10 SDK on the development/build machine | Compiles the source, restores dependencies, runs tests and produces publish output. |
| ASP.NET Core 10 runtime on a Kestrel deployment, or .NET 10 Hosting Bundle on IIS | Runs the published application. The Hosting Bundle also installs the module that connects IIS to ASP.NET Core. |
| A host: Kestrel or IIS | **Kestrel** is the web server included with ASP.NET Core. **IIS** is Windows' web-hosting service, which also manages application processes and HTTPS bindings. |
| Domain access when signing in domain users | A **domain-joined** server is registered with the organization's Active Directory. Its process account needs permission to read user/group data from a **domain controller**, a server providing directory and authentication services. Local accounts use a reduced set of claims. |
| Browser trust for the server address | Browsers restrict automatic Windows sign-in to trusted addresses so they do not offer Windows credentials to arbitrary sites. Managed browser policies or intranet settings may need to include the deployment hostname. |

**Negotiate** is the Windows authentication mechanism used by the application. It selects an available protocol: **Kerberos** uses domain-issued tickets, while **NTLM** uses a challenge/response exchange. An **SPN (Service Principal Name)** associates a service hostname with the account that can accept its Kerberos tickets. The correct SPN account depends on the hosting setup; see [NTLM instead of Kerberos](#troubleshooting) before changing it. [Microsoft's Windows authentication guide](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/windowsauth?view=aspnetcore-10.0) describes the host-specific requirements.

## Installation

First publish with the command in [Quick start](#quick-start). Choose a stable public address, such as `https://identity.example.com`, and arrange its DNS name and HTTPS certificate. **DNS** maps the name to the server's address; the certificate lets browsers verify that name and encrypt the connection.

Copy the publish output to the deployment folder and configure that deployment's settings. `appsettings.Local.json` is excluded from the standalone publish output, so create or deploy that file separately if you use it. Keep test clients and redirect hosts only where they are needed; [Hosts](#identityserverhosts) controls where sign-in results can be sent.

Decide which Windows account will run the application. It needs read/execute access to the application, read access to AD for domain sign-in, and write access to the log directory and persistent key directory when used. These permissions belong to the process account, not to every person signing in. Then choose one of the following hosts.

### Hosting with Kestrel

For a local check of the published files, run this from the repository folder:

```powershell
Set-Location artifacts/publish
.\IdentityServer.exe --urls http://localhost:5000
```

Starting in the publish folder ensures the application finds its required `appsettings.json`. You can use `dotnet IdentityServer.dll` instead of the executable. `--urls` selects the listening address; it does not set the issuer or create an HTTPS certificate.

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

Here `0.0.0.0` means all local IPv4 network interfaces; it is not an address to give clients. The `.pfx` file contains the HTTPS certificate and private key, and the process account must be able to read it. If it has a password, supply `Kestrel__Certificates__Default__Password` through deployment configuration. Run without the HTTP `--urls` argument when using this HTTPS endpoint, allow the chosen port through the server's firewall, and set `IdentityServer:ServerUri` to the public URL clients will use. [Kestrel's endpoint documentation](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/servers/kestrel/endpoints?view=aspnetcore-10.0) covers certificate stores and other binding options.

Running the executable in a terminal does not install a **Windows service**, a process Windows starts and supervises in the background. Service hosting needs an application change to use `UseWindowsService` with `Microsoft.Extensions.Hosting.WindowsServices`, or a service wrapper. Configure its working directory and process account as part of that deployment.

The application permits HTTP for existing intranet setups, but HTTPS protects tokens in transit and avoids common client/browser restrictions. In particular, clients often use a **correlation cookie** to remember which browser started sign-in. A cross-site POST return can require `SameSite=None; Secure`, which requires HTTPS. These are cookies of the client application; this server's sign-in handler does not create a login cookie. See [SameSite cookies](https://learn.microsoft.com/en-us/aspnet/core/security/samesite?view=aspnetcore-10.0).

### Hosting in IIS

1. **Install IIS, its Windows Authentication feature, and the .NET 10 Hosting Bundle.** The bundle installs the runtime and IIS integration module. If it was installed before IIS, repair the bundle afterward so that module is registered. See [Hosting Bundle installation](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/iis/hosting-bundle?view=aspnetcore-10.0).
2. **Create an application pool and a site/application pointing at the publish folder.** An application pool manages a worker process and its account. Use a dedicated pool and select **No Managed Code** for its .NET CLR setting: ASP.NET Core loads its own runtime. This does not mean the application contains no .NET code. Configure the site's HTTPS binding with its hostname, port and certificate. See [IIS hosting](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/iis/?view=aspnetcore-10.0#create-the-iis-site).
3. **Give the pool identity the required access.** Under **Application Pools > Advanced Settings > Identity**, identify the account actually running the app, then check the file and directory permissions described above. An interactive test run may succeed under your account while IIS fails under its different account.
4. **Enable both Windows Authentication and Anonymous Authentication for this application.** Windows Authentication provides the identity when sign-in is needed. Anonymous Authentication lets discovery and token requests reach the application's own checks without an IIS Windows challenge. It does not make protected application routes public: the application decides which routes require sign-in. The settings in `Properties/launchSettings.json` configure IIS Express during development, not this IIS deployment.
5. **Choose how keys survive a pool recycle.** A recycle replaces the worker process. Set `IdentityServer:PersistKeys` to `true` and give the pool identity read/write access to [DataPath](#identityserverdatapath) to retain keys. Otherwise, an idle shutdown or scheduled recycle creates new keys; plan recycle times accordingly. Suspending rather than terminating an idle process can reduce disruption, but does not preserve keys through every restart.
6. **Check the deployment through its public URL.** Follow the checks below before connecting a real application. After publishing updates, preserve deployment settings and key files and recheck IIS authentication settings if `web.config` was replaced.

For a **sub-application**, such as `https://apps.example.com/IdentityServer`, use IIS **Add Application** or **Convert to Application**, not just a virtual directory. Assign its own app pool for in-process hosting. It shares the parent site's HTTPS binding, while IIS passes the `/IdentityServer` path to the app. See [IIS sub-applications](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/iis/?view=aspnetcore-10.0#sub-applications).

With automatic issuer detection, discovery and endpoints include that application path. Configure the client's authority as `https://apps.example.com/IdentityServer`; discovery is then under `/IdentityServer/.well-known/openid-configuration`. A URL rewrite exposing the endpoints at the site root is unnecessary for a client using this authority. Setting `ServerUri` alone does not create a sub-application or change routing.

### Checking a deployment

| Check | What success tells you |
|---|---|
| Open `<public base URL>/.well-known/openid-configuration` without signing in | A JSON response shows that hosting and anonymous discovery work. No password prompt should be needed. |
| Read the returned `issuer`, `authorization_endpoint` and `token_endpoint` | They should reflect the public address and any application path. An internal hostname, HTTP scheme or missing path points to hosting/proxy configuration. |
| Run a sign-in request from a registered client with an allowed callback | This exercises Windows authentication, account lookup and return to the application, which discovery alone does not test. |
| Inspect the application's received claims | Requested fields and roles should match the account type, directory data and group patterns. Missing roles do not necessarily mean authentication failed. |
| If persistence is enabled, verify the two key files are present under `DataPath` after startup | The process can create/read the persisted keys. Preserve these files during later deployments so a restart can reuse them. |

### How authentication is wired

**Middleware** is a sequence of request-processing components. Their order matters: a component can complete a request before later components see it. This server handles protocol requests and application routes at different points:

- **The authorization endpoint** (`/connect/authorize`) is processed by OpenIddict inside ASP.NET Core's authentication middleware. If there is no Windows identity yet, its handler issues a `401 Negotiate` **challenge**, asking the browser to authenticate and retry. A first 401 is part of the exchange, not proof of failure. The Windows exchange needs HTTP/1.1 even if the initial request used HTTP/2. Under IIS an identity may already be available, avoiding another challenge.
- **Routed application endpoints** go through the `Authentication` middleware, which runs after ASP.NET Core's authentication and challenges unauthenticated requests unless the endpoint allows anonymous access. OpenIddict handles discovery and token requests before this middleware. Unknown paths, including `/favicon.ico`, return 404 without triggering a Windows sign-in prompt.

Negotiate can retain authentication state on a connection. A **reverse proxy**, a server forwarding requests to this application, must support that behavior; forwarding the public hostname alone is insufficient. For Kestrel Negotiate, Microsoft requires a persistent one-to-one connection relationship through a proxy. See the [Kestrel Windows authentication guidance](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/windowsauth?view=aspnetcore-10.0#kestrel).

## Configuration

Settings are read in this order, with later sources overriding earlier values for the same key:

1. `appsettings.json` in the host's content root, the folder used to locate application content. This required file supplies the base settings.
2. `appsettings.Local.json` in the same folder, if present. Use it for machine-specific overrides; it is ignored by Git and excluded from the standalone project's publish output.
3. Environment variables available to the process. These let deployment tools supply settings without editing files.
4. Command-line arguments. These are useful for a one-off run and take precedence over the other sources.

The shared host deliberately does **not** load `appsettings.{Environment}.json` or .NET user secrets. Setting `ASPNETCORE_ENVIRONMENT=Development` changes development behavior, but does not make `appsettings.Development.json` load. This differs from a default ASP.NET Core template.

`IdentityServer:PersistKeys` is a **configuration path**: the `PersistKeys` property inside the JSON `IdentityServer` object. In an environment variable, use double underscores instead of colons: `IdentityServer__PersistKeys=true`. On the command line, use `--IdentityServer:PersistKeys=true`. Array positions are zero-based; the second client's secret in the example below can be supplied as `IdentityServer__Clients__1__ClientSecret`. See [ASP.NET Core configuration](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/configuration/?view=aspnetcore-10.0) for provider syntax.

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

**Serilog** is the logging library used by the shared host. `MinimumLevel` selects the least severe events to record: `Information` also records Warning, Error and Fatal events. Logs go to the console and daily rolling files under `logs/`, with up to 14 files retained. A new file is opened for each day that produces logs, so this is a file-count limit rather than a strict 14-day expiry.

The `Default` level applies unless a source has an override. A **namespace** groups related code; overriding `Microsoft` reduces framework chatter, while the more specific `Microsoft.Hosting.Lifetime` keeps server start/stop messages. Merge the following section into the settings object:

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

Set this switch to `true` when your hosting arrangement needs that middleware, and configure accepted headers and trusted proxy addresses in the host's `ForwardedHeadersOptions`. The switch only adds the middleware; it does not configure those trust rules. Trust matters because a header supplied by an arbitrary caller must not redefine the server's public identity. See [ASP.NET Core proxy configuration](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/proxy-load-balancer?view=aspnetcore-10.0). Forwarded headers also do not solve Windows authentication's connection requirements described above.

#### IdentityServer:PersistKeys

Controls whether the server reuses its token keys after a restart. The **signing key** lets recipients check that a token was issued by this server and was not changed. The **encryption key** protects values such as authorization codes that this server later needs to read. These keys are separate from the HTTPS certificate.

- `false` (default): startup generates new keys. Outstanding codes can no longer be decrypted after a restart; existing tokens may fail validation as clients refresh cached signing keys. Some clients may continue accepting a signed token while its old public key remains cached, so a restart is not a reliable immediate revocation mechanism.
- `true`: load the existing files under `DataPath`, or create them if missing. Reusing keys prevents this restart-related disruption; tokens still expire according to their lifetimes.

The files are encrypted using **DPAPI**, Windows' Data Protection API, with machine scope. They are tied to that Windows machine, not just to a named account. File permissions must still restrict who can read them. Copying the files to another server or sharing their folder across servers does not provide a working multi-server key setup.

#### IdentityServer:DataPath

Used only when `PersistKeys` is `true`. It contains `signing-key.bin` and `encryption-key.bin`. The default is a `keys` folder under the application's base directory. An absolute path outside the deployment folder makes it easier to replace application files without deleting the keys.

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

An API client commonly sends the token as `Authorization: Bearer ...`; **bearer** means possession of the token is sufficient to present it. A shorter lifetime limits how long a lost token or outdated role claim can remain useful, but requires the client to obtain new tokens more often. Windows sign-in may be silent in a trusted browser, but the application must still handle that round trip.

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
| CORS (Cross-Origin Resource Sharing) | The origin: scheme, hostname and port | Lets browser JavaScript at an allowed origin read responses, such as a token exchange. A **single-page application (SPA)** often needs this because its code runs in the browser. |

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

A missing or empty client list accepts arbitrary client IDs. A `ClientId` of `"*"` also permits unmatched IDs, and a `ClientSecret` of `"*"` bypasses secret validation. These are permissive compatibility options, not values required to make sign-in work. Use explicit entries to restrict applications. The supplied ID `optional` is just a registered test name, not a special keyword.

#### IdentityServer:Groups

A list of case-insensitive **regular expressions**, text patterns describing which AD group names should become roles. A match is included only for domain accounts when the client requests the `roles` scope. This controls information in tokens; it does not itself grant access to an application route.

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

`^` means the start of a name and `$` its end, so these patterns match complete names. `^MyApp .*$` would match any name starting with `MyApp `: `.` means any character and `*` repeats it. A bare `*` does not work like a filename wildcard. Exact, application-specific names are a useful starting point because broad matches expose more directory information and make tokens larger.

The code reads direct memberships and matches the group's **common name**, its name in AD rather than its full directory path. Nested memberships are not expanded. An empty list emits no roles. Changing a group or pattern affects newly issued claims; it does not rewrite tokens or application sessions that already exist. Restart after changing patterns because they are cached.

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
| `openid` | `sub` (Windows SID, a security identifier that survives an account rename), `name` |
| `profile` | `windowsaccountname`; plus `givenname`, `surname`, `homephone` when available from Active Directory |
| `email` | `email` (from AD `mail` attribute, falling back to `username@localhost`) |
| `roles` | `role` (one per AD group matching `IdentityServer:Groups`) |

For a local machine account, the available claims are `sub`, the account name without its machine prefix, the Windows account name and a placeholder email, according to the requested scopes. There are no AD profile fields or group roles. A `username@localhost` email is a compatibility placeholder, not proof of a deliverable or verified mailbox.

Except for `sub`, this implementation uses .NET `ClaimTypes` identifiers. These look like web addresses but act as names; the client does not fetch them. For example, the name claim is `http://schemas.xmlsoap.org/ws/2005/05/identity/claims/name`, email is `http://schemas.xmlsoap.org/ws/2005/05/identity/claims/emailaddress`, and role is `http://schemas.microsoft.com/ws/2008/06/identity/claims/role`. In .NET, use the corresponding `ClaimTypes` constants. Other clients must read/map these full identifiers instead of assuming short names such as `name` or `email`.

The server marks these claims for inclusion in both access and ID tokens. It also retains OpenIddict's default access-token encryption: a separate API needs suitable token validation and decryption configuration; the public signing keys in discovery are not decryption keys. An **ID token** is not a substitute for that API's access token. See [OpenIddict token formats](https://documentation.openiddict.com/configuration/token-formats).

## Using the core from another repository

Use `IdentityServer.Core` when your application should host these sign-in endpoints and add its own features. It is a **class library**, code loaded by another application rather than an executable. The root `IdentityServer.csproj` is the small standalone executable that uses it.

| Project | Responsibility |
|---|---|
| `IdentityServer.Core` | Reusable authentication and hosting; references `ActiveDirectory` |
| `IdentityServer` | Standalone base web host and generic settings |
| `ActiveDirectory` | Shared AD wrappers, also usable independently |

Create an ASP.NET Core web project targeting .NET 10. Reference the core library so your application owns its entry point and settings. For example, if `MyIdentityHost` and `IdentityServer` are sibling folders, this is a complete `MyIdentityHost.csproj`:

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

`ProjectReference` tells the build to compile and use the source library. `IdentityServerRoot` is an **MSBuild property**, a named value used while building; it is unrelated to `appsettings.json`. The conditional default supports sibling folders while allowing another location with `-p:IdentityServerRoot=C:\src\IdentityServer`.

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

ASP.NET Core provides `IConfiguration` to the constructor through **dependency injection**, its mechanism for supplying registered services. `ConfigureServices` registers what components are available. `Configure` puts request-processing components in order. `UseIdentityServer` already includes routing, CORS, authentication and authorization; map controllers afterward so its endpoint checks can protect your routes. Use `[AllowAnonymous]` only on routes intended to be public. OpenIddict provides the sign-in endpoints through middleware, so you do not need a controller just to expose them.

Add your own `appsettings.json` in the new application's content root and configure its hosts, clients, groups and keys. The base server's settings are **not inherited** by a project reference. The example project excludes its local settings from publish too; also ignore that file in the consuming repository. Run and publish the consuming project, using the same commands as the standalone project with its project filename substituted.

Because these are source references, updating the IdentityServer checkout and rebuilding adopts its changes. Test the consuming application against that revision before deploying. Keep custom controllers, data providers and organization-specific directory fields in the consuming application; the core can then stay reusable without requiring another organization's directory layout.

## Shared libraries

`IdentityServer.Core` provides the server described above. `ActiveDirectory` (`ActiveDirectory/ActiveDirectory.csproj`) wraps the directory APIs in objects such as `ADUser`, `ADGroup` and `DirectorySearch`. “Wraps” means it provides a simpler C# interface over lower-level APIs. It is used for sign-in claims and can also be referenced independently; referencing it does not create a directory website or HTTP endpoints.

**LDAP (Lightweight Directory Access Protocol)** is used to query AD. A search **root** is the starting location in its tree: in `LDAP://OU=People,DC=example,DC=com`, `OU=People` means an organizational unit called People and `DC=example,DC=com` identifies the domain. A **filter** describes which records should match. Replace example roots with locations in your own directory; choosing a root limits the search but does not grant the process account permission to read it.

An object returned by `DirectorySearch` contains only the attributes the search loaded. An unrequested field reads as empty/default until `Bind()` opens a live connection. Request needed fields in `PropertiesToLoad` to avoid a separate connection for every result, and dispose wrappers when finished because they can own native directory resources. `ADObject.GetProperty` / `SetProperty` supports extra attributes; application-specific schema and formatting rules belong in the consuming application. A **schema** defines the directory's object types and attributes.

Use bounded searches for user-facing pickers. `Find<T>(howMany)` limits the number returned, while `PageSize` controls batches fetched from the server; paging is needed to retrieve more than a domain controller's unpaged limit. Sorting a limited result afterward sorts only that subset, not the entire directory. `PropertiesToSearch` combines its entries with OR (any may match); `AdditionalFilter` is a trusted raw LDAP condition and must not contain unescaped user input.

Both libraries target .NET 10. Directory access uses Windows-only `System.DirectoryServices`; suppressing the compiler's CA1416 platform warning does not make those calls portable. To use only the AD wrappers, put these elements inside the consuming project's `.csproj`:

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

**Host tests** run the application in a test server to check discovery, configuration isolation and route handling without a real domain. **Live-directory tests** make read-only queries against the real domain as the account running the tests; they skip with a reason when that environment is unavailable. A skipped live test does not verify the deployment's AD permissions.

To select only live tests, use `dotnet test IdentityServer.sln --filter Category=LiveDirectory`; to omit them, use `--filter "Category!=LiveDirectory"`. **xUnit** is the test framework and **VSTest** is the adapter used by `dotnet test`/Visual Studio Test Explorer; the test project's package references install them during restore.

For manual browser testing with the Kestrel development profile, run:

```powershell
dotnet dev-certs https --trust
dotnet run --project IdentityServer.csproj --launch-profile IdentityServer
```

The first command creates/trusts a local development HTTPS certificate; Windows may ask you to approve that trust. It is for this developer machine, not a deployment certificate; see [`dotnet dev-certs`](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-dev-certs). The second command uses the URLs in `Properties/launchSettings.json`. Alternatively, select **IIS Express** in Visual Studio; it is the development version of IIS, with authentication enabled by this repository's launch settings.

| How the server was started | Discovery address |
|---|---|
| Quick start with `--no-launch-profile` | [HTTP on port 5000](http://localhost:5000/.well-known/openid-configuration) |
| Kestrel `IdentityServer` profile | [HTTPS on port 5001](https://localhost:5001/.well-known/openid-configuration), or [HTTP on port 5000](http://localhost:5000/.well-known/openid-configuration) |
| Visual Studio IIS Express profile | [HTTPS on port 44353](https://localhost:44353/.well-known/openid-configuration), or [HTTP on port 5000](http://localhost:5000/.well-known/openid-configuration) |

Use the ports of the profile actually running; starting both hosts on port 5000 causes an address conflict. A browser may sign in silently when it trusts localhost. Command-line clients need Windows authentication support too: PowerShell's `Invoke-WebRequest -UseDefaultCredentials` offers the current account. For curl, inspect `curl --version` for SSPI/SPNEGO support rather than assuming every Windows curl build can authenticate. These features let the client participate in the Windows challenge/response exchange.

### Testing with oidcdebugger.com

[oidcdebugger.com](https://oidcdebugger.com/) acts as a test client: it constructs a sign-in request and receives the result at its own callback. Use a test account and only the scopes needed for the test, because the returned claims/codes are sent to that external site. Keep IdentityServer running while testing. Fill in the fields below and select **Send Request**; a trusted browser can complete Windows sign-in silently, while another browser may prompt for credentials.

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

PowerShell form-encodes the values, so characters in a code are not accidentally treated as URL separators. Use the original client ID and full redirect URI, not just any allowed host, and redeem the code before its lifetime expires. For a confidential client, use its registered ID and supply `client_secret` from its configured secret source as well; keep the PKCE verifier if that request used PKCE. The token request authenticates the application, so it does not need the user's Windows password.

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

Read a sign-in attempt as a sequence of separate checks. Here, **client** means the application requesting sign-in, **claims** are facts about the person carried in a token, and **scopes** are the categories of claims the application requested.

| Log message or stage | Why it happens and what to check |
|---|---|
| `Authorization request rejected` | The application ID or return destination was not accepted. Check `IdentityServer:Clients` or `IdentityServer:Hosts`, as named in the message. A Windows password change will not fix application configuration. Redirect rejection logs include the host, not the full callback URL or query string. |
| `Issuing Negotiate challenge` | The server is asking the browser to prove the Windows identity. An initial HTTP 401 is normal. If the exchange repeats without progressing, check browser trust, client support for Windows authentication and the offered credentials. |
| `Reading Active Directory profile` | Windows sign-in has succeeded; the server is now fetching profile data using its own process account. A failure here points to a separate directory lookup, so check the exception, domain connectivity and the process account's read access. Local Windows accounts skip this step and have fewer claims. |
| Direct AD group counts and pattern matches | Only direct group memberships matching `IdentityServer:Groups` become role claims, and only when `roles` was requested. Nested memberships are not expanded. Zero matches can be a valid result. |
| `Authorization claims prepared` | Identity lookup and claim preparation succeeded. OpenIddict still has to build the protocol response; this message does not prove that the application has received or accepted a token. |
| `Token request rejected` | In code flow, the application makes a separate call to exchange its code for tokens. The logged client ID/secret failure concerns the application's credentials, not the person's Windows password. |
| `Token endpoint response sizes` | Lengths help diagnose oversized request headers without recording token credentials. Zero means that token was absent, not necessarily an error. This diagnostic covers `/connect/token`; implicit/hybrid tokens returned directly by `/connect/authorize` do not pass through it. |
| `Error building authorization claims ... during ...` | The stage identifies where preparation stopped. Read the attached exception for the actual cause; successful Windows authentication does not guarantee successful directory lookup or role-pattern processing. |

## Troubleshooting

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
Find `Error building authorization claims ... during ...` and read its exception. Windows can verify the person while the server's separate AD read fails, or a group pattern is invalid. Check the account and stage named in the log. Local accounts should skip AD; classification uses Windows' `Local account` SID (`S-1-5-113`). Older builds used the `Local` logon SID, which described how the person logged in rather than where the account lived, and could misclassify browser logons. Update an older deployment if that matches the failure.

**Browser prompts for Windows credentials repeatedly (401 loop).**
Check the logs before entering credentials repeatedly. In older versions, an unregistered client or rejected redirect produced HTTP 401, which IIS could turn into another password challenge even after Windows sign-in succeeded. Current authorization-request checks return HTTP 400 (`invalid_request`) for those failures. Correct the client/redirect configuration if it is named in the log. Otherwise, confirm both IIS authentication features are enabled and the browser trusts the hostname and is offering valid credentials.

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
NTLM can be a valid fallback, especially for local accounts. For a domain account where Kerberos is expected, check the hostname used by the client and the matching SPN. An SPN tells AD which account represents that HTTP service; a missing, duplicate or incorrectly owned entry can prevent ticket use. Ask the domain administrator to verify ownership against the actual host's authentication setup before registering one. Kestrel's Negotiate handler uses the service account; IIS ownership depends on its authentication configuration, so a generic “register it on the app pool account” command is not correct for every deployment. See [Windows authentication configuration](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/windowsauth?view=aspnetcore-10.0).

**Windows authentication succeeds sometimes but fails intermittently with Kerberos.**
One cause is **clock skew**, a time difference between machines. Kerberos uses time-limited tickets, so machines outside the domain's permitted time tolerance can reject otherwise valid credentials. Run `w32tm /query /status` to inspect Windows time synchronization and compare the server's time/source with the domain controller's. A resumed or restored virtual machine may need its time synchronization checked. Also inspect the authentication error rather than assuming every intermittent failure is a clock problem.

## License

[MIT](LICENSE). The license file contains the permission notice and conditions for reuse.
