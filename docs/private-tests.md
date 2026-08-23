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
`Directory.Build.props`.

## Private Owner Build

Create a classic personal access token with `read:packages` and `repo` access,
then keep it only in the environment:

```bash
export GITHUB_PACKAGES_TOKEN="<token>"
export GITHUB_PACKAGES_USER="wongcyrus"
```

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

Private source remains hidden, but compiled .NET assemblies still contain type
and method metadata. Keep the grading Function, deployment storage, reports,
symbols, and package token inaccessible to students. Do not return stack traces
or private expected values through grading responses.
