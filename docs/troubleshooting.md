# Troubleshooting

Find the first stage of the sign-in that failed in the log, then look up the symptom below. Each
stage depends on one [credential](how-it-works.md#three-separate-credentials): the browser's
Kerberos ticket, the server's directory read credential, or the token keys.

This page covers the server and Windows hosting. For Kerberos, keytabs, LDAP TLS, systemd and the
proxies on Linux, use the [Linux troubleshooting table](linux-deployment.md#troubleshooting).

## Reading the logs

The server logs to the console and to `logs/identityserver-YYYYMMDD.log` under its working
directory. At startup it logs the authentication mode and directory provider, whether the keys
persist, the issuer, the allowed hosts, and a line when no group patterns are configured.

Some of the messages below are written at Debug level. To see them, add
`"IdentityServer": "Debug"` under `Serilog:MinimumLevel:Override`, restart, and remove the entry
afterwards. From a checkout:

```powershell
dotnet run --project IdentityServer.csproj --no-launch-profile -- --urls http://localhost:5000 --Serilog:MinimumLevel:Override:IdentityServer=Debug
```

Messages in the order a sign-in produces them:

| Message | Level | Meaning |
|---|---|---|
| `Authorization request rejected` | Warning | The `client_id` is not in `Clients`, or the callback's hostname is not in `Hosts`. The message names which. |
| `Issuing Negotiate challenge` | Debug | The server asked the browser for a ticket. One per sign-in is normal. Many in a row with nothing after them mean the browser does not complete the exchange. |
| `ProxyKerberos request has no verified principal` | Warning | In `ProxyKerberos` mode, the request arrived without the proxy's identity header. The browser gets `403`. |
| `Browser authentication failed` | Warning | The Negotiate exchange or the proxy check failed. The application receives `access_denied`. |
| `Authorization rejected: authentication type` | Warning | The identity arrived by a protocol that the configured mode refuses, such as NTLM in `Kerberos` mode. The application receives `access_denied`. |
| `Reading directory profile` | Debug | Browser authentication succeeded and the directory read starts. |
| `Built claims for` | Debug | The number of direct groups found and the number that matched `Groups`. Zero matches is a valid result. |
| `Authorization claims prepared` | Information | The claims are ready and OpenIddict builds the response. |
| `Authorization denied for user ... during ...` | Warning | The person authenticated, and the directory lookup refused the identity: another realm, a name with no realm, no account or several accounts, or an account with no SID or `ipaUniqueID`. The application receives `access_denied`. |
| `Error building authorization claims ... during ...` | Error | An exception, with the stage it happened in: usually connectivity, certificate trust, a failed bind or an invalid `Groups` pattern. The application receives `server_error`. |
| `Token request rejected` | Warning | The application's `client_id` or `client_secret` is wrong at the token endpoint. |
| `Token endpoint response sizes` | Debug | The lengths of the tokens, to diagnose oversized headers. |

## Startup problems

**The service exits at once with a configuration message.**
The message names the setting. Common causes: no `Directory:Server` on Linux, `BindDn` without
`BindPassword` or the reverse, `PersistKeys` on Linux without `KeyProtectionPassword`, a
misspelled `Provider` or `Mode`, or `ProxyKerberos` without the `ReverseProxy` settings or the
secret. Correct the setting and restart.

**Startup fails while reading a key file.**
The password is wrong, the file is damaged, or the key exists in the other format (`.bin` or
`.pem`). The server leaves the files as they are. Restore the matching password or file. Delete
the keys only when you accept that outstanding codes and tokens become invalid.

**The published application fails before discovery loads.**
Check that the ASP.NET Core 10 runtime, or the Hosting Bundle under IIS, is installed. Check that
`appsettings.json` is in the working directory, and that the process account can read the
application folder and write to the key and log folders. `appsettings.Local.json` is left out of
the publish output, so a value that worked in development may be missing. Under IIS, read the
Windows Application event log when the process does not start.

**The home page returns 404.**
The root URL always returns 404. Open `/.well-known/openid-configuration`, after the application
path when there is one. When that returns 404 too, check the port, the IIS application path and
the published folder.

## Browser authentication problems

**Every authorization request gets `401` with `WWW-Authenticate: Negotiate` and never
completes.**
The browser does not complete the Kerberos exchange. Check in this order:

1. The browser trusts the hostname for integrated authentication
   ([Windows](windows-iis.md#browser-trust),
   [Linux](linux-deployment.md#9-verify-kerberos-and-browser-sso)).
2. The client holds a ticket (`klist`).
3. The hostname in the address bar matches the registered SPN.
4. The clocks agree.

**The browser shows a credential prompt.**
Add the hostname to the browser's integrated-authentication settings, as
[Browser trust](windows-iis.md#browser-trust) describes. Credentials typed into the prompt also
complete the sign-in.

**The log shows that a domain user signed in with NTLM.**
`Authorization claims prepared` names the protocol. NTLM is expected for a local account. For a
domain user it means the browser had no usable ticket: the SPN is missing, duplicated or on the
wrong account, or the browser used an IP address or a name without an SPN.
[Register the SPN](windows-iis.md#kerberos-and-the-service-principal-name) on the account that
decrypts the tickets. Set `Authentication:Mode` to `Kerberos` to refuse NTLM.

**Kerberos fails now and then.**
Compare the server's clock with the domain controller's (`w32tm /query /status` on Windows,
`timedatectl` on Linux), above all after a virtual machine was restored or resumed. Kerberos
allows only a few minutes of difference.

**Behind Apache or Nginx, the server returns 403.**
The proxy sent no principal, or the server did not trust it. Check that the proxy's authorization
location has the authentication directives, that the secret is the same on both sides, that
`KnownProxies` lists the address the proxy connects from, and that the proxy reaches the server
over loopback or TLS. [Backend trust](apache-and-nginx.md#backend-trust) lists every condition.

## Directory and claims problems

**The person signs in, but the application gets `access_denied`.**
Find `Authorization denied` in the log. The server could not map the authenticated name to
exactly one account. Compare `Directory:Realm` with the principal's realm, check the user search
base, and check that one account matches. On Active Directory and Samba, an account whose UPN
suffix differs from the realm is matched by `sAMAccountName` under the configured base.

**The person signs in, but the application gets `server_error`.**
Find `Error building authorization claims` and read the exception. The browser proved who the
person is, and the server's own directory read failed: a wrong server or port, an untrusted
certificate, a wrong bind password, or a `Groups` pattern that is not a valid regular expression.
A local Windows account involves no directory read.

**Expected roles are missing.**
Check in this order:

1. The application requested the `roles` scope.
2. The person is a **direct** member of the group.
3. The group's common name matches a `Groups` pattern. The patterns are regular expressions:
   write `^MyApp .*$`, not `MyApp *`.
4. The server was restarted after the pattern changed.
5. The application obtained a new token.

The Debug message `Built claims for` shows how many groups were found and how many matched.

**Tokens are large, or an application returns 431 Request Header Fields Too Large.**
Too many groups match the patterns, so the access token carries too many role claims. Narrow the
patterns to the groups the application uses. The server logs a warning when a person's role
names exceed 4,096 characters, and when an access token from the token endpoint exceeds 8,192
characters.

## Application and token problems

**Authorization is rejected with `invalid_request` for `client_id` or `redirect_uri`.**
The client ID is not in `Clients`, or the callback's hostname is not in `Hosts`. The log names
the check and the host.

**The application receives a code, but the token request fails.**
The request must carry the `client_id`, the exact callback URL and the PKCE verifier of the
authorization request, and a confidential client's `client_secret`. The code must not have
expired, and the server must still hold the keys that protected it: a restart without persisted
keys invalidates the code. When the log shows no token request, look at the application's network
errors and at CORS.

**Tokens issued before a restart fail afterwards.**
The keys changed. Set `PersistKeys` to `true`, keep `DataPath` and its files across deployments,
and check that the process can read them. On Windows without a password, only the machine that
created the files can read them. Persisting keys does not bring back keys that were already
replaced.

**The issuer in tokens is not the address that applications use.**
Set `ServerUri` to the public address. Behind a proxy, also set `UseForwardedHeaders` and
`ReverseProxy:KnownProxies`, and check that discovery shows the public hostname.

**An API cannot validate the access token.**
Access tokens are encrypted, which is OpenIddict's default. Use OpenIddict validation, or a
validator that holds both the signing key and the decryption key. Send the API the access token,
not the ID token.

**An application refuses the discovery document or the tokens over plain HTTP.**
Most OpenID Connect clients require HTTPS metadata, and browsers require HTTPS for cross-site
cookies. Deploy IdentityServer on HTTPS.
