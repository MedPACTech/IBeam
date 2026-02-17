<# 
Create Azure DevOps pipelines for each YAML matching:
  .azure-pipelines/pipelines.*.nupkg.yml

Pipeline name output:
  pipelines.XYZ.nupkg.yml -> XYZ.Nupkg

Behavior:
- If pipeline exists: skip (no update)
- If not: create
#>

$ErrorActionPreference = "Stop"

$repoUrl = "https://github.com/MedPACTech/IBeam"
$branch  = "main"
$sc      = "f6aea96b-6653-4f5f-b821-80b5bc3497ba"

Get-ChildItem ".\.azure-pipelines" -Filter "pipelines.*.nupkg.yml" | ForEach-Object {
  $yamlPath = "/.azure-pipelines/$($_.Name)"

  $baseName = $_.Name -replace '^pipelines\.', '' -replace '\.nupkg\.yml$', ''
  $pipeName = "$baseName.Nupkg"

  $existingId = az pipelines list --query "[?name=='$pipeName'].id | [0]" -o tsv 2>$null

  if ($existingId) {
    Write-Host "Exists, skipping: $pipeName (id=$existingId)"
    return
  }

  Write-Host "Creating pipeline: $pipeName -> $yamlPath"

  az pipelines create `
    --name "$pipeName" `
    --repository "$repoUrl" `
    --repository-type github `
    --branch "$branch" `
    --yaml-path "$yamlPath" `
    --service-connection "$sc" `
    --skip-first-run true 2>&1 | Out-Host
}


<#$matches = az pipelines list --query "[?name=='$pipeName'].id" -o tsv
if ($matches) {
  $count = @($matches -split "`n" | Where-Object { $_ -ne "" }).Count
  if ($count -gt 1) { Write-Warning "Multiple pipelines named '$pipeName' found! Skipping."; return }
  Write-Host "Exists, skipping: $pipeName (id=$matches)"
  return
}#>