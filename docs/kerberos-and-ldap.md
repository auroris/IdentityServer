# Directory providers

IdentityServer reads each person's profile and groups through a directory provider. This page
describes the providers and every setting under `IdentityServer:Directory`. For the installation
steps on Linux, follow the [Linux guide](linux-deployment.md).

## Choosing a provider

`IdentityServer:Directory:Provider` selects how the server reads the directory.

| Provider | Use it for | How it reads |
|---|---|---|
| `Windows` | A domain-joined Windows server | `System.DirectoryServices`, as the account the server runs as, with automatic domain discovery. It also signs in local Windows accounts, without a directory read. |
| `ActiveDirectory` | Microsoft Active Directory from Linux, or from Windows with an explicit server and read account | LDAP with the Active Directory schema |
| `SambaAd` | A Samba AD domain controller | LDAP with the Active Directory schema |
| `FreeIpa` | A FreeIPA realm | LDAP with the FreeIPA schema |
| `Auto` (default) | | `Windows` on Windows when no `Server` is set, and `ActiveDirectory` otherwise. Name `FreeIpa` yourself for a FreeIPA realm. |

Provider names are matched without regard to case. The server chooses the provider at startup
and logs it.

The `Windows` provider takes no other `Directory` setting and runs on Windows only. The server
stops at startup when either rule is broken. The other three providers need the settings below.

## Authentication mode

[`IdentityServer:Authentication:Mode`](configuration.md#authentication-mode) selects who verifies
the browser. The server checks the mode against the provider at startup:

- `FreeIpa` needs `Kerberos` or `ProxyKerberos`. `Auto` selects `Kerberos` for it.
- `ProxyKerberos` needs `ActiveDirectory`, `SambaAd` or `FreeIpa`, also on Windows. The principal
  a proxy passes carries no SID, so the `Windows` provider cannot resolve it.

## Settings for AD, Samba AD and FreeIPA

Samba AD, with a dedicated read account:

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

For Microsoft Active Directory, use the Samba example with `Provider` set to `ActiveDirectory`.
Supply the password in the environment variable `IdentityServer__Directory__BindPassword`.

| Setting | Default | What it does |
|---|---|---|
| `Server` | Required | `ldaps://host[:port]` for TLS, on port 636 unless you name one, or `ldap://host[:port]` for StartTLS, on port 389. An `ldap://` connection always upgrades to TLS before the server sends credentials. The URL takes no user name, path or query. |
| `Realm` | Required | The Kerberos realm whose users may sign in, such as `EXAMPLE.COM`. The server refuses a principal from another realm before it contacts the directory. |
| `BaseDn` | Required | The directory suffix searched for users and groups. The server does not follow referrals. |
| `UserBaseDn` | `BaseDn`. For FreeIPA, `cn=users,cn=accounts,<BaseDn>`. | Narrows the user search. |
| `GroupBaseDn` | `BaseDn`. For FreeIPA, `cn=groups,cn=accounts,<BaseDn>`. | Narrows the group search. |
| `NetbiosDomain` | none | Active Directory and Samba only. The short domain name, such as `EXAMPLE`. With it, the server resolves an `EXAMPLE\user` identity that carries no SID, and writes the account-name claim as `EXAMPLE\user`. |
| `BindDn`, `BindPassword` | none | Set both to read as a dedicated account, or neither to use the process's own Kerberos credentials. `BindDn` is a distinguished name or, for Active Directory and Samba, a user principal name. |
| `TimeoutSeconds` | `10` | The timeout of each LDAP request, from 1 to 120. |

The server trims whitespace around `Server`, `Realm`, `NetbiosDomain` and the DN settings. It
uses `BindPassword` exactly as supplied. An optional setting that is present and empty stops
startup.

## How the read connection authenticates

**With `BindDn` and `BindPassword`,** the server opens TLS and then binds as that account. Use
one dedicated account that can read users and groups, and rotate its password like any other
service password. A domain administrator or FreeIPA's Directory Manager has far more access than
the server needs.

**With neither bind setting,** the server binds with its own Kerberos credentials. Windows uses
integrated authentication. Linux uses SASL GSSAPI with the process's credential cache, which you
must keep renewed:
see [Kerberos credentials for outbound LDAP](linux-deployment.md#optional-kerberos-credentials-for-outbound-ldap).

**Certificates.** The operating system validates the directory's certificate: the Windows trust
store, or OpenLDAP's on Linux. Install the issuing CA there. The server has no setting that
skips validation.

**StartTLS on Linux.** The server opens an anonymous session, upgrades it to TLS and only then
attaches credentials. When the directory refuses the upgrade, the server closes the connection
with no credentials sent. Use `ldaps://` when the directory rejects anonymous sessions.

Each sign-in opens one directory connection and closes it after the lookup.

## How a person is matched to an account

The authenticated name arrives as a Kerberos principal such as `alice@EXAMPLE.COM`, or on Windows
as `EXAMPLE\alice` with a SID.

| Provider | Match |
|---|---|
| Active Directory and Samba, with a SID | `objectSid` equals the SID. |
| Active Directory and Samba, by name | The realm must equal `Realm`. Then `userPrincipalName` equals the full principal, or `sAMAccountName` equals the part before `@`, under `UserBaseDn`. An `EXAMPLE\alice` name needs `NetbiosDomain` set to `EXAMPLE`. |
| FreeIPA | The realm must equal `Realm`. Then `krbPrincipalName` equals the full principal, under `UserBaseDn`. |

Exactly one account must match.

| Result | The application receives | Log |
|---|---|---|
| No account or several accounts match, the realm differs, the name has no realm or domain, or the name is a service principal such as `HTTP/host@EXAMPLE.COM` | `access_denied` | A warning, `Authorization denied for user` |
| The connection, the certificate check or the bind fails | `server_error` | An error with the exception, `Error building authorization claims` |

The `sub` claim is the account's `objectSid` on Active Directory and Samba, and its `ipaUniqueID`
on FreeIPA, so it survives a rename. The account-name claim is the authenticated principal, or
`NETBIOS\user` when `NetbiosDomain` is set.

## Groups and roles

When the application requests the `roles` scope, the server searches for the groups whose
`member` attribute holds the person's distinguished name: `objectClass=group` under `GroupBaseDn`
on Active Directory and Samba, `objectClass=groupOfNames` on FreeIPA. The search is paged.

The result holds direct memberships only. Nested groups, the Active Directory primary group and
FreeIPA roles are left out. The server then matches each group's common name against the
`IdentityServer:Groups` patterns described in
[Roles from groups](configuration.md#roles-from-groups).

## Checking a provider

[Verification](verification.md) has the full checklist. These points depend on the provider:

1. The subject is a SID on Active Directory and Samba and an `ipaUniqueID` on FreeIPA. Compare it
   with the directory, using `ldapsearch` or PowerShell.
2. A person whose only path to a group runs through a nested group gets no role for it.
3. A user from another realm, an unknown account, and an NTLM sign-in in `Kerberos` mode each
   end in `access_denied`. A wrong read password ends in `server_error` and no token.
4. The server rejects an LDAP certificate from an untrusted CA.

The [live directory test](verification.md#live-directory-tests) runs the first check against
your directory.
