# Creating a release

The [Release workflow](../.github/workflows/release.yml) runs manually and produces one
GitHub release containing Windows and Linux packages plus the embeddable NuGet libraries.
Runs from `main` also publish the libraries to NuGet.org using Trusted Publishing after
the GitHub release succeeds. Complete the one-time setup below before the first NuGet run.

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
| `AurorisNet.IdentityServer.1.2.3.nupkg` | Embeddable core library; depends on `AurorisNet.ActiveDirectory` at the release version or later. |
| `AurorisNet.ActiveDirectory.1.2.3.nupkg` | The Windows directory-provider dependency. |
| `*.snupkg` | Debugging symbols for the two NuGet packages. |
| `SHA256SUMS.txt` | SHA-256 checksums of all six release files. |

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

## NuGet package READMEs

The libraries ship their own NuGet-specific READMEs:
[`IdentityServer.Core/README.md`](../IdentityServer.Core/README.md) and
[`ActiveDirectory/README.md`](../ActiveDirectory/README.md). Each project packs its file
as `README.md` at the package root and selects it with `PackageReadmeFile`.

Keep these package guides in sync with public APIs and configuration changes. Use standard
Markdown and absolute documentation links; the repository's root README uses local badge
images, Mermaid and GitHub callouts that are unsuitable for the NuGet page. Follow
[NuGet's supported Markdown features](https://learn.microsoft.com/en-us/nuget/nuget-org/package-readme-on-nuget-org#supported-markdown-features).

## NuGet.org Trusted Publishing

The publishing job authenticates as **AurorisNet**, hardcoded in `release.yml`, using
[`NuGet/login`](https://github.com/NuGet/login). GitHub issues an OIDC token and NuGet
exchanges it for a short-lived API key immediately before the push. No stored NuGet API
key or username secret is required. The job does not use a GitHub environment and only
runs from `main` in `auroris/IdentityServer`.

### One-time setup

Sign in to NuGet.org as **AurorisNet**, open **Trusted Publishing**, and create one
GitHub policy with the settings below. Leave the optional Environment field blank.

| Setting | Value |
|---|---|
| Policy owner | `AurorisNet` |
| Repository owner | `auroris` |
| Repository | `IdentityServer` |
| Workflow file | `release.yml` |
| Environment | Leave blank |
| Scope | Push new packages and package versions |

In **Glob Patterns and Packages**, enter these exact package IDs on separate lines:

```text
AurorisNet.IdentityServer
AurorisNet.ActiveDirectory
```

Keep both entries in the same policy: NuGet [selects one matching policy](https://github.com/NuGet/NuGetGallery/issues/10947)
when issuing the temporary key, so separate policies with identical workflow criteria do
not combine scopes.

Enter only the workflow file name, without `.github/workflows/`. New-package scope is
needed for the initial publication. These settings follow the
[NuGet Trusted Publishing guide](https://learn.microsoft.com/en-us/nuget/nuget-org/trusted-publishing).

### Publishing and retrying

Run **Actions → Release → Run workflow** from `main` with the desired version. Both OS
test runs and package builds must succeed before release publication. The same `.nupkg`
and `.snupkg` files attached to the GitHub release are then pushed to NuGet.org, with the
Active Directory dependency pushed first. Other branches produce a GitHub release but
skip the NuGet publishing job.

If NuGet login or upload fails after the GitHub release succeeds, fix the policy or
configuration and use **Re-run failed jobs** on that workflow run. Do not start a new run
with the existing version, because version validation rejects existing tags. The push
steps skip duplicate versions so a partial upload can resume, and an adjacent `.snupkg`
is pushed automatically with each `.nupkg`. Packages are immutable on NuGet.org; any code
change requires a new version. Retry while the workflow's `nuget-packages` artifact is
still retained.
