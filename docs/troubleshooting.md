# Troubleshooting

Start with the log. Every sign-in passes through the same stages, and the first stage that
fails tells you which of the [three credentials](how-it-works.md#three-separate-credentials)
to look at. Fixing a later stage never repairs an earlier one: a client secret will not fix
a browser that cannot get a ticket, and a browser setting will not fix a directory read.

Linux-specific problems with Kerberos, keytabs, LDAP TLS, systemd and the proxies are in the
[Linux troubleshooting table](linux-deployment.md#troubleshooting). This page covers the
server itself and Windows hosting.

## Contents

- [Reading the logs](#reading-the-logs)
- [Startup problems](#startup-problems)
- [Browser authentication problems](#browser-authentication-problems)
- [Directory and claims problems](#directory-and-claims-problems)
- [Application and token problems](#application-and-token-problems)

## Reading the logs

Logs go to the console and to `logs/identityserver-YYYYMMDD.log` under the working
directory. Startup lines state the resolved authentication mode and provider, the issuer,
the allowed hosts, whether keys persist, and whether any group patterns are configured.

For more detail, raise this project's level without touching the framework's:

```powershell
dotnet run --project IdentityServer.csproj --no-launch-profile -- --urls http://localhost:5000 --Serilog:MinimumLevel:Override:IdentityServer=Debug
```

On a deployment, add `"IdentityServer": "Debug"` under `Serilog:MinimumLevel:Override` and
restart. Set it back afterwards.

Messages in the order a sign-in produces them:

| Message | What it means |
|---|---|
| `Authorization request rejected` | The `client_id` or the callback host was not accepted. The message names which. Fix `Clients` or `Hosts`; nothing about the person is involved yet. |
| `Issuing Negotiate challenge` | The server asked the browser for a ticket. One of these per sign-in is normal. Many in a row with nothing after them means the browser is not completing the exchange. |
| `Reading directory profile` | The browser authenticated. Anything that goes wrong after this is the directory read: provider, realm, connectivity, certificate trust or the read credential. |
| `Built claims for ...` (Debug) | How many direct groups were found and how many matched the patterns. Zero matches is a valid result. |
| `Authorization claims prepared` | Identity and claims are ready; OpenIddict builds the response. The application has not necessarily received anything yet. |
| `Authorization denied for user ... during ...` | A warning. The person authenticated, but directory policy refused them: wrong realm, an unqualified name, no or several matching accounts, or a missing SID or `ipaUniqueID`. The application receives `access_denied`. |
| `Error building authorization claims ... during ...` | An error with an exception. The stage says where. Usually connectivity, TLS trust, a bind failure or an invalid group pattern. The application receives `server_error`. |
| `Token request rejected` | The application's code exchange failed its own authentication: wrong `client_id` or `client_secret`. Not related to the person's password. |
| `Token endpoint response sizes` (Debug) | Token lengths, to diagnose oversized headers. Values are never logged. |

## Startup problems

**The service exits immediately with a configuration message.**
The message names the key. Common causes: `Directory:Server` missing on Linux, `BindDn`
without `BindPassword` or the reverse, `PersistKeys` on Linux without
`KeyProtectionPassword`, a misspelled `Provider` or `Mode`, or `ProxyKerberos` without
`ReverseProxy` settings. Nothing falls back to a default; fix the setting.

**Startup fails reading a key file.**
The password is wrong, the file is damaged, or a key exists in the other format (`.bin`
versus `.pem`). The server never overwrites a key. Restore the matching password or file;
delete the keys only if you accept that outstanding tokens become invalid.

**The published application fails before discovery loads.**
Check that the ASP.NET Core 10 runtime, or the Hosting Bundle under IIS, is installed, that
`appsettings.json` is in the working directory, and that the process account can read the
application folder and write to the key and log folders. `appsettings.Local.json` is not
published, so a value that worked in development may be missing. Under IIS, read the
Windows Application event log when the process does not start at all.

**The home page returns 404.**
There is no home page. Open `/.well-known/openid-configuration`, including the application
path if there is one. If that also returns 404, the port, IIS application path or published
folder is wrong.

## Browser authentication problems

**Every authorization request gets a 401 with `WWW-Authenticate: Negotiate` and never
completes.**
The browser is not completing the Kerberos exchange. Check, in order: the browser trusts
the hostname for integrated authentication ([Windows](windows-iis.md#browser-trust),
[Linux](linux-deployment.md#9-verify-kerberos-and-browser-sso)); the client holds a ticket
(`klist`); the hostname in the address bar matches the registered SPN; the clocks agree.
`Negotiate is not supported with HTTP/2` in the log means the client did not drop to
HTTP/1.1 for the exchange, which browsers normally do on their own.

**The browser shows a credential prompt instead of signing in silently.**
The hostname is not trusted for integrated authentication. See [Browser trust](windows-iis.md#browser-trust)
for Edge, Chrome and Firefox. Typing credentials into that prompt works, but the trust
setting is still missing.

**The log says a domain user authenticated with NTLM, not Kerberos.**
Expected for local accounts. For a domain user it means no usable ticket: the SPN is
missing, duplicated or on the wrong account, or the browser used an IP address or a name
with no SPN. [Register the SPN](windows-iis.md#kerberos-and-the-service-principal-name) on
the account that IIS or the service uses to decrypt tickets. `Authentication:Mode` set to
`Kerberos` turns this into a refusal instead of a silent fallback.

**Kerberos works most of the time but fails intermittently.**
Compare the server's clock with the domain controller (`w32tm /query /status` on Windows,
`timedatectl` on Linux), especially after a VM restore or resume. Kerberos tolerates only a
few minutes of skew.

**Behind Apache or Nginx, the backend returns 403.**
The proxy did not supply a verified principal, or the backend did not trust it. Check that
the authorization location in the proxy has the authentication directives, that the shared
secret matches in both places, that `KnownProxies` lists the proxy's actual peer address,
and that the backend is reached over loopback or TLS. The [proxy guide](apache-and-nginx.md#backend-trust)
lists every condition.

## Directory and claims problems

**The person signs in, but the application gets `access_denied`.**
Find `Authorization denied` in the log. The directory could not map the authenticated name
to exactly one account. Check the realm in `Directory:Realm` against the principal, the user
search base, and that the account exists once. On AD and Samba, a UPN suffix that differs
from the realm is matched by `sAMAccountName` inside the configured base.

**The person signs in, but the application gets `server_error`.**
Find `Error building authorization claims` and read the exception. The browser proved who
the person is while the server's separate directory read failed: wrong server or port, an
untrusted certificate, a bind account with the wrong password, or a regex in `Groups` that
does not compile. Local Windows accounts skip the directory entirely.

**Expected roles are missing.**
In order: the application requested the `roles` scope; the person is a **direct** member of
the group; the group's common name matches a `Groups` pattern (these are regular
expressions, so `^MyApp .*$`, not `MyApp *`); the server was restarted after the pattern
was edited; and the application obtained a new token. The Debug log line with group counts
shows whether memberships were found and then filtered out.

**Tokens are huge, or an application returns 431 Request Header Fields Too Large.**
Too many groups match the patterns, so the access token carries hundreds of role claims.
Narrow the patterns to the groups the application actually uses. The log warns at roughly
4 KB of role names and when an access token exceeds 8 KB.

## Application and token problems

**Authorization is rejected with `invalid_request` for `client_id` or `redirect_uri`.**
The client ID is not in `Clients`, or the callback's hostname is not in `Hosts`. The log
names the failing check and the host.

**The application receives a code but the token exchange fails.**
The exchange must use the same `client_id`, the exact callback URL, the PKCE verifier that
goes with that request and, for a confidential client, the right `client_secret`. A code
from an earlier request, an expired code, or a restart with ephemeral keys in between all
fail. If no token request appears in the log, the application never reached the server;
look at its network errors and CORS settings.

**Tokens issued before a restart fail afterwards.**
The keys changed. Enable `PersistKeys`, keep `DataPath` and its files across deployments, and
check the process can read them. On Windows without a password the files are bound to the
machine that created them. Persistence protects future keys; it cannot recover ones already
replaced.

**The issuer in tokens is not the address applications use.**
Set `ServerUri` to the public address explicitly. Behind a proxy, also enable
`UseForwardedHeaders` with `ReverseProxy:KnownProxies`, and confirm the discovery document
shows the public hostname.

**An API cannot validate the access token.**
Access tokens are encrypted, as OpenIddict does by default. A generic JWT validator that
only has the public signing keys cannot read them; use OpenIddict validation or configure
decryption. Do not send the ID token to the API as a workaround.

**An application refuses the discovery document or tokens over plain HTTP.**
Most OpenID Connect clients require HTTPS metadata and HTTPS for cross-site cookies. Deploy
IdentityServer on HTTPS; the server's willingness to serve HTTP does not relax the client.
