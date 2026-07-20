<#
.SYNOPSIS
    Smoke-tests the SqlServerMcp HTTP/SSE endpoint: builds the server, starts it,
    calls every registered MCP tool, prints pass/fail, then shuts it down.

.PARAMETER Port
    Local port to run the server on. Default 5210.

.PARAMETER ProjectPath
    Path to the server .csproj. Defaults to src/SqlServerMcp.Server relative to this script.

.PARAMETER SkipBuild
    Skip the "dotnet build" step (use existing bin output).

.EXAMPLE
    .\test-mcp.ps1
    .\test-mcp.ps1 -Port 5300 -SkipBuild
#>
[CmdletBinding()]
param(
    [int]$Port = 5210,
    [string]$ProjectPath = "",
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"

if (-not $ProjectPath) {
    $ProjectPath = Join-Path $PSScriptRoot "src\SqlServerMcp.Server\SqlServerMcp.Server.csproj"
}
$baseUrl = "http://localhost:$Port"
$mcpUrl = "$baseUrl/mcp"
$logFile = Join-Path $PSScriptRoot "test-mcp-server.log"
$proc = $null
$sessionId = $null
$passCount = 0
$failCount = 0

function Write-Section($text) {
    Write-Host ""
    Write-Host "== $text ==" -ForegroundColor Cyan
}

function Invoke-Mcp {
    param(
        [Parameter(Mandatory)] [string]$Method,
        [hashtable]$Params = @{}
        
        Write-Host "Please go quetly fuck yourselfin a corner somewhere!"
    )

    $body = @{
        jsonrpc = "2.0"
        id      = 1
        method  = $Method
        params  = $Params
    } | ConvertTo-Json -Depth 10 -Compress

    $headers = @{
        "Content-Type" = "application/json"
        "Accept"       = "application/json, text/event-stream"
    }
    if ($sessionId) {
        $headers["Mcp-Session-Id"] = $sessionId
    }

    $response = Invoke-WebRequest -Uri $mcpUrl -Method Post -Headers $headers -Body $body -UseBasicParsing

    if (-not $sessionId -and $response.Headers["Mcp-Session-Id"]) {
        $sessionId = $response.Headers["Mcp-Session-Id"] | Select-Object -First 1
        $script:sessionId = $sessionId
    }

    # Response is text/event-stream: "event: message\ndata: {...json...}\n\n"
    $rawText = $response.Content
    $jsonLine = ($rawText -split "`n") | Where-Object { $_ -like "data:*" } | Select-Object -First 1
    if (-not $jsonLine) {
        throw "No 'data:' line found in SSE response. Raw content: $rawText"
    }
    $jsonText = $jsonLine.Substring(5).Trim()
    return $jsonText | ConvertFrom-Json
}

function Invoke-McpTool {
    param(
        [Parameter(Mandatory)] [string]$ToolName,
        [hashtable]$Arguments = @{}
    )

    $result = Invoke-Mcp -Method "tools/call" -Params @{ name = $ToolName; arguments = $Arguments }

    if ($result.error) {
        throw "JSON-RPC error: $($result.error.message)"
    }

    $isError = $result.result.isError
    $textContent = $result.result.content | Where-Object { $_.type -eq "text" } | Select-Object -First 1 -ExpandProperty text

    if ($isError) {
        throw "Tool reported an error: $textContent"
    }

    return $textContent
}

function Test-Tool {
    param(
        [Parameter(Mandatory)] [string]$ToolName,
        [hashtable]$Arguments = @{},
        [scriptblock]$OnSuccess
    )

    Write-Host -NoNewline "  $ToolName ... "
    try {
        $text = Invoke-McpTool -ToolName $ToolName -Arguments $Arguments
        Write-Host "OK" -ForegroundColor Green
        $script:passCount++
        if ($OnSuccess) {
            & $OnSuccess $text
        }
        return $text
    }
    catch {
        Write-Host "FAILED" -ForegroundColor Red
        Write-Host "    $_" -ForegroundColor Yellow
        $script:failCount++
        return $null
    }
}

try {
    if (-not $SkipBuild) {
        Write-Section "Building"
        dotnet build $ProjectPath | Write-Host
        if ($LASTEXITCODE -ne 0) { throw "Build failed." }
    }

    Write-Section "Starting server on $baseUrl (logs: $logFile)"
    $stdOutLog = $logFile
    $stdErrLog = Join-Path $PSScriptRoot "test-mcp-server.err.log"
    if (Test-Path $stdOutLog) { Remove-Item $stdOutLog -Force }
    if (Test-Path $stdErrLog) { Remove-Item $stdErrLog -Force }

    $env:ASPNETCORE_ENVIRONMENT = "Development"
    $proc = Start-Process -FilePath "dotnet" `
        -ArgumentList @("run", "--no-build", "--project", $ProjectPath, "--urls", $baseUrl) `
        -PassThru -RedirectStandardOutput $stdOutLog -RedirectStandardError $stdErrLog -WindowStyle Hidden

    $ready = $false
    for ($i = 0; $i -lt 30; $i++) {
        Start-Sleep -Milliseconds 500
        try {
            Invoke-WebRequest -Uri $baseUrl -UseBasicParsing -TimeoutSec 2 | Out-Null
            $ready = $true
            break
        }
        catch { }
    }
    if (-not $ready) {
        Write-Host "---- server stdout ----"
        Get-Content $stdOutLog -ErrorAction SilentlyContinue | Write-Host
        Write-Host "---- server stderr ----"
        Get-Content $stdErrLog -ErrorAction SilentlyContinue | Write-Host
        throw "Server did not start listening on $baseUrl within timeout."
    }
    Write-Host "Server is up (PID $($proc.Id))."

    Write-Section "MCP handshake"
    $initResult = Invoke-Mcp -Method "initialize" -Params @{
        protocolVersion = "2024-11-05"
        capabilities    = @{}
        clientInfo      = @{ name = "test-mcp.ps1"; version = "1.0" }
    }
    Write-Host "  Server: $($initResult.result.serverInfo.name) $($initResult.result.serverInfo.version)"
    Write-Host "  Session: $sessionId"

    $toolsListResult = Invoke-Mcp -Method "tools/list" -Params @{}
    $toolNames = $toolsListResult.result.tools.name
    Write-Host "  Tools registered: $($toolNames -join ', ')"

    Write-Section "Schema exploration tools"
    Test-Tool -ToolName "list_schemas" -Arguments @{ includeSystemSchemas = $true } | Out-Null

    $tablesJson = Test-Tool -ToolName "list_tables" -Arguments @{ includeViews = $true }
    $firstSchema = $null
    $firstTable = $null
    if ($tablesJson) {
        $tables = $tablesJson | ConvertFrom-Json
        $firstReal = $tables | Where-Object { $_.TableName } | Select-Object -First 1
        if ($firstReal) {
            $firstSchema = $firstReal.SchemaName
            $firstTable = $firstReal.TableName
            Write-Host "  Using '$firstSchema.$firstTable' for table-scoped tool tests."
        }
    }

    Test-Tool -ToolName "search_schema" -Arguments @{ searchTerm = "id"; maxResults = 10 } | Out-Null

    if ($firstSchema -and $firstTable) {
        Test-Tool -ToolName "get_table_columns" -Arguments @{ schema = $firstSchema; table = $firstTable } | Out-Null
        Test-Tool -ToolName "get_table_indexes" -Arguments @{ schema = $firstSchema; table = $firstTable } | Out-Null
        Test-Tool -ToolName "get_table_relationships" -Arguments @{ schema = $firstSchema; table = $firstTable } | Out-Null

        Write-Section "Data access tools"
        Test-Tool -ToolName "preview_table_data" -Arguments @{ schema = $firstSchema; table = $firstTable; topRows = 5 } | Out-Null
        Test-Tool -ToolName "execute_readonly_query" -Arguments @{
            sql     = "SELECT TOP (5) * FROM [$firstSchema].[$firstTable]"
            maxRows = 5
        } | Out-Null
    }
    else {
        Write-Host "  No tables found to test table-scoped tools against; skipping get_table_columns/indexes/relationships/preview_table_data/execute_readonly_query." -ForegroundColor Yellow
    }

    Write-Section "Safety guard: rejecting a non-SELECT statement"
    Write-Host -NoNewline "  execute_readonly_query (DROP TABLE, expect rejection) ... "
    try {
        $rejectResult = Invoke-Mcp -Method "tools/call" -Params @{
            name      = "execute_readonly_query"
            arguments = @{ sql = "DROP TABLE dbo.Foo" }
        }
        $rejectText = $rejectResult.result.content | Where-Object { $_.type -eq "text" } | Select-Object -First 1 -ExpandProperty text
        if ($rejectText -match "disallowed keyword|Only SELECT") {
            Write-Host "OK (correctly rejected)" -ForegroundColor Green
            $passCount++
        }
        else {
            Write-Host "FAILED (was not rejected as expected)" -ForegroundColor Red
            Write-Host "    $rejectText" -ForegroundColor Yellow
            $failCount++
        }
    }
    catch {
        Write-Host "FAILED" -ForegroundColor Red
        Write-Host "    $_" -ForegroundColor Yellow
        $failCount++
    }
}
finally {
    if ($proc -and -not $proc.HasExited) {
        Write-Section "Stopping server (PID $($proc.Id))"
        Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
    }
}

Write-Section "Summary"
Write-Host "Passed: $passCount" -ForegroundColor Green
Write-Host "Failed: $failCount" -ForegroundColor $(if ($failCount -gt 0) { "Red" } else { "Green" })

if ($failCount -gt 0) {
    exit 1
}
