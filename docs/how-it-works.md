# How it works

IdentityServer authenticates a person through the directory and issues OpenID Connect tokens to
your application, which uses them to start its own session. This page explains the sign-in
sequence, the credentials involved and what the server stores.

## A sign-in, step by step

1. The person opens a protected page. The application redirects the browser to
   `/connect/authorize` with its `client_id`, callback URL and scopes.
2. IdentityServer checks the `client_id` against `Clients` and the callback's hostname against
   `Hosts`. It rejects an unknown client or host before any authentication starts.
3. IdentityServer answers `401` with `WWW-Authenticate: Negotiate`. A browser that trusts the
   hostname and holds a Kerberos ticket repeats the request with the ticket, over HTTP/1.1. Under
   IIS, IIS performs this exchange. Behind Apache or Nginx, the proxy performs it and passes the
   verified principal to the server.
4. IdentityServer reads the person's profile from the directory, and their direct group
   memberships when the application asked for the `roles` scope.
5. IdentityServer redirects the browser to the application's callback with an authorization
   code.
6. The application sends the code to `/connect/token`, with its PKCE verifier and, for a
   confidential client, its secret. It receives an ID token and an access token, validates them
   and starts its session.

The application manages its session and its sign-out, and starts a new sign-in when it needs
fresh tokens. IdentityServer keeps no sign-in session of its own.

## Three separate credentials

| Credential | Who holds it | What it does |
|---|---|---|
| The person's Kerberos ticket | The browser | Proves who is signing in. On Linux the server, or the proxy, needs an HTTP service keytab to accept it. |
| The directory read credential | IdentityServer | Reads profiles and groups. On a domain-joined Windows host this is the account the server runs as. Elsewhere it is a dedicated read account, or a Kerberos credential cache that the service keeps renewed. |
| The token keys | IdentityServer | The signing key lets applications verify tokens. The encryption key protects authorization codes and access tokens. |

Each one fails separately, so verify each one separately: complete a browser sign-in, read a
directory profile, and redeem a code after a restart. [Verification](verification.md) gives the
steps.

Proxy hosting adds a shared secret, which lets the server trust the identity header the proxy
sends. [Apache and Nginx](apache-and-nginx.md#backend-trust) explains that trust.

## What the server stores

- **Token keys.** With `PersistKeys` on, the server writes two key files under `DataPath`. These
  files and the logs are all the data it keeps.
- **Nothing about people.** Each sign-in opens a directory connection, reads the current profile
  and closes the connection. A group change takes effect at the person's next sign-in. Tokens
  issued earlier keep their claims until they expire.
- **Nothing about tokens.** OpenIddict runs without a database (its degraded mode), so codes and
  tokens are self-contained. The server cannot revoke a token or mark a code as used. Choose
  lifetimes that limit how long an issued token stays valid, keep the code lifetime short, and
  configure clients to use PKCE.
- **No refresh tokens.** When a token expires, the application starts a new sign-in, which
  completes without a prompt for a browser that holds a ticket.

## Which requests need a signed-in browser

- **Protocol endpoints.** OpenIddict serves discovery, the signing keys, `/connect/authorize` and
  `/connect/token`. Discovery, the signing keys and the token endpoint are anonymous, so an
  application can configure itself and redeem a code without a browser credential. Only the
  authorization endpoint asks the browser to authenticate.
- **Application routes** in an [embedded host](embedding.md). Every routed endpoint requires
  sign-in unless it carries `[AllowAnonymous]` or names its own authentication scheme.
- **Unknown paths**, including `/favicon.ico` and the root URL, return 404 with no credential
  prompt.

In `ProxyKerberos` mode the server leaves the challenge to the proxy. A request that needs
sign-in and arrives without a verified principal gets `403`.

## Limits to plan for

- The server reads direct group memberships only. Nested groups and the Active Directory primary
  group produce no role.
- Browsers send a ticket only to a hostname they trust for integrated authentication. Chrome,
  Edge and Firefox each have a policy for this, shown in the
  [Windows](windows-iis.md#browser-trust) and
  [Linux](linux-deployment.md#9-verify-kerberos-and-browser-sso) guides.
