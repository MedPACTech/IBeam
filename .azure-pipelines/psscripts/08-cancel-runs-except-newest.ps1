<#
Cancel duplicate active runs per pipeline.
Keeps the newest active run per pipeline and cancels older ones.

Usage:
  .\Cancel-DuplicateRuns.ps1 -WhatIf
  .\Cancel-DuplicateRuns.ps1
#>

param(
  [switch]$WhatIf
)

$ErrorActionPreference = "Stop"

$runsJson = az pipelines runs list `
  --query "[?(status=='notStarted' || status=='inProgress')].{id:id,pipelineId:definition.id,pipelineName:definition.name,status:status,queueTime:queueTime}" `
  -o json

$runs = $runsJson | ConvertFrom-Json

if (-not $runs -or $runs.Count -eq 0) {
  Write-Host "No queued or in-progress runs found."
  return
}

$groups = $runs | Group-Object pipelineId

foreach ($g in $groups) {
  if ($g.Count -le 1) { continue }

  $sorted = $g.Group | Sort-Object queueTime -Descending
  $keep = $sorted[0]
  $toCancel = $sorted | Select-Object -Skip 1

  Write-Host "Pipeline '$($keep.pipelineName)' has $($g.Count) active runs."
  Write-Host "Keeping run $($keep.id) (newest queued/running)."

  foreach ($run in $toCancel) {
    if ($WhatIf) {
      Write-Host "[WhatIf] Would cancel run $($run.id) [$($run.status)]"
    } else {
      Write-Host "Canceling run $($run.id) [$($run.status)]"
      az pipelines build cancel --build-id $run.id | Out-Null
    }
  }
}
