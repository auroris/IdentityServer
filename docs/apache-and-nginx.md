# Apache and Nginx hosting

Apache or Nginx can terminate HTTPS and verify the browser's Kerberos ticket for IdentityServer
on Linux. The proxy passes the verified principal to a Kestrel process on loopback, which reads
the directory and issues the tokens. This page describes the settings for that arrangement, how
the server trusts the proxy, and the proxy templates. Prepare the packages, keytab, certificates
and service with the [Linux guide](linux-deployment.md) first.

A Negotiate exchange belongs to one TCP connection. When the proxy completes the exchange, it
can pool its connections to the server. A proxy that forwards the exchange to Kestrel must keep
one backend connection for each client connection.

You need one of these modules:

- **Apache:** [mod_auth_gssapi](https://github.com/gssapi/mod_auth_gssapi), packaged on Debian
  and Ubuntu as `libapache2-mod-auth-gssapi`.
- **Nginx:** the
  [SPNEGO authentication module](https://github.com/stnoonan/spnego-http-auth-nginx-module),
  packaged as `libnginx-mod-http-auth-spnego`.

## Deployment files

| Repository file | Purpose |
|---|---|
| [appsettings.Proxy.json](../deploy/appsettings.Proxy.json) | The starting point for `/opt/identityserver/appsettings.json`. Set the directory, public hostname, client and callback host. |
| [identityserver.service](../deploy/identityserver.service) | The systemd unit. Kestrel listens on `127.0.0.1:5080` only. |
| [identityserver.env.example](../deploy/identityserver.env.example) | The environment file for the server's secrets. |
| [apache/identityserver.conf](../deploy/apache/identityserver.conf) | The Apache virtual host: HTTPS, Kerberos, header handling and the `/IdentityServer` mapping. |
| [apache/proxy-secret.apache.conf.example](../deploy/apache/proxy-secret.apache.conf.example) | Apache's copy of the shared secret. |
| [nginx/identityserver.conf](../deploy/nginx/identityserver.conf) | The Nginx server: HTTPS, SPNEGO authentication and the `/IdentityServer` locations. |
| [nginx/identityserver-proxy.conf](../deploy/nginx/identityserver-proxy.conf) | The headers that the authenticated and anonymous locations share. |
| [nginx/proxy-secret.nginx.conf.example](../deploy/nginx/proxy-secret.nginx.conf.example) | Nginx's copy of the shared secret. |

Install the templates separately from the publish output, and replace their example hostnames,
client and secrets.

## Application configuration

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

| Setting | Default | What it does |
|---|---|---|
| `Authentication:Mode` | `Auto` | Set it to `ProxyKerberos` to accept the principal the proxy passes. The other modes ignore the identity header. |
| `Authentication:ProxySecret` | none | The secret the proxy sends with every request: 32 to 1,024 characters with no whitespace. Supply it as `IdentityServer__Authentication__ProxySecret`. |
| `UseForwardedHeaders` | `false` | Set it to `true`. Without a `ReverseProxy` section, the server uses the forwarded-header options the host configured. |
| `ReverseProxy:KnownProxies` | none | The IP addresses the proxy connects from. Each entry is one address: the server rejects host names, subnets and wildcard addresses at startup. An IPv4 address also matches its IPv4-mapped IPv6 form. |
| `ReverseProxy:AllowedHosts` | The hostname of `ServerUri` | The public hostnames accepted in `X-Forwarded-Host`, without ports. Required when `ServerUri` is `*`. |
| `PathBase` | none | The public path prefix, such as `/IdentityServer`. In `Preserve` mode, a request outside the prefix returns 404. Leave it unset for root hosting and under IIS. |
| `PathBaseMode` | `Preserve` | `Preserve`: the proxy forwards the full path. `Strip`: the proxy removes the prefix and the server restores it, for requests from a known proxy only. |

The server checks these settings at startup:

- `ProxyKerberos` requires the `ReverseProxy` section, `UseForwardedHeaders` set to `true`, the
  secret and an [LDAP directory provider](kerberos-and-ldap.md): `ActiveDirectory`, `SambaAd` or
  `FreeIpa`. This also applies when Kestrel runs on Windows.
- A `ReverseProxy` section requires `UseForwardedHeaders` set to `true` and at least one
  `KnownProxies` address.
- With `PathBase` set, an explicit `ServerUri` must end in the same path.
- `PathBaseMode: Strip` requires `PathBase`, `KnownProxies` and `UseForwardedHeaders`.

**Forwarded headers.** The server accepts one proxy hop. It reads `X-Forwarded-For`,
`X-Forwarded-Proto` and `X-Forwarded-Host`, which must carry the same number of values. The
templates overwrite what the client sent and send one of each. The server ignores
`X-Forwarded-Prefix`: the path comes from `PathBase`. With a load
balancer in front of the proxy, check at every hop which peers and headers that hop accepts.

**Leave forwarded-header handling to `UseIdentityServer`.** It records the connecting address
before it applies the headers. Do not set `ASPNETCORE_FORWARDEDHEADERS_ENABLED`, and do not put
another forwarded-header middleware in front of it.

**Keep the full principal.** The directory lookup needs `user@REALM`, so leave local-name mapping
off at the proxy. The templates do.

### Backend trust

The proxy sends the principal in `X-IdentityServer-User` and the secret in
`X-IdentityServer-Proxy-Secret`. In `ProxyKerberos` mode, the server accepts the principal only
when all of these hold:

- The address that connected to Kestrel is in `KnownProxies`. The server compares the connecting
  address, before any forwarded header is applied.
- The connection is from loopback or uses TLS. A forwarded `https` scheme does not count: an
  HTTP connection from another machine is refused.
- The request carries exactly one secret header, and it matches.
- The request carries exactly one user header, with one `user@REALM` principal.

| Request | Result |
|---|---|
| No user header, on a request that needs sign-in | `403` |
| A user header that fails a check, on a protected application route | `403` |
| A user header that fails a check, at the authorization endpoint | The application receives `access_denied` |

The server never sends a Negotiate challenge in this mode.

The proxy must set the user header only in locations that require Kerberos, clear a user header
that a client sent everywhere else, and overwrite the secret header. The templates do all three.

Keep Kestrel on loopback when the proxy runs on the same machine. For a proxy on another
machine, serve HTTPS from Kestrel, validate its certificate at the proxy, and allow only the
proxy to reach it.

**Protect the secret includes.** Keep them outside the web root and out of source control, logs
and shared configuration dumps. `nginx -T` prints them.

## Installation

The [Linux guide](linux-deployment.md) has the commands. These points are specific to a proxy:

1. Provision `HTTP/identity.example.com@EXAMPLE.COM` for the hostname that browsers use, and
   install the keytab where the **proxy** can read it. The server still needs its own LDAP read
   credential.
2. Install `deploy/appsettings.Proxy.json` as the whole `appsettings.json`. Set the directory,
   client and callback host, and keep `AllowedHosts` set to the public hostname. Check
   `appsettings.Local.json` and the environment for overrides: a later source overrides list
   entries by position, so a partial override can leave an old client or host in place.
3. Fill `/etc/identityserver/identityserver.env` with the proxy secret, the key-protection
   password and the bind password. Put the **same** proxy secret in the proxy's secret include,
   and use a **different** value for the key-protection password. Only root needs to read these
   files: systemd reads the environment file before it switches to the service user.
4. Install the unit and start the service. `/opt/identityserver/logs` must exist and be writable
   by the service.
5. Install the proxy configuration as described below, set the certificate paths, run the
   proxy's configuration check and reload the proxy. **When the check reports a missing module,
   install the module.** Removing the authentication directives leaves the authorization
   endpoint without sign-in.

### Apache

Install the virtual host in the distribution's site folder and the secret include as
`/etc/identityserver/proxy-secret.apache.conf`. Enable the modules `ssl`, `proxy`, `proxy_http`,
`headers`, `alias` and `auth_gssapi`. Change the keytab path when the distribution uses
`/etc/httpd` in place of `/etc/apache2`. Run `apachectl configtest`, enable the site and reload
Apache.

The template allows the `krb5` mechanism only, turns Basic authentication off and keeps
`REMOTE_USER` as the full principal. It requires Kerberos for
`/IdentityServer/connect/authorize` alone, so discovery, the signing keys and the token endpoint
stay open to applications.

### Nginx

Install the server file in the distribution's site folder, the snippet as
`/etc/nginx/snippets/identityserver-proxy.conf` and the secret include as
`/etc/identityserver/proxy-secret.nginx.conf`. Check that the SPNEGO module is loaded. Run
`nginx -t`, then reload Nginx.

- **Realm.** Set `auth_gss_realm` to the realm of the keytab, which is also `Directory:Realm`.
  When the host's default realm differs, the module otherwise looks for the HTTP service in the
  default realm and fails with `No key table entry found`.
- **Principal.** Keep `auth_gss_format_full on`, so the server receives `user@REALM`, and
  `auth_gss_allow_basic_fallback off`.
- **`Authorization` header.** The authorization location clears the header the SPNEGO module
  adds. The anonymous location forwards the header, because an application may authenticate to
  the token endpoint with it.
- **Shared headers.** Both locations include the whole snippet. A location that sets a header of
  its own no longer inherits the headers of the enclosing block.
- **Path.** The `proxy_pass` directives name the scheme, host and port only, which forwards the
  full `/IdentityServer/...` path.
- **Response buffer.** The snippet sets `proxy_buffer_size 16k` and `proxy_buffers 4 16k`. The
  encrypted authorization code travels in the `Location` header and can exceed Nginx's default
  buffer. The symptom is a `502` with `upstream sent too big header` in the Nginx error log.
  Narrow the `Groups` patterns first. For larger redirects, raise both values together: see
  [proxy_buffer_size](https://nginx.org/en/docs/http/ngx_http_proxy_module.html#proxy_buffer_size).

## Root hosting and stripped prefixes

**At the root of the hostname.** For `https://identity.example.com/`, remove `PathBase`, keep
`PathBaseMode` as `Preserve`, and set `ServerUri` to the root URL. Change the proxy's
authenticated location to `/connect/authorize` and its general location to `/`, and remove the
`/IdentityServer` redirect. In the Nginx template, replace the final `location /` that returns
404 with the general proxy location.

**With a proxy that strips the prefix.** The templates forward the prefix, which is the simpler
arrangement. For a proxy that removes it, set `PathBaseMode` to `Strip` and leave `PathBase` and
`ServerUri` as they are. A request that does not come from a known proxy then gets `403`.

- Apache: the backend side of `ProxyPass` and `ProxyPassReverse` becomes
  `http://127.0.0.1:5080/`.
- Nginx: the general `/IdentityServer/` location uses `proxy_pass http://127.0.0.1:5080/`, and
  the authorization location uses `proxy_pass http://127.0.0.1:5080/connect/authorize`.

The authentication locations always match the **public** path.

**Routes of an embedded host.** The templates authenticate the authorization endpoint only. Add a
Kerberos-authenticated location for each further protected route. Every anonymous location must
clear the identity header.

## Verification

`ProxyHostingTests` and `ProxyConfigurationTests` cover the server's side of this arrangement,
with a simulated proxy. Check a real deployment in its own Apache or Nginx:

1. Run the proxy's configuration check with the module and the real includes installed. Check
   that Kestrel listens on loopback only.
2. Fetch `https://identity.example.com/IdentityServer/.well-known/openid-configuration` with no
   credentials. The issuer and every endpoint must carry the public HTTPS hostname and the
   prefix. Fetch the `jwks_uri` the same way.
3. From a client with a Kerberos ticket and browser trust for the hostname, complete an
   authorization code sign-in with PKCE. Check the subject and the roles.
4. Send requests that carry your own identity and forwarding headers, with no Kerberos
   authentication. The proxy must challenge an authorization request and clear the identity
   header on anonymous paths. A request sent straight to Kestrel with a missing or wrong secret
   must not authenticate.
5. Sign in as two different users one after the other. No identity may carry over. Check that
   the token endpoint needs only the client's credentials and the code, and that the root and
   unknown paths return 404.
6. Restart IdentityServer and check that the signing keys are unchanged. Read the service and
   proxy logs.
