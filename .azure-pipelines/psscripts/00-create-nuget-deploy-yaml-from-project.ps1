<# 
Creates .azure-pipelines/pipelines.<ProjectName>.nupkg.yml for each .csproj in the repo.
- Uses your provided YAML template.
- Skips if the YAML already exists.
- Derives trigger path as: <project folder>/**
- Derives csproj glob as: **/<ProjectName>.csproj
- Always includes:
  - .azure-pipelines/templates/**
  - IBeam.sln
  - .azure-pipelines/pipelines.<ProjectName>.nupkg.yml
#>

$ErrorActionPreference = "Stop"

$scriptDir    = $PSScriptRoot
$repoRoot     = (Resolve-Path (Join-Path $scriptDir "..\..")).Path
$pipelineDir  = Join-Path $repoRoot ".azure-pipelines"
$templatePath = Join-Path $pipelineDir "templates\nupkg.template.yml"

Write-Host "scriptDir:    $scriptDir"
Write-Host "repoRoot:     $repoRoot"
Write-Host "pipelineDir:  $pipelineDir"
Write-Host "templatePath: $templatePath"

if (!(Test-Path $pipelineDir)) { New-Item -ItemType Directory -Path $pipelineDir | Out-Null }
if (!(Test-Path $templatePath)) { throw "Template file not found: $templatePath" }

$template = Get-Content -Path $templatePath -Raw -Encoding UTF8


# Read template as raw text (no PowerShell $() expansion issues)
$template = Get-Content -Path $templatePath -Raw -Encoding UTF8

# Find all .csproj files (excluding bin/obj)
$csprojs = Get-ChildItem -Path $repoRoot -Recurse -Filter "*.csproj" -File |
  Where-Object {
    $_.FullName -notmatch "\\bin\\" -and
    $_.FullName -notmatch "\\obj\\" -and
    $_.FullName -notmatch "\\\.git\\"
  }

function Get-RepoRelativeUnixPath([string]$fullPath) {
  $root = $repoRoot.TrimEnd('\','/')
  $p = (Resolve-Path $fullPath).Path
  if ($p.StartsWith($root)) { $p = $p.Substring($root.Length) }
  $p = $p.TrimStart('\','/')
  return ($p -replace "\\","/")
}

$created = 0
$skipped = 0

foreach ($csproj in $csprojs) {
  $projectName = [System.IO.Path]::GetFileNameWithoutExtension($csproj.Name)
  $projectFolder = Get-RepoRelativeUnixPath $csproj.Directory.FullName

  $ymlName = "pipelines.$projectName.nupkg.yml"
  $ymlPath = Join-Path $pipelineDir $ymlName

  if (Test-Path $ymlPath) {
    Write-Host "Skipping existing: $ymlName"
    $skipped++
    continue
  }

  $content = $template.Replace("{PROJECT_NAME}", $projectName).Replace("{PROJECT_FOLDER}", $projectFolder)

  Set-Content -Path $ymlPath -Value $content -Encoding UTF8 -NoNewline
  Write-Host "Created: $ymlName"
  $created++
}

Write-Host ""
Write-Host "Done. Created: $created  Skipped: $skipped"
Write-Host "Template: $templatePath"
Write-Host "Output:   $pipelineDir"
