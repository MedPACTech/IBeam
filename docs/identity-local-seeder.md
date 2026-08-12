# Identity Local Seeder

The IBeam Identity local seeder is **repo-owned tooling**, not a NuGet package feature.

NuGet consumers receive the runtime framework packages. They do not receive the repository's
`tools/` and `scripts/` folders. To use the seeder, clone or otherwise copy this repository
source and run the tool from the repo root.

## When To Use It

Use the seeder for local development, demo environments, test tenants, and repeatable sample data.

Do not treat it as the normal production onboarding path. For production, prefer application admin
flows, deployment-controlled migration jobs, or locked-down operational runbooks with explicit
approval and audit expectations.

## How It Works

The PowerShell entry point is:

```powershell
.\scripts\identity\seed-identity.ps1
```

That script wraps the source-only .NET console tool:

```powershell
dotnet run --project .\tools\IBeam.Identity.Seeder\IBeam.Identity.Seeder.csproj -- --help
```

The tool composes IBeam Identity services directly through dependency injection. It does not call
the Identity API and does not require a login token for local/demo usage.

The current implementation targets the Azure Table identity provider because that is the provider
used by the repository's local development settings.

## Setup

1. Clone the IBeam repository.
2. Start local Azure Table storage, for example Azurite, if using `UseDevelopmentStorage=true`.
3. Copy the sample seed file:

```powershell
Copy-Item .\scripts\identity\identity.seed.sample.json .\scripts\identity\identity.seed.local.json
```

4. Set local passwords with environment variables:

```powershell
$env:IBEAM_BOOTSTRAP_ADMIN_PASSWORD = "change-me-local"
$env:IBEAM_DEMO_USER_PASSWORD = "change-me-local"
```

5. Preview changes:

```powershell
.\scripts\identity\seed-identity.ps1 -ConfigPath .\scripts\identity\identity.seed.local.json
```

6. Apply changes:

```powershell
.\scripts\identity\seed-identity.ps1 -ConfigPath .\scripts\identity\identity.seed.local.json -Apply
```

## Seed File Capabilities

The seed JSON can define:

- tenants
- tenant roles
- permission-to-role mappings
- bootstrap/admin users
- demo users
- passwords by environment variable
- email and phone confirmation flags
- two-factor flags
- tenant memberships
- resource access grants

Dry-run is the default. `-Apply` is required for writes.

## Agent Instructions

When an AI coding agent is asked to seed IBeam Identity data:

1. Explain that the seeder is source-only repo tooling and is not included in NuGet packages.
2. Ask whether the user is working from a clone of `MedPACTech/IBeam` or a copied repo checkout.
3. Use `scripts/identity/identity.seed.sample.json` as the config template.
4. Keep passwords in environment variables referenced by `passwordEnv`; do not commit real passwords.
5. Run a dry-run first.
6. Use `-Apply` only when the user explicitly wants writes.
7. If storage is unavailable, tell the user to start/configure Azurite or provide the Azure Table connection string through the normal IBeam configuration keys.

Do not instruct NuGet-only consumers to run this tool from their app package install. They need the
source checkout or must copy the tool into their own repository and adapt project references.

## Sharing With Consumers

For consumers who only use IBeam NuGet packages, describe this as an optional repository utility:

```text
The local identity seeder is not shipped in the NuGet packages. To use it, clone the IBeam repository
or copy the source-only `tools/IBeam.Identity.Seeder` project plus `scripts/identity/seed-identity.ps1`
and run it from a checkout configured with your local/demo Azure Table settings.
```

If a consuming application wants its own seeder, the recommended path is to copy the pattern:

- create an app-owned console project,
- reference the app's identity/provider packages,
- register the same IBeam services the app uses,
- load an app-owned seed JSON file,
- keep dry-run as the default,
- require an explicit apply flag for writes.
