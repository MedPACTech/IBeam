param(
    [string] $ConnectionString = $env:IBEAM_IDENTITY_AZURE_TABLE_CONNECTION_STRING,
    [string] $TablePrefix = "IBeam",
    [string] $UserTableName = "IdentityUsers",
    [string] $TenantsTableName = "Tenants",
    [string] $TenantUsersTableName = "TenantUsers",
    [string] $UserTenantsTableName = "UserTenants",
    [string] $OtpChallengesTableName = "OtpChallenges",
    [string] $AuthAttemptsTableName = "AuthAttempts",
    [string] $ApiCredentialsTableName = "ApiCredentials",
    [int] $SampleSize = 10,
    [string] $OutputPath,
    [switch] $AsJson,
    [switch] $Help
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = "Stop"

if ($Help) {
    @"
Read-only IBeam Identity Azure Table schema/data migration readout.

Usage:
  .\scripts\identity\read-identity-schema-drift.ps1 -ConnectionString '<storage-connection-string>'
  .\scripts\identity\read-identity-schema-drift.ps1 -ConnectionString '<storage-connection-string>' -TablePrefix IBeam -OutputPath .\artifacts\identity-readout.json
  `$env:IBEAM_IDENTITY_AZURE_TABLE_CONNECTION_STRING='<storage-connection-string>'
  .\scripts\identity\read-identity-schema-drift.ps1

Notes:
  - This script is read-only. It does not update, delete, or migrate rows.
  - TablePrefix defaults to IBeam.
  - UserTableName defaults to IdentityUsers to match current IBeam deployments.
  - Use -AsJson to print machine-readable JSON to stdout.
"@ | Write-Output
    exit 0
}

if ([string]::IsNullOrWhiteSpace($ConnectionString)) {
    throw "ConnectionString is required. Pass -ConnectionString or set IBEAM_IDENTITY_AZURE_TABLE_CONNECTION_STRING."
}

if ($SampleSize -lt 0) {
    throw "SampleSize must be >= 0."
}

function ConvertTo-ConnectionInfo {
    param([string] $Value)

    $parts = @{}
    foreach ($segment in $Value.Split(';')) {
        if ([string]::IsNullOrWhiteSpace($segment)) {
            continue
        }

        $pair = $segment.Split('=', 2)
        if ($pair.Count -eq 2) {
            $parts[$pair[0]] = $pair[1]
        }
    }

    if ($parts.ContainsKey("UseDevelopmentStorage") -and $parts["UseDevelopmentStorage"] -ieq "true") {
        throw "UseDevelopmentStorage=true shorthand is not supported by this readout. Pass an expanded connection string with AccountName, AccountKey, and TableEndpoint."
    }

    if (!$parts.ContainsKey("AccountName") -or !$parts.ContainsKey("AccountKey")) {
        throw "ConnectionString must include AccountName and AccountKey, or UseDevelopmentStorage=true."
    }

    $endpoint = $null
    if ($parts.ContainsKey("TableEndpoint")) {
        $endpoint = $parts["TableEndpoint"]
    }
    else {
        $suffix = if ($parts.ContainsKey("EndpointSuffix")) { $parts["EndpointSuffix"] } else { "core.windows.net" }
        $protocol = if ($parts.ContainsKey("DefaultEndpointsProtocol")) { $parts["DefaultEndpointsProtocol"] } else { "https" }
        $endpoint = "${protocol}://$($parts["AccountName"]).table.$suffix/"
    }

    [pscustomobject]@{
        AccountName = $parts["AccountName"]
        AccountKey = $parts["AccountKey"]
        TableEndpoint = $endpoint.TrimEnd('/') + "/"
    }
}

function New-TableAuthorizationHeader {
    param(
        [pscustomobject] $Connection,
        [uri] $Uri,
        [string] $Date
    )

    $canonicalizedResource = "/$($Connection.AccountName)$($Uri.AbsolutePath)"
    $stringToSign = "$Date`n$canonicalizedResource"
    $keyBytes = [Convert]::FromBase64String($Connection.AccountKey)
    $hmac = New-Object System.Security.Cryptography.HMACSHA256
    $hmac.Key = $keyBytes
    $signatureBytes = $hmac.ComputeHash([Text.Encoding]::UTF8.GetBytes($stringToSign))
    $signature = [Convert]::ToBase64String($signatureBytes)
    "SharedKeyLite $($Connection.AccountName):$signature"
}

function Invoke-TableGet {
    param(
        [pscustomobject] $Connection,
        [string] $RelativePathAndQuery
    )

    $uri = [uri]::new($Connection.TableEndpoint + $RelativePathAndQuery)
    $date = [DateTime]::UtcNow.ToString("R", [Globalization.CultureInfo]::InvariantCulture)
    $headers = @{
        "x-ms-date" = $date
        "x-ms-version" = "2021-08-06"
        "Accept" = "application/json;odata=nometadata"
        "Authorization" = New-TableAuthorizationHeader -Connection $Connection -Uri $uri -Date $date
    }

    Invoke-RestMethod -Method Get -Uri $uri.AbsoluteUri -Headers $headers
}

function Read-TableEntities {
    param(
        [pscustomobject] $Connection,
        [string] $TableName
    )

    $entities = New-Object System.Collections.Generic.List[object]
    $nextPartitionKey = $null
    $nextRowKey = $null

    do {
        $path = "$TableName()?`$top=1000"
        if (![string]::IsNullOrWhiteSpace($nextPartitionKey)) {
            $path += "&NextPartitionKey=$([uri]::EscapeDataString($nextPartitionKey))"
        }
        if (![string]::IsNullOrWhiteSpace($nextRowKey)) {
            $path += "&NextRowKey=$([uri]::EscapeDataString($nextRowKey))"
        }

        $uri = [uri]::new($Connection.TableEndpoint + $path)
        $date = [DateTime]::UtcNow.ToString("R", [Globalization.CultureInfo]::InvariantCulture)
        $headers = @{
            "x-ms-date" = $date
            "x-ms-version" = "2021-08-06"
            "Accept" = "application/json;odata=nometadata"
            "Authorization" = New-TableAuthorizationHeader -Connection $Connection -Uri $uri -Date $date
        }

        try {
            $response = Invoke-WebRequest -Method Get -Uri $uri.AbsoluteUri -Headers $headers
        }
        catch {
            if ($_.Exception.Response -and $_.Exception.Response.StatusCode.value__ -eq 404) {
                return [pscustomobject]@{
                    Exists = $false
                    Entities = @()
                    Error = $null
                }
            }

            throw
        }

        $body = $response.Content | ConvertFrom-Json
        if ($body.value) {
            foreach ($entity in $body.value) {
                $entities.Add($entity)
            }
        }

        $nextPartitionKey = $response.Headers["x-ms-continuation-NextPartitionKey"]
        $nextRowKey = $response.Headers["x-ms-continuation-NextRowKey"]
    } while (![string]::IsNullOrWhiteSpace($nextPartitionKey) -or ![string]::IsNullOrWhiteSpace($nextRowKey))

    [pscustomobject]@{
        Exists = $true
        Entities = $entities.ToArray()
        Error = $null
    }
}

function Get-PropertyNames {
    param([object] $Entity)
    $Entity.PSObject.Properties |
        Where-Object { $_.Name -notlike "odata.*" } |
        Select-Object -ExpandProperty Name
}

function Has-Property {
    param([object] $Entity, [string] $Name)
    [bool]($Entity.PSObject.Properties.Name -contains $Name)
}

function Get-PropertyValue {
    param([object] $Entity, [string] $Name)
    $prop = $Entity.PSObject.Properties[$Name]
    if ($null -eq $prop) {
        return $null
    }
    $prop.Value
}

function Is-Blank {
    param([object] $Value)
    $null -eq $Value -or [string]::IsNullOrWhiteSpace([string]$Value)
}

function New-Sample {
    param([object] $Entity, [string[]] $Fields)

    $sample = [ordered]@{
        PartitionKey = Get-PropertyValue $Entity "PartitionKey"
        RowKey = Get-PropertyValue $Entity "RowKey"
    }

    foreach ($field in $Fields) {
        $sample[$field] = Get-PropertyValue $Entity $field
    }

    [pscustomobject]$sample
}

function Measure-DeprecatedColumns {
    param(
        [string] $TableName,
        [object[]] $Entities,
        [string[]] $Columns,
        [int] $SampleSize
    )

    $findings = @()
    foreach ($column in $Columns) {
        $matches = @($Entities | Where-Object { Has-Property $_ $column })
        if ($matches.Count -gt 0) {
            $findings += [pscustomobject]@{
                Category = "DeprecatedColumn"
                Severity = "Review"
                Table = $TableName
                Field = $column
                Count = $matches.Count
                Description = "Column exists on stored rows but is no longer represented in first-party entities."
                Samples = @($matches | Select-Object -First $SampleSize | ForEach-Object { New-Sample $_ @($column) })
            }
        }
    }
    $findings
}

function Measure-BackfillGaps {
    param(
        [string] $TableName,
        [object[]] $Entities,
        [int] $SampleSize
    )

    $missingDisplayName = @($Entities | Where-Object { Is-Blank (Get-PropertyValue $_ "UserDisplayName") })
    $missingEmail = @($Entities | Where-Object { Is-Blank (Get-PropertyValue $_ "Email") })

    @(
        [pscustomobject]@{
            Category = "BackfillGap"
            Severity = "Examine"
            Table = $TableName
            Field = "UserDisplayName"
            Count = $missingDisplayName.Count
            Description = "Tenant-user membership rows missing denormalized display name."
            Samples = @($missingDisplayName | Select-Object -First $SampleSize | ForEach-Object { New-Sample $_ @("UserId", "UserDisplayName") })
        },
        [pscustomobject]@{
            Category = "BackfillGap"
            Severity = "Examine"
            Table = $TableName
            Field = "Email"
            Count = $missingEmail.Count
            Description = "Tenant-user membership rows missing denormalized email."
            Samples = @($missingEmail | Select-Object -First $SampleSize | ForEach-Object { New-Sample $_ @("UserId", "Email") })
        }
    ) | Where-Object { $_.Count -gt 0 }
}

function Measure-RoleIdGaps {
    param(
        [string] $TableName,
        [object[]] $Entities,
        [int] $SampleSize
    )

    $matches = @($Entities | Where-Object {
        -not (Is-Blank (Get-PropertyValue $_ "RolesCsv")) -and
        (Is-Blank (Get-PropertyValue $_ "RoleIdsCsv"))
    })

    if ($matches.Count -eq 0) {
        return @()
    }

    @([pscustomobject]@{
        Category = "RoleIdGap"
        Severity = "Examine"
        Table = $TableName
        Field = "RoleIdsCsv"
        Count = $matches.Count
        Description = "Rows have role names but no role ids; these need review before a role-id-only migration."
        Samples = @($matches | Select-Object -First $SampleSize | ForEach-Object { New-Sample $_ @("UserId", "TenantId", "RolesCsv", "RoleIdsCsv") })
    })
}

function Measure-RoleNameLegacyUsage {
    param(
        [string] $TableName,
        [object[]] $Entities,
        [int] $SampleSize
    )

    $matches = @($Entities | Where-Object { -not (Is-Blank (Get-PropertyValue $_ "RoleNamesCsv")) })
    if ($matches.Count -eq 0) {
        return @()
    }

    @([pscustomobject]@{
        Category = "LegacyRoleNames"
        Severity = "Informational"
        Table = $TableName
        Field = "RoleNamesCsv"
        Count = $matches.Count
        Description = "Rows still use role-name grants. This is active compatibility data, but should be reviewed before moving fully to role ids."
        Samples = @($matches | Select-Object -First $SampleSize | ForEach-Object { New-Sample $_ @("RoleNamesCsv", "RoleIdsCsv") })
    })
}

function Measure-UnknownColumns {
    param(
        [string] $TableName,
        [object[]] $Entities,
        [string[]] $ExpectedColumns,
        [int] $SampleSize
    )

    $expected = New-Object "System.Collections.Generic.HashSet[string]" ([StringComparer]::OrdinalIgnoreCase)
    foreach ($column in $ExpectedColumns) {
        [void]$expected.Add($column)
    }

    $unknown = @{}
    foreach ($entity in $Entities) {
        foreach ($name in (Get-PropertyNames $entity)) {
            if (!$expected.Contains($name)) {
                if (!$unknown.ContainsKey($name)) {
                    $unknown[$name] = New-Object System.Collections.Generic.List[object]
                }
                $unknown[$name].Add($entity)
            }
        }
    }

    $findings = @()
    foreach ($name in ($unknown.Keys | Sort-Object)) {
        $matches = $unknown[$name].ToArray()
        $findings += [pscustomobject]@{
            Category = "UnknownColumn"
            Severity = "Review"
            Table = $TableName
            Field = $name
            Count = $matches.Count
            Description = "Column was found in storage but is not part of the expected first-party schema list used by this readout."
            Samples = @($matches | Select-Object -First $SampleSize | ForEach-Object { New-Sample $_ @($name) })
        }
    }
    $findings
}

$connection = ConvertTo-ConnectionInfo $ConnectionString

$tables = [ordered]@{
    Users = "$TablePrefix$UserTableName"
    Tenants = "$TablePrefix$TenantsTableName"
    TenantUsers = "$TablePrefix$TenantUsersTableName"
    UserTenants = "$TablePrefix$UserTenantsTableName"
    OtpChallenges = "$TablePrefix$OtpChallengesTableName"
    AuthAttempts = "$TablePrefix$AuthAttemptsTableName"
    ApiCredentials = "$TablePrefix$ApiCredentialsTableName"
}

$expectedColumns = @{
    Tenants = @("PartitionKey", "RowKey", "Timestamp", "Name", "NormalizedName", "Status", "CreatedAt", "UpdatedAt")
    TenantUsers = @("PartitionKey", "RowKey", "Timestamp", "TenantId", "UserId", "Status", "CreatedAt", "DisabledAt", "DisabledReason", "UserDisplayName", "Email", "RolesCsv", "RoleIdsCsv")
    UserTenants = @("PartitionKey", "RowKey", "Timestamp", "UserId", "TenantId", "Status", "CreatedAt", "DisabledAt", "DisabledReason", "TenantDisplayName", "RolesCsv", "RoleIdsCsv", "IsDefault", "LastSelectedAt")
    OtpChallenges = @("PartitionKey", "RowKey", "Timestamp", "ChallengeId", "TenantId", "Purpose", "Channel", "Destination", "CodeHash", "CreatedAt", "ExpiresAt", "IsConsumed", "ConsumedAt", "AttemptCount", "LastAttemptAt", "VerificationToken", "VerificationTokenExpiresAt")
    AuthAttempts = @("PartitionKey", "RowKey", "Timestamp", "Method", "Identifier", "FailedAttempts", "LockedUntilUtc", "LastFailedAtUtc", "LastSucceededAtUtc", "LastFailedIp", "LastSucceededIp", "LastUserAgent", "LastDeviceId", "LastCountry", "LastRegion", "LastCity", "LastCorrelationId", "LastUnlockedAtUtc", "UnlockedByUserId", "UnlockReason", "MetadataJson")
    ApiCredentials = @("PartitionKey", "RowKey", "Timestamp", "TenantId", "CredentialId", "DisplayName", "AgentKey", "KeyPrefix", "SecretHash", "RoleNamesCsv", "RoleIdsCsv", "CreatedUtc", "CreatedByUserId", "ExpiresUtc", "LastUsedUtc", "LastUsedIp", "RotatedUtc", "RevokedUtc", "RevokedByUserId", "RevocationReason", "IsDeleted")
}

$loaded = @{}
$tableSummaries = @()

foreach ($key in $tables.Keys) {
    $tableName = $tables[$key]
    if (!$AsJson) {
        Write-Host "Reading $tableName..." -ForegroundColor Cyan
    }
    $read = Read-TableEntities -Connection $connection -TableName $tableName
    $loaded[$key] = $read
    $tableSummaries += [pscustomobject]@{
        Name = $tableName
        LogicalName = $key
        Exists = $read.Exists
        RowCount = @($read.Entities).Count
    }
}

$findings = @()

if ($loaded.TenantUsers.Exists) {
    $findings += Measure-DeprecatedColumns $tables.TenantUsers $loaded.TenantUsers.Entities @("PermissionsJson") $SampleSize
    $findings += Measure-BackfillGaps $tables.TenantUsers $loaded.TenantUsers.Entities $SampleSize
    $findings += Measure-RoleIdGaps $tables.TenantUsers $loaded.TenantUsers.Entities $SampleSize
    $findings += Measure-UnknownColumns $tables.TenantUsers $loaded.TenantUsers.Entities $expectedColumns.TenantUsers $SampleSize
}

if ($loaded.UserTenants.Exists) {
    $findings += Measure-DeprecatedColumns $tables.UserTenants $loaded.UserTenants.Entities @("PermissionsJson", "UserDisplayName") $SampleSize
    $findings += Measure-RoleIdGaps $tables.UserTenants $loaded.UserTenants.Entities $SampleSize
    $findings += Measure-UnknownColumns $tables.UserTenants $loaded.UserTenants.Entities $expectedColumns.UserTenants $SampleSize
}

if ($loaded.Tenants.Exists) {
    $findings += Measure-DeprecatedColumns $tables.Tenants $loaded.Tenants.Entities @("OwnerUserId") $SampleSize
    $findings += Measure-UnknownColumns $tables.Tenants $loaded.Tenants.Entities $expectedColumns.Tenants $SampleSize
}

if ($loaded.OtpChallenges.Exists) {
    $findings += Measure-DeprecatedColumns $tables.OtpChallenges $loaded.OtpChallenges.Entities @("CodeNonce", "ResendAvailableAt", "DestinationHash") $SampleSize
    $findings += Measure-UnknownColumns $tables.OtpChallenges $loaded.OtpChallenges.Entities $expectedColumns.OtpChallenges $SampleSize
}

if ($loaded.AuthAttempts.Exists) {
    $findings += Measure-UnknownColumns $tables.AuthAttempts $loaded.AuthAttempts.Entities $expectedColumns.AuthAttempts $SampleSize
}

if ($loaded.ApiCredentials.Exists) {
    $findings += Measure-RoleNameLegacyUsage $tables.ApiCredentials $loaded.ApiCredentials.Entities $SampleSize
    $findings += Measure-UnknownColumns $tables.ApiCredentials $loaded.ApiCredentials.Entities $expectedColumns.ApiCredentials $SampleSize
}

$report = [pscustomobject]@{
    GeneratedAtUtc = [DateTimeOffset]::UtcNow.ToString("O")
    Mode = "ReadOnly"
    TablePrefix = $TablePrefix
    Tables = $tableSummaries
    Findings = @($findings | Sort-Object Table, Category, Field)
}

if (![string]::IsNullOrWhiteSpace($OutputPath)) {
    $directory = Split-Path -Parent $OutputPath
    if (![string]::IsNullOrWhiteSpace($directory) -and !(Test-Path -LiteralPath $directory)) {
        New-Item -ItemType Directory -Path $directory | Out-Null
    }

    $report | ConvertTo-Json -Depth 12 | Set-Content -Path $OutputPath -Encoding UTF8
    if (!$AsJson) {
        Write-Host "Wrote readout to $OutputPath" -ForegroundColor Green
    }
}

if ($AsJson) {
    $report | ConvertTo-Json -Depth 12
    exit 0
}

Write-Host ""
Write-Host "IBeam Identity schema/data readout" -ForegroundColor Green
Write-Host "Generated: $($report.GeneratedAtUtc)"
Write-Host "Mode:      $($report.Mode)"
Write-Host "Prefix:    $($report.TablePrefix)"
Write-Host ""
Write-Host "Tables" -ForegroundColor Green
foreach ($summary in $report.Tables) {
    $status = if ($summary.Exists) { "exists" } else { "missing" }
    Write-Host ("  {0,-34} {1,8} rows  {2}" -f $summary.Name, $summary.RowCount, $status)
}

Write-Host ""
Write-Host "Findings" -ForegroundColor Green
if ($report.Findings.Count -eq 0) {
    Write-Host "  No findings."
}
else {
    foreach ($finding in $report.Findings) {
        Write-Host ("  [{0}] {1}.{2}: {3} row(s) - {4}" -f $finding.Severity, $finding.Table, $finding.Field, $finding.Count, $finding.Description)
        foreach ($sample in @($finding.Samples | Select-Object -First $SampleSize)) {
            Write-Host ("    sample PK={0} RK={1}" -f $sample.PartitionKey, $sample.RowKey)
        }
    }
}
