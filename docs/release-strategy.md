# Release Strategy

Last updated: 2026-03-24

## Version and Tag Convention
- Stable release tag: `vMAJOR.MINOR.PATCH` (example: `v2.1.0`)
- Pre-release tag: `vMAJOR.MINOR.PATCH-preview.N` or `vMAJOR.MINOR.PATCH-rc.N`

## Feed Strategy
- Stable packages: NuGet.org
- Pre-release/dev packages: GitHub Packages

## Branch Strategy
- `development`: integration branch for active work
- `main`: stable release branch

## Release Flow
1. Merge approved changes into `development`.
2. Validate CI on `development`.
3. Open PR from `development` to `main`.
4. After merge, create release tag:
   - prerelease: `vX.Y.Z-preview.N` (publishes to GitHub Packages)
   - stable: `vX.Y.Z` (publishes to NuGet.org)
5. Publish GitHub Release notes using `docs/release-notes-template.md`.

## Required Secrets
- `NUGET_API_KEY` for NuGet.org stable publishing workflow.

## Notes
- Repository About/topics are documented in `docs/github-repo-profile.md`.
- Initial publish should use a small starter package set before full portfolio rollout.
