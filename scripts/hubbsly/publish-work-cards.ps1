[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [Parameter(Mandatory = $true)]
    [string]$CardsPath,

    [string]$Endpoint = $(if ($env:HUBBSLY_MCP_ENDPOINT) { $env:HUBBSLY_MCP_ENDPOINT } else { "https://prod-hubbsly-api-fsfchnhcgqc3d8aa.centralus-01.azurewebsites.net/api/mcp" }),

    [string]$ApiKeyEnvironmentVariable = "HUBBSLY_API_KEY",

    [string]$ToolName,

    [ValidateSet("SingleCard", "CardsArray", "RawDocument")]
    [string]$PayloadMode = "SingleCard",

    [string]$ArgumentName = "card",

    [switch]$Publish,

    [switch]$ListTools,

    [switch]$SkipToolDiscovery
)

$ErrorActionPreference = "Stop"

$requiredCardFields = @(
    "boardKey",
    "laneKey",
    "title",
    "description",
    "workType",
    "priority",
    "sourceSystem",
    "sourceKey",
    "sourceUrl",
    "sortOrder"
)

function Resolve-CardPath {
    param([string]$Path)

    $resolved = Resolve-Path -LiteralPath $Path -ErrorAction Stop
    return $resolved.ProviderPath
}

function Read-JsonFile {
    param([string]$Path)

    $raw = Get-Content -LiteralPath $Path -Raw
    if ([string]::IsNullOrWhiteSpace($raw)) {
        throw "Card payload file is empty: $Path"
    }

    return $raw | ConvertFrom-Json
}

function Get-CardItems {
    param([object]$Payload)

    if ($Payload -is [System.Array]) {
        return @($Payload)
    }

    $payloadProperties = @($Payload.PSObject.Properties.Name)
    foreach ($propertyName in @("cards", "Cards", "items", "Items")) {
        if ($payloadProperties -contains $propertyName) {
            return @($Payload.$propertyName)
        }
    }

    return @($Payload)
}

function Test-CardPayload {
    param([object[]]$Cards)

    if ($Cards.Count -eq 0) {
        throw "No cards were found in the payload."
    }

    $index = 0
    foreach ($card in $Cards) {
        $index++
        $propertyNames = @($card.PSObject.Properties.Name)

        foreach ($field in $requiredCardFields) {
            if ($propertyNames -notcontains $field) {
                throw "Card $index is missing required field '$field'."
            }

            $value = $card.$field
            if ($null -eq $value -or ($value -is [string] -and [string]::IsNullOrWhiteSpace($value))) {
                throw "Card $index has an empty value for required field '$field'."
            }
        }
    }
}

function New-McpHeaders {
    param([string]$ApiKey)

    return @{
        "Accept" = "application/json, text/event-stream"
        "Content-Type" = "application/json"
        "X-API-Key" = $ApiKey
        "Authorization" = "ApiKey $ApiKey"
    }
}

function Invoke-McpRequest {
    param(
        [string]$Uri,
        [hashtable]$Headers,
        [string]$Method,
        [object]$Params
    )

    if (-not $script:RequestId) {
        $script:RequestId = 0
    }

    $script:RequestId++

    $body = @{
        jsonrpc = "2.0"
        id = $script:RequestId
        method = $Method
        params = $Params
    } | ConvertTo-Json -Depth 50

    $response = Invoke-RestMethod -Uri $Uri -Method Post -Headers $Headers -Body $body -ContentType "application/json"
    if ($response.error) {
        $message = $response.error.message
        if ([string]::IsNullOrWhiteSpace($message)) {
            $message = ($response.error | ConvertTo-Json -Depth 20)
        }

        throw "MCP method '$Method' failed: $message"
    }

    return $response.result
}

function Invoke-McpNotification {
    param(
        [string]$Uri,
        [hashtable]$Headers,
        [string]$Method,
        [object]$Params
    )

    $body = @{
        jsonrpc = "2.0"
        method = $Method
        params = $Params
    } | ConvertTo-Json -Depth 20

    try {
        Invoke-RestMethod -Uri $Uri -Method Post -Headers $Headers -Body $body -ContentType "application/json" | Out-Null
    }
    catch {
        Write-Verbose "MCP notification '$Method' was not acknowledged: $($_.Exception.Message)"
    }
}

function Initialize-McpSession {
    param(
        [string]$Uri,
        [hashtable]$Headers
    )

    Invoke-McpRequest `
        -Uri $Uri `
        -Headers $Headers `
        -Method "initialize" `
        -Params @{
            protocolVersion = "2025-03-26"
            capabilities = @{}
            clientInfo = @{
                name = "ibeam-hubbsly-card-publisher"
                version = "1.0.0"
            }
        } | Out-Null

    Invoke-McpNotification -Uri $Uri -Headers $Headers -Method "notifications/initialized" -Params @{}
}

