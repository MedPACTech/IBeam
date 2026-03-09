<# 
Creates .azure-pipelines/pipelines.<ProjectName>.nupkg.yml for each .csproj in the repo.
- Uses the provided YAML template.
- Skips if the YAML already exists.
- Derives trigger path as: <project folder>/**
- Derives csproj glob as: **/<ProjectName>.csproj
- Always includes:
  - .azure-pipelines/templates/**
  - IBeam.sln
  - .azure-pipelines/pipelines.<ProjectName>.nupkg.yml
#>

$ErrorActionPreference = "Stop"

$repoRoot = Get-Location
$pipelineDir = Join-Path $repoRoot ".azure-pipelines"
if (!(Test-Path $pipelineDir)) { New-Item -ItemType Directory -Path $pipelineDir | Out-Null }

# Find all csproj files under repo (excluding bin/obj/.git)
$csprojs = Get-ChildItem -Path $repoRoot -Recurse -Filter "*.csproj" -File |
  Where-Object {
    $_.FullName -notmatch "\\bin\\" -and
    $_.FullName -notmatch "\\obj\\" -and
    $_.FullName -notmatch "\\\.git\\"
  }

function To-RepoRelativeUnixPath([string]$fullPath) {
  $root = (Resolve-Path $repoRoot).Path.TrimEnd('\','/')
  $p    = (Resolve-Path $fullPath).Path
  $rel  = $p.Substring($root.Length).TrimStart('\','/')
  return ($rel -replace "\\","/")
}

$template = @"
trigger:
  branches:
    include:
    - main
    - development
    - feature/*
    - bugfix/*
  paths:
    include:
    - {PROJECT_FOLDER}/**
    - .azure-pipelines/templates/**
    - IBeam.sln
    - .azure-pipelines/pipelines.{PROJECT_NAME}.nupkg.yml

pool:
  vmImage: 'ubuntu-22.04'

variables:
- group: GitHubNuGetSecrets

- name: buildPlatform
  value: 'AnyCPU'
- name: buildConfiguration
  value: 'Release'
- name: solution
  value: '**/IBeam.sln'
- name: project
  value: '**/{PROJECT_NAME}.csproj'
- name: packageLocation
  value: 'https://nuget.pkg.github.com/MedPACTech/index.json'
- name: apiKey
  value: '$(GH_TOKEN)'

stages:
- stage: BuildAndPush
  jobs:
  - job: BuildJob
    steps:
    - checkout: self
      clean: true
      fetchDepth: 0        # NBGV needs full history
      fetchTags: true      # needed for tag-based release versions
      lfs: false

    - template: templates/dotnet-nuget-build.yml
      parameters:
        solution: $(solution)
        project: $(project)
        packageLocation: $(packageLocation)
        apiKey: $(apiKey)
        buildConfiguration: $(buildConfiguration)
"@

$created = 0
$skipped = 0

foreach ($csproj in $csprojs) {
  $projectName = [System.IO.Path]::GetFileNameWithoutExtension($csproj.Name)

  # Folder relative to repo root for trigger path
  $projectFolderRel = To-RepoRelativeUnixPath $csproj.Directory.FullName

  $ymlName = "pipelines.$projectName.nupkg.yml"
  $ymlPath = Join-Path $pipelineDir $ymlName

  if (Test-Path $ymlPath) {
    Write-Host "Skipping existing: $ymlName"
    $skipped++
    continue
  }

  # Literal replacements (no regex escaping needed here)
  $content = $template.Replace("{PROJECT_NAME}", $projectName).Replace("{PROJECT_FOLDER}", $projectFolderRel)

  Set-Content -Path $ymlPath -Value $content -Encoding UTF8 -NoNewline
  Write-Host "Created: $ymlName"
  $created++
}

Write-Host ""
Write-Host "Done. Created: $created  Skipped: $skipped"
Write-Host "Output directory: $pipelineDir"
