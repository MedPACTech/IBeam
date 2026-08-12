param(
    [string] $ConfigPath = ".\scripts\identity\identity.seed.sample.json",
    [string] $Environment = $(if ($env:ASPNETCORE_ENVIRONMENT) { $env:ASPNETCORE_ENVIRONMENT } else { "Development" }),
    [ValidateSet("AzureTable")]
    [string] $Provider = "AzureTable",
    [string] $ReportPath,
    [switch] $Apply,
    [switch] $DryRun,
    [switch] $SkipSchema,
    [switch] $VerboseLogs,
    [switch] $Help
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..")
$projectPath = Join-Path $repoRoot "tools\IBeam.Identity.Seeder\IBeam.Identity.Seeder.csproj"

if ($Help) {
    @"
Seed IBeam Identity for local/demo runs.

Usage:
  .\scripts\identity\seed-identity.ps1 -ConfigPath .\scripts\identity\identity.seed.local.json
  .\scripts\identity\seed-identity.ps1 -ConfigPath .\scripts\identity\identity.seed.local.json -Apply

Notes:
  - Dry-run is the default.
  - Pass -Apply to write changes.
  - Passwords should usually come from passwordEnv entries in the seed JSON.
"@ | Write-Output
    exit 0
}

if ($Provider -ne "AzureTable") {
    throw "Only AzureTable provider is currently supported."
}

if ($Apply -and $DryRun) {
    throw "Use either -Apply or -DryRun, not both."
}

$arguments = @(
    "run",
    "--project",
    $projectPath,
    "--",
    "--config",
    $ConfigPath,
    "--content-root",
    $repoRoot,
    "--environment",
    $Environment
)

if ($Apply) {
    $arguments += "--apply"
}
else {
    $arguments += "--dry-run"
}

if ($SkipSchema) {
    $arguments += "--skip-schema"
}

if ($VerboseLogs) {
    $arguments += "--verbose"
}

if (![string]::IsNullOrWhiteSpace($ReportPath)) {
    $arguments += @("--report", $ReportPath)
}

Write-Host "Running IBeam Identity seeder ($Provider, $Environment)..." -ForegroundColor Cyan
if (!$Apply) {
    Write-Host "Dry-run mode: no changes will be written. Pass -Apply to populate." -ForegroundColor Yellow
}

dotnet @arguments
