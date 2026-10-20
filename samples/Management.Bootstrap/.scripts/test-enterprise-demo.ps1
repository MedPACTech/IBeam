param([string]$HostRoot,[string]$Instance='management-enterprise')
. "$PSScriptRoot/common.ps1"
$LocalRoot=Join-Path $RepoRoot ('.local/'+$Instance)
$settings=Get-Content (Join-Path $LocalRoot 'settings.json') -Raw|ConvertFrom-Json
$state=Get-Content (Join-Path $LocalRoot 'processes.json') -Raw|ConvertFrom-Json
$base=$state.ApiUrl
if(-not ([Uri]$base).IsLoopback -or -not $settings.ManagementHost.CustomerDemo.Enabled){throw 'Synthetic loopback instance required.'}
function Api([string]$Method,[string]$Url,$Body=$null,[string]$Token='',[string]$Key='',[string]$Version=''){
 $headers=@{};if($Token){$headers.Authorization='Bearer '+$Token};if($Key){$headers['Idempotency-Key']=$Key};if($Version){$headers['If-Match']=$Version}
 $parameters=@{Uri=$Url;Method=$Method;Headers=$headers;ContentType='application/json';SkipHttpErrorCheck=$true}
 if($null -ne $Body){$parameters.Body=$Body|ConvertTo-Json -Depth 15}
 $response=Invoke-WebRequest @parameters
 $body=try{$response.Content|ConvertFrom-Json}catch{$null}
 return @{Status=[int]$response.StatusCode;Body=$body}
}
function Expect($Result,[int[]]$Statuses,[string]$Action){if($Result.Status -notin $Statuses){throw ($Action+' HTTP '+$Result.Status)}}
function Payload($Result){if($Result.Body.PSObject.Properties.Name -contains 'data'){return $Result.Body.data};return $Result.Body}
function Sign-In([string]$Url,[string]$Email,[string]$Password,[string]$Outbox){
 $login=Api POST ($Url+'/api/auth/password-login') @{email=$Email;password=$Password};Expect $login @(200) ('Password login '+$Url)
 $data=Payload $login
 if(-not $data.requiresTwoFactor -or $data.token){throw 'Real MFA must precede token issuance.'}
 $mail=Get-ChildItem -LiteralPath $Outbox -Filter '*.json'|Sort-Object LastWriteTimeUtc -Descending|ForEach-Object {Get-Content -LiteralPath $_.FullName -Raw|ConvertFrom-Json}|Where-Object {$_.Destination -eq $Email -and $_.Code}|Select-Object -First 1
 if(-not $mail.Code){throw 'Private local MFA delivery missing.'}
 $complete=Api POST ($Url+'/api/auth/2fa/complete-login') @{email=$Email;challengeId=$data.twoFactorChallengeId;code=$mail.Code};Expect $complete @(200) ('MFA completion '+$Url)
 $token=(Payload $complete).token.accessToken;if(-not $token){throw 'Verified login token missing.'};return $token
}
$health=Invoke-RestMethod ($base+'/health');if($health.dataMode -ne 'synthetic-demo'){throw 'Instance health is not synthetic.'}
$operator=Get-Content (Join-Path $LocalRoot 'operator.json') -Raw|ConvertFrom-Json
$reader=@(Get-Content (Join-Path $LocalRoot 'demo-users.json') -Raw|ConvertFrom-Json)|Where-Object Email -eq reader@example.test
$limited=@(Get-Content (Join-Path $LocalRoot 'demo-users.json') -Raw|ConvertFrom-Json)|Where-Object Email -eq limited@example.test
$outbox=Join-Path $LocalRoot 'outbox'
$adminToken=Sign-In $base $operator.Email $operator.Password $outbox
$readerToken=Sign-In $base $reader.Email $reader.Password $outbox
$limitedToken=Sign-In $base $limited.Email $limited.Password $outbox
try{
 $apps=Api GET ($base+'/api/management/applications?pageSize=100') -Token $readerToken;Expect $apps @(200) 'App selector'
 $selected=@($apps.Body.data);$cursor=$apps.Body.continuationToken;$pages=0
 while($cursor){if(++$pages -gt 20){throw 'Selector cursor did not terminate.'};$next=Api GET ($base+'/api/management/applications?pageSize=100&continuationToken='+[Uri]::EscapeDataString($cursor)) -Token $readerToken;Expect $next @(200) 'Selector continuation';$selected+=@($next.Body.data);$cursor=$next.Body.continuationToken}
 if($selected.Count -ne 3){throw ('Expected three assigned synthetic apps; got '+$selected.Count)}
 $spec=Invoke-RestMethod ($base+'/openapi/v1.json')
 if($spec.paths.PSObject.Properties.Name -contains '/api/invites/accept'){throw 'Customer controllers leaked into support Identity.'}
 $preflight=Invoke-WebRequest -Uri ($base+'/api/management/applications/'+$settings.ManagementHost.CustomerDemo.Targets[0].ApplicationId+'/enterprise-onboardings') -Method Options -Headers @{Origin='http://localhost:5145';'Access-Control-Request-Method'='POST';'Access-Control-Request-Headers'='authorization,content-type,idempotency-key,if-match'} -SkipHttpErrorCheck
 if([int]$preflight.StatusCode -ne 204 -or ($preflight.Headers['Access-Control-Allow-Headers'] -join ',') -notmatch 'idempotency-key'){throw 'Frontend write CORS contract failed.'}
 $runPath=Join-Path $LocalRoot 'enterprise-smoke.json'
 if(-not (Test-Path -LiteralPath $runPath)){Write-PrivateJson $runPath @{Intent=[Guid]::NewGuid().ToString('N')}}
 $run=Get-Content $runPath -Raw|ConvertFrom-Json
 foreach($target in $settings.ManagementHost.CustomerDemo.Targets){
  $nativeBoundary=Api GET ($target.Url+'/api/management/me');Expect $nativeBoundary @(404) 'Customer process excludes support controllers'
  $root=$base+'/api/management/applications/'+$target.ApplicationId
  $module=Api GET ($root+'/customer-management') -Token $readerToken;Expect $module @(200) 'Customer availability'
  $tenants=Api GET ($root+'/tenants?pageSize=1') -Token $readerToken;Expect $tenants @(200) 'Tenant directory'
  $people=Api GET ($root+'/tenants/'+$target.TenantId+'/users?pageSize=100') -Token $readerToken;Expect $people @(200) 'Customer directory'
  if(-not @($people.Body.data|Where-Object status -eq disabled).Count){throw 'Disabled synthetic membership missing.'}
  $invitations=Api GET ($root+'/tenants/'+$target.TenantId+'/invites?pageSize=100') -Token $readerToken;Expect $invitations @(200) 'Invitation directory'
  if(-not @($invitations.Body.data|Where-Object status -in @('pending','sent')).Count){throw 'Pending synthetic invite missing.'}
  $policies=Api GET ($root+'/enterprise-onboardings/policies') -Token $readerToken;Expect $policies @(200) 'Onboarding policies'
  if(-not @($policies.Body.data|Where-Object key -eq enterprise-owner).Count){throw 'Trusted policy key missing.'}
  $email='customer@'+$target.ApplicationKey+'.example.test'
  $key='demo-onboard-'+$run.Intent+'-'+$target.ApplicationKey
  $onboarding=Api POST ($root+'/enterprise-onboardings') @{tenantName='Synthetic enterprise';initialAdminEmail=$email;policyKey='enterprise-owner'} $readerToken $key
  Expect $onboarding @(200,202) 'Enterprise onboarding'
  $operation=$onboarding.Body.data.operation
  if(-not $operation.onboarding.tenantId){throw 'Enterprise operation must be known in this local smoke.'}
  if(-not $operation.onboarding.completed){
   $nativeToken=Sign-In $target.Url $email $settings.ManagementHost.CustomerDemo.CustomerPassword (Join-Path $LocalRoot ($target.ApplicationKey+'/outbox'))
   $mail=Get-ChildItem (Join-Path $LocalRoot ($target.ApplicationKey+'/outbox')) -Filter '*.json'|Sort-Object LastWriteTimeUtc -Descending|ForEach-Object {Get-Content $_.FullName -Raw|ConvertFrom-Json}|Where-Object {$_.Destination -eq $email -and $_.Body -match '[?&](token|inviteToken|code)='}|Select-Object -First 1
   $match=[regex]::Match($mail.Body,'[?&](?:token|inviteToken|code)=([^&\s"<>]+)')
   if(-not $match.Success){throw 'Private customer invite delivery missing.'}
   $inviteToken=[Uri]::UnescapeDataString($match.Groups[1].Value)
   $accepted=Api POST ($target.Url+'/api/invites/accept') @{inviteToken=$inviteToken;mode='existing-session'} $nativeToken
   Expect $accepted @(200) 'Verified customer acceptance'
  }
  $reference=$onboarding.Body.data.reference
  $complete=Api GET ($root+'/operations/'+$reference+'?authority=management.identity.onboarding.create') -Token $readerToken;Expect $complete @(200) 'Onboarding reconciliation'
  if(-not $complete.Body.data.operation.onboarding.completed){throw 'Customer acceptance did not complete onboarding.'}
  $nativeDenied=Api GET ($target.Url+'/api/management-identity/info') -Token $readerToken;Expect $nativeDenied @(401,403) 'Support token cannot replace target machine auth'
  $limitedDenied=Api GET ($root+'/tenants') -Token $limitedToken;Expect $limitedDenied @(403) 'Limited support customer scope'
  $anonymous=Api GET ($root+'/tenants');Expect $anonymous @(401) 'Anonymous customer scope'
 }
 $manifest=Get-Content (Join-Path $LocalRoot 'demo-manifest.json') -Raw|ConvertFrom-Json
 $target=$settings.ManagementHost.CustomerDemo.Targets|Select-Object -Last 1
 $assignmentUrl=$base+'/api/management/applications/'+$target.ApplicationId+'/support-users/'+$manifest.users.'reader@example.test'
 $assignments=Api GET ($base+'/api/management/applications/'+$target.ApplicationId+'/support-users?pageSize=100') -Token $adminToken;Expect $assignments @(200) 'Assignment listing'
 $original=$assignments.Body.data|Where-Object userId -eq $manifest.users.'reader@example.test'
 $changed=Api PUT $assignmentUrl @{permissions=@('management.diagnostics.errors.read');expectedVersion=$original.version} $adminToken;Expect $changed @(200) 'Revoke customer assignment'
 try{
  $denied=Api GET ($base+'/api/management/applications/'+$target.ApplicationId+'/tenants') -Token $readerToken;Expect $denied @(403) 'Live customer revocation'
  $savedConfig=$env:IBEAM_MANAGEMENT_LOCAL_CONFIG;$savedEnvironment=$env:ASPNETCORE_ENVIRONMENT
  try{$env:IBEAM_MANAGEMENT_LOCAL_CONFIG=Join-Path $LocalRoot 'settings.json';$env:ASPNETCORE_ENVIRONMENT='Development';& dotnet (Join-Path $LocalRoot 'host/IBeam.Management.Host.dll') --configure-customer-demo --contentRoot $ContentRoot;if($LASTEXITCODE -ne 0){throw 'Repeat trusted seed failed'}}finally{$env:IBEAM_MANAGEMENT_LOCAL_CONFIG=$savedConfig;$env:ASPNETCORE_ENVIRONMENT=$savedEnvironment}
  $stillDenied=Api GET ($base+'/api/management/applications/'+$target.ApplicationId+'/tenants') -Token $readerToken;Expect $stillDenied @(403) 'Repeat seed preserves revocation'
 }finally{
  $restored=Api PUT $assignmentUrl @{permissions=@($original.permissions);expectedVersion=$changed.Body.data.version} $adminToken;Expect $restored @(200) 'Explicit test assignment restoration'
 }
 Write-Host 'PASS: real support and customer password/MFA, enterprise acceptance, tenant/user/invite views, multi-app denials, realm boundaries and repeat seed preservation.'
}finally{foreach($token in @($adminToken,$readerToken,$limitedToken)){if($token){$logout=Api POST ($base+'/api/auth/logout') -Token $token}}}
