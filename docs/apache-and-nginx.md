# Apache and Nginx hosting

The Linux hosting arrangement is a separate Kestrel process, supervised by systemd, behind Apache or Nginx. On Windows, IIS supplies its own authentication and application path.

Start with the [Linux deployment guide](linux-deployment.md) for package installation, keytab provisioning, LDAP/HTTPS trust, accounts and file permissions. This page describes the proxy-specific behavior and templates. See the [live validation record](validation-2026-10-31.md) for the tested provider/hosting matrix and fixes.

The supplied examples use **ProxyKerberos**: the front server authenticates the browser with Kerberos and forwards the full principal to IdentityServer over an authenticated, restricted backend connection. IdentityServer then reads the selected LDAP directory and issues OIDC tokens. This avoids passing a connection-bound Negotiate exchange through a generic backend connection pool.

Apache requires **mod_auth_gssapi**. Nginx requires the **SPNEGO authentication module**, available in distributions such as Debian/Ubuntu as `libnginx-mod-http-auth-spnego`; it is not part of stock Nginx. Both examples disable password/Basic fallback and preserve the principal's realm. See the [Apache module](https://github.com/gssapi/mod_auth_gssapi) and [Nginx module](https://github.com/stnoonan/spnego-http-auth-nginx-module) documentation. Forwarding native Kestrel Negotiate requires a proxy with the [connection affinity Microsoft specifies](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/windowsauth?view=aspnetcore-10.0#kestrel); ordinary keepalive or IP stickiness is insufficient. These templates use proxy authentication instead.

## Deployment files

| Repository file | Deployment purpose |
|---|---|
| [appsettings.Proxy.json](../deploy/appsettings.Proxy.json) | Starting point for `/opt/identityserver/appsettings.json`; adapt the directory, public hostname, client and callback settings. |
| [identityserver.service](../deploy/identityserver.service) | systemd unit; Kestrel listens only on `127.0.0.1:5080`. |
| [identityserver.env.example](../deploy/identityserver.env.example) | Private environment file for application secrets. |
| [Apache virtual host](../deploy/apache/identityserver.conf) | HTTPS, Kerberos, safe headers and `/IdentityServer` proxy mapping. |
| [Apache secret include](../deploy/apache/proxy-secret.apache.conf.example) | The proxy's copy of the shared attestation secret. |
| [Nginx server](../deploy/nginx/identityserver.conf) | HTTPS, SPNEGO authentication and `/IdentityServer` locations. |
| [Nginx proxy snippet](../deploy/nginx/identityserver-proxy.conf) | Headers shared by authenticated and anonymous locations. |
| [Nginx secret include](../deploy/nginx/proxy-secret.nginx.conf.example) | The proxy's copy of the shared attestation secret. |

These files are templates and are deliberately excluded from application publish output. Install the chosen files separately. Do not deploy their placeholder domains, client identifiers or secret values unchanged.

## Application configuration

Configure proxy authentication, the public URL and forwarding:

```json
{
  "IdentityServer": {
    "ServerUri": "https://identity.example.com/IdentityServer",
    "PathBase": "/IdentityServer",
    "PathBaseMode": "Preserve",
    "UseForwardedHeaders": true,
    "ReverseProxy": {
      "KnownProxies": ["127.0.0.1"]
    },
    "Authentication": { "Mode": "ProxyKerberos" }
  }
}
```

Keep the appropriate [LDAP provider settings](kerberos-and-ldap.md). Proxy principals have no Windows token or SID claims, so ProxyKerberos requires `ActiveDirectory`, `SambaAd` or `FreeIpa`, including when Kestrel runs on Windows. LDAP resolves AD/Samba subjects from `objectSid` and FreeIPA subjects from `ipaUniqueID`. The full realm-qualified principal must reach the backend; do not enable local-name mapping at the proxy.

| Setting | Behavior |
|---|---|
| `PathBase` | Optional fixed public application path, such as `/IdentityServer`. Omit for IIS integration or root hosting. Matching incoming prefixes are separated before routing and authentication; other paths return 404. |
| `PathBaseMode` | `Preserve` (default): proxy forwards the complete path. `Strip`: proxy removes the prefix, so the app restores its configured PathBase, accepting this arrangement only from an explicitly trusted proxy. |
| `UseForwardedHeaders` | Enable with `true` for these examples. Uses host-configured options when no `ReverseProxy` section is supplied. |
| `ReverseProxy:KnownProxies` | Exact TCP peer IP addresses, not DNS names or subnets. Trust is determined before forwarded headers change the apparent client address. IPv4-mapped IPv6 addresses are handled. |
| `ReverseProxy:AllowedHosts` | Exact public hostnames without ports. Defaults to the hostname in an explicit `ServerUri`. Required when issuer selection is automatic (`ServerUri: "*"`). |
| `Authentication:Mode` | Explicitly select `ProxyKerberos` to accept a proxy-authenticated principal. Auto/native modes never trust the identity header. |
| `Authentication:ProxySecret` | A random shared secret, at least 32 non-whitespace characters, supplied separately through `IdentityServer__Authentication__ProxySecret`. |

