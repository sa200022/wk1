# Smoke test for Webhook Delivery System (local compose stack)
# Steps:
#   1) Create subscription
#   2) Publish event
#   3) Poll DB for saga/job status
# Notes:
#   - If Router offset is already >= the new event id, Router won't route that event again.
#     Use -ResetRouterOffset (dev/demo only) or re-run to generate a newer event id.
#
# Prerequisites: postgres running (localhost:5432, user postgres, db webhook_delivery)
# Services running: subscription-api (5001), event-ingestion (5002)

param(
    [string]$EventType = "user.created",
    [string]$CallbackUrl = "https://httpbin.org/status/200",
    [string]$DbUser = "postgres",
    [string]$DbPassword = $env:POSTGRES_PASSWORD,
    [string]$DbName = "webhook_delivery",
    [string]$DbHost = "localhost",
    [int]$WaitSeconds = 15,
    [string]$ApiKey = "",
    [switch]$ResetRouterOffset
)

$ErrorActionPreference = "Stop"

function Import-LocalSecrets {
    $secretsScript = Join-Path $PSScriptRoot "secrets.local.ps1"
    if (Test-Path $secretsScript) {
        . $secretsScript
    }

    $dotenv = Join-Path $PSScriptRoot ".env.local"
    if (Test-Path $dotenv) {
        Get-Content $dotenv | ForEach-Object {
            $line = $_.Trim()
            if (-not $line -or $line.StartsWith("#")) { return }
            $parts = $line.Split("=", 2)
            if ($parts.Count -ne 2) { return }
            $key = $parts[0].Trim()
            $value = $parts[1].Trim().Trim("'").Trim('"')
            if ($key) { [Environment]::SetEnvironmentVariable($key, $value, "Process") }
        }
    }
}

function Test-Health {
    param(
        [string]$Name,
        [string]$Url
    )

    try {
        $res = Invoke-WebRequest -Uri $Url -UseBasicParsing -TimeoutSec 2
        if ($res.StatusCode -ge 200 -and $res.StatusCode -lt 300) {
            Write-Host ("{0}: OK ({1})" -f $Name, $Url) -ForegroundColor Green
            return $true
        }

        Write-Host ("{0}: HTTP {1} ({2})" -f $Name, $res.StatusCode, $Url) -ForegroundColor Yellow
        return $false
    }
    catch {
        Write-Host ("{0}: NOT RUNNING ({1})" -f $Name, $Url) -ForegroundColor Red
        return $false
    }
}

function Invoke-Psql {
    param([string]$Sql)

    $output = & $psql @connArgs -c $Sql 2>&1
    $exitCode = $LASTEXITCODE
    if ($exitCode -ne 0) {
        $message = ($output | Out-String).Trim()
        if (-not $message) { $message = "psql failed with exit code $exitCode" }
        throw $message
    }
    return $output
}

function Invoke-PsqlScalar {
    param([string]$Sql)

    $output = Invoke-Psql -Sql $Sql
    if (-not $output) { return "" }
    return ($output | Out-String).Trim()
}

function Require-Command {
    param([string]$Name)
    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
        Write-Error "Required command '$Name' not found. Please install it."
        exit 1
    }
}

function Resolve-PsqlPath {
    $cmd = Get-Command "psql.exe" -ErrorAction SilentlyContinue
    if (-not $cmd) { $cmd = Get-Command "psql" -ErrorAction SilentlyContinue }
    if ($cmd -and $cmd.CommandType -eq "Application" -and $cmd.Source -and (Test-Path $cmd.Source)) {
        return $cmd.Source
    }

    $roots = @(
        (Join-Path $env:ProgramFiles "PostgreSQL"),
        (Join-Path ${env:ProgramFiles(x86)} "PostgreSQL")
    ) | Where-Object { $_ -and (Test-Path $_) }

    $candidates = @()
    foreach ($root in $roots) {
        $candidates += Get-ChildItem -Path $root -Directory -ErrorAction SilentlyContinue |
            ForEach-Object { Join-Path $_.FullName "bin\\psql.exe" }
    }

    $candidates = $candidates | Where-Object { Test-Path $_ }
    if ($candidates.Count -gt 0) {
        $picked = ($candidates | Sort-Object -Descending | Select-Object -First 1)
        if ($picked -and (Test-Path $picked)) { return $picked }
    }

    throw "psql not found. Install PostgreSQL client tools or add PostgreSQL\\bin to PATH."
}

Import-LocalSecrets

$psql = Resolve-PsqlPath

if (-not $DbPassword -or $DbPassword.Trim() -eq "") {
    $DbPassword = "dev_password_postgres"
}

$env:PGPASSWORD = $DbPassword
$connArgs = @("-h", $DbHost, "-U", $DbUser, "-d", $DbName, "-t", "-A")

