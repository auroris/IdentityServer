# Directory providers

IdentityServer reads each person's profile and groups from one directory. This page explains
how to choose the provider, what each `IdentityServer:Directory` setting does, how an
authenticated name is matched to a directory account, and how the token keys are stored.

For the installation steps on Linux, follow the [Linux guide](linux-deployment.md). On a
domain-joined Windows server you normally need nothing from this page beyond the first
section.

## Contents

- [Choosing a provider](#choosing-a-provider)
- [Authentication mode](#authentication-mode)
- [Settings for AD, Samba AD and FreeIPA](#settings-for-ad-samba-ad-and-freeipa)
- [How the read connection authenticates](#how-the-read-connection-authenticates)
- [How a person is matched to an account](#how-a-person-is-matched-to-an-account)
- [Groups and roles](#groups-and-roles)
- [Persistent token keys](#persistent-token-keys)
- [Checking a provider](#checking-a-provider)

## Choosing a provider

`IdentityServer:Directory:Provider` selects how the directory is read.

| Provider | Use it when | How it reads |
|---|---|---|
| `Windows` | The server is domain-joined Windows and you want zero directory configuration | `System.DirectoryServices` as the service account, with automatic domain discovery. Also handles local machine accounts. |
| `ActiveDirectory` | Microsoft AD from Linux, or from Windows with an explicit server and read account | LDAP with the AD schema |
| `SambaAd` | A Samba AD domain controller | LDAP with the AD schema |
| `FreeIpa` | A FreeIPA realm | LDAP with the FreeIPA schema |
| `Auto` (default) | | `Windows` on Windows when no `Server` is set; otherwise `ActiveDirectory`. FreeIPA is never chosen automatically. |

Values are case-insensitive. The provider is fixed at startup and logged. A directory
failure at run time never switches provider; it fails that sign-in.

The `Windows` provider accepts no other `Directory` settings and cannot run on Linux. The
other three need the settings below.

## Authentication mode

`IdentityServer:Authentication:Mode` decides who verifies the browser and what is accepted.

| Mode | Behaviour |
|---|---|
| `Auto` (default) | `Negotiate` on Windows, except with FreeIPA; `Kerberos` on Linux. |
| `Negotiate` | Kerberos or NTLM, including local accounts. Windows only. |
| `Kerberos` | Kerberos only. NTLM and anything that cannot prove its protocol is refused, for sign-in and for protected application routes alike, including identities IIS supplies. |
| `ProxyKerberos` | A Kerberos-authenticating Apache or Nginx verifies the browser and passes the principal to the backend under the trust rules in the [proxy guide](apache-and-nginx.md). Requires an LDAP provider; proxy identities carry no SID. |

`Auto` never accepts proxy headers; `ProxyKerberos` must be chosen deliberately.

## Settings for AD, Samba AD and FreeIPA

Samba AD, with a dedicated read account whose password comes from the environment:

```json
{
  "IdentityServer": {
    "Directory": {
      "Provider": "SambaAd",
      "Server": "ldaps://dc.example.com",
      "Realm": "EXAMPLE.COM",
      "BaseDn": "DC=example,DC=com",
      "BindDn": "identityserver-reader@EXAMPLE.COM"
    }
  }
}
```

FreeIPA:

```json
{
  "IdentityServer": {
    "Directory": {
      "Provider": "FreeIpa",
      "Server": "ldaps://ipa.example.com",
      "Realm": "EXAMPLE.COM",
      "BaseDn": "dc=example,dc=com",
      "BindDn": "uid=identityserver-reader,cn=users,cn=accounts,dc=example,dc=com"
    }
  }
}
```

Use `ActiveDirectory` with the Samba shape for Microsoft AD. Supply the password as
`IdentityServer__Directory__BindPassword`.

| Setting | Meaning |
|---|---|
| `Server` | Required. `ldaps://host[:port]` (TLS, default port 636) or `ldap://host[:port]` (StartTLS, default port 389). StartTLS is mandatory on `ldap://`; credentials are never sent before TLS is up. No user info, path or query. |
| `Realm` | Required. The Kerberos realm whose users may sign in, for example `EXAMPLE.COM`. A principal from any other realm is refused before the directory is consulted. |
| `BaseDn` | Required. The directory suffix searched for users and groups. Referral chasing is off. |
| `UserBaseDn` | Optional. Narrows the user search. Defaults to `BaseDn` for AD and Samba, `cn=users,cn=accounts,<BaseDn>` for FreeIPA. |
| `GroupBaseDn` | Optional. Narrows the group search. Defaults to `BaseDn` for AD and Samba, `cn=groups,cn=accounts,<BaseDn>` for FreeIPA. |
| `NetbiosDomain` | Optional, AD and Samba only. The short domain name such as `EXAMPLE`. Lets a `EXAMPLE\user` identity be looked up when it has no SID, and formats the account-name claim as `EXAMPLE\user`. |
| `BindDn`, `BindPassword` | Set both to read as a dedicated account, or neither to use the process's own Kerberos credentials. The name can be a DN or, on AD and Samba, a user principal name. |
| `TimeoutSeconds` | Per-request LDAP timeout. Default 10, range 1 to 120. |

Surrounding whitespace is trimmed from `Server`, `Realm`, `NetbiosDomain` and the DN
settings. `BindPassword` is used exactly as supplied.

## How the read connection authenticates

**With `BindDn` and `BindPassword`** the server opens TLS, then binds as that account. This
is the recommended arrangement: one dedicated account with read access to users and groups,
rotated like any other service password. Do not use a domain administrator or FreeIPA's
Directory Manager.

**Without them** the server binds with its own Kerberos credentials: integrated
authentication on Windows, SASL GSSAPI from the process's credential cache on Linux. The
HTTP keytab that accepts browser tickets does **not** provide those credentials; on Linux
you must keep a separate credential cache renewed, as described in the
[Linux guide](linux-deployment.md#optional-kerberos-credentials-for-outbound-ldap). Against
Samba over TLS, the tested OpenLDAP client also needed `LDAPSASL_CBINDING=tls-endpoint` and
`LDAPSASL_SECPROPS=maxssf=0` in the service environment.

**Certificates** are validated by the operating system's trust store, and on Linux by
OpenLDAP's. There is no switch to skip validation; install the issuing CA instead.

**StartTLS on Linux** first opens an anonymous session, upgrades it to TLS, and only then
attaches credentials. If the upgrade is refused the connection is closed and no credentials
are sent. Use LDAPS if the directory rejects even an anonymous session.

Each sign-in opens, uses and closes its own connection. There is no connection pool to go
stale.

## How a person is matched to an account

The authenticated name arrives as a Kerberos principal such as `alice@EXAMPLE.COM`, or on
Windows as `EXAMPLE\alice` with a SID.

| Provider | Match |
|---|---|
| AD and Samba, with a SID | By `objectSid`. Unambiguous, even across trusts. |
| AD and Samba, by name | The realm must equal `Realm`. Then `userPrincipalName` equals the full principal, or `sAMAccountName` equals the part before `@`, within `UserBaseDn`. A `EXAMPLE\alice` name needs `NetbiosDomain`. |
| FreeIPA | The realm must equal `Realm`. Then `krbPrincipalName` equals the full principal within `UserBaseDn`. |

Exactly one account must match. None, or more than one, is refused. Unqualified names and
service principals are refused. All of these are reported to the application as
`access_denied` with a warning in the log; connection, certificate and bind failures are
reported as `server_error` with an error in the log.

The subject claim is the account's `objectSid` on AD and Samba, or `ipaUniqueID` on
FreeIPA, so it survives renames. The account-name claim is the authenticated principal, or
`NETBIOS\user` when `NetbiosDomain` is set.

## Groups and roles

Groups are read by searching for group objects whose `member` attribute contains the
person's DN: `objectClass=group` under `GroupBaseDn` on AD and Samba, `groupOfNames` under
the FreeIPA groups container. Results are paged. Only direct memberships are returned;
nested groups, the AD primary group and FreeIPA roles are not. The group's common name is
then matched against the `IdentityServer:Groups` patterns described in
[Configuration](configuration.md#roles-from-groups).

The group search runs only when the application requests the `roles` scope.

## Persistent token keys

Set `IdentityServer:PersistKeys` to `true` on every real deployment and choose an absolute
`IdentityServer:DataPath` the service can write.

- **Linux** requires `IdentityServer__KeyProtectionPassword`. The keys are stored as
  password-encrypted PKCS#8 (`signing-key.pem`, `encryption-key.pem`; AES-256-CBC,
  PBKDF2/SHA-256, 200,000 iterations) with owner-only permissions. Keep the password with
  your backups of the key files.
- **Windows** without a password stores DPAPI-protected `signing-key.bin` and
  `encryption-key.bin`, readable only on the machine that created them. With a password it
  behaves like Linux.

The server never replaces a key it cannot read. A wrong password, a damaged file or a key in
the other format stops startup. Token keys are unrelated to the HTTPS certificate.

## Checking a provider

The [verification guide](verification.md) has the full checklist. The provider-specific
points are:

1. Subjects are SIDs on AD and Samba and `ipaUniqueID` values on FreeIPA. Compare against
   the directory with `ldapsearch` or PowerShell.
2. A user whose only path to a group is through a nested group does not receive that role.
3. A user from another realm, an unknown account, and an NTLM sign-in in `Kerberos` mode
   are refused with `access_denied`. A wrong read password gives `server_error` and no
   token.
4. An LDAP server certificate from an untrusted CA is rejected.

The opt-in [live directory test](verification.md#live-directory-tests) performs the first
check automatically against your directory.
