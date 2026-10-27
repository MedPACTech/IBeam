$ErrorActionPreference = 'Stop'
$RepoRoot = if ($HostRoot) {[IO.Path]::GetFullPath($HostRoot)} else {Split-Path $PSScriptRoot -Parent}
$LocalRoot = Join-Path $RepoRoot '.local/management'
$Project = Join-Path $RepoRoot 'src/IBeam.Management.API/IBeam.Management.API.csproj'
$ContentRoot = Split-Path $Project
$HostDll = Join-Path $ContentRoot 'bin/Release/net10.0/IBeam.Management.Host.dll'
$StatePath = Join-Path $LocalRoot 'processes.json'
function Protect-LocalRoot {
    New-Item -ItemType Directory -Path $LocalRoot -Force | Out-Null
    if ($IsWindows) {
        $directory = [IO.DirectoryInfo]::new($LocalRoot)
        $acl = [IO.FileSystemAclExtensions]::GetAccessControl($directory, [Security.AccessControl.AccessControlSections]::Access)
        $acl.SetAccessRuleProtection($true, $false)
        foreach ($existing in @($acl.GetAccessRules($true, $true, [Security.Principal.SecurityIdentifier]))) { $acl.RemoveAccessRuleSpecific($existing) }
        $sid = [Security.Principal.WindowsIdentity]::GetCurrent().User
        $rule = [Security.AccessControl.FileSystemAccessRule]::new($sid, 'FullControl', 'ContainerInherit,ObjectInherit', 'None', 'Allow')
        $acl.AddAccessRule($rule)
        [IO.FileSystemAclExtensions]::SetAccessControl($directory, $acl)
    } else { [IO.File]::SetUnixFileMode($LocalRoot, [IO.UnixFileMode]::UserRead -bor [IO.UnixFileMode]::UserWrite -bor [IO.UnixFileMode]::UserExecute) }
}
function Get-Azurite {
    $node = (Get-Command node -ErrorAction Stop).Source
    $command = Get-Command azurite -ErrorAction Stop
    $main = Join-Path (Split-Path $command.Source) 'node_modules/azurite/dist/src/table/main.js'
    if (-not (Test-Path -LiteralPath $main)) { throw 'Install the local prerequisite with npm install -g azurite@3.35.0.' }
    return @{Node=$node; Main=$main}
}
function Get-EmulatorConnection([int]$Port) {
    $azurite = Get-Azurite
    $module = (Join-Path (Split-Path (Split-Path $azurite.Main)) 'common/utils/constants.js').Replace('\','/')
    $key = & $azurite.Node -e "const c=require(process.argv[1]).EMULATOR_ACCOUNT_KEY; process.stdout.write(Buffer.isBuffer(c)?c.toString('base64'):c)" $module
    if ($LASTEXITCODE -ne 0 -or -not $key) { throw 'Cannot load the public emulator credential.' }
    return "DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;AccountKey=$key;TableEndpoint=http://127.0.0.1:$Port/devstoreaccount1;"
}
function New-Secret { return [Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(48)) }
function Write-PrivateJson([string]$Path, $Value) { [IO.File]::WriteAllText($Path, ($Value | ConvertTo-Json -Depth 20), [Text.UTF8Encoding]::new($false)) }
function Wait-Port([int]$Port, [Diagnostics.Process]$Process=$null) {
    for ($i=0; $i -lt 100; $i++) {
        if ($Process -and $Process.HasExited) {throw 'The local process exited during startup. See its private instance logs.'}
        $tcp = [Net.Sockets.TcpClient]::new()
        try { $tcp.Connect('127.0.0.1',$Port); return } catch { Start-Sleep -Milliseconds 200 } finally { $tcp.Dispose() }
    }
    throw "Local process did not open port $Port. See the private .local/management logs."
}
function Get-ProcessRecord($Process, [string]$Marker) {
    return @{Id=$Process.Id; StartTimeUtc=$Process.StartTime.ToUniversalTime().ToString('O'); Marker=$Marker}
}
function Stop-RecordedProcess($Record) {
    if (-not $Record) { return }
    $process = Get-Process -Id $Record.Id -ErrorAction SilentlyContinue
    if (-not $process) { return }
    if ($process.StartTime.ToUniversalTime().Ticks -ne ([DateTimeOffset]$Record.StartTimeUtc).UtcDateTime.Ticks) { throw 'A recorded process ID has been reused; refusing to stop it.' }
    if ($IsWindows) {
        $command = (Get-CimInstance Win32_Process -Filter "ProcessId = $($process.Id)").CommandLine
        if (-not $command.Contains($Record.Marker, [StringComparison]::OrdinalIgnoreCase)) { throw 'Recorded process does not match this local stack.' }
    }
    Stop-Process -Id $process.Id
}
function Test-Listening([int]$Port) {
    $tcp = [Net.Sockets.TcpClient]::new()
    try { $tcp.Connect('127.0.0.1',$Port); return $true } catch { return $false } finally { $tcp.Dispose() }
}
