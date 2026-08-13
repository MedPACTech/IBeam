# IBeam Release Strategy

Last updated: 2026-08-12

IBeam uses a release-train model for NuGet packages. All packages produced by a release train share one package version so consumers can keep related IBeam packages aligned without chasing independent package numbers.

## Branches and Package Channels

| Branch or ref | GitHub environment | Package channel | Feed | Version example |
| --- | --- | --- | --- | --- |
| `development` | `development` | Alpha prerelease | GitHub Packages | `2.10.0-alpha.123.1` |
| `test` | `test` | Beta prerelease | GitHub Packages | `2.10.0-beta.124.1` |
| `release` or `release/*` | `test` | Release candidate | NuGet.org prerelease | `2.10.0-rc.1` |
| `main` release tag | `production` | Stable | NuGet.org | `2.10.0` |

Pull requests and validation workflows build, test, and pack artifacts. Publishing workflows are reserved for branch pushes, release-candidate dispatches, and production releases.

## Versioning

Set `PRERELEASE_VERSION_PREFIX` to the next intended stable version, such as `2.10.0`. Development and test publishing append the channel and GitHub run identity:

```text
{PRERELEASE_VERSION_PREFIX}-alpha.{github.run_number}.{github.run_attempt}
{PRERELEASE_VERSION_PREFIX}-beta.{github.run_number}.{github.run_attempt}
```

NuGet sorts these prerelease channels in the expected release-train order:

```text
alpha < beta < rc < stable
```

Release candidates are manually supplied as exact package versions and must match:

```text
MAJOR.MINOR.PATCH-rc.NUMBER
```

Stable packages are published from release tags that must match:

```text
vMAJOR.MINOR.PATCH
```

## Workflows

- `.github/workflows/validate-development.yml`: validates pushes and pull requests targeting `development`.
- `.github/workflows/validate-test.yml`: validates pushes and pull requests targeting `test`.
- `.github/workflows/validate-release.yml`: validates `release` and `release/*` branches.
- `.github/workflows/validate-production.yml`: validates `main`.
- `.github/workflows/publish-development-packages.yml`: publishes `alpha` packages to GitHub Packages from `development`.
- `.github/workflows/publish-test-packages.yml`: publishes `beta` packages to GitHub Packages from `test`.
- `.github/workflows/publish-release-candidate.yml`: manually publishes `rc` packages to NuGet.org from `release` or `release/*`.
- `.github/workflows/publish-nuget-release.yml`: publishes stable packages to NuGet.org from a GitHub Release or manual stable tag dispatch.

Reusable templates keep the mechanics centralized:

- `.github/workflows/_dotnet-build.yml`: restore, build, test, pack, and upload artifacts.
- `.github/workflows/_github-packages-publish.yml`: restore, build, test, pack, upload artifacts, and publish to GitHub Packages.
- `.github/workflows/_nuget-publish.yml`: restore, build, pack, and publish to NuGet.org with trusted publishing.

## GitHub Settings

Configure these as repository or environment variables:

| Name | Required | Suggested scope | Notes |
| --- | --- | --- | --- |
| `PRERELEASE_VERSION_PREFIX` | Yes for alpha/beta publishing | `development` and `test` environments, or repository | Keep both environments on the same next stable version when the release train is shared. |
| `DOTNET_VERSION` | No | Repository | Defaults to `10.0.x`. |
| `BUILD_CONFIGURATION` | No | Repository | Defaults to `Release`. |
| `GITHUB_PACKAGES_NUGET_SOURCE` | No | Repository | Defaults to `https://nuget.pkg.github.com/{repository_owner}/index.json`. |
| `NUGET_SOURCE` | No | Repository | Defaults to `https://api.nuget.org/v3/index.json`. |

Configure this secret for NuGet.org publishing:

| Name | Required | Suggested scope | Notes |
| --- | --- | --- | --- |
| `NUGET_USER` | Yes for RC and stable publishing | `test` and `production` environments, or repository | Used by `NuGet/login@v1` for trusted publishing. |

GitHub Packages publishing uses the built-in `GITHUB_TOKEN` with `packages: write`; no separate package token is required for normal repository-owned packages.

## Release Flow

1. Merge approved work into `development`.
2. Let development validation and development package publishing run.
3. Promote to `test` when the train is ready for wider prerelease testing.
4. Create or update the `release` branch, or a `release/*` branch, for release-candidate hardening.
5. Run `Publish Release Candidate Packages` with a version like `2.10.0-rc.1`.
6. Merge the release branch to `main` after RC validation.
7. Create a GitHub Release with a stable tag like `v2.10.0`; this publishes stable NuGet.org packages.

## Consumer Guidance

Production consumers should use stable packages from NuGet.org and avoid floating prerelease versions.

Consumers who want development or beta builds must explicitly add the GitHub Packages source for this repository owner. RC packages are NuGet.org prerelease packages, so consumers can opt in using normal prerelease package resolution.

Keep related IBeam packages on the same release-train version unless a consuming application intentionally isolates a package and validates that dependency graph on its own.
