# Kerberos and LDAP deployment

The default Windows setup remains unchanged. Select an explicit directory provider to use AD, Samba AD or FreeIPA through portable LDAP, including on Linux. Authentication verifies the person through the operating system's Negotiate/Kerberos handler or an explicitly configured Kerberos-authenticating proxy; directory lookup runs separately with the server's credentials. No user passwords are collected by IdentityServer.

## Selection and configuration

`IdentityServer:Authentication:Mode` is `Auto`, `Negotiate`, `Kerberos` or explicit `ProxyKerberos`. Auto permits the existing Windows Negotiate behavior, except with FreeIPA, and requires Kerberos on Linux. Explicit Kerberos mode rejects identities reporting NTLM or an unknown protocol. It applies to token issuance and default protected application routes, including identities supplied by IIS. Anonymous endpoints and routes naming their own authentication schemes retain their existing behavior. Hosts must expose the actual protocol: a generic `Negotiate` label is insufficient proof of Kerberos. ProxyKerberos uses a Kerberos-authenticating Apache/Nginx front server with separate trust settings; see [Apache and Nginx hosting](apache-and-nginx.md). Auto never accepts forwarded identity headers.

`IdentityServer:Directory:Provider` is `Auto`, `Windows`, `ActiveDirectory`, `SambaAd` or `FreeIpa`. Auto chooses Windows on Windows when no directory server is configured; otherwise it selects portable ActiveDirectory. FreeIPA must be selected explicitly and requires Kerberos. Windows uses automatic discovery/process credentials and rejects additional LDAP settings. The resolved provider and authentication mode are logged at startup. Settings are case-insensitive and changes require a restart. Failed authentication or directory access never causes a provider fallback.

For **Samba AD** (use `ActiveDirectory` for Microsoft AD), merge this into the deployment settings:

```json
{
  "IdentityServer": {
    "Directory": {
      "Provider": "SambaAd",
      "Server": "ldaps://dc.example.com",
      "Realm": "EXAMPLE.COM",
      "BaseDn": "DC=example,DC=com"
    }
  }
}
```

For **FreeIPA**, use:

```json
{
  "IdentityServer": {
    "Directory": {
      "Provider": "FreeIpa",
      "Server": "ldaps://ipa.example.com",
      "Realm": "EXAMPLE.COM",
      "BaseDn": "dc=example,dc=com"
    }
  }
}
```

These are overrides for the existing host/client/redirect/group settings, not complete configuration files.

| Key under `IdentityServer:Directory` | Meaning |
|---|---|
| `Server` | Required `ldaps://host[:port]` (TLS, default 636) or `ldap://host[:port]` (mandatory StartTLS, default 389). Plaintext binds are not supported. |
| `Realm` | Required accepted Kerberos realm, for example `EXAMPLE.COM`. Checked before name-based account lookup. |
| `BaseDn` | Required directory suffix/search root. Referrals are disabled. |
| `UserBaseDn` | Optional search root. Defaults to `BaseDn` for AD/Samba, or `cn=users,cn=accounts,<BaseDn>` for FreeIPA. |
| `GroupBaseDn` | Optional search root. Defaults to `BaseDn` for AD/Samba, or `cn=groups,cn=accounts,<BaseDn>` for FreeIPA. |
| `NetbiosDomain` | Optional AD/Samba short domain such as `EXAMPLE`. Allows `EXAMPLE\user` lookup when the identity has no SID and formats the account-name claim as `EXAMPLE\user`. |
| `BindDn`, `BindPassword` | Set both for a dedicated read account over TLS, or neither for process credentials. The name may be a bind DN or an AD-supported bind name. Supply the password through `IdentityServer__Directory__BindPassword` or another deployment secret source. |
| `TimeoutSeconds` | Per LDAP request timeout, default 10, allowed 1–120 seconds. |

Without explicit bind credentials, LDAP uses integrated authentication on Windows and GSSAPI/Kerberos from the process credential cache on Linux. The HTTP keytab does **not** populate that outbound LDAP cache. Arrange a service credential cache with renewal or configure a dedicated read account. Trust the LDAP CA in the operating system/OpenLDAP trust store; no certificate-validation bypass is provided. Do not use FreeIPA's Directory Manager account for application reads. See [FreeIPA LDAP guidance](https://www.freeipa.org/page/HowTo/LDAP.html).

AD/Samba use the authenticated Windows SID when available. Otherwise they accept a principal in the configured realm and search UPN or SAM name; duplicate matches are rejected. FreeIPA matches `krbPrincipalName` and reads `uid` and `ipaUniqueID`. Unqualified usernames and unknown realms are rejected. This version configures one directory/realm per host. FreeIPA identities must arrive as realm-qualified Kerberos principals.

Group queries use direct `member` relationships and page through results, then apply the existing `IdentityServer:Groups` regex allowlist to group common names. They do not expand nested groups, include the AD primary group, or copy roles supplied by the authentication handler. FreeIPA searches the accounts group container, rather than its separate role/permission containers.

AD/Samba retain SID subjects; FreeIPA uses `ipaUniqueID`. Moving existing people from AD to FreeIPA requires deliberate subject mapping in client applications or a custom `IUserProfileProvider`. Changing providers does not preserve account identifiers automatically. The existing claim URI names are retained, including `ClaimTypes.WindowsAccountName` for the authenticated account/principal name.

## Linux setup

These steps use native Kestrel Kerberos. For Apache/Nginx, follow the [proxy deployment guide](apache-and-nginx.md), which places the HTTP keytab at the front server instead.

