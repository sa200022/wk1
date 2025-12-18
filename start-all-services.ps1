# Webhook Delivery System - Start all services (local, no Docker)
# Usage examples:
#   $env:POSTGRES_PASSWORD="your_password"; .\start-all-services.ps1
#   .\start-all-services.ps1 -DbPassword "your_password"
#   .\start-all-services.ps1 -UseDevRoles

param(
    [string]$DbHost = "localhost",
    [int]$DbPort = 5432,
    [string]$DbName = "webhook_delivery",
    [string]$DbUser = "postgres",
    [string]$DbPassword = $env:POSTGRES_PASSWORD,
    [switch]$UseDevRoles,
    [switch]$SkipDbSetup,
    [string]$ApiKey = $env:API_KEY
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

function Ensure-DbPassword {
    param([string]$Value)
    if ($Value -and $Value.Trim() -ne "") { return $Value }
    $prompted = Read-Host "PostgreSQL password (leave empty to use 'dev_password_postgres')"
    if ($prompted -and $prompted.Trim() -ne "") { return $prompted }
    return "dev_password_postgres"
}

function Psql {
    param(
        [string]$Database,
        [string]$Sql
    )
    $env:PGPASSWORD = $DbPassword
    try {
        & $psql -h $DbHost -p $DbPort -U $DbUser -d $Database -t -A -c $Sql
    }
    finally {
        Remove-Item Env:\PGPASSWORD -ErrorAction SilentlyContinue
    }
}

function Psql-File {
    param(
        [string]$Database,
        [string]$Path
    )
    $env:PGPASSWORD = $DbPassword
    try {
        & $psql -h $DbHost -p $DbPort -U $DbUser -d $Database -f $Path
    }
    finally {
        Remove-Item Env:\PGPASSWORD -ErrorAction SilentlyContinue
    }
}

$ErrorActionPreference = "Stop"

Require-Command "dotnet"

$DbPassword = Ensure-DbPassword $DbPassword

Write-Host "Starting Webhook Delivery System (no Docker)..." -ForegroundColor Green
Write-Host "DB: Host=$DbHost Port=$DbPort Db=$DbName User=$DbUser UseDevRoles=$UseDevRoles" -ForegroundColor DarkGray
Write-Host ""

if (-not $SkipDbSetup) {
    $psql = Resolve-PsqlPath
    if (-not $psql -or -not (Test-Path $psql)) { throw "psql path resolution failed." }

    Write-Host "Checking database..." -ForegroundColor Yellow

    $dbExists = Psql -Database "postgres" -Sql "SELECT 1 FROM pg_database WHERE datname = '$DbName';"
    if (-not $dbExists -or $dbExists.Trim() -eq "") {
        Write-Host "Database '$DbName' does not exist. Creating..." -ForegroundColor Yellow
        Psql -Database "postgres" -Sql "CREATE DATABASE ""$DbName"";"
    }

    Write-Host "Applying schema migration..." -ForegroundColor Yellow
    Psql-File -Database $DbName -Path "src/WebhookDelivery.Database/Migrations/001_InitialSchema.sql"

    if ($UseDevRoles) {
        Write-Host "Applying DEV roles/grants (creates dev_password_* logins)..." -ForegroundColor Yellow
        Psql-File -Database $DbName -Path "src/WebhookDelivery.Database/Scripts/002_DatabaseRoles_Development.sql"
    }

    Write-Host "Database ready." -ForegroundColor Green
    Write-Host ""
}

function Build-ConnString {
    param(
        [string]$HostName,
        [int]$Port,
        [string]$Database,
        [string]$Username,
        [string]$Password
    )
    return "Host=$HostName;Port=$Port;Database=$Database;Username=$Username;Password=$Password;"
}

function Service-ConnString {
    param([string]$ServiceName)

    if (-not $UseDevRoles) {
        return Build-ConnString -HostName $DbHost -Port $DbPort -Database $DbName -Username $DbUser -Password $DbPassword
    }

    $passwordMap = @{
        "EventIngestion"   = ($env:DB_PASSWORD_EVENT_INGEST)
        "SubscriptionApi"  = ($env:DB_PASSWORD_SUBSCRIPTION_ADMIN)
        "Router"           = ($env:DB_PASSWORD_ROUTER_WORKER)
        "Orchestrator"     = ($env:DB_PASSWORD_SAGA_ORCHESTRATOR)
        "Worker"           = ($env:DB_PASSWORD_JOB_WORKER)
        "DeadLetterApi"    = ($env:DB_PASSWORD_DEAD_LETTER_OPERATOR)
    }

    $userMap = @{
        "EventIngestion"   = "event_ingest_writer"
        "SubscriptionApi"  = "subscription_admin"
        "Router"           = "router_worker"
        "Orchestrator"     = "saga_orchestrator"
        "Worker"           = "job_worker"
        "DeadLetterApi"    = "dead_letter_operator"
    }

    $fallbackPasswordMap = @{
        "EventIngestion"   = "dev_password_event_ingest"
        "SubscriptionApi"  = "dev_password_subscription"
        "Router"           = "dev_password_router"
        "Orchestrator"     = "dev_password_orchestrator"
        "Worker"           = "dev_password_worker"
        "DeadLetterApi"    = "dev_password_deadletter"
    }

    $password = $passwordMap[$ServiceName]
    if (-not $password -or $password.Trim() -eq "") { $password = $fallbackPasswordMap[$ServiceName] }
    $username = $userMap[$ServiceName]
    return Build-ConnString -HostName $DbHost -Port $DbPort -Database $DbName -Username $username -Password $password
}

$services = @(
    @{ Name = "SubscriptionApi"; Path = "src\\WebhookDelivery.SubscriptionApi"; Url = "http://localhost:5001"; Color = "Cyan" }
    @{ Name = "EventIngestion";  Path = "src\\WebhookDelivery.EventIngestion";  Url = "http://localhost:5002"; Color = "Yellow" }
    @{ Name = "DeadLetterApi";   Path = "src\\WebhookDelivery.DeadLetter";      Url = "http://localhost:5003"; Color = "Magenta" }
    @{ Name = "Router";          Path = "src\\WebhookDelivery.Router";          Url = ""; Color = "Green" }
    @{ Name = "Orchestrator";    Path = "src\\WebhookDelivery.Orchestrator";    Url = ""; Color = "Blue" }
    @{ Name = "Worker";          Path = "src\\WebhookDelivery.Worker";          Url = ""; Color = "White" }
)

Write-Host "Launching services (each in its own PowerShell window)..." -ForegroundColor Green
Write-Host ""

foreach ($service in $services) {
    $title = "WebhookDelivery - $($service.Name)"
    $conn = Service-ConnString $service.Name
    $apiKey = $ApiKey

    $runCommand =
        "& {" +
        "`$host.ui.RawUI.WindowTitle = '$title';" +
        "cd '$($service.Path)';" +
        "`$env:ASPNETCORE_ENVIRONMENT = 'Development';" +
        "if ('$($service.Url)' -ne '') { `$env:ASPNETCORE_URLS = '$($service.Url)'; }" +
        "`$env:ConnectionStrings__DefaultConnection = '$conn';" +
        "if ('$apiKey' -ne '') { `$env:Security__ApiKey = '$apiKey'; }" +
        "Write-Host 'Starting $($service.Name)...' -ForegroundColor $($service.Color);" +
        "dotnet run" +
        "}"

    Start-Process powershell -ArgumentList @("-NoExit", "-Command", $runCommand)
    Start-Sleep -Milliseconds 400
}

Write-Host ""
Write-Host "Endpoints:" -ForegroundColor Cyan
Write-Host "  Subscription API: http://localhost:5001/swagger" -ForegroundColor White
Write-Host "  Event Ingestion:  http://localhost:5002/swagger (and POST /api/events)" -ForegroundColor White
Write-Host "  Dead Letter API:  http://localhost:5003/swagger" -ForegroundColor White
Write-Host ""
Write-Host "Tip: Run smoke test: .\\smoke-test.ps1 (set POSTGRES_PASSWORD / API_KEY if needed)" -ForegroundColor DarkGray
