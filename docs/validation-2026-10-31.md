<a id="live-directory-and-browser-validation--2026-10-31"></a>

# Live directory and browser validation: 2026-10-31

VM tests exercised Microsoft AD, Samba AD and FreeIPA with Kerberos, LDAP and browser authorization-code flows. All checks in the final run passed after the changes below. The results describe the environments and coverage recorded here. Use the [Linux deployment guide](linux-deployment.md) to install and verify your own deployment.

## Updates from the tests

| Finding | Resulting update | Verification |
|---|---|---|
| Linux `ldap://` lookups failed because the .NET LDAP connection's server URI was not initialized before StartTLS. | [LdapSearch](../IdentityServer.Core/LdapUserProfileProvider.cs) opens an anonymous session on Linux, requires the TLS upgrade, and assigns/sends read credentials only afterward. A refused upgrade closes the connection. | Live Samba and FreeIPA StartTLS reads with password and GSSAPI binds; [protocol-level regression](../tests/IdentityServer.Tests/LdapTransportTests.cs) checks that no credentialed bind follows a refused upgrade. |
| Nginx returned 502, logging `upstream sent too big header`, when the encrypted authorization code exceeded its default response-header buffer. | [Proxy snippet](../deploy/nginx/identityserver-proxy.conf) uses a 16 KiB header buffer and four 16 KiB response buffers. | Successful authorization redirects and code redemption through the actual Nginx server. Larger profiles still require suitable claim limits/buffer sizing. |
| Nginx selected the host's default realm for a service keytab belonging to another realm. | [Server template](../deploy/nginx/identityserver.conf) explicitly sets `auth_gss_realm`; operators replace it with the keytab/application realm. | Samba and FreeIPA SSO passed while AD remained the Linux host's default realm. |
| The Windows live-AD test passed a bare member DN to an API that interpreted it as a SAM account name. | [AD integration test](../tests/IdentityServer.Tests/ActiveDirectoryIntegrationTests.cs) uses an `LDAP://` path for the DN. | The live test passed on the Windows DC. This was a test-fixture correction. |

The directory fixtures also needed configuration changes:

- The Samba HTTP account initially exported RC4-only keys. Setting `msDS-SupportedEncryptionTypes=24` and exporting AES keys resolved Linux client compatibility.
- Samba GSSAPI LDAP over TLS required `LDAPSASL_CBINDING=tls-endpoint` and `LDAPSASL_SECPROPS=maxssf=0` for the tested OpenLDAP/SASL client. The same configuration passed against FreeIPA. TLS encryption and certificate verification remained enabled.
- Each public HTTP hostname needed its own correctly owned SPN/keytab, matching DNS/realm mapping and trusted HTTPS certificate. The server's LDAP reader credentials were independent of browser authentication.

These findings are covered in the [Kerberos/LDAP reference](kerberos-and-ldap.md), [proxy guide](apache-and-nginx.md), and [Linux installation steps](linux-deployment.md). Native Kestrel now has accompanying [settings](../deploy/appsettings.Kerberos.json), [systemd unit](../deploy/identityserver-native.service) and [secret-file template](../deploy/identityserver-native.env.example); the existing proxy templates remain available.

## Environment

| Component | Tested version/setup |
|---|---|
| Windows | Windows Server 2022 domain controller with IIS; ASP.NET Core 10.0.14 |
| Linux | Ubuntu 26.04.1; .NET SDK 10.0.114 and ASP.NET Core 10.0.14 |
| Samba AD | 4.23.6, provisioned DC in an Ubuntu container |
| FreeIPA | 4.13.4, official AlmaLinux 9 server container under rootful Podman; 389 DS 2.8.0 / MIT Kerberos 1.21.1 |
| Apache | 2.4.66 with mod_auth_gssapi 1.6.4 |
| Nginx | 1.28.3 with SPNEGO module 1.1.3 |
| LDAP client | OpenLDAP 2.6.10, SASL GSSAPI libraries |
| Browser automation | Headed Linux Chromium 156.0.8078.4, Playwright 1.64.1, Xvfb |

The official FreeIPA image was `quay.io/freeipa/freeipa-server:almalinux-9`, digest `sha256:62bb88d7e4ffe4691c123d8cdaf3d8a65dae1d9bd8e1e6baf469ca6f18770980`. These versions identify the tested environment. Validate the versions chosen for your deployment.

The checkpoint VMs hosted temporary test accounts, direct/nested groups, test CAs, service keytabs and isolated directory realms. Each application instance selected one provider/realm. Directory trust relationships were not configured.

