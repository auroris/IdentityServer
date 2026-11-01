# Creating a release

The [Release workflow](../.github/workflows/release.yml) runs manually and produces one
GitHub release containing Windows and Linux packages.

1. Commit and push the workflow to the repository's default branch so GitHub shows the
   **Run workflow** button.
2. Open **Actions → Release → Run workflow** and select the branch to release.
3. Enter a version such as `1.2.3` or `1.2.3-rc.1`, then choose **Run workflow**.

An optional `v` prefix is accepted. Versions use `MAJOR.MINOR.PATCH`, optionally followed
by a prerelease suffix; build metadata (`+...`) is not supported. Numeric version components
must be between 0 and 65534 to fit .NET assembly versions. Existing tags are rejected.

The workflow tests on Windows and Linux, excluding tests that require a live directory,
and publishes self-contained x64 builds using .NET 10. The supplied version is embedded
in the assemblies. Both builds must succeed before the workflow creates the `v<version>`
tag at the exact commit selected when the workflow started and publishes the release with
automatically generated notes. A suffix such as `-rc.1` marks the release as a prerelease.
The built-in `GITHUB_TOKEN` supplies release permissions; no personal access token is needed.

For version `1.2.3`, the release assets are:

| Asset | Contents |
|---|---|
| `IdentityServer-1.2.3-win-x64.zip` | Windows x64 application and bundled .NET runtime. |
| `IdentityServer-1.2.3-linux-x64.tar.gz` | Linux x64 application and bundled .NET runtime; executable permissions are preserved. |
| `SHA256SUMS.txt` | SHA-256 checksums of both archives. |

Each archive also includes the README, license, documentation and deployment templates.
Extract it into a deployment folder and configure it using the [Windows](windows-iis.md)
or [Linux](linux-deployment.md) guide. Start the bundled application with
`./IdentityServer.exe` on Windows or `./IdentityServer` on Linux. These packages do not need
a separate .NET runtime installation. IIS still needs the ASP.NET Core Hosting Bundle,
and Linux still needs the native libraries and Kerberos/LDAP configuration in its guide.
When using the supplied systemd templates with these packages, change `ExecStart` from
`/usr/bin/dotnet /opt/identityserver/IdentityServer.dll` to `/opt/identityserver/IdentityServer`.
Retain any arguments on that line.

If a build or test fails, no release or tag is created. Fix the problem and run the workflow
again. If uploading release assets fails, GitHub CLI can leave a draft release; remove that
incomplete draft before retrying, and use a new version if its tag already exists. Published
releases and tags are never overwritten by this workflow.
