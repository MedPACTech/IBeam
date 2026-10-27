param([string]$OutputDirectory,[string]$PackageVersion,[switch]$ReusePackages)
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot -Parent
if(-not $OutputDirectory){$OutputDirectory=Join-Path $repo ('_tmp_pkg_smoke/customer-'+[Guid]::NewGuid().ToString('N'))}
if(-not $PackageVersion){$PackageVersion='0.0.0-customer.smoke.'+[Guid]::NewGuid().ToString('N')}
$OutputDirectory=[IO.Path]::GetFullPath($OutputDirectory)
foreach($name in @('IBEAM_MANAGEMENT_TEST_STORAGE','IBEAM_MANAGEMENT_TEST_SEPARATE_STORAGE')){
 $connection=[Environment]::GetEnvironmentVariable($name)
 $match=[regex]::Match($connection,'(?:^|;)TableEndpoint=([^;]+)')
 if(-not $match.Success){throw 'Two explicit loopback Azurite Table connections are required.'}
 $uri=[Uri]$match.Groups[1].Value
 if(-not $uri.IsLoopback -or $uri.Scheme -ne 'http' -or $connection -notmatch '(?:^|;)AccountName=devstoreaccount1(?:;|$)'){throw 'This validation accepts only private loopback Azurite.'}
}
$suites=@('Management.CustomerIdentity','Management.Applications','Management.Identity','Management.Credentials','Management.Diagnostics','Management','Identity.Services','Identity','Identity.Api','Management.Licensing','Management.Operations','Management.TenantData','Licensing')
$roots=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach($suite in $suites){
 [xml]$xml=Get-Content (Join-Path $repo "IBeam.Tests.$suite/IBeam.Tests.$suite.csproj") -Raw
 foreach($reference in $xml.SelectNodes('//ProjectReference/@Include')){[void]$roots.Add([IO.Path]::GetFileNameWithoutExtension($reference.Value.Replace('\','/')))}
}
& (Join-Path $PSScriptRoot 'Test-ManagementPackages.ps1') -OutputDirectory $OutputDirectory -PackageVersion $PackageVersion -ReusePackages:$ReusePackages -AdditionalModules @($roots)
if($LASTEXITCODE -ne 0){throw 'Management package preparation failed'}
$feed=Join-Path $OutputDirectory 'feed'
$results=[Collections.Generic.List[object]]::new()
foreach($suite in @($suites|Where-Object {$_ -ne 'Management.Applications'})){
 $name='IBeam.Tests.'+$suite; $source=Join-Path $repo $name; $consumer=Join-Path $OutputDirectory ('consumers/'+$name)
 New-Item -ItemType Directory -Path $consumer -Force|Out-Null
 [xml]$xml=Get-Content (Join-Path $source "$name.csproj") -Raw
 foreach($reference in @($xml.SelectNodes('//ProjectReference'))){
  $package=$xml.CreateElement('PackageReference'); $package.SetAttribute('Include',[IO.Path]::GetFileNameWithoutExtension($reference.Include.Replace('\','/')))
  $package.SetAttribute('Version',$PackageVersion); [void]$reference.ParentNode.ReplaceChild($package,$reference)
 }
 if($suite -eq 'Management.CustomerIdentity'){
  $group=$xml.CreateElement('ItemGroup');[void]$xml.Project.AppendChild($group)
  foreach($extra in @('IBeam.Management.Identity.Services','IBeam.Management.Secrets.AzureKeyVault')){
   $package=$xml.CreateElement('PackageReference');$package.SetAttribute('Include',$extra);$package.SetAttribute('Version',$PackageVersion);[void]$group.AppendChild($package)
  }
  $appSources=Join-Path $consumer 'ApplicationFixtures';New-Item -ItemType Directory -Path $appSources -Force|Out-Null
  Get-ChildItem -LiteralPath (Join-Path $repo 'IBeam.Tests.Management.Applications') -Filter '*.cs' | Copy-Item -Destination $appSources
  Copy-Item -LiteralPath (Join-Path $repo 'samples/Management.CustomerIdentity.PackageSmoke/EnterpriseWorkflowPackageTests.cs') -Destination $consumer
 }
 $xml.Save((Join-Path $consumer "$name.csproj"))
 Get-ChildItem -LiteralPath $source -Filter '*.cs' | Copy-Item -Destination $consumer
 '<Project />' | Set-Content (Join-Path $consumer 'Directory.Build.props')
 '<Project />' | Set-Content (Join-Path $consumer 'Directory.Build.targets')
 Copy-Item -LiteralPath (Join-Path $OutputDirectory 'consumer/NuGet.Config') -Destination $consumer
 $project=Join-Path $consumer "$name.csproj"
 & dotnet restore $project --configfile (Join-Path $consumer 'NuGet.Config') --packages (Join-Path $OutputDirectory 'cache') --nologo -v quiet
 if($LASTEXITCODE -ne 0){throw ('Package consumer restore failed: '+$name)}
 $assets=Get-Content (Join-Path $consumer 'obj/project.assets.json') -Raw|ConvertFrom-Json
 $ibeam=@($assets.libraries.PSObject.Properties|Where-Object Name -like 'IBeam.*/*')
 if(-not $ibeam.Count -or @($ibeam|Where-Object {$_.Value.type -ne 'package'}).Count){throw 'Consumer contains a source framework dependency'}
 & dotnet test $project --no-restore --nologo -v minimal --logger 'trx;LogFileName=package-validation.trx' --results-directory (Join-Path $consumer 'results') -p:WarningLevel=0
 if($LASTEXITCODE -ne 0){throw ('Package consumer tests failed: '+$name)}
 [xml]$trx=Get-Content (Join-Path $consumer 'results/package-validation.trx') -Raw
 $counts=$trx.TestRun.ResultSummary.Counters
 if([int]$counts.notExecuted -ne 0 -or [int]$counts.failed -ne 0){throw ('Package consumer skipped or failed checks: '+$name)}
 $results.Add(@{suite=$name;passed=[int]$counts.passed;sourceProjectReferences=0;frameworkDependencies='generated NuGet only'})
}
$results|ConvertTo-Json -Depth 5|Set-Content (Join-Path $OutputDirectory 'customer-validation-summary.json')
Write-Output ('Package-only customer regression checks passed: '+(($results|Measure-Object passed -Sum).Sum))