1. Install a supported ASP.NET Core 10 runtime, the distribution's Kerberos/GSSAPI libraries and tools, and OpenLDAP/SASL GSSAPI client libraries. Configure domain DNS, synchronized time, `/etc/krb5.conf`, and LDAP CA trust.
2. Have the directory administrator provision an HTTP service principal for the **public hostname**, for example `HTTP/identity.example.com@EXAMPLE.COM`, and its keytab. Use FreeIPA's service/keytab tools or AD/Samba's domain tools as appropriate. Protect the keytab and set `KRB5_KTNAME` in the application's service environment. A domain/realm join can provision the host but is not performed by this application. See [Microsoft's Linux Kerberos setup](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/windowsauth?view=aspnetcore-10.0#linux-and-macos-environment-configuration).
3. Configure a directory provider above, including either LDAP bind credentials or a renewable process credential cache. Set `IdentityServer:ServerUri` to the public HTTPS address and configure the existing clients, redirect hosts and group patterns.
4. Configure persistent token keys as below and a dedicated service user with write access to the log/key directories.
5. Publish for the target architecture. For x64: `dotnet publish IdentityServer.csproj -c Release -r linux-x64 --self-contained false -o artifacts/linux`. Copy the output to Linux and supply deployment settings (`appsettings.Local.json` is not published). Run `dotnet IdentityServer.dll` with the publish directory as the working directory. Configure Kestrel HTTPS and process supervision, for example systemd.
6. Obtain a Kerberos ticket on the client and configure browser trust for the public hostname. Run the deployment checks below.

A reverse proxy forwarding native Negotiate to Kestrel must maintain persistent one-to-one connection affinity. Forwarded headers do not solve that requirement or service-principal configuration. The [Apache/Nginx templates](apache-and-nginx.md) instead authenticate Kerberos at the proxy, using the explicit ProxyKerberos mode. Their HTTP keytab belongs to the proxy; outbound LDAP credentials still belong to IdentityServer. See [Kestrel Windows authentication guidance](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/windowsauth?view=aspnetcore-10.0#kestrel).

## Persistent token keys

On Linux set `IdentityServer:PersistKeys` to `true`, choose an absolute `IdentityServer:DataPath` owned by the service user, and supply a strong `IdentityServer__KeyProtectionPassword` through the deployment's secret mechanism. Keep that password separate from the key directory. The files are password-encrypted PKCS#8 (AES-256-CBC, PBKDF2/SHA-256, 200,000 iterations). New Linux files have owner-only read/write permissions. Preserve both files and secret across restarts and deployments.

Windows without a protection password continues to use the existing DPAPI `signing-key.bin` and `encryption-key.bin`. Supplying a password opts into `signing-key.pem` and `encryption-key.pem` on either OS. Existing keys are never silently replaced when changing formats, entering a wrong password, or reading damaged files. A format change requires deliberate migration; password rotation requires re-encrypting the existing keys outside this application. DPAPI keys must be decrypted on their original Windows machine to migrate them.

Ephemeral keys remain available with `PersistKeys: false` for a quick check, with the existing restart/token limitations. Token keys and HTTPS certificates are separate.

## Verification

Run `dotnet test IdentityServer.sln`. Tests cover platform/provider selection, rejection of weaker protocols, realm boundaries, LDAP escaping, missing/duplicate users, AD/Samba and FreeIPA profile mapping, direct-group filters, scoped claims, and saved key continuity. Host tests execute the real authorization-code/PKCE/token flow using a simulated authenticated principal and profile; they do **not** prove a live Kerberos handshake or directory interoperability. Existing Windows live-AD tests skip when the directory is unavailable.

Before deploying each provider, check it against a real directory and client:

1. Anonymous discovery succeeds, and unrecognized paths do not prompt for credentials.
2. A browser completes Kerberos sign-in and authorization-code redemption through the public hostname.
3. AD/Samba subjects match existing Windows SIDs; FreeIPA subjects match `ipaUniqueID`. Check name/email and allowed direct groups, including a user whose only path to a group is nested (that group must be absent).
4. Unknown realms, missing/ambiguous accounts, and NTLM when Kerberos is required are rejected. A failed directory bind issues no tokens and does not change provider.
5. LDAP rejects an untrusted certificate, and the configured read account has only the necessary directory access.
6. With persistence enabled, an unexpired pre-restart authorization code can be redeemed after a restart. A wrong key password fails startup without modifying files.
7. Recheck the unchanged Windows default, including local accounts and the configured IIS/Kestrel hosting mode.

Live Linux/FreeIPA/Samba/Kerberos verification requires an appropriate environment; it is not implied by passing the isolated tests or publishing a Linux binary.

To exercise real portable LDAP reads, set `IDENTITYSERVER_LDAP_TEST_CONFIG` to an absolute path to a private JSON file containing the desired `IdentityServer:Directory` settings. Set `IDENTITYSERVER_LDAP_TEST_PRINCIPAL` to a known user's realm-qualified principal and `IDENTITYSERVER_LDAP_TEST_SUBJECT` to their independently verified SID or `ipaUniqueID`. Optionally set `IDENTITYSERVER_LDAP_TEST_GROUP` to an expected direct group common name. Supply a bind password with `IDENTITYSERVER_LDAP_TEST_IdentityServer__Directory__BindPassword` or use the test process's Kerberos credential cache. Run `dotnet test IdentityServer.sln --filter FullyQualifiedName~LdapIntegrationTests` for each provider. When explicitly enabled, connection, certificate or mapping failures fail the test rather than skip it. This checks LDAP, not the HTTP Kerberos exchange.
