<#
Delete all pipelines starting with a prefix (default: "IBeam")
#>

$ErrorActionPreference = "Stop"

param(
  [string]$Prefix = "IBeam"
)

$toDelete = az pipelines list --query "[?starts_with(name, '$Prefix')].id" -o tsv

if (-not $toDelete) {
  Write-Host "No pipelines found starting with '$Prefix'."
  return
}

foreach ($id in $toDelete) {
  Write-Host "Deleting pipeline id=$id"
  az pipelines delete --id $id --yes
}
