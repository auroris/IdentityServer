# Release assets

Each release publishes the standalone server for Windows and Linux and the three NuGet packages.
This page lists the files and shows how to run the server from an archive.

The [release guide](https://github.com/auroris/AurorisNet.Build/blob/main/docs/releasing.md)
describes the automation that every AurorisNet repository shares: what starts a release, how the
version is chosen, the inputs of the Release workflow and trusted publishing on nuget.org.

## Files in a release

For version `1.2.3`, the GitHub release holds:

| File | Contents |
|---|---|
| `IdentityServer-1.2.3-win-x64.zip` | The server for Windows x64, with the .NET runtime. |
| `IdentityServer-1.2.3-linux-x64.tar.gz` | The server for Linux x64, with the .NET runtime. The archive keeps the executable permission. |
| `AurorisNet.IdentityServer.1.2.3.nupkg` | The embeddable core. It depends on `AurorisNet.ActiveDirectory` at the same version or later. |
| `AurorisNet.ActiveDirectory.1.2.3.nupkg` | The `System.DirectoryServices` wrappers. |
| `AurorisNet.IdentityServer.Directory.1.2.3.nupkg` | The people and group lookup. It depends on `AurorisNet.ActiveDirectory` at the same version or later. |
| Three `.snupkg` files | The debugging symbols of the three packages. |
| `SHA256SUMS.txt` | The SHA-256 checksums of the eight files above. |

The workflow also pushes the three packages, with their symbols, to nuget.org.

Each archive also holds the README, the license, the `docs` folder and the `deploy` templates.

## Run a self-contained archive

The archives need no .NET installation on the server.

1. Extract the archive into the deployment folder.
2. Replace `appsettings.json`, which holds the repository's demonstration settings, with your
   own. Follow the [Windows](windows-iis.md) or [Linux](linux-deployment.md) guide.
3. Start `IdentityServer.exe` on Windows, or `./IdentityServer` on Linux, from that folder.

IIS still needs the ASP.NET Core Hosting Bundle, which supplies the IIS module. Linux still needs
the Kerberos and LDAP client libraries and the configuration in the
[Linux guide](linux-deployment.md#2-install-the-runtime-and-client-libraries).

The systemd templates in `deploy/` start a framework-dependent build. For an archive, change
`ExecStart` in the unit from

```ini
ExecStart=/usr/bin/dotnet /opt/identityserver/IdentityServer.dll
```

to

```ini
ExecStart=/opt/identityserver/IdentityServer
```

## Testing before a release

The workflows run the tests on Windows and Linux with
`dotnet test IdentityServer.sln --configuration Release --filter 'Category!=LiveDirectory'`.
Tests in the `LiveDirectory` category need a real directory and run on a developer's machine.
Run them before a release that changes how the server or the lookup reads the directory:
see [Live directory tests](verification.md#live-directory-tests).