Forwarding accepts **one hop** and requires matching counts of `X-Forwarded-For`, `X-Forwarded-Proto` and `X-Forwarded-Host`. Templates overwrite client-supplied values and send exactly one of each. `X-Forwarded-Prefix` is ignored; the path comes from deployment configuration. For an additional external load balancer, adapt and validate the entire trust chain rather than adding arbitrary forwarded-header values or clearing the trust lists.

Let `UseIdentityServer` apply the forwarded headers after capturing the connection peer. Do not enable `ASPNETCORE_FORWARDEDHEADERS_ENABLED` or put a separate forwarded-header middleware ahead of it in this mode.

`ServerUri` is the OIDC issuer, not a routing switch. When an explicit `PathBase` is used, the URI must include that same path. Discovery, authorization, token and signing-key URLs then use the public prefix. A host-supplied/IIS PathBase is used without adding it twice. With IIS integration, omit `PathBase` and `PathBaseMode` so IIS supplies the application path.

### Backend trust

The proxy sends `X-IdentityServer-User` and `X-IdentityServer-Proxy-Secret`. IdentityServer accepts a principal only when all of the following hold:

- ProxyKerberos is explicitly enabled.
- The **original connection peer** matches a configured proxy address.
- The backend connection is loopback or uses actual TLS; forwarded `https` does not make an off-host HTTP connection safe.
- Exactly one secret matches the deployment secret, and exactly one full `user@REALM` principal is supplied.

This mode trusts the configured proxy's Kerberos verification; it does not independently revalidate a Kerberos ticket at Kestrel. Only a Kerberos-authenticating location may set the user header. The examples clear incoming user headers elsewhere and overwrite the shared-secret header. Duplicate/malformed values and failed verification cannot fall back to native Negotiate. A missing proxy identity produces 403 when authentication is required, indicating that the front server did not authenticate that route. Invalid authorization requests can instead return an OIDC error; neither case starts a backend Negotiate exchange.

Keep Kestrel on loopback for the supplied same-machine arrangement. For separate machines, configure HTTPS on the backend, validate its certificate at the proxy, and restrict backend access to the trusted proxy. Keep secret includes outside the web root and out of source control, access logs, diagnostics and shared configuration dumps (`nginx -T` prints includes). The application does not log secret values.

## Installation

