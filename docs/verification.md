# Verification

Verify a deployment by completing a browser sign-in, redeeming the authorization code and
checking the claims. A discovery document that loads shows only that the server runs. This page
lists the checks, walks through a sign-in with a public debugging tool, and describes the tests.

## Checks for a new deployment

Run these from a client machine that trusts the HTTPS certificate. For the sign-in checks, the
machine must also hold a Kerberos ticket and trust the identity hostname for integrated
authentication.

| Check | What success shows |
|---|---|
| Open `<issuer>/.well-known/openid-configuration` without signing in. | The server runs and discovery is anonymous. The browser shows no credential prompt. |
| Read `issuer`, `authorization_endpoint`, `token_endpoint` and `jwks_uri` in that document, then open `jwks_uri`. | Every URL carries the public HTTPS hostname and the application path. An internal name, `http://` or a missing prefix points at the hosting or proxy settings. |
| Open the server's root URL or a made-up path. | 404 with no credential prompt. |
| Sign in from a registered application, or from the debugger below, with `openid profile email roles`. | Browser authentication, the directory read and the return to the application work. The log shows `Authorization claims prepared`. |
| Read the claims the application received. | The subject is the SID or the `ipaUniqueID`, the email is right, and the roles are the direct groups that match your patterns. A group reached only through a nested group is absent. |
| Sign in as a second user, and try once from a session with no ticket. | Each request gets its own identity. The attempt with no ticket gets a challenge and no token. |
| With `PersistKeys` on: check that both key files exist under `DataPath`, restart, and redeem a code issued just before the restart. | The keys persist and the service can read them. |
| On Linux or behind a proxy, run the platform checks too. | [Browser sign-in on Linux](linux-deployment.md#9-verify-kerberos-and-browser-sso) and [proxy verification](apache-and-nginx.md#verification). |

## Checking a sign-in with oidcdebugger.com

[oidcdebugger.com](https://oidcdebugger.com/) acts as the application: it builds an
authorization request and shows what comes back. **The result, with the person's claims, goes to
that external site.** Use a test account and only the scopes you need.

The repository's `appsettings.json` registers a client named `optional` and allows
`https://oidcdebugger.com` as a callback host. On a real deployment, add those two entries for
the test and remove them afterwards.

Fill in the form:

| Field | Value |
|---|---|
| Authorize URI | `<issuer>/connect/authorize`, such as `http://localhost:5000/connect/authorize` for a local run |
| Redirect URI | `https://oidcdebugger.com/debug` |
| Client ID | `optional` |
| Scope | `openid` first. Add `profile email roles` on a second run. |
| State | Any new random value |
| Nonce | The generated value |

Then choose a flow.

**Implicit, to check authentication and claims.** Set the response type to `id_token` and the
response mode to `form_post`, and send the request. The browser signs in, and the debugger shows
an ID token. The server log shows `Authorization claims prepared`.

**Authorization code with PKCE, to check the whole flow.** Set the response type to `code` and
the response mode to `query`, turn on PKCE with SHA-256, and set the token URI to
`<issuer>/connect/token`. The debugger receives a code and tries to redeem it. A browser may
refuse to call a plain HTTP token endpoint from the HTTPS debugger page. The code itself shows
that authorization succeeded, and you can redeem it from PowerShell:

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

Use the code and the verifier of the same request, before the code expires. A confidential
client adds its `client_secret`.

**Hybrid.** Set the response type to `code` plus `id_token` and the response mode to
`form_post`. Use it to test an existing client that expects both.

## Automated tests

```powershell
dotnet test IdentityServer.sln --configuration Release --filter 'Category!=LiveDirectory'
```

These tests run on Windows and Linux with no directory. GitHub runs the same command on each
pull request and before a release. They cover:

- **Settings.** Platform and provider defaults, misspelled values, trimming, and the hosting and
  proxy settings that must stop startup.
- **Authentication.** `Kerberos` mode refusing NTLM. The proxy headers when they are missing,
  wrong, duplicated or sent from an unknown address. Identities on reused connections.
- **Directory mapping.** Realm checks, LDAP filter escaping, SID encoding, and the profile and
  group queries for Active Directory, Samba AD and FreeIPA against a fake directory. On Linux,
  the StartTLS sequence against a local LDAP listener.
- **Sign-in.** The authorization code flow with PKCE through OpenIddict with a simulated
  identity, also behind a proxy prefix.
- **Token keys.** Persistence, wrong passwords and mismatched formats.
- **The people lookup.** Query splitting, global-scope limits, filter escaping, identifier
  parsing, application filters and the records' JSON. The tests that parse SIDs run on Windows
  only.

The tests simulate the browser's Kerberos exchange. Check the real exchange and the directory
access with the deployment checks above.

## Live directory tests

Tests in the `LiveDirectory` category read a real directory. They run on a developer's machine
only:

```powershell
dotnet test IdentityServer.sln --configuration Release --filter 'Category=LiveDirectory'
```

**The Active Directory tests** read the signed-in user's account through the `ActiveDirectory`
library and the people lookup. They run on Windows under a domain account, and skip when the
machine is signed in with a local account or cannot reach a domain controller.

**The LDAP test** reads one profile through the server's LDAP provider. It skips until you set
`IDENTITYSERVER_LDAP_TEST_CONFIG`:

| Variable | Value |
|---|---|
| `IDENTITYSERVER_LDAP_TEST_CONFIG` | The path of a private JSON file that holds an `IdentityServer:Directory` section. |
| `IDENTITYSERVER_LDAP_TEST_PRINCIPAL` | A known user's principal with its realm, such as `alice@EXAMPLE.COM`. Required. |
| `IDENTITYSERVER_LDAP_TEST_SUBJECT` | That user's SID or `ipaUniqueID`, read from the directory by other means. Required. |
| `IDENTITYSERVER_LDAP_TEST_GROUP` | Optional. A group the user is a direct member of. |
| `IDENTITYSERVER_LDAP_TEST_IdentityServer__Directory__BindPassword` | The read account's password. Leave it out when the process binds with a Kerberos credential cache. |

```powershell
dotnet test IdentityServer.sln --filter FullyQualifiedName~LdapIntegrationTests
```

With the first variable set, a connection, certificate or mapping problem fails the test. Run it
once for each directory provider you deploy.

## Local addresses during development

| How the server was started | Discovery address |
|---|---|
| `dotnet run --project IdentityServer.csproj --no-launch-profile -- --urls http://localhost:5000` | `http://localhost:5000/.well-known/openid-configuration` |
| `dotnet run --project IdentityServer.csproj --launch-profile IdentityServer` | `https://localhost:5001/...` or `http://localhost:5000/...` |
| Visual Studio, the IIS Express profile | `https://localhost:44353/...` |

The HTTPS profiles use the ASP.NET Core development certificate: run
`dotnet dev-certs https --trust` once. The IIS Express profile turns on Windows and anonymous
authentication and opens an implicit-flow request to oidcdebugger.com.

To sign in from a command line on Windows, use `Invoke-WebRequest -UseDefaultCredentials` or a
`curl` build with SPNEGO support.
