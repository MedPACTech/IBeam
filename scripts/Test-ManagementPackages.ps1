param([string]$OutputDirectory,[string]$PackageVersion,[switch]$ReusePackages)
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot -Parent
if (-not $OutputDirectory) {$OutputDirectory=Join-Path $repo ('_tmp_pkg_smoke/management-'+[Guid]::NewGuid().ToString('N'))}
if (-not $PackageVersion) {$PackageVersion='0.0.0-management.smoke.'+[Guid]::NewGuid().ToString('N')}
$feed=Join-Path $OutputDirectory 'feed'; $hostFolder=Join-Path $OutputDirectory 'consumer'
New-Item -ItemType Directory -Path $feed,$hostFolder -Force|Out-Null
$ordered=[Collections.Generic.List[string]]::new(); $visited=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
function Visit([string]$project) {
 $project=[IO.Path]::GetFullPath($project)
 if (-not $project.StartsWith($repo+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) {throw 'Package dependency escapes the repository'}
 if (-not $visited.Add($project)) {return}
 [xml]$xml=Get-Content -LiteralPath $project -Raw
 foreach ($reference in $xml.SelectNodes('//ProjectReference/@Include')) {Visit (Join-Path (Split-Path $project) $reference.Value)}
 $ordered.Add($project)
}
$modules=@('IBeam.Management','IBeam.Management.Services','IBeam.Management.Api','IBeam.Management.Diagnostics.Api','IBeam.Management.Repositories.AzureTable','IBeam.Management.Identity.Services','IBeam.Management.Identity.Api','IBeam.Management.Secrets.AzureKeyVault')
foreach ($module in $modules) {Visit (Join-Path $repo "$module/$module.csproj")}
if (-not $ReusePackages) { foreach ($project in $ordered) {
 & dotnet pack $project -c Release --nologo -v quiet "-p:ReleasePackageVersion=$PackageVersion" -p:WarningLevel=0 -o $feed
 if ($LASTEXITCODE -ne 0) {throw ('Pack failed: '+[IO.Path]::GetFileName($project))}
}
}
Add-Type -AssemblyName System.IO.Compression.FileSystem
$inspected=0
foreach ($module in $modules) {
 $archive=[IO.Compression.ZipFile]::OpenRead((Join-Path $feed "$module.$PackageVersion.nupkg"))
 try {
  $names=@($archive.Entries.FullName)
  foreach ($required in @('README.md','ibeam-icon.png','.agent/implementation-guide.md','.agent/project/prompt.md',"lib/net10.0/$module.dll")) {
   if ($names -notcontains $required) {throw "Missing intended package entry: $module/$required"}
  }
  if ($names|Where-Object {$_ -match '(?i)(secrets\.json|appsettings.*local|\.trx$|\.log$|TestResults/|samples/)'}) {throw 'Unexpected local/test artifact in management package'}
  foreach ($entry in $archive.Entries|Where-Object {$_.FullName -match '\.(md|json|nuspec|config|cs)$'}) {
   $reader=[IO.StreamReader]::new($entry.Open()); try {$content=$reader.ReadToEnd()} finally {$reader.Dispose()}
   foreach ($variable in @('HUBBSLY_API_KEY','IBEAM_MANAGEMENT_TEST_STORAGE','IBEAM_MANAGEMENT_TEST_SEPARATE_STORAGE')) {
    $value=[Environment]::GetEnvironmentVariable($variable)
    if ($value -and $content.Contains($value,[StringComparison]::Ordinal)) {throw 'Secret material found in package content'}
   }
  }
  $inspected++
 } finally {$archive.Dispose()}
}
Copy-Item -LiteralPath (Join-Path $repo 'samples/Management.PackageSmoke/Management.PackageSmoke.csproj') -Destination $hostFolder
Get-ChildItem -LiteralPath (Join-Path $repo 'samples/Management.PackageSmoke') -Filter '*.cs' | Copy-Item -Destination $hostFolder
# Prevent repository build props/project dependencies from influencing this package-only consumer.
'<Project />'|Set-Content (Join-Path $hostFolder 'Directory.Build.props')
'<Project />'|Set-Content (Join-Path $hostFolder 'Directory.Build.targets')
$escapedFeed=[Security.SecurityElement]::Escape($feed)
"<configuration><packageSources><clear/><add key='generated' value='$escapedFeed'/><add key='nuget.org' value='https://api.nuget.org/v3/index.json'/></packageSources></configuration>"|Set-Content (Join-Path $hostFolder 'NuGet.Config')
$consumer=Join-Path $hostFolder 'Management.PackageSmoke.csproj'
& dotnet restore $consumer --configfile (Join-Path $hostFolder 'NuGet.Config') "-p:ManagementSmokeVersion=$PackageVersion" --packages (Join-Path $OutputDirectory 'cache') --nologo -v quiet
if ($LASTEXITCODE -ne 0) {throw 'Package-only consumer restore failed'}
& dotnet run --project $consumer -c Release --no-restore "-p:ManagementSmokeVersion=$PackageVersion" -p:WarningLevel=0
if ($LASTEXITCODE -ne 0) {throw 'Package-only consumer smoke failed'}
@{packageVersion=$PackageVersion;packageCount=$ordered.Count;inspectedManagementPackages=$inspected;consumer='passed';sourceProjectReferences=0}|ConvertTo-Json|Set-Content (Join-Path $OutputDirectory 'validation-summary.json')
Write-Output "Validated $inspected management packages and $($ordered.Count) package dependencies; package-only two-target smoke passed."