try {
    Test-Health -Name "Router" -Url "http://localhost:6001/health/" | Out-Null
    Test-Health -Name "Orchestrator" -Url "http://localhost:6002/health/" | Out-Null
    Test-Health -Name "Worker" -Url "http://localhost:6003/health/" | Out-Null

    try {
        Invoke-Psql -Sql "INSERT INTO router_offsets (id, last_processed_event_id) VALUES (1, 0) ON CONFLICT (id) DO NOTHING;"
    }
    catch {
        throw "Database schema not ready. Please run start-all-services.ps1 (without -SkipDbSetup) once. Details: $($_.Exception.Message)"
    }

    $routerOffsetBefore = Invoke-PsqlScalar -Sql "SELECT last_processed_event_id FROM router_offsets WHERE id = 1;"
    Write-Host "Router offset (before): $routerOffsetBefore" -ForegroundColor Cyan

    if ($ResetRouterOffset) {
        Write-Host "Resetting router offset to 0..." -ForegroundColor Yellow
        Invoke-Psql -Sql "UPDATE router_offsets SET last_processed_event_id = 0 WHERE id = 1;"
        $routerOffsetBefore = Invoke-PsqlScalar -Sql "SELECT last_processed_event_id FROM router_offsets WHERE id = 1;"
        Write-Host "Router offset (after reset): $routerOffsetBefore" -ForegroundColor Cyan
    }

$subscriptionPayload = @{
    eventType   = $EventType
    callbackUrl = $CallbackUrl
} | ConvertTo-Json

Write-Host "Creating subscription..." -ForegroundColor Cyan
$subHeaders = @{}
if ($ApiKey -and $ApiKey.Trim() -ne "") { $subHeaders["X-Api-Key"] = $ApiKey }
$subResponse = Invoke-RestMethod -Method Post -Uri "http://localhost:5001/api/subscriptions" -Headers $subHeaders -ContentType "application/json" -Body $subscriptionPayload
$subscriptionId = $subResponse.id
Write-Host "Subscription created: id=$subscriptionId" -ForegroundColor Green

Write-Host "Verifying subscription..." -ForegroundColor Cyan
$verifyResponse = Invoke-RestMethod -Method Post -Uri "http://localhost:5001/api/subscriptions/$subscriptionId/verify" -Headers $subHeaders -ContentType "application/json"
Write-Host "Subscription verified: verified=$($verifyResponse.verified)" -ForegroundColor Green

$eventPayload = @{
    eventType       = $EventType
    externalEventId = "smoke-" + [guid]::NewGuid().ToString("N")
    payload         = @{ userId = 1; name = "Smoke Test" }
} | ConvertTo-Json

Write-Host "Publishing event..." -ForegroundColor Cyan
$evtHeaders = @{}
if ($ApiKey -and $ApiKey.Trim() -ne "") { $evtHeaders["X-Api-Key"] = $ApiKey }
$evtResponse = Invoke-RestMethod -Method Post -Uri "http://localhost:5002/api/events" -Headers $evtHeaders -ContentType "application/json" -Body $eventPayload
$eventId = $evtResponse.id
Write-Host "Event published: id=$eventId" -ForegroundColor Green

if ($WaitSeconds -lt 1) { $WaitSeconds = 1 }
Write-Host "Waiting for processing (${WaitSeconds}s)..." -ForegroundColor Yellow
Start-Sleep -Seconds $WaitSeconds

Write-Host "Saga status counts:" -ForegroundColor Cyan
Invoke-Psql -Sql "SELECT status, COUNT(*) FROM webhook_delivery_sagas GROUP BY status;"
Write-Host "Total sagas:" -ForegroundColor Cyan
Invoke-Psql -Sql "SELECT COUNT(*) FROM webhook_delivery_sagas;"

Write-Host "Job status counts:" -ForegroundColor Cyan
Invoke-Psql -Sql "SELECT status, COUNT(*) FROM webhook_delivery_jobs GROUP BY status;"
Write-Host "Total jobs:" -ForegroundColor Cyan
Invoke-Psql -Sql "SELECT COUNT(*) FROM webhook_delivery_jobs;"

Write-Host "Total events:" -ForegroundColor Cyan
Invoke-Psql -Sql "SELECT COUNT(*) FROM events;"
Write-Host "Max event id:" -ForegroundColor Cyan
Invoke-Psql -Sql "SELECT COALESCE(MAX(id), 0) FROM events;"

Write-Host "Total subscriptions (verified):" -ForegroundColor Cyan
Invoke-Psql -Sql "SELECT COUNT(*) FROM subscriptions WHERE verified = TRUE;"

$routerOffsetAfter = Invoke-PsqlScalar -Sql "SELECT last_processed_event_id FROM router_offsets WHERE id = 1;"
Write-Host "Router offset (after): $routerOffsetAfter" -ForegroundColor Cyan

Write-Host ("Latest saga for event {0}:" -f $eventId) -ForegroundColor Cyan
Invoke-Psql -Sql "SELECT id, status, attempt_count, final_error_code FROM webhook_delivery_sagas WHERE event_id = $eventId ORDER BY id DESC LIMIT 1;"
Write-Host ("Latest job for event {0}:" -f $eventId) -ForegroundColor Cyan
Invoke-Psql -Sql @"
SELECT id, saga_id, status, response_status, error_code
FROM webhook_delivery_jobs
WHERE saga_id = (
  SELECT id FROM webhook_delivery_sagas
  WHERE event_id = $eventId
  ORDER BY id DESC
  LIMIT 1
)
ORDER BY id DESC
LIMIT 1;
"@

$sagaCountForEvent = Invoke-PsqlScalar -Sql "SELECT COUNT(*) FROM webhook_delivery_sagas WHERE event_id = $eventId;"
if ([int64]$sagaCountForEvent -eq 0 -and $routerOffsetAfter -and $routerOffsetAfter -match '^[0-9]+$') {
    if ([int64]$routerOffsetAfter -ge [int64]$eventId) {
        Write-Host "WARNING: Router offset ($routerOffsetAfter) >= event id ($eventId), so this event won't be routed again. Re-run to generate a new event id, or use -ResetRouterOffset." -ForegroundColor Yellow
    }
}
}
finally {
    Remove-Item Env:\PGPASSWORD -ErrorAction SilentlyContinue
}
