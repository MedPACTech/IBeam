<#
Cancel all queued/in-progress runs in the current Azure DevOps project
#>

$ErrorActionPreference = "Stop"

$runs = az pipelines runs list `
  --query "[?(status=='notStarted' || status=='inProgress')].[id,definition.name,status]" `
  -o tsv

if (-not $runs) {
  Write-Host "No queued or in-progress runs found."
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
