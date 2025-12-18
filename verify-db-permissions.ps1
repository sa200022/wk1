# Verify PostgreSQL role/grant isolation for Webhook Delivery System (no Docker)
# This script creates a temporary database, applies schema + DEV roles, and checks expected permissions.
#
# Usage:
#   $env:POSTGRES_PASSWORD="你的密碼"; .\verify-db-permissions.ps1
#   .\verify-db-permissions.ps1 -AdminPassword "你的密碼"

param(
    [string]$DbHost = "localhost",
    [int]$DbPort = 5432,
    [string]$AdminUser = "postgres",
    [string]$AdminPassword = $env:POSTGRES_PASSWORD
)

function Require-Command {
    param([string]$Name)
    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
        throw "Required command '$Name' not found. Please install it and retry."
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

function Ensure-Password {
    param([string]$Value, [string]$Prompt)
    if ($Value -and $Value.Trim() -ne "") { return $Value }
    $prompted = Read-Host $Prompt
    if ($prompted -and $prompted.Trim() -ne "") { return $prompted }
    throw "Password is required."
}

function Psql-Admin {
    param(
        [string]$Database,
        [string]$Sql
    )
    $env:PGPASSWORD = $AdminPassword
    try {
        & $psql -h $DbHost -p $DbPort -U $AdminUser -d $Database -v ON_ERROR_STOP=1 -t -A -c $Sql
    }
    finally {
        Remove-Item Env:\PGPASSWORD -ErrorAction SilentlyContinue
    }
}

function Psql-AdminFile {
    param(
        [string]$Database,
        [string]$Path
    )
    $env:PGPASSWORD = $AdminPassword
    try {
        & $psql -h $DbHost -p $DbPort -U $AdminUser -d $Database -v ON_ERROR_STOP=1 -f $Path
    }
    finally {
        Remove-Item Env:\PGPASSWORD -ErrorAction SilentlyContinue
    }
}

function Psql-AsRole {
    param(
        [string]$Database,
        [string]$User,
        [string]$Password,
        [string]$Sql
    )
    $env:PGPASSWORD = $Password
    try {
        & $psql -h $DbHost -p $DbPort -U $User -d $Database -v ON_ERROR_STOP=1 -t -A -c $Sql 2>&1
    }
    finally {
        Remove-Item Env:\PGPASSWORD -ErrorAction SilentlyContinue
    }
}

function Expect-Success {
    param([string]$Name, [string]$Output)
    if ($LASTEXITCODE -ne 0) {
        throw "FAILED (expected success): $Name`n$Output"
    }
    Write-Host "OK: $Name" -ForegroundColor Green
}

function Expect-Fail {
    param([string]$Name, [string]$Output)
    if ($LASTEXITCODE -eq 0) {
        throw "FAILED (expected permission error): $Name`n$Output"
    }
    if ($Output -notmatch "permission denied|42501") {
        throw "FAILED (expected permission denied): $Name`n$Output"
    }
    Write-Host "OK (denied as expected): $Name" -ForegroundColor Green
}

$ErrorActionPreference = "Stop"
$psql = Resolve-PsqlPath

$AdminPassword = Ensure-Password $AdminPassword "PostgreSQL admin password (postgres)"

$dbName = "webhook_delivery_permtest_$([Guid]::NewGuid().ToString('N'))"

Write-Host "Creating temp database: $dbName" -ForegroundColor Yellow
Psql-Admin -Database "postgres" -Sql "CREATE DATABASE ""$dbName"" WITH TEMPLATE = template1;"

try {
    Write-Host "Applying schema..." -ForegroundColor Yellow
    Psql-AdminFile -Database $dbName -Path "src/WebhookDelivery.Database/Migrations/001_InitialSchema.sql"

    Write-Host "Applying DEV roles/grants..." -ForegroundColor Yellow
    Psql-AdminFile -Database $dbName -Path "src/WebhookDelivery.Database/Scripts/002_DatabaseRoles_Development.sql"

    $roles = @{
        event_ingest_writer    = "dev_password_event_ingest"
        router_worker          = "dev_password_router"
        saga_orchestrator      = "dev_password_orchestrator"
        job_worker             = "dev_password_worker"
        dead_letter_operator   = "dev_password_deadletter"
        subscription_admin     = "dev_password_subscription"
    }

    Write-Host "Running permission checks..." -ForegroundColor Yellow

    # Seed a subscription (as subscription_admin)
    $out = Psql-AsRole -Database $dbName -User "subscription_admin" -Password $roles.subscription_admin -Sql @"
INSERT INTO subscriptions (event_type, callback_url, active, verified, created_at, updated_at)
VALUES ('perm.test', 'https://example.com', true, true, NOW(), NOW())
RETURNING id;
"@
    Expect-Success "subscription_admin can INSERT subscriptions" $out

    # event_ingest_writer: INSERT ok, UPDATE denied
    $out = Psql-AsRole -Database $dbName -User "event_ingest_writer" -Password $roles.event_ingest_writer -Sql @"
INSERT INTO events (external_event_id, event_type, payload, created_at)
VALUES ('perm-event-1', 'perm.test', '{}'::jsonb, NOW())
RETURNING id;
"@
    Expect-Success "event_ingest_writer can INSERT events" $out

    $out = Psql-AsRole -Database $dbName -User "event_ingest_writer" -Password $roles.event_ingest_writer -Sql "UPDATE events SET event_type='x' WHERE external_event_id='perm-event-1';"
    Expect-Fail "event_ingest_writer cannot UPDATE events" $out

    # router_worker: can INSERT saga, cannot UPDATE saga
    $out = Psql-AsRole -Database $dbName -User "router_worker" -Password $roles.router_worker -Sql @"
INSERT INTO webhook_delivery_sagas (event_id, subscription_id, status, attempt_count, next_attempt_at, created_at, updated_at)
VALUES (1, 1, 'Pending', 0, NOW(), NOW(), NOW())
RETURNING id;
"@
    Expect-Success "router_worker can INSERT sagas" $out

    $out = Psql-AsRole -Database $dbName -User "router_worker" -Password $roles.router_worker -Sql "UPDATE webhook_delivery_sagas SET status='InProgress' WHERE id=1;"
    Expect-Fail "router_worker cannot UPDATE sagas" $out

    # saga_orchestrator: can UPDATE saga + manage jobs
    $out = Psql-AsRole -Database $dbName -User "saga_orchestrator" -Password $roles.saga_orchestrator -Sql "UPDATE webhook_delivery_sagas SET status='InProgress' WHERE id=1;"
    Expect-Success "saga_orchestrator can UPDATE sagas" $out

    $out = Psql-AsRole -Database $dbName -User "saga_orchestrator" -Password $roles.saga_orchestrator -Sql @"
INSERT INTO webhook_delivery_jobs (saga_id, status, attempt_at)
VALUES (1, 'Pending', NOW())
RETURNING id;
"@
    Expect-Success "saga_orchestrator can INSERT jobs" $out

    # job_worker: can UPDATE jobs, cannot UPDATE saga
    $out = Psql-AsRole -Database $dbName -User "job_worker" -Password $roles.job_worker -Sql "UPDATE webhook_delivery_jobs SET status='Completed', response_status=200 WHERE id=1;"
    Expect-Success "job_worker can UPDATE jobs" $out

    $out = Psql-AsRole -Database $dbName -User "job_worker" -Password $roles.job_worker -Sql "UPDATE webhook_delivery_sagas SET status='Completed' WHERE id=1;"
    Expect-Fail "job_worker cannot UPDATE sagas" $out

    # dead_letter_operator: can INSERT dead_letters, cannot UPDATE jobs
    $out = Psql-AsRole -Database $dbName -User "dead_letter_operator" -Password $roles.dead_letter_operator -Sql @"
INSERT INTO dead_letters (saga_id, event_id, subscription_id, final_error_code, payload_snapshot)
VALUES (1, 1, 1, 'PERM_TEST', '{}'::jsonb)
RETURNING id;
"@
    Expect-Success "dead_letter_operator can INSERT dead_letters" $out

    $out = Psql-AsRole -Database $dbName -User "dead_letter_operator" -Password $roles.dead_letter_operator -Sql "UPDATE webhook_delivery_jobs SET status='Failed' WHERE id=1;"
    Expect-Fail "dead_letter_operator cannot UPDATE jobs" $out

    Write-Host "All permission checks passed." -ForegroundColor Green
}
finally {
    Write-Host "Dropping temp database: $dbName" -ForegroundColor Yellow
    try {
        Psql-Admin -Database "postgres" -Sql "DROP DATABASE IF EXISTS ""$dbName"";"
    }
    catch {
        Write-Warning "Failed to drop temp database '$dbName': $($_.Exception.Message)"
    }
}
