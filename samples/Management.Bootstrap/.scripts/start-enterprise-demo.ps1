param([string]$HostRoot,[Parameter(Mandatory=$true)][string]$PackageFeed,[string]$Instance='management-enterprise',[int]$ApiPort=5155,[int]$TablePort=15122,[int]$FirstTargetPort=5255)
$ErrorActionPreference='Stop'
. "$PSScriptRoot/common.ps1"
& "$PSScriptRoot/start-all.ps1" -HostRoot $RepoRoot -PackageFeed $PackageFeed -Instance $Instance -ApiPort $ApiPort -TablePort $TablePort -Demo
$LocalRoot=Join-Path $RepoRoot ('.local/'+$Instance)
if(-not (Test-Path -LiteralPath (Join-Path $LocalRoot 'demo-manifest.json'))){& "$PSScriptRoot/seed-demo.ps1" -HostRoot $RepoRoot -Instance $Instance}
$settingsPath=Join-Path $LocalRoot 'settings.json';$statePath=Join-Path $LocalRoot 'processes.json'
$settings=Get-Content $settingsPath -Raw|ConvertFrom-Json -AsHashtable
$manifest=Get-Content (Join-Path $LocalRoot 'demo-manifest.json') -Raw|ConvertFrom-Json
$state=Get-Content $statePath -Raw|ConvertFrom-Json -AsHashtable
Stop-RecordedProcess $state.Api
$state.Api=$null;Write-PrivateJson $statePath $state
if(-not $settings.ManagementHost.CustomerDemo){
 $rsa=[Security.Cryptography.RSA]::Create(2048)
 try{[IO.File]::WriteAllText((Join-Path $LocalRoot 'actor-signing.pem'),$rsa.ExportRSAPrivateKeyPem())}finally{$rsa.Dispose()}
 $targets=@();$index=0
 foreach($app in $manifest.applications){
  $credential=[Guid]::NewGuid();$secret=[Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(32)).TrimEnd('=').Replace('+','-').Replace('/','_')
  $targets+=@{ApplicationKey=$app.applicationKey;ApplicationId=$app.applicationId;RealmId=[Guid]::NewGuid().ToString();CredentialId=$credential.ToString()
   MachineKey=('ibmgmt_v1.'+$credential.ToString('N')+'.'+$secret);TablePrefix=('DemoCustomer'+$index);Url=('http://127.0.0.1:'+($FirstTargetPort+$index))
   JwtKey=(New-Secret);ProtectionKey=[Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(32));TenantId=[Guid]::NewGuid().ToString();DisabledTenantId=[Guid]::NewGuid().ToString()}
  $index++
 }
 $settings.ManagementHost.CustomerDemo=@{Enabled=$true;Targets=$targets;CustomerPassword=((New-Secret)+'Aa1!')}
 $settings.ManagementHost.CustomerIdentity=@{Enabled=$true}
 $settings.IBeam.Management.PortalCustomerIdentity=@{ProtectionKey=[Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))}
 $settings.IBeam.Management.Delegation=@{Issuer=@{Issuer='synthetic-support';Targets=@($targets|ForEach-Object{@{ApplicationId=$_.ApplicationId;Audience=('synthetic-'+$_.ApplicationKey)}})}}
 $delegations=@()
 foreach($target in $targets){foreach($email in @('operator@example.test','reader@example.test')){
  $person=$manifest.users.$email
  if($person){$delegations+=@{UserId=$person;ApplicationId=$target.ApplicationId;Operations=@('tenants.read','tenants.create','tenants.update','tenants.status','users.read','users.provision','invites.read','invites.manage','memberships.manage','roles.assign','roles.read','onboarding.create','onboarding.read','onboarding.retry')|ForEach-Object{'management.identity.'+$_}}}
 }}
 $settings.IBeam.Management.Identity.CustomerDelegations=$delegations
 Write-PrivateJson $settingsPath $settings
}
$dll=Join-Path $LocalRoot 'host/IBeam.Management.Host.dll'
$saved=@{}
foreach($name in @('ASPNETCORE_ENVIRONMENT','IBEAM_MANAGEMENT_LOCAL_CONFIG')){$saved[$name]=[Environment]::GetEnvironmentVariable($name)}
$targets=@()
try{
 $env:ASPNETCORE_ENVIRONMENT='Development';$env:IBEAM_MANAGEMENT_LOCAL_CONFIG=$settingsPath
 foreach($definition in $settings.ManagementHost.CustomerDemo.Targets){
  $uri=[Uri]$definition.Url
  if(-not $uri.IsLoopback -or (Test-Listening $uri.Port)){throw 'A synthetic target endpoint is invalid or occupied.'}
  & dotnet $dll --setup-customer-target $definition.ApplicationKey --contentRoot $ContentRoot
  if($LASTEXITCODE -ne 0){throw 'Synthetic target setup failed; private seed state preserved.'}
  $process=Start-Process -FilePath (Get-Command dotnet).Source -ArgumentList @(('"'+$dll+'"'),'--customer-target',$definition.ApplicationKey,'--contentRoot',('"'+$ContentRoot+'"')) -WorkingDirectory $ContentRoot -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $LocalRoot ($definition.ApplicationKey+'.log')) -RedirectStandardError (Join-Path $LocalRoot ($definition.ApplicationKey+'-errors.log'))
  $targets+=Get-ProcessRecord $process $dll;$state.CustomerTargets=$targets;Write-PrivateJson $statePath $state
  Wait-Port $uri.Port $process
 }
 & dotnet $dll --configure-customer-demo --contentRoot $ContentRoot
 if($LASTEXITCODE -ne 0){throw 'Trusted synthetic connection installation failed.'}
 $process=Start-Process -FilePath (Get-Command dotnet).Source -ArgumentList @(('"'+$dll+'"'),'--urls',"http://127.0.0.1:$ApiPort") -WorkingDirectory $ContentRoot -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $LocalRoot 'api.log') -RedirectStandardError (Join-Path $LocalRoot 'api-errors.log')
 $state.Api=Get-ProcessRecord $process $dll;Write-PrivateJson $statePath $state;Wait-Port $ApiPort $process
 Write-Host "Enterprise demo API: http://127.0.0.1:$ApiPort; real support password/MFA; three isolated synthetic customer realms."
 Write-Host "Private support/customer credentials and mail remain under .local/$Instance. Browser configuration needs only the API URL."
}catch{
 foreach($target in $targets){Stop-RecordedProcess $target}
 throw
}finally{foreach($name in $saved.Keys){[Environment]::SetEnvironmentVariable($name,$saved[$name])}}
