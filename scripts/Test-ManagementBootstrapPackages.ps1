param([Parameter(Mandatory=$true)][string]$PackageFeed,[string]$PackageVersion,[string]$OutputDirectory)
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot -Parent
$feed=[IO.Path]::GetFullPath($PackageFeed)
if(!(Test-Path -LiteralPath $feed -PathType Container)){throw 'Generated package feed is missing.'}
Add-Type -AssemblyName System.IO.Compression.FileSystem
if(!$PackageVersion){
 $candidates=@(Get-ChildItem -LiteralPath $feed -File -Filter 'IBeam.Management.Api.*.nupkg')
 if($candidates.Count -ne 1){throw 'Select an exact PackageVersion when the feed has multiple management API versions.'}
 $zip=[IO.Compression.ZipFile]::OpenRead($candidates[0].FullName)
 try {
  $entry=@($zip.Entries|Where-Object FullName -like '*.nuspec')[0]
  $reader=[IO.StreamReader]::new($entry.Open());try{[xml]$spec=$reader.ReadToEnd()}finally{$reader.Dispose()}
  $PackageVersion=[string]$spec.package.metadata.version
 }finally{$zip.Dispose()}
}
if($PackageVersion -notmatch '^[0-9]+\.[0-9]+\.[0-9]+(?:-[A-Za-z0-9][A-Za-z0-9.-]*)?(?:\+[A-Za-z0-9.-]+)?$' -or
 !(Test-Path -LiteralPath (Join-Path $feed "IBeam.Management.Api.$PackageVersion.nupkg"))){throw 'Exact generated package version is unavailable.'}
if(!$OutputDirectory){$OutputDirectory=Join-Path $repo ('_tmp_pkg_smoke/bootstrap-'+[Guid]::NewGuid().ToString('N'))}
$output=[IO.Path]::GetFullPath($OutputDirectory)
$consumer=Join-Path $output 'consumer';New-Item -ItemType Directory -Force $consumer|Out-Null
$source=Join-Path $repo 'samples/Management.Bootstrap/src'
foreach($file in Get-ChildItem -LiteralPath $source -Recurse -File){
 $relative=[IO.Path]::GetRelativePath($source,$file.FullName)
 if($relative -match '(^|[\\/])(bin|obj|\.local)([\\/]|$)' -or $file.Extension -notin @('.cs','.csproj','.json')){continue}
 if($file.Name -match '(?i)secrets|\.local\.json$'){throw 'A private configuration cannot be copied into the package consumer.'}
 $destination=Join-Path $consumer $relative;New-Item -ItemType Directory -Force (Split-Path $destination -Parent)|Out-Null
 Copy-Item -LiteralPath $file.FullName -Destination $destination
}
'<Project />'|Set-Content (Join-Path $consumer 'Directory.Build.props')
'<Project />'|Set-Content (Join-Path $consumer 'Directory.Build.targets')
$config=Join-Path $output 'NuGet.Config';$escaped=[Security.SecurityElement]::Escape($feed)
"<configuration><packageSources><clear/><add key='generated' value='$escaped'/><add key='nuget.org' value='https://api.nuget.org/v3/index.json'/></packageSources></configuration>"|Set-Content $config
$project=Join-Path $consumer 'IBeam.Management.API/IBeam.Management.API.csproj'
[xml]$xml=Get-Content -LiteralPath $project -Raw
if($xml.SelectNodes('//ProjectReference').Count){throw 'Bootstrap must consume NuGet packages only.'}
& dotnet restore $project --configfile $config --packages (Join-Path $output 'cache') "-p:IBeamVersion=$PackageVersion" --nologo -v quiet
if($LASTEXITCODE -ne 0){throw 'Bootstrap package-only restore failed.'}
& dotnet publish $project --no-restore -c Release -o (Join-Path $output 'published') "-p:IBeamVersion=$PackageVersion" --nologo -v quiet
if($LASTEXITCODE -ne 0){throw 'Bootstrap package-only publish failed.'}
$assets=Get-Content (Join-Path (Split-Path $project) 'obj/project.assets.json') -Raw|ConvertFrom-Json
$framework=@($assets.libraries.PSObject.Properties|Where-Object Name -like 'IBeam.*/*')
if(!$framework.Count -or @($framework|Where-Object {$_.Value.type -ne 'package' -or $_.Name -notlike ('*/'+$PackageVersion)}).Count){throw 'Bootstrap contains an unexpected framework source or package version.'}
@{passed=$true;packageVersion=$PackageVersion;frameworkPackages=$framework.Count;sourceProjectReferences=0;privateRuntimeCopied=$false}|ConvertTo-Json|Set-Content (Join-Path $output 'bootstrap-validation-summary.json')
Write-Output 'PASS: portable Management bootstrap restored and published using generated NuGets only; private runtime state excluded.'
