<# 
Create Azure DevOps pipelines for each YAML matching:
  .azure-pipelines/pipelines.*.nupkg.yml

Pipeline name output:
  pipelines.XYZ.nupkg.yml -> XYZ.Nupkg
#>

$ErrorActionPreference = "Stop"

$repoUrl = "https://github.com/MedPACTech/IBeam"
$branch  = "main"
$sc      = "f6aea96b-6653-4f5f-b821-80b5bc3497ba"

# Optional: ensure you're logged in + correct org/project already set
# az devops configure --defaults organization=https://dev.azure.com/<ORG> project=<PROJECT>

Get-ChildItem ".\.azure-pipelines" -Filter "pipelines.*.nupkg.yml" | ForEach-Object {
  $yamlPath = "/.azure-pipelines/$($_.Name)"

  # pipelines.XYZ.nupkg.yml -> XYZ.Nupkg
  $baseName = $_.Name -replace '^pipelines\.', '' -replace '\.nupkg\.yml$', ''
  $pipeName = "$baseName.Nupkg"

  Write-Host "Creating pipeline: $pipeName  ($yamlPath)"

  az pipelines create `
    --name "$pipeName" `
    --repository "$repoUrl" `
    --repository-type github `
    --branch "$branch" `
    --yaml-path "$yamlPath" `
    --service-connection "$sc" `
    --skip-first-run true
}
