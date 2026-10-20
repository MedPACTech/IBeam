param([string]$HostRoot, [string]$Instance='management-demo', [switch]$RestoreAssignments)
. "$PSScriptRoot/common.ps1"
if ($Instance -notmatch '^[a-zA-Z0-9-]{1,40}$') {throw 'Invalid instance name.'}
$LocalRoot=Join-Path $RepoRoot ('.local/'+$Instance)
$state=Get-Content (Join-Path $LocalRoot 'processes.json') -Raw | ConvertFrom-Json
$settings=Get-Content (Join-Path $LocalRoot 'settings.json') -Raw | ConvertFrom-Json
if (-not $settings.ManagementHost.Demo.Enabled) {throw 'Demo seeding is forbidden on a normal instance.'}
$process=Get-Process -Id $state.Api.Id -ErrorAction Stop
if ($process.StartTime.ToUniversalTime().Ticks -ne ([DateTimeOffset]$state.Api.StartTimeUtc).UtcDateTime.Ticks) {throw 'The recorded demo instance is not running.'}
$base=[Uri]$state.ApiUrl
if (-not $base.IsLoopback) {throw 'Demo seeding requires loopback.'}
$health=Invoke-RestMethod ($base.GetLeftPart([UriPartial]::Authority)+'/health')
if ($health.service -ne 'IBeam.Management.API' -or $health.dataMode -ne 'synthetic-demo') {throw 'The API is not a synthetic demo instance.'}
function Api([string]$Method,[string]$Path,$Body=$null,[string]$Token='') {
    $params=@{Uri=($base.GetLeftPart([UriPartial]::Authority)+$Path);Method=$Method;ContentType='application/json'}
    if ($Token) {$params.Headers=@{Authorization=('Bearer '+$Token)}}
    if ($null -ne $Body) {$params.Body=$Body | ConvertTo-Json -Depth 12}
    try { return Invoke-RestMethod @params } catch { throw ("Synthetic seed request failed: "+$Method+" "+$Path+" HTTP "+[int]$_.Exception.Response.StatusCode) }
}
function Sign-In($Account) {
    $login=Api POST '/api/auth/password-login' @{email=$Account.Email;password=$Account.Password}
    if (-not $login.data.requiresTwoFactor -or $login.data.token) {throw 'The fixture requires real MFA.'}
    $mail=Get-ChildItem (Join-Path $LocalRoot 'outbox') -Filter '*.json' | Sort-Object LastWriteTimeUtc -Descending | ForEach-Object {Get-Content $_.FullName -Raw | ConvertFrom-Json} | Where-Object {$_.Purpose -eq 'LoginMfa' -and $_.Destination -eq $Account.Email} | Select-Object -First 1
    if (-not $mail.Code) {throw 'Local MFA delivery was not found.'}
    $complete=Api POST '/api/auth/2fa/complete-login' @{email=$Account.Email;challengeId=$login.data.twoFactorChallengeId;code=$mail.Code}
    return $complete.data.token.accessToken
}
function All-Data([string]$Path,[string]$Token) {
    $cursor=$null
    do {
        $suffix=if ($cursor) {'&continuationToken='+[Uri]::EscapeDataString($cursor)} else {''}
        $page=Api GET ($Path+'?pageSize=100'+$suffix) -Token $Token
        foreach ($item in $page.data) {$item}
        $cursor=$page.continuationToken
    } while ($cursor)
}
$operator=Get-Content (Join-Path $LocalRoot 'operator.json') -Raw | ConvertFrom-Json
$token=Sign-In $operator
try {
    $me=(Api GET '/api/management/me' -Token $token).data
    if ($me.managementOrganizationId -ne $settings.IBeam.Management.Identity.OrganizationId) {throw 'The support organization does not match this fixture.'}
    $usersPath=Join-Path $LocalRoot 'demo-users.json'
    if (-not (Test-Path $usersPath)) {
        Write-PrivateJson $usersPath @(
            @{Email='reader@example.test';Password=((New-Secret)+'Aa1!');Scenario='all-apps'},
            @{Email='limited@example.test';Password=((New-Secret)+'Aa1!');Scenario='atlas-errors-only'},
            @{Email='unassigned@example.test';Password=((New-Secret)+'Aa1!');Scenario='no-apps'}
        )
    }
    $accounts=@(Get-Content $usersPath -Raw | ConvertFrom-Json)
    $directory=@((Api GET '/api/management/support-users' -Token $token).data)
    $userIds=@{$operator.Email=$me.userId}
    foreach ($account in $accounts) {
        $existing=$directory | Where-Object email -eq $account.Email
        if (-not $existing) {
            $invite=Api POST '/api/management/support-invitations' @{email=$account.Email} $token
            $mail=Get-ChildItem (Join-Path $LocalRoot 'outbox') -Filter '*.json' | Sort-Object LastWriteTimeUtc -Descending | ForEach-Object {Get-Content $_.FullName -Raw | ConvertFrom-Json} | Where-Object {$_.Purpose -eq 'TenantInvitation' -and $_.Destination -eq $account.Email} | Select-Object -First 1
            if (-not $mail.Code) {throw 'Local fixture invitation delivery failed.'}
            $accepted=Api POST '/api/management/support-invitations/accept' @{inviteToken=$mail.Code;password=$account.Password}
            $userIds[$account.Email]=$accepted.data.userId
        } elseif ($existing.isEnabled) {$userIds[$account.Email]=$existing.userId}
        else {Write-Host "Preserving disabled demo support membership: $($account.Email)"}
    }
    $definitions=@(Get-Content (Join-Path $ContentRoot 'Demo/scenario.json') -Raw | ConvertFrom-Json)
    $keys=Get-Content (Join-Path $LocalRoot 'demo-keys.json') -Raw | ConvertFrom-Json -AsHashtable
    $registry=@(All-Data '/api/management/applications/registry' $token)
    $manifest=@()
    foreach ($definition in $definitions) {
        $app=$registry | Where-Object {$_.applicationKey -eq $definition.applicationKey -and $_.environment -eq $definition.environment}
        if (-not $app) {$app=(Api POST '/api/management/applications' @{applicationKey=$definition.applicationKey;name=$definition.name;environment=$definition.environment;baseUrl=$definition.baseUrl} $token).data}
        if (-not $app.isEnabled) {Write-Host "Preserving disabled demo app: $($definition.applicationKey)";continue}
        if ($app.baseUrl -ne $definition.baseUrl) {throw 'A demo application origin was changed; seed does not overwrite it.'}
        if (-not $app.hasConnection) {
            $connection=(Api PUT "/api/management/applications/$($app.applicationId)/connection" @{apiKey=$keys[$definition.applicationKey];expectedVersion=$app.version} $token).data
        } else {$connection=(Api POST "/api/management/applications/$($app.applicationId)/connection/test" -Token $token).data}
        $permissions=@($connection.capabilities | Where-Object {$_ -in @('management.diagnostics.errors.read','management.diagnostics.logs.read')})
        $assignments=@(All-Data "/api/management/applications/$($app.applicationId)/support-users" $token)
        foreach ($email in @($operator.Email,'reader@example.test','limited@example.test')) {
            if (-not $userIds.ContainsKey($email)) {continue}
            if ($email -eq 'limited@example.test' -and $definition.applicationKey -ne 'demo-atlas') {continue}
            $grants=if ($email -eq 'limited@example.test') {@('management.diagnostics.errors.read')} else {$permissions}
            $current=$assignments | Where-Object userId -eq $userIds[$email]
            if ($current -and -not $RestoreAssignments) {continue}
            $body=@{permissions=@($grants)}
            if ($current) {$body.expectedVersion=$current.version}
            $saved=Api PUT "/api/management/applications/$($app.applicationId)/support-users/$($userIds[$email])" $body $token
        }
        $manifest+=@{applicationId=$app.applicationId;applicationKey=$definition.applicationKey;name=$definition.name;permissions=$permissions;errors=$definition.errorCount;logs=$definition.logCount}
    }
    Write-PrivateJson (Join-Path $LocalRoot 'demo-manifest.json') @{apiUrl=$state.ApiUrl;dataMode='synthetic-demo';applications=$manifest;users=$userIds;credentialsFile='demo-users.json'}
    Write-Host "Synthetic baseline ready: $($manifest.Count) apps and $($userIds.Count) support identities (including administrator)."
    Write-Host "Private login files: .local/$Instance/operator.json and demo-users.json. Passwords and fixture keys were not printed."
} finally {if ($token) {$logout=Api POST '/api/auth/logout' -Token $token}}
