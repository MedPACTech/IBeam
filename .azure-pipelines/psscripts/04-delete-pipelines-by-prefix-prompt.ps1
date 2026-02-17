<#
Prompt delete pipelines starting with a prefix (default: "IBeam")
#>

$ErrorActionPreference = "Stop"

param(
  [string]$Prefix = "IBeam"
)

$items = az pipelines list --query "[?starts_with(name, '$Prefix')].[name,id]" -o tsv

if (-not $items) {
  Write-Host "No pipelines found starting with '$Prefix'."
  return
}

foreach ($line in $items) {
  $parts = $line -split "`t"
  $name  = $parts[0]
  $id    = $parts[1]

  $ans = Read-Host "Delete '$name' (id=$id)? y/N"
  if ($ans -eq "y") {
    az pipelines delete --id $id --yes
  }
}
