# Running IdentityServer on Linux

Follow these steps to run IdentityServer on Linux with Microsoft Active Directory, Samba AD or
FreeIPA. The commands are for Ubuntu 26.04 with systemd and a dedicated LDAP read account. On
another distribution, use its equivalent packages and keep the file permissions shown here.

[How it works](how-it-works.md#three-separate-credentials) explains the three credentials the
steps set up. [Directory providers](kerberos-and-ldap.md) describes each directory setting, and
[Apache and Nginx](apache-and-nginx.md) describes proxy hosting.

## Choose the hosting arrangement

| Arrangement | Browser authentication | Public URL in the examples | Template |
|---|---|---|---|
| Native Kestrel | IdentityServer reads the HTTP keytab | `https://identity.example.com:8443` | [Settings](../deploy/appsettings.Kerberos.json), [unit](../deploy/identityserver-native.service) |
| Apache | mod_auth_gssapi reads the keytab. The server uses `ProxyKerberos`. | `https://identity.example.com/IdentityServer` | [Apache](apache-and-nginx.md#apache) |
| Nginx | The SPNEGO module reads the keytab. The server uses `ProxyKerberos`. | `https://identity.example.com/IdentityServer` | [Nginx](apache-and-nginx.md#nginx) |

Use one arrangement for a public hostname. In the proxy arrangements, Kestrel listens on
`127.0.0.1:5080` only, and the proxy terminates HTTPS and authenticates the browser.

A Negotiate exchange belongs to one TCP connection. A generic HTTP proxy that pools connections
in front of native Kestrel breaks it, so use the authenticating proxy templates.

## 1. Decide the names, accounts and network access

Replace these example values everywhere before you run a command or install a template:

| Value | Example | Used by |
|---|---|---|
| Public identity hostname | `identity.example.com` | Browser, HTTPS certificate, HTTP SPN, `AllowedHosts` |
| Kerberos realm | `EXAMPLE.COM` | Kerberos client configuration, keytab, `Directory:Realm`, Nginx `auth_gss_realm` |
| Directory and KDC hostname | `dc.example.com` or `ipa.example.com` | Kerberos and LDAP connections, LDAP certificate name |
| Directory suffix | `DC=example,DC=com` or `dc=example,dc=com` | LDAP searches |
| HTTP service principal | `HTTP/identity.example.com@EXAMPLE.COM` | Inbound browser authentication. It has no URL scheme, port or path. |
| Directory read account | `identityserver-reader` | Outbound LDAP only |
| Client and callback | `example-app`, `https://app.example.com/signin-oidc` | The application's configuration |

The host does not need to be joined to the domain: the keytab and the read account are enough.
Create the accounts and SPNs with the directory's administration tools. On a host that is joined
to a domain or enrolled in IPA, keep the DNS, Kerberos and certificate settings that the join
manages.

Allow these connections:

| Source → destination | Ports | Purpose |
|---|---|---|
| Browser → identity host | TCP 443 (proxy) or 8443 (native example) | HTTPS |
| Linux host and browser client → DNS and time service | TCP and UDP 53, UDP 123 | Name resolution and synchronized clocks |
| Browser client → KDC, and the server → KDC for a Kerberos LDAP bind | TCP and UDP 88 | Kerberos tickets |
| IdentityServer → directory | TCP 636 for LDAPS **or** TCP 389 for StartTLS | Profile and group reads |
| Proxy → Kestrel on the same host | Loopback TCP 5080 | The backend |

These ports cover sign-in and directory reads. Enrollment, password changes and provisioning can
need more directory ports. **Keep port 5080 reachable from loopback only.**

## 2. Install the runtime and client libraries

```bash
sudo apt-get update
sudo apt-get install -y aspnetcore-runtime-10.0 krb5-user libgssapi-krb5-2 \
  ldap-utils libsasl2-modules-gssapi-mit ca-certificates openssl curl jq dnsutils
dotnet --list-runtimes
command -v dotnet
```

`dotnet --list-runtimes` must list both `Microsoft.AspNetCore.App 10.0.x` and
`Microsoft.NETCore.App 10.0.x`. `ldap-utils` supplies the OpenLDAP client library, and the SASL
GSSAPI package supports Kerberos LDAP binds. If the Kerberos installer asks for a default realm,
enter yours.

The service templates run `/usr/bin/dotnet`. Change `ExecStart` when `dotnet` is installed
somewhere else, or when you deploy a
[self-contained release archive](releasing.md#run-a-self-contained-archive). On a build machine,
install the SDK with `sudo apt-get install dotnet-sdk-10.0`. For another Ubuntu release, follow
[Microsoft's Ubuntu instructions](https://learn.microsoft.com/en-us/dotnet/core/install/linux-ubuntu-install),
and for another distribution the
[Linux instructions](https://learn.microsoft.com/en-us/dotnet/core/install/linux).

For a proxy arrangement, install the front server you chose:

```bash
# Apache:
sudo apt-get install -y apache2 libapache2-mod-auth-gssapi
# OR Nginx:
sudo apt-get install -y nginx libnginx-mod-http-auth-spnego
```

Nginx needs the separate SPNEGO module package. Check that your distribution has it before you
plan on Nginx.

## 3. Configure DNS, time and Kerberos

On the Linux host and on the browser's machine, check that the public hostname resolves to the
identity host and the directory hostname resolves to the directory server:

```bash
getent hosts identity.example.com dc.example.com
timedatectl status
dig +short SRV _kerberos._tcp.example.com
```

Use the public FQDN in the browser, so the browser asks for a ticket for the hostname in the
keytab. Synchronize the clocks with the domain's time source.

When DNS has no usable Kerberos SRV records, merge this into `/etc/krb5.conf`, with
`ipa.example.com` for a FreeIPA KDC. On an enrolled host, read the included configuration first.

```ini
[libdefaults]
 default_realm = EXAMPLE.COM
 dns_lookup_realm = false
 dns_lookup_kdc = false
 rdns = false
 dns_canonicalize_hostname = false

[realms]
 EXAMPLE.COM = {
  kdc = dc.example.com
  admin_server = dc.example.com
 }

[domain_realm]
 .example.com = EXAMPLE.COM
 example.com = EXAMPLE.COM
```

Add a mapping for each realm when several realms share the host. Each IdentityServer instance
reads one directory and one realm. See
[MIT's `krb5.conf` reference](https://www.mit.edu/~kerberos/krb5-devel/doc/admin/conf_files/krb5_conf.html).

## 4. Provision the HTTP service keytab

Run the steps for your directory as a directory administrator. Use a dedicated service identity
and a keytab with AES keys. **Check for an existing owner of the SPN first.** Two accounts with
the same HTTP principal break Kerberos for that hostname.

### Microsoft AD

Create a dedicated account such as `svc-identityserver`. On an administrative Windows host with
the Active Directory module, check and register the SPN and export an AES keytab:

```powershell
setspn -Q HTTP/identity.example.com
Set-ADUser svc-identityserver -KerberosEncryptionType AES128,AES256
setspn -S HTTP/identity.example.com EXAMPLE\svc-identityserver
ktpass /out identityserver.http.keytab /princ HTTP/identity.example.com@EXAMPLE.COM /mapuser EXAMPLE\svc-identityserver /crypto AES256-SHA1 /ptype KRB5_NT_PRINCIPAL /pass *
```

`/pass *` prompts for the password, which keeps it out of the shell history. `ktpass` can change
the account's keys and mappings, so run it for this dedicated account only, and coordinate a key
rotation with everything that uses the keytab. Keep the principal's exact spelling and case.
Transfer the file to the Linux host over a secure channel. See
[Microsoft's ktpass reference](https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/ktpass)
and
[ASP.NET Core's Kerberos setup for Linux](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/windowsauth?view=aspnetcore-10.0#linux-and-macos-environment-configuration).

### Samba AD

On the Samba domain controller, for a new dedicated account:

```bash
sudo samba-tool user create svc-identityserver
sudo samba-tool spn add HTTP/identity.example.com svc-identityserver
sudo samba-tool user edit svc-identityserver
```

In the editor, set `msDS-SupportedEncryptionTypes: 24` (AES128 plus AES256) and leave the other
attributes as they are. Then export the keytab to a new, protected file:

```bash
sudo samba-tool domain exportkeytab /root/identityserver.http.keytab \
  --principal=HTTP/identity.example.com
sudo klist -kte /root/identityserver.http.keytab
```

**Confirm that the keytab has AES entries before you transfer it.** A keytab with RC4 entries
only fails with the Linux Kerberos client: set the encryption-type attribute and export again.
An account with older keys may need a password change before it has AES keys. See
[samba-tool](https://www.samba.org/samba/docs/current/man-html/samba-tool.8.html).

### FreeIPA

Use an IPA administration client, with the hostname already in DNS. For a new host and service,
with the host's real IP address:

```bash
kinit admin
ipa host-add identity.example.com --ip-address=192.0.2.10
ipa service-add HTTP/identity.example.com
umask 077
ipa-getkeytab -s ipa.example.com -p HTTP/identity.example.com \
  -k ./identityserver.http.keytab
klist -kte ./identityserver.http.keytab
```

When the host or service already exists, inspect it first. **`ipa-getkeytab` issues new service
keys**, which invalidates every keytab already in use for the principal, so coordinate the change
with every front server that shares it. Retrieve the existing keys with `-r` only when the
administrator has granted retrieval rights. See
[FreeIPA keytab retrieval](https://www.freeipa.org/page/V4/Keytab_Retrieval).

### Check the keytab

For every directory, `klist -kte` must show `HTTP/identity.example.com@EXAMPLE.COM`, AES
encryption types and the current key version. **Never run `klist -K`: it prints the keys.** Keep
keytabs out of the web root and out of source control.

## 5. Establish LDAP and HTTPS trust

The directory server needs an LDAPS or StartTLS listener with a certificate that is valid for
its LDAP hostname. Get the issuing CA certificate through a channel you trust, then install and
check it:

```bash
sudo install -m 0644 directory-ca.crt /usr/local/share/ca-certificates/identityserver-directory-ca.crt
sudo update-ca-certificates
openssl s_client -connect dc.example.com:636 -servername dc.example.com \
  -verify_hostname dc.example.com -verify_return_error -CApath /etc/ssl/certs </dev/null
```

For StartTLS, use `-starttls ldap -connect dc.example.com:389` in the last command. For FreeIPA,
use `ipa.example.com`. The check must pass for both the chain and the hostname. On another
distribution, use its CA trust mechanism. OpenLDAP may also need its CA bundle set in
`/etc/ldap/ldap.conf` or `/etc/openldap/ldap.conf`. **Leave certificate verification on.**

Get an HTTPS certificate whose subject alternative names include `identity.example.com`, with its
private key and intermediate chain. Browsers must trust its issuer. The native template reads PEM
files. Kestrel also accepts a PFX: see
[Kestrel endpoint configuration](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/servers/kestrel/endpoints?view=aspnetcore-10.0).

## 6. Create the service account and publish files

For a new installation on the Linux host:

```bash
sudo useradd --system --user-group --home-dir /var/lib/identityserver \
  --no-create-home --shell /usr/sbin/nologin identityserver
sudo install -d -o root -g root -m 0755 /opt/identityserver
sudo install -d -o identityserver -g identityserver -m 0700 /opt/identityserver/logs
sudo install -d -o root -g identityserver -m 0750 /etc/identityserver
```

When the account and folders already exist, check their owners and permissions. The systemd unit
creates `/var/lib/identityserver`, owned by the service, for the token keys.

On the build machine, from the root of the checkout:

```bash
dotnet test IdentityServer.sln --configuration Release --filter 'Category!=LiveDirectory'
dotnet publish IdentityServer.csproj -c Release -r linux-x64 \
  --self-contained false -o artifacts/linux
```

Use `linux-arm64` for an ARM64 host. Copy the **whole** publish folder to the Linux host. When
you built on that host:

```bash
sudo cp -a artifacts/linux/. /opt/identityserver/
sudo chown -R root:root /opt/identityserver
sudo chown identityserver:identityserver /opt/identityserver/logs
sudo chmod 0700 /opt/identityserver/logs
```

These commands are for a first installation. For an existing service, follow
[Upgrades, backups and rotation](#upgrades-backups-and-rotation). The application files stay
owned by root: the service reads them and writes only to its state and log folders.

Copy the `deploy/` templates, and any `appsettings.Local.json`, to the host separately from the
publish output. The publish output's `appsettings.json` holds the repository's demonstration
settings, which the next step replaces.
[Where settings come from](configuration.md#where-settings-come-from) lists the sources the
server reads. Two rules matter here: restart the service after a change, and replace a `Hosts` or
`Clients` list as a whole, because a later source overrides list entries by position and keeps
the rest.

## 7. Install the selected application configuration

Run these commands on the Linux host, from the folder that holds `deploy/`.

### Native Kestrel

```bash
sudo install -m 0644 deploy/appsettings.Kerberos.json /opt/identityserver/appsettings.json
sudo install -m 0644 deploy/identityserver-native.service /etc/systemd/system/identityserver.service
sudo install -m 0600 deploy/identityserver-native.env.example /etc/identityserver/identityserver.env
sudo install -d -o root -g identityserver -m 0750 /etc/identityserver/tls
sudo install -o root -g identityserver -m 0640 identityserver.http.keytab /etc/identityserver/http.keytab
sudo install -o root -g identityserver -m 0640 fullchain.pem /etc/identityserver/tls/fullchain.pem
sudo install -o root -g identityserver -m 0640 privkey.pem /etc/identityserver/tls/privkey.pem
sudoedit /opt/identityserver/appsettings.json /etc/identityserver/identityserver.env
```

The template sets `Authentication:Mode` to `Kerberos` and listens on HTTPS port 8443. The unit
sets `KRB5_KTNAME=FILE:/etc/identityserver/http.keytab`. `ServerUri` must include `:8443`, and
`AllowedHosts` takes the hostname alone.

To serve on port 443, change the listener and `ServerUri`, and add
`AmbientCapabilities=CAP_NET_BIND_SERVICE` and `CapabilityBoundingSet=CAP_NET_BIND_SERVICE` in a
systemd override, which lets the service account bind a privileged port.

### Apache or Nginx

```bash
sudo install -m 0644 deploy/appsettings.Proxy.json /opt/identityserver/appsettings.json
sudo install -m 0644 deploy/identityserver.service /etc/systemd/system/identityserver.service
sudo install -m 0600 deploy/identityserver.env.example /etc/identityserver/identityserver.env
sudoedit /opt/identityserver/appsettings.json /etc/identityserver/identityserver.env
```

Keep `ProxyKerberos`, the loopback listener, `KnownProxies: ["127.0.0.1"]`, and the same
`/IdentityServer` path in `ServerUri` and `PathBase`. The proxy verifies the browser's tickets,
so install the keytab where the proxy's user can read it. On Ubuntu the proxy workers normally
run as `www-data`: check the account your proxy uses.

For Apache, after you set the hostname and certificate paths in the copied site file:

```bash
sudo install -o root -g www-data -m 0640 identityserver.http.keytab /etc/apache2/identityserver.http.keytab
sudo install -m 0644 deploy/apache/identityserver.conf /etc/apache2/sites-available/identityserver.conf
sudo install -m 0600 deploy/apache/proxy-secret.apache.conf.example /etc/identityserver/proxy-secret.apache.conf
sudoedit /etc/apache2/sites-available/identityserver.conf /etc/identityserver/proxy-secret.apache.conf
sudo a2enmod ssl proxy proxy_http headers alias auth_gssapi
sudo a2ensite identityserver
sudo apachectl configtest
```

For Nginx:

```bash
sudo install -o root -g www-data -m 0640 identityserver.http.keytab /etc/nginx/identityserver.http.keytab
sudo install -d /etc/nginx/snippets
sudo install -m 0644 deploy/nginx/identityserver.conf /etc/nginx/sites-available/identityserver
sudo install -m 0644 deploy/nginx/identityserver-proxy.conf /etc/nginx/snippets/identityserver-proxy.conf
sudo install -m 0600 deploy/nginx/proxy-secret.nginx.conf.example /etc/identityserver/proxy-secret.nginx.conf
sudoedit /etc/nginx/sites-available/identityserver /etc/nginx/snippets/identityserver-proxy.conf /etc/identityserver/proxy-secret.nginx.conf
sudo ln -s /etc/nginx/sites-available/identityserver /etc/nginx/sites-enabled/identityserver
sudo nginx -t
```

Create the symlink only when it does not exist yet. In the Nginx files:

- Set `auth_gss_realm` to your realm, also when the host's default realm differs.
- Keep `proxy_buffer_size 16k` and `proxy_buffers 4 16k`. The encrypted authorization code can
  exceed Nginx's default buffer, which causes a 502.
- Keep `auth_gss_allow_basic_fallback off` and `auth_gss_format_full on`.

For both proxies, install the HTTPS certificate and key at the paths the site file names. The
templates expect `/etc/ssl/identity.example.com/fullchain.pem` and `privkey.pem`. The private key
can be owned by root with mode 0600 when the proxy starts as root.

Put the **same** random proxy secret in `identityserver.env` and in the proxy's secret include.
Commands that dump the configuration, such as `nginx -T`, print the include, so keep their
output private. Discovery, the signing keys and the token endpoint must stay anonymous at the
proxy: only `/connect/authorize` requires Kerberos.
[Apache and Nginx](apache-and-nginx.md) covers the trust rules, custom routes, root hosting and
stripped prefixes.

### Directory settings for either arrangement

Edit the deployed `appsettings.json` and keep its hosting settings. Both templates start with
Samba AD. Replace the whole `IdentityServer:Directory` object for your directory:

| Setting | Microsoft AD | Samba AD | FreeIPA |
|---|---|---|---|
| `Provider` | `ActiveDirectory` | `SambaAd` | `FreeIpa` |
| `Server` | `ldaps://dc.example.com` | `ldaps://dc.example.com` | `ldaps://ipa.example.com` |
| `Realm` | `EXAMPLE.COM` | `EXAMPLE.COM` | `EXAMPLE.COM` |
| `BaseDn` | `DC=example,DC=com` | `DC=example,DC=com` | `dc=example,dc=com` |
| `BindDn` | Reader DN or `identityserver-reader@EXAMPLE.COM` | Reader DN or `identityserver-reader@EXAMPLE.COM` | `uid=identityserver-reader,cn=users,cn=accounts,dc=example,dc=com` |

Create the dedicated read account with the directory's administration tools and give it read
access to user profiles and groups. A new IPA user must complete its first password change before
it can bind. Set a password policy for the account, and plan each password rotation together
with the change to the server's settings.

For StartTLS, write the server as `ldap://...`. TLS is still required: see
[How the read connection authenticates](kerberos-and-ldap.md#how-the-read-connection-authenticates).
When the directory forbids anonymous binds, use `ldaps://`.

Check the read account before you start the service. The command prompts for its password:

```bash
ldapsearch -LLL -x -H ldaps://dc.example.com \
  -D 'identityserver-reader@EXAMPLE.COM' -W \
  -b 'DC=example,DC=com' '(sAMAccountName=alice)' dn userPrincipalName objectSid mail
```

For StartTLS, add `-ZZ` and use `ldap://...`. For FreeIPA, use its bind DN and server, and search
`-b 'cn=users,cn=accounts,dc=example,dc=com' '(uid=alice)' dn krbPrincipalName ipaUniqueID mail`.
The account must be able to read the user attributes shown and the groups.

### Secrets, clients and groups

Fill every entry of `/etc/identityserver/identityserver.env`:

| Entry | Value |
|---|---|
| `IdentityServer__Directory__BindPassword` | The read account's password. |
| `IdentityServer__KeyProtectionPassword` | A random value of its own. It encrypts the token keys, so keep it with the backups of the key files. |
| `IdentityServer__Authentication__ProxySecret` | Proxy hosting only. A random value of at least 32 characters with no whitespace, the same as in the proxy's secret include. |

`openssl rand -base64 32` generates one secret. Generate each one separately. The server stops at
startup while a required secret is empty.

systemd reads the file as root and passes the values to the service. Write plain `NAME=value`
lines: `export` and command substitution do not work there. Quote a value that contains
whitespace as
[`EnvironmentFile`](https://www.freedesktop.org/software/systemd/man/latest/systemd.exec.html#EnvironmentFile=)
describes. Keep the file owned by root with mode 0600.

In `appsettings.json`:

- Replace the example client and callback host. **The redirect check on `Hosts` compares the
  hostname only**, and all clients share the list, so set the exact callback URL in the
  application.
- **Keep an explicit `Clients` list.** An empty list or a `*` entry accepts any client ID.
- Keep `AllowedHosts` set to the identity hostname.
- To issue roles, fill the empty `Groups` list with narrow regular expressions on group common
  names, such as `^MyApp Readers$`, and have the application request the `roles` scope. Roles
  come from direct memberships only.

[Configuration](configuration.md) describes each setting.

## 8. Start the service and verify anonymous endpoints

```bash
sudo systemd-analyze verify /etc/systemd/system/identityserver.service
sudo systemctl daemon-reload
sudo systemctl enable --now identityserver
sudo systemctl status identityserver --no-pager
sudo journalctl -u identityserver -n 80 --no-pager
sudo ss -ltnp
```

The unit runs the service from `/opt/identityserver` and restarts it after a failure. It hides
home directories and lets the service write only to `/var/lib/identityserver`,
`/opt/identityserver/logs` and a private `/tmp`. Check in the `ss` output that the
listener is the one you configured. An unexpected HTTP listener comes from an environment
variable or a command-line argument.

For proxy hosting, reload the proxy once its configuration check passes:
`sudo systemctl reload apache2` or `sudo systemctl reload nginx`.

Open the public port in the firewall: with UFW, `sudo ufw allow 8443/tcp` for the native example
or `sudo ufw allow 443/tcp` for a proxy. **Do not turn on a firewall over SSH before it allows
your SSH connection.**

From a client that trusts the HTTPS CA:

```bash
authority='https://identity.example.com:8443'
# Proxy example instead: authority='https://identity.example.com/IdentityServer'
curl --fail --silent --show-error "$authority/.well-known/openid-configuration" | jq .
jwks_uri=$(curl --fail --silent --show-error "$authority/.well-known/openid-configuration" | jq -r .jwks_uri)
curl --fail --silent --show-error "$jwks_uri" | jq .
```

Both requests must return 200 with no credentials. Check that the issuer and every endpoint URL
carry the exact HTTPS authority, port and path. The root URL and unknown paths return 404. Leave
certificate verification on for these checks.

## 9. Verify Kerberos and browser SSO

On a Linux browser client, in the user's own session:

```bash
kinit alice@EXAMPLE.COM
klist
kvno HTTP/identity.example.com@EXAMPLE.COM
```

`kinit` gets the user's ticket-granting ticket, and `kvno` shows that the KDC issues a ticket for
the HTTP principal. Neither shows that the server's keytab can decrypt that ticket: only a
sign-in does.

Configure the browser to allow integrated authentication to `identity.example.com` and to trust
the HTTPS CA. The browser process must be able to read the user's Kerberos cache.

For Chrome and Chromium, set the
[`AuthServerAllowlist` policy](https://chromeenterprise.google/policies/#AuthServerAllowlist). For
Google Chrome on Linux, create a root-owned file such as
`/etc/opt/chrome/policies/managed/identityserver.json`:

```json
{
  "AuthServerAllowlist": "identity.example.com"
}
```

Chromium normally reads `/etc/chromium/policies/managed`. A distribution's packaging or sandbox
can change the policy folder and the browser's access to the Kerberos cache: follow the
[Linux policy instructions](https://www.chromium.org/administrators/linux-quick-start/) and check
the value in `chrome://policy`. Limit the allowlist to the identity host. The server needs no
credential delegation. Restart the browser completely after a policy change, because a new
window can reuse the old process. When the browser has its own trust store, install the HTTPS CA
there.

Sign in with an application that uses these settings:

| Setting | Native example |
|---|---|
| Authority | `https://identity.example.com:8443` |
| Client ID | `example-app` |
| Redirect URI | `https://app.example.com/signin-oidc` |
| Response type and PKCE | `code` with `S256` |
| Scopes | `openid profile email roles`, without the ones you do not use |

For a proxy, the authority includes `/IdentityServer`. The application must generate and check
`state` and `nonce`, keep the PKCE verifier, redeem the code without browser credentials, and
validate each token's signature, issuer and audience. Use your own test application. The
[oidcdebugger.com walkthrough](verification.md#checking-a-sign-in-with-oidcdebuggercom) sends
the person's claims to that external site, and needs its client and callback host added to the
settings.

1. Sign in as two different users, each in a separate operating-system session with its own
   credential cache and browser session.
2. For each user, check the subject, the email and the roles. Include a group the user reaches
   only through a nested group: it must be absent.
3. Check that the code is redeemed at the token endpoint, as well as that authorization
   succeeds.
4. Repeat from a session with no ticket. Authorization must challenge or fail, and no token may
   be issued.

The [live directory test](verification.md#live-directory-tests) automates the profile checks.

## Optional: Kerberos credentials for outbound LDAP

The server can bind to the directory with Kerberos in place of a bind password. This needs a
directory read identity, its keytab and a credential cache that the service owns. Remove both
`BindDn` and `BindPassword` from every settings source, including the environment file, to
select this method.

With the service installed and its state folder created, check the arrangement:

```bash
sudo -u identityserver kinit -k -t /etc/identityserver/ldap-reader.keytab \
  -c FILE:/var/lib/identityserver/ldap.ccache svc-reader@EXAMPLE.COM
sudo -u identityserver env KRB5CCNAME=FILE:/var/lib/identityserver/ldap.ccache klist
sudo -u identityserver env KRB5CCNAME=FILE:/var/lib/identityserver/ldap.ccache \
  LDAPSASL_CBINDING=tls-endpoint LDAPSASL_SECPROPS=maxssf=0 \
  ldapsearch -LLL -Y GSSAPI -H ldaps://dc.example.com \
  -b 'DC=example,DC=com' '(sAMAccountName=alice)' dn objectSid
```

Install the reader keytab owned by root, readable by the service group (mode 0640). Set
`KRB5CCNAME=FILE:/var/lib/identityserver/ldap.ccache` in the service environment. For Samba over
TLS, also set `LDAPSASL_CBINDING=tls-endpoint` and `LDAPSASL_SECPROPS=maxssf=0` there. They make
the OpenLDAP client use TLS channel binding without a second SASL security layer inside TLS, and
they work with FreeIPA too. Leave TLS and certificate validation on.

**A single `kinit` is a check, not a setup: tickets expire.** Before you rely on this method,
arrange three things:

1. A ticket is acquired from the keytab **before the service starts**.
2. The ticket is renewed, and acquired again once its renewable lifetime ends, by your
   credential manager or a supervised timer.
3. A failed or expired ticket raises an alert.

Keep the cache out of home directories and the shared `/tmp`, which the unit hides from the
service. A bind account with a password needs none of this.

## Upgrades, backups and rotation

1. Back up the deployed settings, the environment file, the HTTPS files, the keytab and **both**
   token-key files in `/var/lib/identityserver/keys`. The key files are useless without their
   key-protection password. Protect the backups as you protect the originals.
2. Publish into a new staging folder. Stop `identityserver` before you replace its files. Deploy
   the whole publish output, and keep the deployed `appsettings.json` and any local overrides:
   the publish output carries the repository's demonstration clients and hosts.
3. Keep `/var/lib/identityserver` and the key-protection password. **Do not delete key files to
   get past a startup failure.** Check the password, the file format and the permissions first.
4. Start the service, read the journal, check discovery and complete a sign-in. Check that the
   signing keys in the JWKS are unchanged and that a code issued before the restart, and not yet
   expired, can be redeemed.
5. Rotate service-account keys, keytabs and the read password together with the settings that
   use them. Update every front server that shares an HTTP principal at the same time. Renew the
   HTTPS certificate separately, and reload or restart the process that reads it. Keep the
   previous binaries and their settings until the new version is verified.

## Troubleshooting

| Symptom | Check and fix |
|---|---|
| `Microsoft.AspNetCore.App` is missing | Install the ASP.NET Core 10 runtime. Check the unit's `dotnet` path and the architecture. |
| Startup asks for LDAP settings or a key password | The settings are still the Windows demonstration file. Supply a provider, server, realm and base DN, and a nonempty key-protection password. Set both bind values or neither. |
| The service fails but the same command works in a shell | Check the working directory, read access to the keytab and TLS files, write access to the log and state folders, the quoting in the environment file, and the unit's hidden home directories and private `/tmp`. |
| `Cannot contact any KDC`, or clock skew | Check the realm mappings, the KDC's DNS records and ports, and the clock, on the machine that fails. |
| `Server not found in Kerberos database` | The browser's FQDN must match the HTTP SPN. Check for duplicate SPNs, the realm and hostname canonicalization. |
| `No key table entry found`, a decrypt failure or an unsupported encryption type | Check the keytab's principal, AES entries and key version, the service's read access, and whether the account's keys were rotated. For Nginx, set `auth_gss_realm`. |
| `curl` with Kerberos works but the browser prompts | Check the browser's allowlist, its HTTPS trust and which Kerberos cache its process reads. Restart the browser completely after a policy change. Do not turn on Basic fallback. |
| LDAP server unavailable, or a TLS error | Test the exact LDAP hostname with `openssl` and `ldapsearch`. Check the CA chain, hostname, listener and port. Use `ldaps://` when the directory forbids anonymous binds. |
| A GSSAPI LDAP bind fails though browser sign-in works | Check the reader's credential cache and ticket expiry, the SASL GSSAPI library, and the channel-binding settings above. |
| The browser authenticates but the application gets no tokens or `access_denied` | Check `Directory:Realm`, the read account's access, the user search base and the principal attributes. For FreeIPA, set `Provider` to `FreeIpa`. |
| Nginx reports `unknown directive auth_gss` | Install and load the SPNEGO module before you enable the site. |
| Nginx returns 502 with `upstream sent too big header` | Keep `proxy_buffer_size 16k; proxy_buffers 4 16k;`. Raise both together for larger profiles, and narrow the `Groups` patterns. |
| The proxy returns 502, connection refused | Check that the service listens on `127.0.0.1:5080` and that the proxy uses that port. Read the service journal. |
| The backend returns 403, the issuer is wrong or the prefix is missing | Check the shared secret, `KnownProxies`, the forwarded host and scheme, and that `ServerUri`, `PathBase` and the proxy's path mapping agree. See [Backend trust](apache-and-nginx.md#backend-trust). |
| Roles are empty | The application must request `roles`. The `Groups` patterns must match group common names. The membership must be direct. Restart after a settings change. |
| Codes stop working after a restart | Keep the token keys and their password. Check `PersistKeys`, `DataPath` and the folder's owner. An expired code stays invalid. |

Read `journalctl -u identityserver`, the files in `/opt/identityserver/logs/` and the proxy's
error log. **Never share authorization URLs that carry a code or token, raw authentication
traces, the secret includes or key material.**
[Troubleshooting](troubleshooting.md) explains the server's own log messages.
