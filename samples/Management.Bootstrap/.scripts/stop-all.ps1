param([string]$HostRoot, [string]$Instance='management')
. "$PSScriptRoot/common.ps1"
if ($Instance -notmatch '^[a-zA-Z0-9-]{1,40}$') { throw 'Invalid local instance name.' }
$LocalRoot=Join-Path $RepoRoot ('.local/'+$Instance)
$StatePath=Join-Path $LocalRoot 'processes.json'
if (-not (Test-Path $StatePath)) { Write-Host 'No recorded Management API stack.'; return }
$state=Get-Content $StatePath -Raw | ConvertFrom-Json
foreach($target in $state.CustomerTargets){Stop-RecordedProcess $target}
Stop-RecordedProcess $state.Api
Stop-RecordedProcess $state.Table
Remove-Item -LiteralPath $StatePath
Write-Host 'Management API and its private emulator stopped. Local data and secrets were retained.'
