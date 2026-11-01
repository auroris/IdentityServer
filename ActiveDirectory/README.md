# AurorisNet.ActiveDirectory

Windows-only wrappers around `System.DirectoryServices` for .NET 10, providing `ADUser`,
`ADGroup`, `ADComputer`, `ADPrinter` and directory-search helpers in the `ActiveDirectory`
namespace.

## Install

```shell
dotnet add package AurorisNet.ActiveDirectory
```

This package is also installed automatically by `AurorisNet.IdentityServer`. The library
assembly remains `ActiveDirectory.dll`. Its directory operations require Windows and
appropriate directory access.

## Read a user

Run on Windows with an account that has access to the directory. Replace the example
domain and account name with your own:

```csharp
using System;
using ActiveDirectory;

using var user = new ADUser(@"EXAMPLE\alice");
Console.WriteLine(user.DisplayName);
Console.WriteLine(user.Email);
```

`ADUser` accepts a SAM account name, a `DOMAIN\user` name or a full `LDAP://` path.
Dispose wrappers after use to release any directory connections they own.

For searches, use `DirectorySearch` with an LDAP root to scope results. Request needed
attributes in `PropertiesToLoad`; other attributes read as empty until you call `Bind()`
on the returned wrapper. `PageSize` enables paging and `Find<T>(howMany)` limits results.

For OpenID Connect hosting, install `AurorisNet.IdentityServer`. Its portable LDAP
providers use `System.DirectoryServices.Protocols` for Active Directory, Samba AD and
FreeIPA on Windows or Linux.

## Documentation and support

- [Library usage and disposal guide](https://github.com/auroris/IdentityServer/blob/main/docs/embedding.md#using-the-active-directory-library-alone)
- [Source code](https://github.com/auroris/IdentityServer/tree/main/ActiveDirectory)
- [Report an issue](https://github.com/auroris/IdentityServer/issues)

## License

[MIT](https://github.com/auroris/IdentityServer/blob/main/LICENSE)
