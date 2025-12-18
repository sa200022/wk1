# Smoke test for Webhook Delivery System (local compose stack)
# Steps:
#   1) Create subscription
#   2) Publish event
#   3) Poll DB for saga/job status
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
    [string]$ApiKey = ""
)

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

$psql = Resolve-PsqlPath

if (-not $DbPassword -or $DbPassword.Trim() -eq "") {
    $DbPassword = "dev_password_postgres"
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

Write-Host "Waiting for processing (10s)..." -ForegroundColor Yellow
Start-Sleep -Seconds 10

$env:PGPASSWORD = $DbPassword
$connArgs = @("-h", $DbHost, "-U", $DbUser, "-d", $DbName, "-t", "-A")

Write-Host "Saga status counts:" -ForegroundColor Cyan
& $psql @connArgs -c "SELECT status, COUNT(*) FROM webhook_delivery_sagas GROUP BY status;"

Write-Host "Job status counts:" -ForegroundColor Cyan
& $psql @connArgs -c "SELECT status, COUNT(*) FROM webhook_delivery_jobs GROUP BY status;"

Write-Host "Latest saga for event $eventId:" -ForegroundColor Cyan
& $psql @connArgs -c "SELECT id, status, attempt_count, final_error_code FROM webhook_delivery_sagas WHERE event_id = $eventId ORDER BY id DESC LIMIT 1;"

Remove-Item Env:\PGPASSWORD
