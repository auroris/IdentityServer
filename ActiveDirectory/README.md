# AurorisNet.ActiveDirectory

Wrappers over `System.DirectoryServices` for .NET 10 that read Active Directory users, groups,
computers and printers.

The package adds `ADUser`, `ADGroup`, `ADComputer`, `ADPrinter` and the `DirectorySearch` helper,
in the `ActiveDirectory` namespace. It runs on Windows, as an account that can read the
directory.

## Install

```shell
dotnet add package AurorisNet.ActiveDirectory
```

`AurorisNet.IdentityServer` and `AurorisNet.IdentityServer.Directory` install this package as a
dependency.

## Use

Read one user:

```csharp
using System;
using ActiveDirectory;

using var user = new ADUser(@"EXAMPLE\alice");
Console.WriteLine(user.DisplayName);
Console.WriteLine(user.Email);
```

`ADUser` takes a SAM account name, a `DOMAIN\user` name or a full `LDAP://` path, and throws
`InvalidOperationException` when it finds no such user. Dispose each wrapper when you are done
with it.

Search for users:

```csharp
using ActiveDirectory;

var search = new DirectorySearch("LDAP://OU=Staff,DC=example,DC=com", ADObject.ObjectClass.User);
search.PropertiesToSearch.Add(ADProperties.User.Surname, "Smith", DirectorySearch.MatchType.StartsWith);
search.PropertiesToLoad.Add(ADProperties.User.DisplayName);
foreach (ADUser found in search.Find<ADUser>(25))
{
    using (found) Console.WriteLine(found.DisplayName);
}
```

- A result holds only the attributes named in `PropertiesToLoad`. Any other attribute reads as
  empty until you call `Bind()` on the result, which opens a connection for it.
- `PropertiesToSearch` values are escaped. `AdditionalFilter` is raw LDAP: build it from trusted
  values only.
- `Find<T>(howMany)` limits the results. Set `PageSize` to read more results than the domain
  controller returns in one page.

## Related packages

- `AurorisNet.IdentityServer.Directory` builds address-book searches, people pickers and group
  listings on this library.
- `AurorisNet.IdentityServer` is an OpenID Connect server for Active Directory, Samba AD and
  FreeIPA.

## Documentation

- [Using the Active Directory library](https://github.com/auroris/AurorisNet.IdentityServer/blob/main/docs/embedding.md#using-the-active-directory-library-alone)
- [Source and issues](https://github.com/auroris/AurorisNet.IdentityServer)

## License

[MIT](https://github.com/auroris/AurorisNet.IdentityServer/blob/main/LICENSE)