1. Install ASP.NET Core 10 and the LDAP client dependencies described in the [Linux guide](linux-deployment.md#2-install-the-runtime-and-client-libraries). Install Apache with mod_auth_gssapi or Nginx with the SPNEGO module, plus Kerberos libraries and the relevant TLS modules. Configure DNS, time synchronization, `/etc/krb5.conf` and LDAP CA trust.
2. Provision `HTTP/identity.example.com@EXAMPLE.COM` for the public hostname and put the keytab where the chosen **proxy** can read it. Match the principal to the hostname clients actually use. The proxy handles inbound Kerberos; IdentityServer still needs independent LDAP read credentials or a renewable service credential cache.
3. Publish with `dotnet publish IdentityServer.csproj -c Release -r linux-x64 --self-contained false -o artifacts/linux` (change architecture if needed). Copy the result into `/opt/identityserver`. Create a dedicated `identityserver` service user/group and give it read access to application files and write access to `/opt/identityserver/logs`. The unit's `StateDirectory` creates `/var/lib/identityserver` for persistent keys.
4. Adapt `deploy/appsettings.Proxy.json` and install it as the deployment's **complete** `appsettings.json`. The sample uses Samba AD and a read-account UPN; for FreeIPA change the provider/server/base/realm and supply a suitable bind DN. Replace the example client and callback host. Keep `AllowedHosts` set to the public identity hostname. Check any `appsettings.Local.json` or environment overrides as well; configuration arrays merge by index, so partial overrides can retain old demo clients/hosts.
5. Create `/etc/identityserver/identityserver.env` from the example and fill the proxy secret, an independent token-key protection password, and the LDAP bind password. `openssl rand -base64 32` can generate each random secret separately. Use the **same proxy secret** in the chosen proxy's private include, but a **different** value for token-key protection. Restrict these files to the administrator; systemd reads the environment file before switching users. Preserve the token-key secret across restarts. The sample intentionally fails startup until required secrets are supplied.
6. Install the unit as `/etc/systemd/system/identityserver.service`, then run `systemctl daemon-reload` and `systemctl enable --now identityserver`. Its working directory is `/opt/identityserver`; `/opt/identityserver/logs` must exist and be writable. If using a Kerberos credential cache for outbound LDAP, place it in an accessible service-owned location: the unit isolates `/tmp` and protects home directories. A dedicated TLS LDAP bind account is usually simpler with this unit.
7. Install the chosen proxy configuration as described below, adapt certificate paths, run its configuration check, then reload the proxy. Do not remove authentication directives to work around a missing module.

### Apache

Install the virtual-host template in the distribution's site configuration directory, and the secret include as `/etc/identityserver/proxy-secret.apache.conf`. Enable `ssl`, `proxy`, `proxy_http`, `headers`, `alias`, and `auth_gssapi`. Adapt the keytab path if the distribution uses `/etc/httpd` rather than `/etc/apache2`.

Run `apachectl configtest` (or the distribution's equivalent), enable the site, and reload Apache. The sample restricts the authentication mechanism to `krb5`, disables Basic fallback and preserves `REMOTE_USER` as a full principal. Only `/IdentityServer/connect/authorize` requires user authentication; discovery/JWKS and token requests remain accessible to OIDC clients.

### Nginx

Install the server template in the distribution's active configuration directory, the common snippet as `/etc/nginx/snippets/identityserver-proxy.conf`, and the secret include as `/etc/identityserver/proxy-secret.nginx.conf`. Confirm the SPNEGO module is installed and loaded. Run `nginx -t`, then reload Nginx.

The authorization location uses `auth_gss_format_full on` and `auth_gss_allow_basic_fallback off`. It clears the SPNEGO module's synthesized `Authorization` header before forwarding. The anonymous location preserves `Authorization`, allowing OIDC token clients to use their own client authentication. Both locations include the complete proxy-header snippet because Nginx header directive inheritance changes when a location defines its own headers. The `proxy_pass` directives contain no URI suffix, so the full `/IdentityServer/...` path reaches Kestrel.

## Root hosting and stripped prefixes

For `https://identity.example.com/`, remove `PathBase` (or set it to `/`), use `PathBaseMode: Preserve`, and set `ServerUri` to the root URL. Change the proxy's authenticated location to `/connect/authorize` and its general location to `/`. Remove the `/IdentityServer` redirect. For the Nginx template, replace its final catch-all 404 location with the general proxy location so there is only one `location /`.

Prefer preserving the prefix, as the supplied templates do. If an existing proxy strips it, set `PathBaseMode: Strip` while leaving the public `PathBase` and `ServerUri` unchanged. On Apache the backend side of the `ProxyPass`/`ProxyPassReverse` mappings becomes `http://127.0.0.1:5080/`. On Nginx, the general `/IdentityServer/` location can use `proxy_pass http://127.0.0.1:5080/`, while the exact authorization location must use `proxy_pass http://127.0.0.1:5080/connect/authorize`. Authentication locations always match the **public** path. An untrusted connection cannot activate the stripped-prefix mapping.

For an embedded host with other protected routes, add equivalent Kerberos-authenticated locations for those routes. Anonymous endpoints must clear the identity header. The base standalone server exposes no application controllers, so the supplied templates authenticate only the OIDC authorization endpoint.

## Verification

The automated suite covers public discovery/JWKS URLs, authorization-code redemption through a prefix, preserved and stripped paths, anonymous token requests, IIS-provided paths, unknown proxies, spoofed forwarding headers, invalid/duplicate/missing secrets and usernames, off-host TLS enforcement, native-mode isolation and separate identities on reused backend connections. Run `dotnet test IdentityServer.sln`.

Before enabling a real deployment:

1. Run the proxy's configuration check with the installed authentication module and actual includes. Confirm Kestrel is bound only to loopback for the sample arrangement.
2. Fetch `https://identity.example.com/IdentityServer/.well-known/openid-configuration` without user credentials. Verify the issuer and every advertised endpoint use the expected HTTPS hostname and prefix; follow `jwks_uri` anonymously.
3. From a client with a Kerberos ticket and browser trust for the public hostname, complete authorization-code/PKCE sign-in with a registered application. Confirm the directory subject and allowed roles. Discovery succeeding alone does not demonstrate Kerberos authentication.
4. Attempt requests with caller-supplied identity/forwarding headers and no Kerberos authentication. The proxy must challenge authorization requests and must clear identity headers on anonymous paths. Direct backend requests with a missing/wrong secret must not authenticate.
5. Test two different users sequentially over reused connections; no identity may carry over. Confirm token redemption needs only client/code credentials, and unknown/root paths do not expose a second copy of prefixed protocol endpoints.
6. Restart IdentityServer and verify saved-key continuity, then check service/proxy logs without exposing secrets. For Windows/IIS deployments, also run the [IIS verification checks](../README.md#hosting-in-iis).

Proxy template syntax, module behavior, live Kerberos and LDAP interoperability require the actual Apache/Nginx environment. Passing the in-process tests or publishing Linux binaries does not substitute for those deployment checks.

The Nginx proxy snippet reserves a 16 KiB response-header buffer for authorization-code redirects. The encrypted code travels in the `Location` header and can exceed Nginx's default 4/8 KiB buffer even with a modest profile. A `502` with `upstream sent too big header` in the Nginx error log indicates this response limit. Keep role claims limited to the application's needs; if larger redirects are required, adjust `proxy_buffer_size` and the corresponding `proxy_buffers` together. See [Nginx response buffering](https://nginx.org/en/docs/http/ngx_http_proxy_module.html#proxy_buffer_size).

Set the Nginx template's `auth_gss_realm` to the service keytab's realm and the application's `Directory:Realm`. This matters when several realms share a host: the installed SPNEGO module may otherwise look for the HTTP service in the host's default realm and fail with `No key table entry found`, even when the keytab contains the correct principal in another realm. Keep `auth_gss_format_full on` so the backend receives the realm-qualified username.
