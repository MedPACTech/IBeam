param([string]$HostRoot,[string]$Instance='management-ownership')
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

if(-not $settings.ManagementHost.CustomerDemo.OwnershipEnabled){throw 'A fresh explicit ownership demo instance is required.'}
$health=Invoke-RestMethod ($base+'/health');if($health.dataMode -ne 'synthetic-demo'){throw 'Only the synthetic loopback demo may be used.'}
$reader=@(Get-Content (Join-Path $LocalRoot 'demo-users.json') -Raw|ConvertFrom-Json)|Where-Object Email -eq reader@example.test
$readerToken=Sign-In $base $reader.Email $reader.Password (Join-Path $LocalRoot 'outbox')
$run=[Guid]::NewGuid().ToString('N');$completed=0
try{
 foreach($target in $settings.ManagementHost.CustomerDemo.Targets){
  if(-not ([Uri]$target.Url).IsLoopback){throw 'Customer target must be synthetic loopback.'}
  $root=$base+'/api/management/applications/'+$target.ApplicationId+'/tenants/'+$target.TenantId
  $view=Api GET ($root+'/ownership') -Token $readerToken;Expect $view @(200) 'Ownership lookup'
  $original=$view.Body.data;if(@($original.owners).Count -ne 1){throw 'Single-owner fixture required.'}
  $people=Api GET ($root+'/users?pageSize=100') -Token $readerToken;Expect $people @(200) 'Member directory'
  $member=@($people.Body.data|Where-Object email -eq ('member@'+$target.ApplicationKey+'.example.test'))
  if($member.Count -ne 1 -or $member[0].status -ne 'active'){throw 'An enabled synthetic destination member is required.'}
  $destination=$member[0]
  $sourceEmail='customer@'+$target.ApplicationKey+'.example.test';$memberEmail='member@'+$target.ApplicationKey+'.example.test'
  $outbox=Join-Path $LocalRoot ($target.ApplicationKey+'/outbox')
  $oldSource=Sign-In $target.Url $sourceEmail $settings.ManagementHost.CustomerDemo.CustomerPassword $outbox
  $oldDestination=Sign-In $target.Url $memberEmail $settings.ManagementHost.CustomerDemo.CustomerPassword $outbox
  $nativeUrl=$target.Url+'/api/tenants/'+$target.TenantId
  Expect (Api GET $nativeUrl -Token $oldSource) @(200) 'Original customer owner access'
  $sourceId=$original.owners[0].userId;$sourceVersion=$original.owners[0].membershipVersion
  $roles=Api PUT ($root+'/users/'+$sourceId+'/roles') @{policyKey='member';roleIds=@()} $readerToken ('owner-deny-role-'+$run+'-'+$target.ApplicationKey) $sourceVersion
  Expect $roles @(403) 'Narrow role editing cannot strip Owner'
  $disable=Api PATCH ($root+'/users/'+$sourceId+'/membership') @{isEnabled=$false} $readerToken ('owner-deny-disable-'+$run+'-'+$target.ApplicationKey) $sourceVersion
  Expect $disable @(409) 'Final owner/administrator remains enabled'
  $command=@{sourceUserId=$sourceId;destinationUserId=$destination.userId;sourceVersion=$sourceVersion;destinationVersion=$destination.version}
  $changed=Api POST ($root+'/ownership/transfers') $command $readerToken ('owner-transfer-'+$run+'-'+$target.ApplicationKey) $original.version
  Expect $changed @(200) 'Atomic ownership transfer'
  if($changed.Body.data.tracking.status -ne 'succeeded'){throw 'Transfer did not reach a known succeeded receipt.'}
  Expect (Api GET $nativeUrl -Token $oldSource) @(401) 'Issued source JWT is stale after transfer'
  Expect (Api GET $nativeUrl -Token $oldDestination) @(401) 'Issued destination JWT is stale after transfer'
  $after=Api GET ($root+'/ownership') -Token $readerToken;Expect $after @(200) 'Authoritative owner after transfer'
  if(@($after.Body.data.owners).Count -ne 1 -or $after.Body.data.owners[0].userId -ne $destination.userId){throw 'Authoritative owner did not move.'}
  $freshDestination=Sign-In $target.Url $memberEmail $settings.ManagementHost.CustomerDemo.CustomerPassword $outbox
  Expect (Api GET $nativeUrl -Token $freshDestination) @(200) 'Fresh customer MFA sees committed ownership'
  $people=Api GET ($root+'/users?pageSize=100') -Token $readerToken;Expect $people @(200) 'Updated membership versions'
  $former=$people.Body.data|Where-Object userId -eq $sourceId
  $restore=@{sourceUserId=$destination.userId;destinationUserId=$sourceId;sourceVersion=$after.Body.data.owners[0].membershipVersion;destinationVersion=$former.version}
  $returned=Api POST ($root+'/ownership/transfers') $restore $readerToken ('owner-return-'+$run+'-'+$target.ApplicationKey) $after.Body.data.version
  Expect $returned @(200) 'Explicit fixture ownership restoration'
  $final=Api GET ($root+'/ownership') -Token $readerToken;Expect $final @(200) 'Restored owner lookup'
  if(@($final.Body.data.owners).Count -ne 1 -or $final.Body.data.owners[0].userId -ne $sourceId){throw 'Fixture owner was not restored.'}
  $completed++
 }
 Write-PrivateJson (Join-Path $LocalRoot 'ownership-smoke-summary.json') @{passed=$true;apps=$completed;realSupportAndCustomerMfa=$true;legacyOwnerColumnUsed=$false;syntheticOnly=$true}
 Write-Host ('PASS: '+$completed+' synthetic apps; real support/customer MFA, role policy, final-owner/admin safety, atomic transfer, stale JWT rejection and authoritative owner lookup.')
}finally{if($readerToken){$null=Api POST ($base+'/api/auth/logout') -Token $readerToken}}
