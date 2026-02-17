<#
Run all pipelines whose name ends with ".Nupkg"
#>

$ErrorActionPreference = "Stop"

$branch = "main"

$ids = az pipelines list --query "[?ends_with(name, '.Nupkg')].id" -o tsv

if (-not $ids) {
  Write-Host "No pipelines found ending with '.Nupkg'."
  return
}

foreach ($id in $ids) {
  Write-Host "Queueing NuGet pipeline id=$id"
  az pipelines run --id $id --branch $branch
}
