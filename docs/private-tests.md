# Public and Private Grading Tests

## Repository Layout

- [`wongcyrus/AzureProjectTestLib`](https://github.com/wongcyrus/AzureProjectTestLib)
  contains the current public test source and is mounted at
  `AzureProjectTestLib` as a Git submodule.
- `wongcyrus/AzureProjectTestLib.Private` is private and publishes the
  `WongCyrus.AzureProjectTestLib.Private` GitHub NuGet package.

The private package is a drop-in replacement: its package ID is private, but it
contains an assembly named `AzureProjectTestLib.dll` with the same namespaces
and public API. Public and private suites are mutually exclusive in one build.

`Directory.Build.props` controls private-mode selection:

- `UsePrivateTests`: `false` by default; the wrapper sets it to `true`.
- `PrivateTestPackageId`: the replacement package ID.
- `PrivateTestPackageVersion`: the exact package version to restore.

The current paired baseline is private package `1.0.6`: public and private
suites expose the same 33 game tasks and 40 Azure assertions. Keep those task
identifiers, instructions, and assertion counts synchronized unless a hidden
private-only difference is intentional.

## Public Deployment

Clone all public source recursively:

```bash
git clone --recurse-submodules \
  https://github.com/wongcyrus/AzureAutomaticGradingEngine_Assignments
```

Normal restore, test, and deployment commands use the public submodule and need
no package credentials:

```bash
dotnet test AzureProjectGrader.sln --configuration Release
cd Infrastructure
npx cdktn deploy --auto-approve
```

## Publish Private Tests

Make future hidden test changes only in the private repository. Keep its
assembly name, namespaces, task identifiers, and helper API compatible with the
public suite. Increment `<Version>` in `AzureProjectTestLib.csproj`, commit the
change, and push a matching tag:

```bash
git tag -a v1.1.0 -m "Private grading package 1.1.0"
git push origin main v1.1.0
```

The private repository workflow tests and publishes the package using its
repository-scoped `GITHUB_TOKEN`. Do not commit package tokens, NuGet
configuration containing credentials, symbols, or package artifacts.

After publishing a new version, update `PrivateTestPackageVersion` in
`Directory.Build.props`, deploy both the Function package and hosted runner,
verify the active Function `.deps.json` references the new version, and
regenerate pre-generated task messages.

## Configure Your Own Private Suite

Fork maintainers can create an equivalent private suite under their own GitHub
account or organization:

1. Create a private repository containing a compatible
   `AzureProjectTestLib.csproj`.
2. Keep `<AssemblyName>AzureProjectTestLib</AssemblyName>` so the package
   remains a drop-in replacement.
3. Assign a unique `<PackageId>` and package version.
4. Add a GitHub Actions workflow that runs tests, packs the project, and
   publishes to GitHub Packages using `GITHUB_TOKEN` with
   `packages: write`.
5. Set `PrivateTestPackageId` and `PrivateTestPackageVersion` in this
   repository's `Directory.Build.props`.

Do not reuse `WongCyrus.AzureProjectTestLib.Private` unless your GitHub account
has permission to download it.

## Private Maintainer Build

Create a classic personal access token with `read:packages` and `repo` access,
then keep it only in the environment:

```bash
export GITHUB_PACKAGES_TOKEN="<token>"
export GITHUB_PACKAGES_USER="<your-github-username>"
export GITHUB_PACKAGES_OWNER="<package-owner-or-organization>"
```

`GITHUB_PACKAGES_USER` is the account authenticating the restore.
`GITHUB_PACKAGES_OWNER` identifies the account or organization hosting the
package feed; these values can differ for organization-owned packages. The
token must be authorized for the organization when SAML SSO is enforced.

Use the wrapper to create a temporary authenticated NuGet configuration:

```bash
scripts/with-private-tests.sh \
  dotnet test AzureProjectGrader.sln --configuration Release

scripts/with-private-tests.sh \
  bash -lc 'cd Infrastructure && npx cdktn deploy --auto-approve'
```

The wrapper deletes its temporary credential file when the command exits.
Every nested `dotnet` build inherits `UsePrivateTests=true` and the temporary
restore configuration, including Function and hosted test-runner publication.

## Security Boundary

`TestSuiteIdentity.Name` reports `Public` or `Private` in owner-only Function
and hosted-runner logs so deployment mode can be verified without exposing test
source.

When `WEBSITE_RUN_FROM_PACKAGE=1`, Kudu's `site/wwwroot` can retain files from
an older extraction. Verify the active zip named by
`data/SitePackages/packagename.txt` instead of treating `wwwroot` hashes as the
running Function package.

Private source remains hidden, but compiled .NET assemblies still contain type
and method metadata. Keep the grading Function, deployment storage, reports,
symbols, and package token inaccessible to students. Do not return stack traces
or private expected values through grading responses.
