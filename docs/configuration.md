# Configuration

IdentityServer reads its settings from the `IdentityServer` section of `appsettings.json`. This
page describes each setting and the flows, scopes and claims the server issues. The directory
connection has its own page, [Directory providers](kerberos-and-ldap.md).

## Where settings come from

The host reads four sources. A later source overrides an earlier one for the same key.

1. `appsettings.json` in the application folder. Required.
2. `appsettings.Local.json` beside it. Optional. Git ignores it and `dotnet publish` leaves it
   out, so use it for values that belong to one machine.
3. Environment variables, with `__` between levels: `IdentityServer__PersistKeys=true`.
4. Command-line arguments: `--IdentityServer:PersistKeys=true`.

The host reads these sources only. It skips `appsettings.Production.json`,
`appsettings.Development.json` and .NET user secrets. Setting `ASPNETCORE_ENVIRONMENT` to
`Development` turns on the developer exception page.

- **Lists merge by index.** A `Hosts` or `Clients` list in a later source replaces entries
  position by position and leaves the rest in place. To remove an entry, edit the list in
  `appsettings.json`.
- **Restart the server after a change.** It reads the keys, the issuer, the authentication mode,
  the directory connection and the group patterns once.
- **Keep secrets out of the file.** Supply client secrets, the bind password, the proxy secret and
  the key-protection password as environment variables or in a protected local file. The second
  client's secret, for example, is `IdentityServer__Clients__1__ClientSecret`.

## A complete example

A server at `https://identity.example.com` that serves a public browser application and a
server-side application, persists its keys and maps two groups to roles:

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

On a domain-joined Windows server this is complete: the server authenticates with Negotiate and
reads Active Directory as its own account. On Linux, add a `Directory` section from
[Directory providers](kerberos-and-ldap.md) and supply `KeyProtectionPassword` from the
environment. The templates in [`deploy/`](../deploy/) are complete Linux examples.

## Server address

| Setting | Default | What it does |
|---|---|---|
| `ServerUri` | `*` | The issuer: the address applications find in discovery and in every token. Use the public base URL, including any application path, such as `https://apps.example.com/IdentityServer`. With `*`, an empty value or no setting, each request's URL supplies the issuer. |
| `UseForwardedHeaders` | `false` | Applies `X-Forwarded-*` headers, so the server sees the public scheme and hostname behind a proxy that terminates HTTPS. Leave it `false` under IIS. |
| `ReverseProxy:KnownProxies`, `ReverseProxy:AllowedHosts` | none | The proxy addresses and public hostnames that forwarded headers are accepted from. |
| `PathBase`, `PathBaseMode` | none, `Preserve` | A fixed public path prefix behind a proxy. Leave both unset under IIS, which supplies the application path. |

**Set `ServerUri` in production.** With an automatic issuer, a proxy's internal address or a
second hostname changes the issuer that tokens carry.

