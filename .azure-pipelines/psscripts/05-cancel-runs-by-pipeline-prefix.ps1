<#
Cancel queued/in-progress runs for pipelines whose name starts with a prefix (default: "IBeam")
#>

$ErrorActionPreference = "Stop"

param(
  [string]$Prefix = "IBeam"
)

$runs = az pipelines runs list `
  --query "[?starts_with(definition.name, '$Prefix') && (status=='notStarted' || status=='inProgress')].[id,definition.name,status]" `
  -o tsv

if (-not $runs) {
  Write-Host "No queued or in-progress runs found for prefix '$Prefix'."
  return
}

foreach ($line in $runs) {
  $parts = $line -split "`t"
  $runId = $parts[0]
  $pipe  = $parts[1]
  $stat  = $parts[2]

  Write-Host "Canceling run $runId ($pipe) [$stat]"
  az pipelines build cancel --build-id $runId | Out-Null
}
