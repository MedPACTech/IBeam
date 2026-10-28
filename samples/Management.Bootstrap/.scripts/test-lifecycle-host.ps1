param([string]$HostRoot,[string]$Instance='management-lifecycle',[Parameter(Mandatory)][string]$ExpectedPackageVersion)
. "$PSScriptRoot/common.ps1"
$LocalRoot=Join-Path $RepoRoot ('.local/'+$Instance)
$settingsPath=Join-Path $LocalRoot 'settings.json'
$statePath=Join-Path $LocalRoot 'processes.json'
$settings=Get-Content $settingsPath -Raw|ConvertFrom-Json -AsHashtable
$state=Get-Content $statePath -Raw|ConvertFrom-Json -AsHashtable
$base=$state.ApiUrl
if(-not ([Uri]$base).IsLoopback -or -not $settings.ManagementHost.CustomerDemo.Enabled -or $settings.ManagementHost.LifecyclePortal.Enabled){throw 'An owned loopback enterprise demo with lifecycle routes disabled is required.'}
$checks=0
function Check([bool]$condition,[string]$message){if(!$condition){throw $message};$script:checks++}
function Get-Document {
 $response=Invoke-WebRequest ($base+'/openapi/v1.json') -SkipHttpErrorCheck
 Check ($response.StatusCode -eq 200) 'Development OpenAPI must render successfully.'
 return $response.Content|ConvertFrom-Json -AsHashtable
}
$before=@{}
foreach($name in @('settings.json','operator.json','demo-users.json','demo-keys.json','actor-signing.pem')){
 $path=Join-Path $LocalRoot $name;if(Test-Path $path){$before[$name]=(Get-FileHash $path -Algorithm SHA256).Hash}
}
$health=Invoke-RestMethod ($base+'/health')
Check ($health.iBeamVersion -eq $ExpectedPackageVersion) 'Health must report the loaded private package version.'
$ui=Invoke-WebRequest ($base+'/swagger/index.html')
Check ($ui.StatusCode -eq 200 -and $ui.Content.Contains('Swagger UI')) 'Development Swagger UI must be served locally.'
$initializer=Invoke-WebRequest ($base+'/swagger/index.js')
Check ($initializer.Content.Contains('/openapi/v1.json') -and $initializer.Content.Contains('persistAuthorization')) 'Swagger must use the local document and explicit nonpersistent authorization.'
$document=Get-Document
Check ($document.info.version -eq $ExpectedPackageVersion) 'Document must report the loaded package version.'
Check ($document.components.securitySchemes.SupportBearer.scheme -eq 'bearer') 'Swagger requires support Bearer metadata.'
$root='/api/management/applications/{applicationId}'
$tenantRoot=$root+'/tenants/{tenantId}'
$optional=@(($root+'/operations'),($tenantRoot+'/exports*'),($tenantRoot+'/purge-plans'),($tenantRoot+'/purges*'),($tenantRoot+'/licenses/*'),($tenantRoot+'/invitation-revocations/*'))
foreach($suffix in $optional){Check (@($document.paths.Keys|Where-Object {$_ -like $suffix}).Count -eq 0) ('Optional portal route present without opt-in: '+$suffix)}
Check (@($document.paths.Keys|Where-Object {$_ -like '/api/invites/*'}).Count -eq 0) 'Support OpenAPI must not expose native customer invitation controllers.'
$password=$document.paths['/api/auth/password-login'].post
Check ($null -ne $password -and ($null -eq $password.security -or $password.security.Count -eq 0)) 'Normal password login remains anonymous; MFA is still required by the actual auth flow.'

# The test explicitly supplies private signer options. The feature flag itself generates no key,
# changes no grants and installs no target hook/worker. Existing settings are never overwritten.
$testConfig=Join-Path $LocalRoot 'lifecycle-test-settings.json'
$settings.ManagementHost.LifecyclePortal=@{Enabled=$true}
if(!$settings.IBeam.Management.Operations){$settings.IBeam.Management.Operations=@{}}
$settings.IBeam.Management.Operations.Portal=@{Issuer='synthetic-support';LifetimeSeconds=30
 OperationIdKey=[Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
 Targets=@($settings.ManagementHost.CustomerDemo.Targets|ForEach-Object {@{ApplicationId=$_.ApplicationId;Audience=('synthetic-'+$_.ApplicationKey)}})}
Write-PrivateJson $testConfig $settings
$dll=Join-Path $LocalRoot 'host/IBeam.Management.Host.dll'
$savedEnvironment=$env:ASPNETCORE_ENVIRONMENT;$savedConfig=$env:IBEAM_MANAGEMENT_LOCAL_CONFIG
function Start-TestApi([string]$config,[string]$stem){
 $env:ASPNETCORE_ENVIRONMENT='Development';$env:IBEAM_MANAGEMENT_LOCAL_CONFIG=$config
 $process=Start-Process -FilePath (Get-Command dotnet).Source -ArgumentList @(('"'+$dll+'"'),'--urls',$base) -WorkingDirectory $ContentRoot -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $LocalRoot ($stem+'.log')) -RedirectStandardError (Join-Path $LocalRoot ($stem+'-errors.log'))
 $state.Api=Get-ProcessRecord $process $dll;Write-PrivateJson $statePath $state;Wait-Port ([Uri]$base).Port $process
}
try {
 Stop-RecordedProcess $state.Api
 Start-TestApi $testConfig 'lifecycle-test'
 $document=Get-Document
 foreach($suffix in $optional){
  $paths=@($document.paths.Keys|Where-Object {$_ -like $suffix})
  Check ($paths.Count -gt 0) ('Explicit portal composition missing: '+$suffix)
  foreach($path in $paths){foreach($method in $document.paths[$path].Keys|Where-Object {$_ -in @('get','post','put','patch','delete')}){
   Check (@($document.paths[$path][$method].security|Where-Object {$_.ContainsKey('SupportBearer')}).Count -eq 1) 'Each optional operation must require the support Bearer scheme in OpenAPI.'
  }}
 }
 $app=[Guid]::NewGuid();$tenant=[Guid]::NewGuid()
 foreach($route in @("/api/management/applications/$app/operations","/api/management/applications/$app/tenants/$tenant/exports/$([Guid]::NewGuid())")){
  $response=Invoke-WebRequest ($base+$route) -SkipHttpErrorCheck
  Check ($response.StatusCode -eq 401) 'Optional routes must reject anonymous users before target access.'
 }
 $request=$document.paths.Keys|Where-Object {$_ -like '*/invitation-revocations/{batchId}'}|Select-Object -First 1
 Check ($null -ne $document.paths[$request].post.requestBody.content['application/json']) 'Bulk revoke must expose a typed JSON request schema.'
} finally {
 Stop-RecordedProcess $state.Api
 try{Start-TestApi $settingsPath 'api'}finally{$env:ASPNETCORE_ENVIRONMENT=$savedEnvironment;$env:IBEAM_MANAGEMENT_LOCAL_CONFIG=$savedConfig}
}
foreach($name in $before.Keys){Check ((Get-FileHash (Join-Path $LocalRoot $name) -Algorithm SHA256).Hash -eq $before[$name]) 'Original credentials/configuration must remain unchanged.'}
Write-PrivateJson (Join-Path $LocalRoot 'lifecycle-host-summary.json') @{passed=$true;checks=$checks;version=$ExpectedPackageVersion;syntheticOnly=$true;originalConfigurationPreserved=$true}
Write-Host ('PASS: '+$checks+' lifecycle host/OpenAPI/optional-route checks; original private configuration preserved.')

