# Configuration

Configure sign-in, clients and token settings in the `IdentityServer` section of
`appsettings.json`. Use `Serilog` for logging and the ASP.NET Core `Kestrel` and
`AllowedHosts` settings for listeners and host filtering.

Directory and proxy settings have their own pages: [Directory providers](kerberos-and-ldap.md)
and [Apache and Nginx](apache-and-nginx.md).

## Contents

- [Where settings come from](#where-settings-come-from)
- [A complete example](#a-complete-example)
- [Server identity and hosting](#server-identity-and-hosting)
- [Token keys](#token-keys)
- [Token lifetimes](#token-lifetimes)
- [Applications and callbacks](#applications-and-callbacks)
- [Roles from groups](#roles-from-groups)
- [Authentication and directory](#authentication-and-directory)
- [Logging](#logging)
- [Flows, scopes and claims](#flows-scopes-and-claims)

## Where settings come from

Settings are read in this order. A later source overrides an earlier one for the same key.

1. `appsettings.json` in the application folder. Required.
2. `appsettings.Local.json` beside it. Optional, ignored by Git and excluded from publish
   output. Use it for machine-specific development values.
3. Environment variables, with `__` between levels: `IdentityServer__PersistKeys=true`.
4. Command-line arguments: `--IdentityServer:PersistKeys=true`.

The host does not load `appsettings.Production.json`, `appsettings.Development.json` or
.NET user secrets. `ASPNETCORE_ENVIRONMENT` controls development behaviour such as detailed
error pages.

Configuration changes follow these rules:

- **Lists merge by index.** A `Hosts` or `Clients` list in a later source overrides entries
  position by position and leaves the rest in place. To remove a demonstration client, edit
  the full list in `appsettings.json`.
- **Restart after changes.** Keys, the issuer, the authentication mode and the group patterns
  are read once at startup.

Store client secrets, bind passwords and the key-protection password in environment
variables or a protected local file outside source control.
For example, the second client's secret is `IdentityServer__Clients__1__ClientSecret`.

## A complete example

A server at `https://identity.example.com` serving a public browser application and a
server-side application, with keys persisted and two groups mapped to roles:

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
    "UseForwardedHeaders": false,
    "PersistKeys": true,
    "DataPath": "C:\\IdentityServerData\\keys",
    "AccessTokenLifetime": "01:00:00",
    "IdentityTokenLifetime": "01:00:00",
    "AuthorizationCodeLifetime": "00:05:00",
    "Hosts": [ "https://app.example.com" ],
    "Clients": [
      { "ClientId": "my-public-app" },
      { "ClientId": "my-server-app", "ClientSecret": "replace-with-a-generated-secret" }
    ],
    "Groups": [ "^MyApp Readers$", "^MyApp Editors$" ]
  }
}
```

On Windows this uses the defaults: Negotiate authentication and the domain-joined server's
own directory access. On Linux, add a `Directory` section from the
[directory guide](kerberos-and-ldap.md) and a `KeyProtectionPassword` from the environment.
The templates in [`deploy/`](../deploy/) are complete Linux examples.

## Server identity and hosting

### `IdentityServer:ServerUri`

The **issuer**: the address applications expect to find in discovery and in every token.
Use the public base URL, including any application path, such as
`https://apps.example.com/IdentityServer`.

With `"*"`, an empty value or an absent setting, the issuer comes from each request's URL.
This supports testing with different development addresses. In production, set the issuer
explicitly so a proxy's internal address or a second hostname cannot change it. Configure
listeners and HTTPS separately through the host.

### `IdentityServer:UseForwardedHeaders`

Defaults to `false`. Set it to `true` when a reverse proxy terminates HTTPS and forwards to
IdentityServer over HTTP, so the server sees the public scheme and hostname. Pair it with
`IdentityServer:ReverseProxy:KnownProxies` listing the proxy's IP address. The
[Apache and Nginx guide](apache-and-nginx.md#application-configuration) explains these and
the related `PathBase` and `PathBaseMode` settings. Leave it `false` under IIS.

### `AllowedHosts`

Standard ASP.NET Core host filtering: the hostnames this server answers to, without ports.
Set it to the identity hostname in production. `IdentityServer:Hosts` controls callback
destinations and CORS origins.

## Token keys

### `IdentityServer:PersistKeys`

Whether token keys survive a restart.

- `false` (default in the repository file): new keys every start. Codes issued before the
  restart cannot be redeemed, and applications that cached the old signing key reject new
  tokens until they refresh. Use this setting for temporary development instances.
- `true`: load the key files from `DataPath`, creating them on first start.

### `IdentityServer:DataPath`

The folder holding the two key files. Defaults to `keys` under the application folder;
choose an absolute path outside it so application updates preserve the keys. The process needs
read and write access.

### `IdentityServer:KeyProtectionPassword`

How the key files are protected at rest.

- **Windows, password omitted:** `signing-key.bin` and `encryption-key.bin`, encrypted with
  machine-scoped DPAPI. They can only be read on the server that created them.
- **Any OS, with a password:** `signing-key.pem` and `encryption-key.pem`, encrypted
  PKCS#8. Linux requires this. Supply it as `IdentityServer__KeyProtectionPassword` from
  your secret store and keep it with the backups of the key files.

A wrong password, a damaged file or a key in the other format stops startup and leaves the
existing key files intact.

## Token lifetimes

All three use `hours:minutes:seconds`. Omit a value to keep OpenIddict's default.

| Key | Repository default | Meaning |
|---|---|---|
| `AccessTokenLifetime` | `01:00:00` | How long an API should accept the access token. Shorter means stale roles expire sooner, at the cost of more sign-ins. |
| `IdentityTokenLifetime` | `01:00:00` | How long the application may rely on the ID token. The application's own session cookie has its own lifetime. |
| `AuthorizationCodeLifetime` | `00:05:00` | How long the application has to redeem a code. Keep it short; there is no store of used codes, so PKCE and a short lifetime are the protection. |

Applications obtain fresh tokens by starting a new sign-in when a token expires. This is
silent for a browser holding a ticket. Refresh tokens are not supported.

## Applications and callbacks

### `IdentityServer:Clients`

The applications allowed to ask for sign-in.

```json
"Clients": [
  { "ClientId": "my-public-app" },
  { "ClientId": "my-server-app", "ClientSecret": "replace-with-a-generated-secret" }
]
```

- A **public client** (browser JavaScript, a desktop app) cannot keep a secret, so its entry
  has none. Require PKCE in the client.
- A **confidential client** (a server-side web application) presents its `ClientSecret` when
  redeeming a code. The secret authenticates the application during that exchange.

Client IDs are matched case-insensitively; secrets are exact. An empty or missing list
accepts **any** client ID, and a `ClientId` of `"*"` or a `ClientSecret` of `"*"` act as
wildcards. Use explicit entries in production.

### `IdentityServer:Hosts`

Where sign-in results may be sent, as origins such as `https://app.example.com`.

| Used for | What is compared |
|---|---|
| The `redirect_uri` on an authorization request | Hostname only. `https://app.example.com` also allows other ports and paths on that host. |
| CORS, for browser applications calling the token endpoint | Scheme, host and port. For a callback on port 8443, list `https://app.example.com:8443`. |

The list is shared by all clients. With no entries, every authorization request is rejected.
The application still configures its exact callback URL on its side.

## Roles from groups

### `IdentityServer:Groups`

Regular expressions, matched case-insensitively against the **common name** of each group
the person is a direct member of. A match becomes one `role` claim, provided the application
requested the `roles` scope.

```json
"Groups": [ "^MyApp Readers$", "^MyApp Editors$" ]
```

- Use regex syntax: `^MyApp .*$` matches every group starting with `MyApp `, and `MyApp *`
  does not mean what a file wildcard would.
- Nested memberships are not expanded, and on AD the primary group is not included.
- An empty list issues no roles. The startup log says so.
- A person with hundreds of matching groups gets hundreds of role claims and a large
  token; the log warns past about 4 KB of role names. Keep patterns narrow.

## Authentication and directory

### `IdentityServer:Authentication:Mode`

| Value | Behaviour |
|---|---|
| `Auto` (default) | `Negotiate` on Windows, `Kerberos` on Linux and whenever the provider is FreeIPA. |
| `Negotiate` | Accept Kerberos or NTLM, including local machine accounts. Windows only. |
| `Kerberos` | Accept Kerberos only. An identity that arrived by NTLM is refused. |
| `ProxyKerberos` | Trust a principal supplied by a Kerberos-authenticating Apache or Nginx. Needs the [proxy trust settings](apache-and-nginx.md#application-configuration). |

### `IdentityServer:Directory`

Which directory to read and how. `Provider` is `Auto`, `Windows`, `ActiveDirectory`,
`SambaAd` or `FreeIpa`. On Windows with no `Server` setting, `Auto` means the Windows
provider, which uses the service account and automatic domain discovery. Everything else is
explicit LDAP and is described in [Directory providers](kerberos-and-ldap.md).

Both settings are checked at startup. A misspelled value, a half-configured bind account or
a provider that cannot run on this platform stops the server with a message naming the key.

## Logging

Serilog writes to the console and to `logs/identityserver-YYYYMMDD.log` under the working
directory, keeping 14 files. The `Serilog:MinimumLevel` section controls volume:

```json
"Serilog": {
  "MinimumLevel": {
    "Default": "Information",
    "Override": { "Microsoft": "Warning", "Microsoft.Hosting.Lifetime": "Information" }
  }
}
```

`Information` records startup decisions and each sign-in's outcome. For troubleshooting,
add `"IdentityServer": "Debug"` to `Override` to keep framework logging at its current
level. Levels, most to least verbose: `Verbose`, `Debug`, `Information`,
`Warning`, `Error`, `Fatal`. The [troubleshooting guide](troubleshooting.md) explains the
messages.

Logs contain account names, client IDs and network addresses. Review them before sharing.

## Flows, scopes and claims

### Flows

| Flow | `response_type` | Use it for |
|---|---|---|
| Authorization code | `code` | New applications. Pair it with PKCE. |
| Implicit | `id_token`, `token`, `id_token token` | Older clients that cannot do a back-channel exchange. |
| Hybrid | `code id_token` and variants | Clients that need a token immediately and a code as well. |

PKCE is supported but not enforced server-side; configure the client to use it. The server
accepts plain HTTP for development, but applications normally require HTTPS metadata and
cookies, so deploy on HTTPS.

### Scopes and claims

Request `openid` always, and only the other scopes the application needs.

| Scope | Claims |
|---|---|
| `openid` | `sub` (the Windows SID, or the FreeIPA `ipaUniqueID`) and the display name |
| `profile` | The account name (`DOMAIN\user`, or the Kerberos principal), given name, surname and phone when the directory has them |
| `email` | The directory `mail` attribute, or `username@localhost` as a placeholder when it is empty |
| `roles` | One `role` per direct group matching `Groups` |

A local Windows machine account receives `sub`, its name and the placeholder email.

Apart from `sub`, claim names are the .NET `ClaimTypes` URIs, for example
`http://schemas.xmlsoap.org/ws/2005/05/identity/claims/emailaddress` for email and
`http://schemas.microsoft.com/ws/2008/06/identity/claims/role` for roles. .NET applications
use the `ClaimTypes` constants; other platforms must map these full URI names.

Claims are placed in both the ID token and the access token. Access tokens are encrypted
with the server's encryption key, as OpenIddict does by default, so an API validating them
needs OpenIddict validation or an equivalent with access to the decryption key. See
[OpenIddict token formats](https://documentation.openiddict.com/configuration/token-formats).
