# Open Source Release Checklist

Last updated: 2026-03-24

## Core Release Readiness
- [x] 1. Produce a full production (`Release`) build for all solution projects.
- [x] 2. Run full test suite in `Release` mode and capture baseline results.
- [x] 3. Standardize package metadata across all publishable projects (`PackageId`, `Description`, `PackageTags`, license, repository URL, README).
- [x] 4. Validate package dependency graph and remove/upgrade vulnerable dependencies where possible.
- [x] 5. Confirm license artifacts and OSS docs are complete (`LICENSE`, `NOTICE`, `CONTRIBUTING`, `docs/licensing.md`).

## GitHub Repository Setup
- [ ] 6. Add repository About summary, website link, and topic tags. (Prepared in `docs/github-repo-profile.md`; pending manual apply)
- [x] 7. Create release strategy (stable tags + prerelease tags).
- [x] 8. Add/verify CI workflows for build + test + pack.
- [x] 9. Add publish workflows for NuGet stable and GitHub Packages prerelease/dev feeds.
- [x] 10. Add README badges (build, NuGet version/downloads, license, latest release).

## NuGet Publishing Readiness
- [x] 11. Verify every NuGet package has narrative README and package icon.
- [x] 12. Validate package quality with local `dotnet pack -c Release` and install smoke tests.
- [x] 13. Prepare initial release notes and package changelog entries.
- [ ] 14. Publish initial set to NuGet.org. (Attempted; blocked pending `NUGET_API_KEY`)

## Post-Release Operations
- [ ] 15. Create issue templates and discussion guidance for community support.
- [ ] 16. Define versioning policy and support window.
- [ ] 17. Draft landing page content and deployment plan.

## Baseline Results
- 2026-03-24: `dotnet test IBeam.sln -c Release` completed successfully.
- Test outcome: all discovered test projects passed (no test failures).
- Build/test warnings observed:
  - `MSB9008`: missing `IBeam.Models` project reference under `IBeam.Services`.
  - no remaining package vulnerability warnings after dependency updates.

- 2026-03-24: package metadata baseline standardized for packable projects via `Directory.Build.props`:
  - default authors/company/description/tags
  - Apache-2.0 license expression
  - repository/project URL metadata
  - explicit README packaging added for `IBeam.Services.Logging`

- 2026-03-24: dependency/vulnerability remediation pass completed:
  - upgraded `AutoMapper` to `16.1.1` in `IBeam.Services` and `IBeam.Services.AutoMapper`
  - upgraded `AWSSDK.S3` to `4.0.19.1` in `IBeam.Storage.S3`
  - re-ran `dotnet list IBeam.sln package --vulnerable --include-transitive` with zero vulnerable packages reported

- 2026-03-24: OSS/legal document set confirmed and expanded:
  - `LICENSE` (Apache-2.0), `NOTICE`, `CONTRIBUTING.md`, and `docs/licensing.md` verified
  - added `CODE_OF_CONDUCT.md` and `SECURITY.md`
  - linked community/security docs from root `README.md`

- 2026-03-24: release operations scaffolding added:
  - release strategy: `docs/release-strategy.md`
  - release notes template: `docs/release-notes-template.md`
  - changelog release entry added for `2.0.10`
  - CI workflow: `.github/workflows/ci.yml`
  - prerelease publish workflow (GitHub Packages): `.github/workflows/publish-prerelease-gpr.yml`
  - stable publish workflow (NuGet.org): `.github/workflows/publish-nuget-release.yml`
  - README badges for CI/NuGet/license/release

- 2026-03-24: package readiness checks completed:
  - `dotnet pack IBeam.sln -c Release -o artifacts/packages` succeeded
  - local smoke consumer project (`_tmp_pkg_smoke`) restored and built against packed local artifacts
  - standardized package icon and readme metadata in `Directory.Build.props`

- 2026-03-24: NuGet publish attempt status:
  - attempted push to NuGet.org
  - blocked with `401` due missing API key header
