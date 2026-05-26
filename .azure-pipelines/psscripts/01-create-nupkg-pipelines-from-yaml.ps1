<#
Create Azure DevOps pipelines for each YAML matching:
  .azure-pipelines/pipelines.*.nupkg.yml
#>

$ErrorActionPreference = "Stop"

$repoUrl = "https://github.com/MedPACTech/IBeam"
$branch  = "main"
$sc      = "f6aea96b-6653-4f5f-b821-80b5bc3497ba"

# Always resolve relative to THIS script's location
$pipeDir = Resolve-Path (Join-Path $PSScriptRoot "..")

Get-ChildItem -Path $pipeDir -Filter "pipelines.*.nupkg.yml" -File | ForEach-Object {
  $yamlPath = "/.azure-pipelines/$($_.Name)"
  }

Write-Host "Working dir:      $PWD"
Write-Host "Script root:      $PSScriptRoot"
Write-Host "Pipelines folder: $pipeDir"

if (-not (Test-Path $pipeDir)) {
  throw "Folder not found: $pipeDir"
}

$files = Get-ChildItem -Path $pipeDir -Filter "pipelines.*.nupkg.yml" -File
Write-Host "Matched YAML files: $($files.Count)"

if ($files.Count -eq 0) {
  Write-Warning "No files matched 'pipelines.*.nupkg.yml' in $pipeDir"
  return
}

foreach ($f in $files) {
  $yamlPath = "/.azure-pipelines/$($f.Name)"

  $baseName = $f.Name -replace '^pipelines\.', '' -replace '\.nupkg\.yml$', ''
  $pipeName = "$baseName.Nupkg"

  # Don't fully swallow errors; capture output and check exit code
  $existingId = az pipelines list --query "[?name=='$pipeName'].id | [0]" -o tsv 2>&1
  if ($LASTEXITCODE -ne 0) {
    throw "az pipelines list failed for '$pipeName': $existingId"
  }

  if ($existingId) {
    Write-Host "Exists, skipping: $pipeName (id=$existingId)"
    continue
  }

  Write-Host "Creating pipeline: $pipeName -> $yamlPath"

  $createOut = az pipelines create `
    --name "$pipeName" `
    --repository "$repoUrl" `
    --repository-type github `
    --branch "$branch" `
    --yaml-path "$yamlPath" `
    --service-connection "$sc" `
    --skip-first-run true 2>&1

  if ($LASTEXITCODE -ne 0) {
    throw "az pipelines create failed for '$pipeName': $createOut"
  }

  $createOut | Out-Host
}