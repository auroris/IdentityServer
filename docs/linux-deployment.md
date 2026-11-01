# Running IdentityServer on Linux

Run IdentityServer on Linux to provide OpenID Connect sign-in through Microsoft AD, Samba AD or FreeIPA. The steps below use Ubuntu 26.04, systemd and a dedicated LDAP read account. On other distributions, use the equivalent packages and retain the file permissions shown here.

Before you start, skim [How it works](how-it-works.md) for the three credentials involved. The [directory reference](kerberos-and-ldap.md) explains each setting, and the [proxy guide](apache-and-nginx.md) covers Apache and Nginx in depth.

## Contents

- [Choose a host](#choose-the-hosting-arrangement)
- [Names, accounts and network access](#1-decide-the-names-accounts-and-network-access)
- [Runtime and libraries](#2-install-the-runtime-and-client-libraries)
- [DNS, time and Kerberos](#3-configure-dns-time-and-kerberos)
- [AD, Samba and FreeIPA keytabs](#4-provision-the-http-service-keytab)
- [LDAP and HTTPS certificates](#5-establish-ldap-and-https-trust)
- [Service account and publishing](#6-create-the-service-account-and-publish-files)
- [Native/proxy configuration, readers and secrets](#7-install-the-selected-application-configuration)
- [Starting and checking the service](#8-start-the-service-and-verify-anonymous-endpoints)
- [Browser SSO](#9-verify-kerberos-and-browser-sso)
- [Optional GSSAPI LDAP credentials](#optional-kerberos-credentials-for-outbound-ldap)
- [Upgrades and rotation](#upgrades-backups-and-rotation)
- [Troubleshooting](#troubleshooting)

## Choose the hosting arrangement

| Arrangement | Browser authentication | Public URL in the examples | Template |
|---|---|---|---|
| Native Kestrel | IdentityServer reads the HTTP keytab | `https://identity.example.com:8443` | [Native settings](../deploy/appsettings.Kerberos.json), [native unit](../deploy/identityserver-native.service) |
| Apache | mod_auth_gssapi reads the keytab; backend uses `ProxyKerberos` | `https://identity.example.com/IdentityServer` | [Apache guide](apache-and-nginx.md#apache) |
| Nginx | SPNEGO module reads the keytab; backend uses `ProxyKerberos` | `https://identity.example.com/IdentityServer` | [Nginx guide](apache-and-nginx.md#nginx) |

Choose one arrangement for a public hostname. For the proxy arrangements, Kestrel listens only on `127.0.0.1:5080`; the proxy terminates HTTPS and authenticates the browser. A generic HTTP proxy in front of native Negotiate needs special connection affinity, so use the supplied authenticating proxy templates when following this guide.

## 1. Decide the names, accounts and network access

Replace these example values consistently before running commands or installing templates:

| Value | Example | Used by |
|---|---|---|
| Public identity hostname | `identity.example.com` | Browser, HTTPS certificate, HTTP SPN, `AllowedHosts` |
| Kerberos realm | `EXAMPLE.COM` | Client Kerberos configuration, keytab, `Directory:Realm`, Nginx `auth_gss_realm` |
| Directory/KDC hostname | `dc.example.com` or `ipa.example.com` | Kerberos and LDAP connections; LDAP certificate name |
| Directory suffix | `DC=example,DC=com` or `dc=example,dc=com` | LDAP searches |
| HTTP service principal | `HTTP/identity.example.com@EXAMPLE.COM` | Inbound browser authentication; no URL scheme, port or path |
| Directory read account | `identityserver-reader` | Outbound LDAP only |
| Client and callback | `example-app`, `https://app.example.com/signin-oidc` | Relying-party configuration |

The HTTP service keytab accepts browser tickets, the read account retrieves profiles and groups, and the key-protection password encrypts persisted token keys. Proxy hosting also uses a shared secret to verify the proxy's identity.

Start with a configured directory service and provision its accounts and SPNs through its administration tools. The keytab and read account support hosts both inside and outside the domain. Configure domain DNS, Kerberos and certificate trust on the host; preserve any settings already managed by a domain join or IPA enrollment.

Allow these connections where applicable:

| Source → destination | Ports | Purpose |
|---|---|---|
| Browser → identity host | TCP 443 (proxy) or 8443 (native example) | HTTPS |
| Linux host and browser client → DNS / time service | DNS TCP/UDP 53; NTP UDP 123 | Name resolution and synchronized clocks |
| Browser client → KDC; LDAP GSSAPI client → KDC | TCP/UDP 88 | Kerberos tickets |
| IdentityServer → directory | TCP 636 for LDAPS **or** TCP 389 for StartTLS | Profile/group reads |
| Proxy → Kestrel on the same host | Loopback TCP 5080 | Restricted backend |

The table covers routine sign-in and directory reads. Enrollment, password changes and administrative provisioning can require additional directory ports. Keep backend port 5080 restricted to loopback for this setup.

## 2. Install the runtime and client libraries

On Ubuntu 26.04, run:

```bash
sudo apt-get update
sudo apt-get install -y aspnetcore-runtime-10.0 krb5-user libgssapi-krb5-2 \
  ldap-utils libsasl2-modules-gssapi-mit ca-certificates openssl curl jq dnsutils
dotnet --list-runtimes
command -v dotnet
```

Confirm that `dotnet --list-runtimes` lists both `Microsoft.AspNetCore.App 10.0.x` and `Microsoft.NETCore.App 10.0.x`. The ASP.NET Core runtime package supplies both. `ldap-utils` supplies the OpenLDAP client library; the SASL GSSAPI package supports Kerberos LDAP binds. If the Kerberos installer prompts for a default realm, supply your realm.

The service templates use `/usr/bin/dotnet`. Adjust `ExecStart` for another installation location. On a build machine, install the SDK instead: `sudo apt-get install dotnet-sdk-10.0`; the SDK includes the runtime. Follow [Microsoft's Ubuntu instructions](https://learn.microsoft.com/en-us/dotnet/core/install/linux-ubuntu-install) for another Ubuntu release or unavailable packages, and the [Linux distribution instructions](https://learn.microsoft.com/en-us/dotnet/core/install/linux) elsewhere.

Install only your chosen front server if using a proxy:

```bash
# Apache:
sudo apt-get install -y apache2 libapache2-mod-auth-gssapi
# OR Nginx:
sudo apt-get install -y nginx libnginx-mod-http-auth-spnego
```

Nginx Kerberos authentication requires the additional SPNEGO module package. Check its availability for your distribution before deploying.

## 3. Configure DNS, time and Kerberos

On the Linux host and the browser's machine, verify the public hostname resolves to the identity host and the directory hostname resolves to the appropriate server:

```bash
getent hosts identity.example.com dc.example.com
timedatectl status
dig +short SRV _kerberos._tcp.example.com
```

Use the public FQDN in the browser so the client requests the hostname recorded in the keytab. Synchronize clocks with the domain's time source.

For a small environment without usable Kerberos SRV records, merge this into `/etc/krb5.conf`, substituting `ipa.example.com` for a FreeIPA KDC. In an enrolled host, review the existing included configuration first.

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

This mapping assigns the sample hostname to a realm and KDC explicitly. A deployment using directory-managed DNS can use its existing KDC discovery configuration. Add distinct realm mappings when several realms share a host; each IdentityServer instance selects one directory and realm. See [MIT's Kerberos configuration reference](https://www.mit.edu/~kerberos/krb5-devel/doc/admin/conf_files/krb5_conf.html).

## 4. Provision the HTTP service keytab

Run the relevant directory-administration steps with an authorized administrator. Use a dedicated service identity and an AES-capable keytab. Check for an existing SPN owner before creating a new mapping; the same HTTP principal must not be assigned to competing accounts.

### Microsoft AD

Create a dedicated AD account such as `svc-identityserver` using your normal account-provisioning process. On an administrative Windows host with the Active Directory module, these commands check/register the SPN and export an AES keytab for that account:

```powershell
setspn -Q HTTP/identity.example.com
Set-ADUser svc-identityserver -KerberosEncryptionType AES128,AES256
setspn -S HTTP/identity.example.com EXAMPLE\svc-identityserver
ktpass /out identityserver.http.keytab /princ HTTP/identity.example.com@EXAMPLE.COM /mapuser EXAMPLE\svc-identityserver /crypto AES256-SHA1 /ptype KRB5_NT_PRINCIPAL /pass *
```

`/pass *` prompts for the password, keeping it out of shell history. `ktpass` can change account keys and mappings: use it for this dedicated identity and coordinate key rotation with every consumer. Preserve the principal's exact spelling and case, then securely transfer the resulting file to the Linux host. See [Microsoft's ktpass reference](https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/ktpass) and [ASP.NET Core Kerberos setup](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/windowsauth?view=aspnetcore-10.0#linux-and-macos-environment-configuration).

### Samba AD

On the Samba DC, for a new dedicated account:

```bash
sudo samba-tool user create svc-identityserver
sudo samba-tool spn add HTTP/identity.example.com svc-identityserver
sudo samba-tool user edit svc-identityserver
```

In the account editor, set `msDS-SupportedEncryptionTypes: 24` (AES128 plus AES256) and retain the other attributes. Then export to a new protected output file:

```bash
sudo samba-tool domain exportkeytab /root/identityserver.http.keytab \
  --principal=HTTP/identity.example.com
sudo klist -kte /root/identityserver.http.keytab
```

Confirm AES entries exist before transferring the file. The tested Samba account initially exported RC4-only keys and could not authenticate with the Linux client until AES support was configured. Older account keys may require a coordinated password/key rotation. See [samba-tool](https://www.samba.org/samba/docs/current/man-html/samba-tool.8.html).

### FreeIPA

Use an IPA administration client, with the intended hostname already registered in DNS. For a new host/service (substitute the actual host IP):

```bash
kinit admin
ipa host-add identity.example.com --ip-address=192.0.2.10
ipa service-add HTTP/identity.example.com
umask 077
ipa-getkeytab -s ipa.example.com -p HTTP/identity.example.com \
  -k ./identityserver.http.keytab
klist -kte ./identityserver.http.keytab
```

If the host or service already exists, inspect its configuration before proceeding. Normal `ipa-getkeytab` issuance changes service keys, so coordinate the change across every frontend using the principal. Retrieve existing keys with `-r` only when the administrator has granted retrieval rights, or distribute the coordinated keytab securely. See [FreeIPA keytab retrieval](https://www.freeipa.org/page/V4/Keytab_Retrieval).

For every provider, `klist -kte` should show `HTTP/identity.example.com@EXAMPLE.COM`, appropriate encryption types and the current key version. Do not use `klist -K`, which prints key material. Keep transferred keytabs outside the web root and source control.

## 5. Establish LDAP and HTTPS trust

Configure a working LDAPS or StartTLS listener on the directory server, with a certificate valid for its LDAP hostname. Obtain the issuing CA certificate through your organization's trusted channel, then install and check it on Ubuntu:

```bash
sudo install -m 0644 directory-ca.crt /usr/local/share/ca-certificates/identityserver-directory-ca.crt
sudo update-ca-certificates
openssl s_client -connect dc.example.com:636 -servername dc.example.com \
  -verify_hostname dc.example.com -verify_return_error -CApath /etc/ssl/certs </dev/null
```

For StartTLS, use `-starttls ldap -connect dc.example.com:389` in the last command. Substitute `ipa.example.com` for FreeIPA. A trusted chain alone is insufficient if the certificate names a different host. Use the distribution's CA trust mechanism on non-Ubuntu systems; OpenLDAP may also need its CA bundle configured in `/etc/ldap/ldap.conf` (or `/etc/openldap/ldap.conf`). Do not disable certificate verification.

Obtain an HTTPS certificate whose SAN includes `identity.example.com`, with its private key and intermediate chain. Browser clients must trust that issuer too. The native template uses PEM files; a PFX is another supported [Kestrel certificate configuration](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/servers/kestrel/endpoints?view=aspnetcore-10.0). The certificate protects the HTTPS connection; the keytab lets the service verify Kerberos tickets.

## 6. Create the service account and publish files

For a new installation on the Linux host:

```bash
sudo useradd --system --user-group --home-dir /var/lib/identityserver \
  --no-create-home --shell /usr/sbin/nologin identityserver
sudo install -d -o root -g root -m 0755 /opt/identityserver
sudo install -d -o identityserver -g identityserver -m 0700 /opt/identityserver/logs
sudo install -d -o root -g identityserver -m 0750 /etc/identityserver
```

For an existing account and directories, verify their ownership and permissions. The systemd unit creates `/var/lib/identityserver`, owned by the service, for persisted keys.

On the build machine, from the checkout root:

```bash
dotnet restore IdentityServer.sln
dotnet test IdentityServer.sln -c Release
dotnet publish IdentityServer.csproj -c Release -r linux-x64 \
  --self-contained false -o artifacts/linux
```

Use `linux-arm64` for an ARM64 host. Copy the **entire** publish directory to the Linux host. For a build performed on that same host:

```bash
sudo cp -a artifacts/linux/. /opt/identityserver/
sudo chown -R root:root /opt/identityserver
sudo chown identityserver:identityserver /opt/identityserver/logs
sudo chmod 0700 /opt/identityserver/logs
```

These are initial-install commands; use the [upgrade procedure](#upgrades-backups-and-rotation) for an existing service. Keep application binaries administrator-owned. The service needs read access to binaries/settings and write access only to its state/log locations.

Transfer and install the `deploy/` templates and any `appsettings.Local.json` separately from the publish output. The standalone host loads `appsettings.json`, optional `appsettings.Local.json`, environment variables, then command-line settings, in that precedence order. It does not load `appsettings.Production.json` or user secrets. Restart after configuration changes. Replace configuration arrays completely: partially overriding `Hosts` or `Clients` can retain entries from earlier files.

## 7. Install the selected application configuration

Run template-install commands from the transferred checkout/template directory on Linux.

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

The template selects `Kerberos`, listens on HTTPS port 8443 and sets `KRB5_KTNAME=FILE:/etc/identityserver/http.keytab` in the unit. `ServerUri` must include `:8443`; `AllowedHosts` takes the hostname alone. To serve directly on 443, change both the listener and `ServerUri` and add `AmbientCapabilities=CAP_NET_BIND_SERVICE` and `CapabilityBoundingSet=CAP_NET_BIND_SERVICE` in a systemd service override. These capabilities let the dedicated service account bind a privileged port.

### Apache or Nginx

```bash
sudo install -m 0644 deploy/appsettings.Proxy.json /opt/identityserver/appsettings.json
sudo install -m 0644 deploy/identityserver.service /etc/systemd/system/identityserver.service
sudo install -m 0600 deploy/identityserver.env.example /etc/identityserver/identityserver.env
sudoedit /opt/identityserver/appsettings.json /etc/identityserver/identityserver.env
```

Retain `ProxyKerberos`, the loopback backend, `KnownProxies: ["127.0.0.1"]`, and the matching `/IdentityServer` path in `ServerUri` and `PathBase`. Install the HTTP keytab with read access for the proxy's user and group, since the proxy authenticates browser tickets. On Ubuntu the proxy workers normally use `www-data`; verify the actual runtime account.

Apache installation, after adapting the hostname and certificate paths in the copied site:

```bash
sudo install -o root -g www-data -m 0640 identityserver.http.keytab /etc/apache2/identityserver.http.keytab
sudo install -m 0644 deploy/apache/identityserver.conf /etc/apache2/sites-available/identityserver.conf
sudo install -m 0600 deploy/apache/proxy-secret.apache.conf.example /etc/identityserver/proxy-secret.apache.conf
sudoedit /etc/apache2/sites-available/identityserver.conf /etc/identityserver/proxy-secret.apache.conf
sudo a2enmod ssl proxy proxy_http headers alias auth_gssapi
sudo a2ensite identityserver
sudo apachectl configtest
```

Or Nginx installation:

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

Create the symlink only if absent. Set Nginx `auth_gss_realm EXAMPLE.COM` to your actual realm, including when the machine's default realm differs. Keep the 16 KiB response-header buffer: encrypted authorization codes can exceed the default buffer and otherwise cause a 502. Keep Basic fallback disabled and full principal names enabled. Install the HTTPS certificate/key at the site's configured paths; the default templates expect `/etc/ssl/identity.example.com/fullchain.pem` and `privkey.pem`. Root-owned mode 0600 is appropriate for the private key with the standard root-started proxy service.

Put the **same** random proxy secret in `identityserver.env` and the selected proxy's private include. Do not print the resulting includes with configuration-dump commands. Complete the [proxy guide](apache-and-nginx.md) for backend trust, custom routes, root hosting or stripped prefixes. Discovery, JWKS and token redemption must remain anonymous at the proxy; only authorization needs browser Kerberos.

### Directory settings for either arrangement

Edit the complete deployed `appsettings.json`, retaining the chosen hosting configuration. Both templates initially select Samba AD; replace the entire `IdentityServer:Directory` object as appropriate:

| Setting | Microsoft AD | Samba AD | FreeIPA |
|---|---|---|---|
| `Provider` | `ActiveDirectory` | `SambaAd` | `FreeIpa` |
| `Server` | `ldaps://dc.example.com` | `ldaps://dc.example.com` | `ldaps://ipa.example.com` |
| `Realm` | `EXAMPLE.COM` | `EXAMPLE.COM` | `EXAMPLE.COM` |
| `BaseDn` | `DC=example,DC=com` | `DC=example,DC=com` | `dc=example,dc=com` |
| `BindDn` | Reader DN or `identityserver-reader@EXAMPLE.COM` | Reader DN or `identityserver-reader@EXAMPLE.COM` | `uid=identityserver-reader,cn=users,cn=accounts,dc=example,dc=com` |

Create a dedicated read account through the directory's administration tools and grant the profile and group read permissions it needs. Complete any initial password change and account activation before using a newly created IPA user for binds. Set a service-account password policy and schedule password rotation with the corresponding application configuration change.

FreeIPA defaults searches to its accounts user and group containers. AD and Samba use the configured suffix unless `UserBaseDn` or `GroupBaseDn` narrows it. For StartTLS, use `ldap://...`; TLS remains mandatory. Linux first opens an anonymous LDAP session to initialize the .NET connection, upgrades it to TLS, and then supplies credentials. A refused upgrade closes the connection before credentials are sent. If the directory forbids anonymous binds, use LDAPS.

Check the actual reader before starting the app (the command prompts for its password):

```bash
ldapsearch -LLL -x -H ldaps://dc.example.com \
  -D 'identityserver-reader@EXAMPLE.COM' -W \
  -b 'DC=example,DC=com' '(sAMAccountName=alice)' dn userPrincipalName objectSid mail
```

For StartTLS add `-ZZ` and use `ldap://...`. For FreeIPA use its bind DN and server, and search `-b 'cn=users,cn=accounts,dc=example,dc=com' '(uid=alice)' dn krbPrincipalName ipaUniqueID mail`. Check access to the user attributes and groups that IdentityServer will read.

### Secrets, clients and groups

Fill both native environment entries with nonempty values: `IdentityServer__Directory__BindPassword` is the actual reader password; `IdentityServer__KeyProtectionPassword` is an independent strong random value. Proxy hosting also requires `IdentityServer__Authentication__ProxySecret` (at least 32 non-whitespace characters). `openssl rand -base64 32` can generate each random secret independently.

systemd reads the environment file as root and passes its values to the service. Use literal `NAME=value` entries; shell `export` statements and command substitution are unsupported. Quote values containing whitespace using systemd's [EnvironmentFile syntax](https://www.freedesktop.org/software/systemd/man/latest/systemd.exec.html#EnvironmentFile=). Keep the file root-owned with mode 0600. Preserve the key-protection password with the saved signing and encryption keys across restarts and deployments.

Replace the example client, callback host and hostname restrictions. `IdentityServer:Hosts` accepts callback origins, but its redirect check matches **hostnames**, not exact callback paths, and is shared across clients. `AllowedHosts` restricts requests to the identity hostname. Configure the actual callback URL in the client. Keep an explicit `Clients` list; an absent list or wildcard allows unmatched client IDs. See the [configuration guide](configuration.md).

To issue role claims, populate the initially empty `Groups` list with narrow common-name regexes such as `^MyApp Readers$` or `^myapp-readers$`, and have the client request `roles`. Roles come from direct directory memberships; nested groups, AD primary-group membership and proxy-provided roles are excluded. AD and Samba subjects are SIDs; FreeIPA subjects are `ipaUniqueID` values.

## 8. Start the service and verify anonymous endpoints

```bash
sudo systemd-analyze verify /etc/systemd/system/identityserver.service
sudo systemctl daemon-reload
sudo systemctl enable --now identityserver
sudo systemctl status identityserver --no-pager
sudo journalctl -u identityserver -n 80 --no-pager
sudo ss -ltnp
```

The unit runs from `/opt/identityserver` and restarts on failures. Its filesystem restrictions isolate `/tmp`, hide home directories and permit writes to the state and log paths. Verify file access under the service account with these restrictions active. Confirm that the listener matches your configuration; an unexpected HTTP listener may come from environment or command-line overrides.

For proxy hosting, reload the selected proxy after its configuration check succeeds: `sudo systemctl reload apache2` or `sudo systemctl reload nginx`. Allow the public port through the host/network firewall while retaining remote administration access. On a host already managed by UFW, for example, `sudo ufw allow 8443/tcp` applies to the native example or `sudo ufw allow 443/tcp` to a proxy. Do not blindly enable a new firewall over SSH.

From a client that trusts the HTTPS CA:

```bash
authority='https://identity.example.com:8443'
# Proxy example instead: authority='https://identity.example.com/IdentityServer'
curl --fail --silent --show-error "$authority/.well-known/openid-configuration" | jq .
jwks_uri=$(curl --fail --silent --show-error "$authority/.well-known/openid-configuration" | jq -r .jwks_uri)
curl --fail --silent --show-error "$jwks_uri" | jq .
```

Both requests should return 200 anonymously. Check the issuer and all endpoint URLs for the exact HTTPS authority, port and path. The root and unknown paths return 404. Keep certificate verification enabled during these checks.

## 9. Verify Kerberos and browser SSO

On a Linux browser client, use its regular user session:

```bash
kinit alice@EXAMPLE.COM
klist
kvno HTTP/identity.example.com@EXAMPLE.COM
```

`kinit` establishes the user's TGT; `kvno` proves the KDC can issue a ticket for the exact HTTP principal. Neither alone proves that the server's keytab can decrypt it. Configure the browser to permit integrated authentication to `identity.example.com`, trust the HTTPS CA, and ensure its process can access the same Kerberos cache.

For Chrome/Chromium, use the [`AuthServerAllowlist` policy](https://chromeenterprise.google/policies/#AuthServerAllowlist). On a standard Google Chrome Linux installation, create an administrator-owned file such as `/etc/opt/chrome/policies/managed/identityserver.json` containing:

```json
{
  "AuthServerAllowlist": "identity.example.com"
}
```

Chromium normally uses `/etc/chromium/policies/managed`; distribution/sandbox packaging can change policy locations and GSSAPI/cache access. Follow the browser's [Linux policy instructions](https://www.chromium.org/administrators/linux-quick-start/) and verify the effective value in `chrome://policy`. Keep the allowlist limited to your identity host; credential delegation is not required by this design. Fully restart the browser after changing policy/environment; a new window may reuse an old process/cache environment. Install the HTTPS CA into the browser's supported trust store if it does not use the operating system store.

Use a client/relying party with these settings:

| Setting | Native example |
|---|---|
| Authority | `https://identity.example.com:8443` |
| Client ID | `example-app` |
| Redirect URI | `https://app.example.com/signin-oidc` |
| Response type / PKCE | `code` / `S256` |
| Scopes | `openid profile email roles` (omit unused scopes) |

For a proxy, substitute its authority including `/IdentityServer`. The client should generate/check state and nonce, retain the PKCE verifier, exchange the authorization code without browser Kerberos credentials, and validate token signatures, issuer and audience. Use your own test client; if following the [OIDC debugger walkthrough](verification.md#checking-a-sign-in-with-oidcdebuggercom), explicitly configure its client/callback host and understand that directory claims are returned to that external site.

Sign in as two different users, each with a separate operating-system session, credential cache and browser session. Confirm each subject, email and allowed direct groups; include a nested-only group that must be absent. Check both successful authorization and server-side code redemption. Then repeat from a session with no ticket: authorization must challenge or fail and produce no successful token result.

Automate directory profile checks with [the live directory test](verification.md#live-directory-tests).

## Optional: Kerberos credentials for outbound LDAP

To authenticate directory reads with Kerberos, provision a directory-read identity, its keytab and a service-owned credential cache. Remove both `BindDn` and `BindPassword` from every configuration source, including the environment file, to select this authentication method.

For a service already installed with its state directory, an administrator can verify the arrangement with:

```bash
sudo -u identityserver kinit -k -t /etc/identityserver/ldap-reader.keytab \
  -c FILE:/var/lib/identityserver/ldap.ccache svc-reader@EXAMPLE.COM
sudo -u identityserver env KRB5CCNAME=FILE:/var/lib/identityserver/ldap.ccache klist
sudo -u identityserver env KRB5CCNAME=FILE:/var/lib/identityserver/ldap.ccache \
  LDAPSASL_CBINDING=tls-endpoint LDAPSASL_SECPROPS=maxssf=0 \
  ldapsearch -LLL -Y GSSAPI -H ldaps://dc.example.com \
  -b 'DC=example,DC=com' '(sAMAccountName=alice)' dn objectSid
```

Install the reader keytab with root ownership and read access for the service group (0640), then set `KRB5CCNAME=FILE:/var/lib/identityserver/ldap.ccache` in the service environment. On Samba, the tested client also required `LDAPSASL_CBINDING=tls-endpoint` and `LDAPSASL_SECPROPS=maxssf=0` to use TLS channel binding without a second SASL signing/sealing layer inside TLS. These settings passed against FreeIPA too. Keep TLS encryption and certificate validation enabled.

A one-time `kinit` is only a diagnostic: tickets expire. Before relying on this mode continuously, arrange initial ticket acquisition **before application startup**, ongoing renewal/reacquisition from the reader keytab (for example with your existing credential manager or supervised timer), and failure/expiry monitoring. Reacquisition is needed after the maximum renewable lifetime. Keep the cache out of login home directories and shared `/tmp`, which the supplied unit isolates. The default explicit TLS bind account avoids this additional ticket lifecycle.

## Upgrades, backups and rotation

1. Back up the deployed settings, environment secret, HTTPS files/keytab and **both** persisted token-key files. The key files live in `/var/lib/identityserver/keys`; they require their original key-protection password. Store backups with equivalent access restrictions.
2. Build/publish into a new staging directory. Stop `identityserver` before replacing its binaries. Deploy the full publish output while preserving the deployment-specific `appsettings.json`/local overrides; do not restore demonstration clients/hosts from the source defaults.
3. Preserve `/var/lib/identityserver` and its key-protection password. Do not delete unreadable keys to bypass a startup failure: diagnose the password, format and permissions first.
4. Restart, check the journal and anonymous discovery, then repeat a real sign-in. With persistence enabled, check that JWKS signing keys remain unchanged and a still-valid pre-restart code can be redeemed.
5. Rotate service-account keys/keytabs and directory-read passwords with their corresponding configuration. Update every frontend sharing an HTTP principal together. Renew HTTPS certificates independently and reload/restart the process that reads them. Keep a rollback copy of the previous binaries and matching deployment settings.

## Troubleshooting

| Symptom | Check / corrective action |
|---|---|
| `Microsoft.AspNetCore.App` missing | Install the ASP.NET Core 10 runtime; verify the unit's `dotnet` path and architecture. |
| Startup asks for LDAP settings or a key password | Replace the Windows demonstration configuration; supply a valid provider/server/realm/base and nonempty key-protection password. Set both LDAP bind values or neither. |
| Service fails but interactive execution works | Check working directory, readable keytab/TLS files, writable logs/state, environment file quoting and systemd home/tmp isolation. |
| `Cannot contact any KDC` / clock skew | Verify realm mappings, KDC DNS/ports and clock synchronization from the failing client. |
| `Server not found in Kerberos database` | Match the browser FQDN to the HTTP SPN; check duplicates, realm and unintended hostname canonicalization. |
| `No key table entry found`, decrypt failure, unsupported encryption | Check keytab principal, AES entries, key version, service permissions and recently rotated account keys. For Nginx, set the explicit `auth_gss_realm`. |
| curl/Kerberos works but browser prompts | Check browser allowlist, HTTPS trust, browser process/cache identity and packaging. Fully restart after a Negotiate challenge/policy change. Do not enable Basic fallback as the fix. |
| LDAP server unavailable / TLS error | Test the exact LDAP hostname with `openssl` and `ldapsearch`; verify CA chain, hostname, listener and port. Linux StartTLS needs a build containing the initialization fix; use LDAPS if anonymous binds are prohibited. |
| GSSAPI LDAP fails though HTTP SSO works | Check the directory reader's credential cache, ticket expiry, SASL GSSAPI libraries and the TLS channel-binding/SASL settings above. |
| Authenticated browser, but no tokens / user not found | Check the configured realm, read-account access, user search base and principal attributes. For FreeIPA, select the `FreeIpa` provider. |
| Nginx `unknown directive auth_gss` | Install/load the matching SPNEGO module before enabling the site. |
| Nginx 502 with `upstream sent too big header` | Keep `proxy_buffer_size 16k; proxy_buffers 4 16k;`; larger profiles may need coordinated tuning and narrower role claims. |
| Proxy 502 connection refused | Verify the service is listening on loopback 5080 and the proxy uses that port; check the application journal. |
| Backend 403 / wrong issuer / missing prefix | Check the shared secret, original trusted peer, forwarded host/scheme and matching `ServerUri`, `PathBase` and preserved/stripped mapping. See the proxy guide. |
| Empty roles | Request `roles`, set group regexes to common names, verify direct membership and restart after configuration edits. |
| Codes stop working after restart | Preserve token keys and their password; verify `PersistKeys`, `DataPath` and ownership. Expired codes remain invalid even with persistent keys. |

Use `journalctl -u identityserver`, `/opt/identityserver/logs/`, and the chosen proxy's error log. Do not share token-bearing authorization URLs, raw authentication traces, secret includes or key material.
