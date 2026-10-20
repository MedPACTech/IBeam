param([string]$HostRoot, [string]$PackageFeed, [int]$ApiPort=5150, [int]$TablePort=15102, [string]$AdministratorEmail='operator@example.test', [string]$Instance='management', [switch]$Demo)
. "$PSScriptRoot/common.ps1"
if ($Instance -notmatch '^[a-zA-Z0-9-]{1,40}$') { throw 'Invalid local instance name.' }
$LocalRoot=Join-Path $RepoRoot ('.local/'+$Instance)
$StatePath=Join-Path $LocalRoot 'processes.json'
Protect-LocalRoot
if (Test-Path $StatePath) { throw 'Local stack state exists. Run .scripts/stop-all.ps1 before starting it again.' }
if ((Test-Listening $ApiPort) -or (Test-Listening $TablePort)) { throw 'A requested local port is occupied. Choose other ports; no existing process was stopped.' }
$settingsPath=Join-Path $LocalRoot 'settings.json'
$operatorPath=Join-Path $LocalRoot 'operator.json'
$connection=Get-EmulatorConnection $TablePort
$prefix=if ($Demo) {'DemoManagement'} else {'Management'}
$scenario=@(Get-Content (Join-Path $ContentRoot 'Demo/scenario.json') -Raw | ConvertFrom-Json)
if (-not (Test-Path $settingsPath)) {
    $settings=@{
        ManagementHost=@{
            LocalDataDirectory=$LocalRoot; Storage=@{ConnectionString=$connection; TablePrefix=$prefix}; Demo=@{Enabled=$Demo.IsPresent}
            Secrets=@{Provider='DevelopmentFile'}; Mail=@{Provider='DevelopmentOutbox'}
            Cors=@{AllowedOrigins=@('http://127.0.0.1:5145','http://localhost:5145')}
        }
        IBeam=@{Management=@{
            Identity=@{OrganizationId=[Guid]::NewGuid().ToString(); BootstrapEnabled=$false
                Storage=@{Mode='Separate';ConnectionString=$connection;TablePrefix=$prefix;ProvisionSchema=$false}
                Jwt=@{SigningKey=(New-Secret)}
                Otp=@{HashSalt=(New-Secret);VerificationTokenSecret=(New-Secret)}
                Invites=@{Links=@{AllowedOrigins=@('http://127.0.0.1:5145','http://localhost:5145');DefaultAcceptUrl='http://127.0.0.1:5145/support/accept-invitation'}}
            }
            Connections=@{ApprovedOrigins=@(if ($Demo) {$scenario.baseUrl} else {'https://diagnostics.example.com'})}
            PortalDiagnostics=@{ProtectionKey=[Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))}
        }}
    }
    Write-PrivateJson $settingsPath $settings
    Write-PrivateJson $operatorPath @{Email=$AdministratorEmail;Password=((New-Secret)+'Aa1!')}
} else {
    $settings=Get-Content $settingsPath -Raw | ConvertFrom-Json -AsHashtable
    if ($settings.ManagementHost.Storage.ConnectionString -ne $connection) { throw 'This local configuration uses a different emulator port. Keep the original port or create a separate local workspace.' }
}
if (-not (Test-Path $operatorPath)) { throw 'The protected local operator file is missing. Recover it through trusted setup; credentials were not reset.' }
if ([bool]$settings.ManagementHost.Demo.Enabled -ne $Demo.IsPresent) { throw 'Normal and demo instances must use separate local configurations.' }
if ($Demo) {
    $keysPath=Join-Path $LocalRoot 'demo-keys.json'
    if (-not (Test-Path $keysPath)) {
        $keys=@{}; foreach ($app in $scenario) {$keys[$app.applicationKey]=New-Secret}
        Write-PrivateJson $keysPath $keys
    }
}
$state=@{}
try {
    $azurite=Get-Azurite
    $data=Join-Path $LocalRoot 'azurite'
    New-Item -ItemType Directory -Path $data -Force | Out-Null
    $emulator=Start-Process -FilePath $azurite.Node -ArgumentList @(('"'+$azurite.Main+'"'),'--tableHost','127.0.0.1','--tablePort',"$TablePort",'--location',('"'+$data+'"'),'--silent') -WorkingDirectory $RepoRoot -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $LocalRoot 'azurite.log') -RedirectStandardError (Join-Path $LocalRoot 'azurite-errors.log')
    $state.Table=Get-ProcessRecord $emulator $azurite.Main
    Write-PrivateJson $StatePath $state
    Wait-Port $TablePort $emulator
    $runtimeDirectory=Join-Path $LocalRoot 'host'
    $HostDll=Join-Path $runtimeDirectory 'IBeam.Management.Host.dll'
    if ($PackageFeed) {
        $feed=[IO.Path]::GetFullPath($PackageFeed)
        if(-not (Test-Path -LiteralPath $feed -PathType Container)){throw 'Local package feed not found.'}
        $escaped=[Security.SecurityElement]::Escape($feed)
        $restoreConfig=Join-Path $LocalRoot 'NuGet.Config'
        "<configuration><packageSources><clear/><add key='local' value='$escaped'/><add key='nuget.org' value='https://api.nuget.org/v3/index.json'/></packageSources></configuration>" | Set-Content $restoreConfig
        & dotnet restore $Project --configfile $restoreConfig --nologo -v quiet
        if($LASTEXITCODE -ne 0){throw 'Local package restore failed.'}
    } else {
        & dotnet restore $Project --nologo -v quiet
        if($LASTEXITCODE -ne 0){throw 'Published package restore failed.'}
    }
    & dotnet publish $Project --no-restore -c Release -o $runtimeDirectory --nologo -v quiet
    if ($LASTEXITCODE -ne 0) { throw 'Management host publish failed.' }
    $operator=Get-Content $operatorPath -Raw | ConvertFrom-Json
    $saved=@{}
    foreach ($name in @('ASPNETCORE_ENVIRONMENT','IBEAM_MANAGEMENT_LOCAL_CONFIG','IBEAM_MANAGEMENT_SETUP_EMAIL','IBEAM_MANAGEMENT_SETUP_PASSWORD')) { $saved[$name]=[Environment]::GetEnvironmentVariable($name) }
    try {
        $env:ASPNETCORE_ENVIRONMENT='Development'; $env:IBEAM_MANAGEMENT_LOCAL_CONFIG=$settingsPath
        $env:IBEAM_MANAGEMENT_SETUP_EMAIL=$operator.Email; $env:IBEAM_MANAGEMENT_SETUP_PASSWORD=$operator.Password
        & dotnet $HostDll --setup --contentRoot $ContentRoot
        if ($LASTEXITCODE -ne 0) { throw 'Trusted management setup failed.' }
    } finally { foreach ($name in $saved.Keys) { [Environment]::SetEnvironmentVariable($name,$saved[$name]) } }
    $hostProcess=Start-Process -FilePath (Get-Command dotnet).Source -ArgumentList @(('"'+$HostDll+'"'),'--urls',"http://127.0.0.1:$ApiPort") -WorkingDirectory $ContentRoot -Environment @{ASPNETCORE_ENVIRONMENT='Development';IBEAM_MANAGEMENT_LOCAL_CONFIG=$settingsPath;IBEAM_MANAGEMENT_SETUP_EMAIL='';IBEAM_MANAGEMENT_SETUP_PASSWORD=''} -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $LocalRoot 'api.log') -RedirectStandardError (Join-Path $LocalRoot 'api-errors.log')
    $state.Api=Get-ProcessRecord $hostProcess $HostDll
    $state.ApiUrl="http://127.0.0.1:$ApiPort"; $state.TablePort=$TablePort
    Write-PrivateJson $StatePath $state
    Wait-Port $ApiPort $hostProcess
    $health=Invoke-RestMethod "$($state.ApiUrl)/health"
    if ($health.service -ne 'IBeam.Management.API') { throw 'Unexpected service on the Management API port.' }
    Write-Host "Management API running: $($state.ApiUrl)"
    Write-Host "OpenAPI: $($state.ApiUrl)/openapi/v1.json"
    Write-Host "Operator credentials: .local/$Instance/operator.json (private; not printed)."
    Write-Host "Local MFA and invitation delivery: .local/$Instance/outbox (private operator files)."
    Write-Host "Configure real approved target origins in .local/$Instance/settings.json and restart before connecting apps."
} catch {
    Stop-RecordedProcess $state.Api
    Stop-RecordedProcess $state.Table
    if (Test-Path $StatePath) { Remove-Item -LiteralPath $StatePath }
    throw
}