## Browser SSO results

| Directory | Native Kestrel | Apache | Nginx | IIS | Successful flows |
|---|---:|---:|---:|---:|---:|
| Microsoft AD | 2 | 2 | 2 | 2 | 8 |
| Samba AD | 2 | 2 | 2 | — | 6 |
| FreeIPA | 2 | 2 | 2 | — | 6 |
| **Total** | **6** | **6** | **6** | **2** | **20** |

Every endpoint was tested with two distinct users. Chromium used a real OS Kerberos credential cache and hostname allowlist, with HTTPS certificate verification enabled. No browser passwords, HTTP Basic credentials or injected Authorization headers were used. Fresh test profiles initialized Chromium's GSSAPI loader from its observed Negotiate-challenge state; netlogs confirmed Negotiate handlers for the tested endpoints.

A separate HTTPS relying party initiated the flow, checked its Secure/HttpOnly/SameSite state cookie on callback, redeemed the code server-side with S256 PKCE, and verified:

- ID-token RSA signature against JWKS, issuer, audience and nonce.
- The subject against an independently obtained AD/Samba SID or FreeIPA `ipaUniqueID`.
- Email and the exact expected direct roles; nested-only groups were absent.
- Anonymous discovery/JWKS and code redemption without user Kerberos credentials.

All **10 no-ticket browser attempts** received an HTTP 401 challenge and produced no successful token result. Final successful browser runs used the updated core assembly on all nine Linux application services and the IIS application.

## Live LDAP and other checks

Both Samba and FreeIPA passed these configurations with actual directory accounts:

| Transport | Explicit read-account password | Kerberos/GSSAPI cache |
|---|---|---|
| LDAPS | Passed for both providers | Passed for both providers |
| StartTLS | Passed for both providers | Passed for both providers |

These **8 positive LDAP configurations** each checked profile reads both with groups and with the group query omitted. They verified the subject against an independently obtained value and checked an expected direct group. Both directories rejected an untrusted CA over LDAPS and StartTLS, giving **4 expected certificate rejections**.

The earlier AD phase also verified spoofed identity/forwarding-header rejection, anonymous discovery/JWKS, correct public URLs/prefixes, distinct users over reused connections, and persisted key continuity. After service restarts/IIS recycling, signing JWKS remained unchanged and still-valid pre-restart authorization codes redeemed successfully across the four AD hosting modes. That restart/code-continuity matrix was not repeated for Samba/FreeIPA.

| Automated suite | Passed | Skipped | Failed | Scope |
|---|---:|---:|---:|---|
| Linux, after StartTLS fix | 102 | 9 | 0 | Eight Windows-only cases and the separately enabled portable LDAP case skipped |
| Windows workstation, after fix | 104 | 7 | 0 | Five domain tests, portable LDAP and Linux transport test skipped |
| Windows DC, earlier AD phase | 109 | 1 | 0 | Portable LDAP then enabled and passed separately, for 110 enabled cases |

The full Windows DC suite was not rerun during the Samba/FreeIPA expansion; IIS browser flows were rerun with the updated core. The Release build completed with no warnings/errors, and `git diff --check` passed.

During the documentation follow-up, the new native settings and systemd template were also installed on the Linux VM, substituting the fixture's hostname, client, directory credentials and an unused HTTPS port. The service started under the supplied filesystem restrictions, served trusted HTTPS discovery, persisted both token keys with mode 0600, and completed two additional browser/PKCE flows against Samba with independent users. These template checks are separate from the 20-flow matrix above. Documentation links, JSON examples, Bash/PowerShell syntax and systemd unit syntax were checked as well; the guide's administrative provisioning commands were not rerun against fresh domains during this follow-up.

## Evidence and limits

The local ignored artifact directories are `artifacts/vm-validation/20261031-2109/` and `artifacts/vm-validation/20261031-expanded/`. They contain reports, JSON results, TRX files, screenshots and fixture scripts retained on the validation host. This document preserves the outcome for repository readers after the artifacts or VM checkpoints are removed. Fixture passwords, keytabs and raw authentication logs remain outside the repository documentation.

The expanded run added Samba AD, FreeIPA and browser coverage to the earlier AD results. Browser coverage is limited to the stated Linux Chromium version; Windows-native browsers, Firefox/Safari, cross-realm trusts and production load were not exercised. The fixture scripts configure the disposable test environment used for these checks.
