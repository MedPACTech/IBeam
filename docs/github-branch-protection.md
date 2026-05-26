# GitHub Branch Protection

Last updated: 2026-03-24

## Ruleset
- Name: `main-protection`
- Target: branch `main`

## Recommended Settings
- Restrict deletions: enabled
- Block force pushes: enabled
- Require a pull request before merging: enabled
- Require approvals: enabled (minimum 1)
- Require conversation resolution before merging: enabled
- Require status checks to pass before merging: enabled
- Require branches to be up to date before merging: enabled

## Required Status Checks
- `build-test-pack`

This check is produced by:
- [ci.yml](c:/Projects/medpactech/IBeam/.github/workflows/ci.yml)

## NuGet Trusted Publishing Alignment
- Workflow file: `.github/workflows/publish-nuget-release.yml`
- Environment name: `production`

Create the environment in GitHub:
1. Go to `Settings` -> `Environments`.
2. Create `production`.
3. Add required reviewers (optional but recommended).
4. Add environment secrets if needed.

## Apply Steps
1. Go to `Settings` -> `Rules` -> `Rulesets`.
2. Click `New branch ruleset`.
3. Set name to `main-protection`.
4. Target `main`.
5. Enable settings listed above.
6. Add required status check `build-test-pack`.
7. Save and verify by opening a PR into `main`.