[Apache and Nginx](apache-and-nginx.md#application-configuration) describes the proxy settings in
full.

`AllowedHosts`, at the top level of the file, is ASP.NET Core's host filter: the hostnames the
server answers to, without ports. Set it to the identity hostname. It is separate from
`IdentityServer:Hosts`, which lists callback destinations.

## Token keys

The server signs tokens with one RSA key and encrypts authorization codes and access tokens with
another.

| Setting | Default | What it does |
|---|---|---|
| `PersistKeys` | `false` | With `true`, the server loads both keys from `DataPath` and creates them on the first start. With `false`, it creates new keys at every start. |
| `DataPath` | `keys` in the application folder | The folder that holds the two key files. The process needs read and write access. |
| `KeyProtectionPassword` | none | Selects how the key files are protected. See below. |

**Set `PersistKeys` to `true` on every production server.** After a restart with new keys, a
code issued earlier cannot be redeemed, and an application that cached the old signing key
rejects new tokens until it reads the JWKS again.

Choose an absolute `DataPath` outside the application folder so that an update of the
application keeps the keys.

| Platform | `KeyProtectionPassword` | Files | Protection |
|---|---|---|---|
| Windows | Omitted | `signing-key.bin`, `encryption-key.bin` | Machine-scoped DPAPI. Only the server that created the files can read them. |
| Windows or Linux | Set | `signing-key.pem`, `encryption-key.pem` | Password-encrypted PKCS#8 (AES-256-CBC, PBKDF2 with SHA-256, 200,000 iterations). On Linux the folder and files are readable by their owner only. |

Linux requires the password: the server stops at startup when `PersistKeys` is `true` without
one. Supply it as `IdentityServer__KeyProtectionPassword` from your secret store and keep it with
the backups of the key files.

A wrong password, a damaged file or a key file in the other format stops startup. The server
leaves the existing files as they are.

## Token lifetimes

Each value uses `hours:minutes:seconds`. Omit a setting to use OpenIddict's default.

| Setting | In the repository's file | What it limits |
|---|---|---|
| `AccessTokenLifetime` | `01:00:00` | How long an API accepts the access token. A shorter value drops stale roles sooner and causes more sign-ins. |
| `IdentityTokenLifetime` | `01:00:00` | How long the application may rely on the ID token. The application's session cookie has its own lifetime. |
| `AuthorizationCodeLifetime` | `00:05:00` | How long the application has to redeem a code. |

**Keep the code lifetime short and use PKCE.** The server stores no record of redeemed codes, so
a code stays redeemable until it expires.

The server issues no refresh tokens. When a token expires, the application starts a new sign-in,
which completes without a prompt for a browser that holds a ticket.

## Applications and callbacks

### `Clients`

The applications that may request sign-in.

```json
"Clients": [
  { "ClientId": "my-public-app" },
  { "ClientId": "my-server-app", "ClientSecret": "replace-with-a-generated-secret" }
]
```

- A **public client**, such as browser JavaScript or a desktop application, has no secret.
  Configure the client to use PKCE.
- A **confidential client**, such as a server-side web application, sends its `ClientSecret` when
  it redeems a code.

Client IDs are compared without regard to case. Secrets are compared exactly.

**List every client in production.** An empty or missing list accepts any client ID. An entry
with a `ClientId` of `"*"` matches any client ID, and a `ClientSecret` of `"*"` accepts any
secret.

### `Hosts`

Where sign-in results may be sent. Write each entry as an origin, such as
`https://app.example.com`.

| Used for | What is compared |
|---|---|
| The `redirect_uri` of an authorization request | The hostname only. `https://app.example.com` also allows other ports and paths on that host. |
| CORS, for browser applications that call the token endpoint | Scheme, host and port. For a callback on port 8443, list `https://app.example.com:8443`. |

All clients share the list. With no entries, the server rejects every authorization request.
Configure the exact callback URL in the application.

## Roles from groups

`Groups` is a list of regular expressions. The server matches each one, without regard to case,
against the common name of every group the person is a direct member of. Each matching group
becomes one `role` claim when the application requests the `roles` scope.

```json
"Groups": [ "^MyApp Readers$", "^MyApp Editors$" ]
```

- Use regular-expression syntax: `^MyApp .*$` matches every group whose name starts with
  `MyApp `. The file wildcard `MyApp *` has a different meaning in a regular expression.
- The server reads direct memberships only. A group reached through a nested group, and the
  Active Directory primary group, produce no role.
- An empty list issues no roles. The startup log says so.
- Keep the patterns narrow. A person in hundreds of matching groups gets hundreds of role claims
  and a large token. The log warns when a person's role names exceed 4,096 characters.

## Authentication mode

`Authentication:Mode` selects who verifies the browser and which protocols are accepted.

| Value | Behavior |
|---|---|
| `Auto` (default) | `Negotiate` on Windows. `Kerberos` on Linux, and on Windows with the `FreeIpa` provider. |
| `Negotiate` | Accepts Kerberos or NTLM, including local Windows accounts. Rejected at startup with the `FreeIpa` provider. |
| `Kerberos` | Accepts Kerberos only. The server refuses an identity that arrived by NTLM, at the authorization endpoint and on protected application routes, including an identity that IIS supplies. |
| `ProxyKerberos` | Accepts the principal that a Kerberos-authenticating Apache or Nginx passes in a header. Needs `Authentication:ProxySecret`, the [proxy trust settings](apache-and-nginx.md#application-configuration) and an LDAP directory provider. |

`Authentication:ProxySecret` is the secret the proxy sends with each request in `ProxyKerberos`
mode: 32 to 1,024 characters with no whitespace.

The `Directory` section selects the directory and its connection. Both it and the mode are
checked at startup: a misspelled value, a bind name without a password, or a provider that cannot
run on the platform stops the server with a message that names the setting.

## Logging

Serilog writes to the console and to `logs/identityserver-YYYYMMDD.log` under the working
directory, and keeps 14 daily files. The `Serilog:MinimumLevel` section sets the level:

```json
"Serilog": {
  "MinimumLevel": {
    "Default": "Information",
    "Override": { "Microsoft": "Warning", "Microsoft.Hosting.Lifetime": "Information" }
  }
}
```

`Information` records the startup choices and the result of each sign-in. To see each stage of a
sign-in, add `"IdentityServer": "Debug"` to `Override`. The levels, from most to least detail,
are `Verbose`, `Debug`, `Information`, `Warning`, `Error` and `Fatal`.
[Troubleshooting](troubleshooting.md#reading-the-logs) explains the messages.

Logs contain account names, client IDs and network addresses. Review them before you share them.

## Flows, scopes and claims

### Endpoints

| Endpoint | Path |
|---|---|
| Discovery | `/.well-known/openid-configuration` |
| Authorization | `/connect/authorize` |
| Token | `/connect/token` |
| Signing keys | The `jwks_uri` in the discovery document |

Every path follows the application path, when there is one. The server has no userinfo endpoint
and no sign-out endpoint: the claims travel in the tokens, and the application ends its own
session.

### Flows

| Flow | `response_type` | Use it for |
|---|---|---|
| Authorization code | `code` | New applications, with PKCE. |
| Implicit | `id_token`, `token`, `id_token token` | Older clients that cannot make a back-channel request. |
| Hybrid | `code id_token` and its variants | Clients that need a token from the authorization response and a code as well. |

The server checks a PKCE verifier when the client sent a challenge, and accepts a code request
without one. Configure each client to use PKCE.

The server accepts plain HTTP, for development. Most OpenID Connect clients require HTTPS
metadata, so deploy on HTTPS.

### Scopes and claims

Request `openid` and the other scopes the application needs.

| Scope | Claim | `ClaimTypes` constant | Value |
|---|---|---|---|
| `openid` | `sub` | | The Windows SID, or the FreeIPA `ipaUniqueID`. |
| `openid` | Name | `Name` | The display name. |
| `profile` | Account name | `WindowsAccountName` | `DOMAIN\user` on Windows. With an LDAP provider, the Kerberos principal, or `NETBIOS\user` when `Directory:NetbiosDomain` is set. |
| `profile` | Given name, surname, phone | `GivenName`, `Surname`, `HomePhone` | The directory's `givenName`, `sn` and `telephoneNumber`, when they have a value. |
| `email` | Email | `Email` | The directory's `mail` attribute, or `username@localhost` when it is empty. |
| `roles` | Role | `Role` | One claim for each direct group that matches `Groups`. |

A local Windows account gets `sub`, its user name as the display name, its account name and the
`@localhost` email.

Apart from `sub`, the claim names are the URIs of the .NET `ClaimTypes` constants, such as
`http://schemas.xmlsoap.org/ws/2005/05/identity/claims/emailaddress` for the email and
`http://schemas.microsoft.com/ws/2008/06/identity/claims/role` for a role. A .NET application
reads them with the constants. Other platforms map the URIs.

The server puts every claim in both the ID token and the access token. Access tokens are
encrypted with the server's encryption key, which is OpenIddict's default. An API that validates
them needs OpenIddict validation, or another validator that holds the decryption key. See
[OpenIddict token formats](https://documentation.openiddict.com/configuration/token-formats).
