# Verification

Discovery loading is not a sign-in. This page lists the checks that prove a deployment
works end to end, walks through a sign-in with a public test client, and describes the
automated and live test suites.

## Contents

- [Checks for a new deployment](#checks-for-a-new-deployment)
- [Checking a sign-in with oidcdebugger.com](#checking-a-sign-in-with-oidcdebuggercom)
- [Automated tests](#automated-tests)
- [Live directory tests](#live-directory-tests)
- [Local addresses during development](#local-addresses-during-development)

## Checks for a new deployment

Run these from a client machine that trusts the HTTPS certificate and, for the sign-in
steps, holds a Kerberos ticket and trusts the identity hostname.

| Check | What success tells you |
|---|---|
| Open `<issuer>/.well-known/openid-configuration` without signing in | Hosting works and discovery is anonymous. There must be no credential prompt. |
| Read `issuer`, `authorization_endpoint`, `token_endpoint` and `jwks_uri` in that document, then open `jwks_uri` | Every URL uses the public HTTPS hostname and any application path. An internal name, `http://` or a missing prefix points at hosting or proxy settings. |
| Open the server root or a made-up path | 404 with no credential prompt. The standalone server has no home page. |
| Sign in from a registered application, or from the debugger below, with `openid profile email roles` | Browser authentication, the directory read and the return to the application all work. The log shows `Authorization claims prepared`. |
| Inspect the claims the application received | The subject is the SID or `ipaUniqueID`, the email is right, and the roles are exactly the direct groups that match your patterns. A group reachable only through nesting must be absent. |
| Sign in as a second user, and once from a session with no ticket | Identities do not leak between requests, and the no-ticket attempt gets a challenge and no token. |
| With `PersistKeys` on, confirm both key files exist under `DataPath`, restart, and redeem a code issued just before the restart | Keys persist and are readable by the service. |
| On Linux or behind a proxy, also run the platform checks | [Linux browser SSO](linux-deployment.md#9-verify-kerberos-and-browser-sso) and [proxy verification](apache-and-nginx.md#verification). |

## Checking a sign-in with oidcdebugger.com

[oidcdebugger.com](https://oidcdebugger.com/) builds an authorization request and shows
what comes back, acting as the application. The result, including the person's claims, is
sent to that external site, so use a test account and only the scopes you need.

The repository's demonstration `appsettings.json` already registers a client named
`optional` and allows `https://oidcdebugger.com` as a return host. On a real deployment, add
those two entries temporarily and remove them afterwards.

Fill in the form:

| Field | Value |
|---|---|
| Authorize URI | `<issuer>/connect/authorize`, for example `http://localhost:5000/connect/authorize` for the quick start |
| Redirect URI | `https://oidcdebugger.com/debug` |
| Client ID | `optional` |
| Scope | `openid` first; add `profile email roles` on a second run |
| State | Any fresh random value |
| Nonce | Keep the generated value |

Then choose a flow.

**Implicit, the quickest check.** Response type `id_token`, response mode `form_post`.
Select *Send Request*. The browser signs in (silently on a domain-joined machine) and the
debugger shows an ID token. The server log shows `Authorization claims prepared`. This
proves browser authentication and the directory read without a token exchange.

**Authorization code with PKCE, what a real application does.** Response type `code`,
response mode `query`, *Use PKCE* on with SHA-256, Token URI `<issuer>/connect/token`.
The debugger receives a code and tries to exchange it. A browser may refuse to call a plain
HTTP token endpoint from the HTTPS debugger page; in that case the code arriving at all
already proves authorization worked, and you can finish the exchange yourself:

```powershell
$tokenRequest = @{
    grant_type    = "authorization_code"
    code          = "<code shown by the debugger>"
    redirect_uri  = "https://oidcdebugger.com/debug"
    client_id     = "optional"
    code_verifier = "<verifier shown for that same request>"
}
Invoke-RestMethod -Method Post -Uri "http://localhost:5000/connect/token" `
    -ContentType "application/x-www-form-urlencoded" -Body $tokenRequest
```

Use the code and verifier from the same request, before the code expires. A confidential
client would add `client_secret`. This step needs no Windows credentials; it authenticates
the application, not the person.

**Hybrid.** Response type `code` plus `id_token`, response mode `form_post`. Useful only when
testing an existing client that expects both.

## Automated tests

```powershell
dotnet test IdentityServer.sln
```

The suite runs on Windows and Linux without a directory. It covers:

- Configuration: platform and provider defaults, rejected misspellings, trimming, and
  hosting and proxy settings that must fail at startup.
- Authentication policy: Kerberos-only mode refusing NTLM, proxy attestation with missing,
  wrong, duplicate or spoofed headers, forwarded-header limits, and identities not leaking
  across reused connections.
- Directory mapping: realm checks, LDAP filter escaping, SID encoding, AD, Samba AD and
  FreeIPA profile and group queries against a fake directory, and the Linux StartTLS
  sequence against a loopback LDAP listener.
- The full authorization-code and PKCE flow through OpenIddict with a simulated
  authenticated identity, including paths behind a proxy prefix.
- Token-key persistence, wrong passwords and format mismatches.

Tests that need a real Windows domain skip themselves when none is reachable. Filter with
`--filter Category=LiveDirectory` to run only those, or `--filter "Category!=LiveDirectory"`
to exclude them.

Passing tests show the server logic is right. They do not exercise a real Kerberos
handshake or a real directory; the checks above do.

## Live directory tests

One opt-in test reads a real profile through the portable LDAP provider. Set these
environment variables and run it on a machine that can reach the directory:

| Variable | Value |
|---|---|
| `IDENTITYSERVER_LDAP_TEST_CONFIG` | Absolute path to a private JSON file containing an `IdentityServer:Directory` section |
| `IDENTITYSERVER_LDAP_TEST_PRINCIPAL` | A known user's realm-qualified principal, `alice@EXAMPLE.COM` |
| `IDENTITYSERVER_LDAP_TEST_SUBJECT` | That user's SID or `ipaUniqueID`, obtained independently |
| `IDENTITYSERVER_LDAP_TEST_GROUP` | Optional: a group the user is a direct member of |
| `IDENTITYSERVER_LDAP_TEST_IdentityServer__Directory__BindPassword` | The read account's password, unless the process has a Kerberos credential cache |

```powershell
dotnet test IdentityServer.sln --filter FullyQualifiedName~LdapIntegrationTests
```

When enabled, a connection, certificate or mapping failure fails the test rather than
skipping it. Run it once per provider you deploy.

## Local addresses during development

| How the server was started | Discovery address |
|---|---|
| `dotnet run ... --no-launch-profile -- --urls http://localhost:5000` | `http://localhost:5000/.well-known/openid-configuration` |
| `dotnet run --project IdentityServer.csproj --launch-profile IdentityServer` | `https://localhost:5001/...` or `http://localhost:5000/...` |
| Visual Studio, IIS Express profile | `https://localhost:44353/...` |

The HTTPS profiles use the ASP.NET Core development certificate; run
`dotnet dev-certs https --trust` once. The IIS Express profile enables Windows and anonymous
authentication and opens an implicit-flow request against the debugger.

For command-line sign-in on Windows, `Invoke-WebRequest -UseDefaultCredentials` and curl
builds with SPNEGO support both work.
