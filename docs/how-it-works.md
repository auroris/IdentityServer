# How it works

This page explains what happens during a sign-in, which credentials are involved, and
where IdentityServer stops and your application takes over. Read it once before deploying;
the platform guides assume these ideas.

## The words you will meet

| Term | Meaning in this project |
|---|---|
| Client, relying party, application | The web application asking for sign-in. It identifies itself with a `client_id`. |
| Issuer, authority | IdentityServer's public base URL, for example `https://identity.example.com`. Tokens name it, and clients configure it as their authority. |
| Redirect URI, callback | The application URL that receives the sign-in result. |
| Scope | A category of information the application asks for: `openid`, `profile`, `email`, `roles`. |
| Claim | One fact inside a token, such as the person's name or a role. |
| ID token | Tells the application who signed in. |
| Access token | Presented by the application to an API. It is not interchangeable with the ID token. |
| Authorization code | A short-lived value the browser carries back to the application, which exchanges it for tokens. |

## A sign-in, step by step

1. The person opens a protected page. The application redirects the browser to
   IdentityServer's authorization endpoint with its `client_id`, callback URL and scopes.
2. IdentityServer checks the `client_id` against its client list and the callback host
   against its allowed hosts. Anything unknown is rejected before any authentication starts.
3. The browser is asked to authenticate with Negotiate. A browser that trusts the hostname
   and holds a Kerberos ticket answers silently. On Linux behind Apache or Nginx, the proxy
   performs this step and passes the verified principal to the backend.
4. IdentityServer reads the person's profile from the directory, and their direct group
   memberships if the application asked for `roles`. On a domain-joined Windows host this
   uses the service account's own access; otherwise it uses the LDAP settings you configure.
5. IdentityServer sends the browser back to the application's callback with an
   authorization code.
6. The application exchanges the code at the token endpoint, with PKCE and, for a
   confidential client, its secret. It receives an ID token and an access token, checks
   them, and starts its own session.

From then on the application owns the session. IdentityServer keeps no record of who is
signed in and has no sign-out page; the application decides when to send the browser back
for a fresh token.

## Three separate credentials

It helps to keep these apart when something fails, because each one can be wrong on its own.

| Credential | Who holds it | What it is for |
|---|---|---|
| The person's Kerberos ticket | The browser | Proves who is at the keyboard. On Linux the server, or the proxy, needs an HTTP service keytab to accept it. |
| The directory read credential | IdentityServer | Reads profiles and groups. On a domain-joined Windows host this is the service account. Elsewhere it is a dedicated read account, or a Kerberos credential cache the service keeps renewed. |
| The token keys | IdentityServer | The signing key lets applications verify tokens; the encryption key protects authorization codes. Persist them so a restart does not invalidate outstanding tokens. |

A successful browser sign-in therefore does not prove that the server can read the
directory, and a working directory read does not prove the keys will survive a restart.
The [verification guide](verification.md) checks each one separately.

Proxy hosting adds a fourth item: a shared secret that lets the backend trust the identity
header the proxy sends. The [Apache and Nginx guide](apache-and-nginx.md) explains how that
trust is established.

## What the server stores, and what it does not

- **Keys.** With `PersistKeys` enabled, two key files under `DataPath`. Nothing else is
  written except logs.
- **No user database.** Profiles are read from the directory on every sign-in. A group
  change takes effect on the next sign-in, not on tokens already issued.
- **No token store.** The server runs OpenIddict in its store-free "degraded" mode. Codes and
  tokens are self-contained, so there is no revocation list; choose lifetimes accordingly.
  Authorization codes are short-lived and protected by PKCE.
- **No refresh tokens.** When a token expires the application starts a new sign-in, which is
  silent for a browser with a ticket.

## Where authentication happens inside the server

Two kinds of request arrive, and they are protected differently.

- **Protocol endpoints** (`/.well-known/openid-configuration`, `/connect/authorize`,
  `/connect/token`, JWKS) are handled by OpenIddict. Discovery, JWKS and the token endpoint
  are anonymous so that applications can configure themselves and redeem codes without a
  browser credential. Only the authorization endpoint asks the browser to authenticate.
- **Application routes** in an embedded host are protected by a fallback policy: every
  routed endpoint requires sign-in unless it opts out with `[AllowAnonymous]` or names its
  own authentication scheme. Unknown paths, including `/favicon.ico`, return 404 without a
  credential prompt.

With native authentication, the first response to an authorization request is a `401` with
`WWW-Authenticate: Negotiate`. That is the browser being asked for a ticket, not an error.
The browser repeats the request with the ticket over HTTP/1.1 and the exchange completes.
Behind Apache or Nginx the proxy does this exchange, and the backend never issues a
Negotiate challenge of its own; a request that reaches it without a verified principal gets
a `403`.

## Trade-offs to know about

- Keys are generated fresh at every start unless you enable persistence. On a production
  host, enable it.
- The server trusts the directory's view of group membership and reads only direct
  memberships. Nested groups are not expanded, and the Active Directory primary group is
  not included.
- Browsers must be configured to trust the identity hostname for integrated authentication.
  Chrome, Edge and Firefox each have a policy or preference for this; the platform guides
  show where.
- Each sign-in opens a fresh connection to the directory. That is deliberate: it keeps the
  server free of long-lived connection state that could go stale.