function Get-HubbslyTools {
    param(
        [string]$Uri,
        [hashtable]$Headers
    )

    $result = Invoke-McpRequest -Uri $Uri -Headers $Headers -Method "tools/list" -Params @{}
    return @($result.tools)
}

function Resolve-WorkCardToolName {
    param(
        [object[]]$Tools,
        [string]$ExplicitToolName
    )

    if (-not [string]::IsNullOrWhiteSpace($ExplicitToolName)) {
        return $ExplicitToolName
    }

    $candidates = @(
        $Tools |
            Where-Object {
                $_.name -match "(?i)card" -and
                $_.name -match "(?i)(work|hubbsly)" -and
                $_.name -match "(?i)(create|upsert|import|publish|sync|save)"
            } |
            Select-Object -ExpandProperty name
    )

    if ($candidates.Count -eq 1) {
        return $candidates[0]
    }

    if ($candidates.Count -gt 1) {
        $names = $candidates -join ", "
        throw "Multiple likely work-card tools were found. Re-run with -ToolName. Candidates: $names"
    }

    $available = @($Tools | Select-Object -ExpandProperty name) -join ", "
    throw "No likely work-card tool was found. Re-run with -ListTools or pass -ToolName. Available tools: $available"
}

function New-ToolArguments {
    param(
        [string]$Mode,
        [string]$Name,
        [object]$Payload
    )

    if ([string]::IsNullOrWhiteSpace($Name)) {
        throw "ArgumentName cannot be empty."
    }

    return @{ $Name = $Payload }
}

$resolvedCardsPath = Resolve-CardPath -Path $CardsPath
$payload = Read-JsonFile -Path $resolvedCardsPath
$cards = @(Get-CardItems -Payload $payload)
Test-CardPayload -Cards $cards

Write-Host "Validated $($cards.Count) card(s) from $resolvedCardsPath"
$cards |
    Select-Object sourceKey, laneKey, priority, title |
    Format-Table -AutoSize |
    Out-String |
    Write-Host

if (-not $Publish -and -not $ListTools) {
    Write-Host "Dry run complete. Add -Publish to call Hubbsly."
    return
}

$apiKey = [Environment]::GetEnvironmentVariable($ApiKeyEnvironmentVariable, "Process")
if ([string]::IsNullOrWhiteSpace($apiKey)) {
    $apiKey = [Environment]::GetEnvironmentVariable($ApiKeyEnvironmentVariable, "User")
}

if ([string]::IsNullOrWhiteSpace($apiKey)) {
    throw "Missing API key. Set $ApiKeyEnvironmentVariable in the process or user environment."
}

$headers = New-McpHeaders -ApiKey $apiKey
Initialize-McpSession -Uri $Endpoint -Headers $headers

$tools = @()
if (-not $SkipToolDiscovery -or $ListTools -or [string]::IsNullOrWhiteSpace($ToolName)) {
    $tools = @(Get-HubbslyTools -Uri $Endpoint -Headers $headers)
}

if ($ListTools) {
    $tools |
        Select-Object name, description |
        Format-Table -AutoSize |
        Out-String |
        Write-Host

    if (-not $Publish) {
        return
    }
}

$resolvedToolName = Resolve-WorkCardToolName -Tools $tools -ExplicitToolName $ToolName
Write-Host "Publishing with MCP tool '$resolvedToolName'."

switch ($PayloadMode) {
    "SingleCard" {
        foreach ($card in $cards) {
            if ($PSCmdlet.ShouldProcess($card.sourceKey, "Publish Hubbsly work card")) {
                $arguments = New-ToolArguments -Mode $PayloadMode -Name $ArgumentName -Payload $card
                Invoke-McpRequest -Uri $Endpoint -Headers $headers -Method "tools/call" -Params @{
                    name = $resolvedToolName
                    arguments = $arguments
                } | Out-Null

                Write-Host "Published $($card.sourceKey)"
            }
        }
    }
    "CardsArray" {
        if ($PSCmdlet.ShouldProcess($resolvedCardsPath, "Publish Hubbsly work card batch")) {
            $arguments = New-ToolArguments -Mode $PayloadMode -Name $ArgumentName -Payload $cards
            Invoke-McpRequest -Uri $Endpoint -Headers $headers -Method "tools/call" -Params @{
                name = $resolvedToolName
                arguments = $arguments
            } | Out-Null

            Write-Host "Published $($cards.Count) card(s)."
        }
    }
    "RawDocument" {
        if ($PSCmdlet.ShouldProcess($resolvedCardsPath, "Publish Hubbsly raw card document")) {
            $arguments = New-ToolArguments -Mode $PayloadMode -Name $ArgumentName -Payload $payload
            Invoke-McpRequest -Uri $Endpoint -Headers $headers -Method "tools/call" -Params @{
                name = $resolvedToolName
                arguments = $arguments
            } | Out-Null

            Write-Host "Published raw card document."
        }
    }
}
