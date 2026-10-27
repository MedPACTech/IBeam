param([string]$HostRoot, [string]$PackageFeed, [string]$PackageVersion, [int]$ApiPort=5151, [int]$TablePort=15112, [string]$Instance='management-demo')
$ErrorActionPreference='Stop'
& "$PSScriptRoot/start-all.ps1" -HostRoot $HostRoot -PackageFeed $PackageFeed -PackageVersion $PackageVersion -ApiPort $ApiPort -TablePort $TablePort -Instance $Instance -Demo
& "$PSScriptRoot/seed-demo.ps1" -HostRoot $HostRoot -Instance $Instance
Write-Host "Baseline app API: http://127.0.0.1:$ApiPort (synthetic-demo)."
