# Webhook Delivery System - 啟動所有服務
# 使用方式: .\start-all-services.ps1

Write-Host "🚀 啟動 Webhook Delivery System..." -ForegroundColor Green
Write-Host ""

# 檢查 PostgreSQL 是否運行
Write-Host "📊 檢查 PostgreSQL 狀態..." -ForegroundColor Yellow
$pgService = Get-Service -Name "postgresql*" -ErrorAction SilentlyContinue

if ($null -eq $pgService) {
    Write-Host "❌ PostgreSQL 服務未找到！請先安裝 PostgreSQL。" -ForegroundColor Red
    Write-Host "   下載位置: https://www.postgresql.org/download/windows/" -ForegroundColor Cyan
    exit 1
}

if ($pgService.Status -ne "Running") {
    Write-Host "⚠️  PostgreSQL 服務未運行，正在啟動..." -ForegroundColor Yellow
    Start-Service $pgService.Name
    Start-Sleep -Seconds 2
}

Write-Host "✅ PostgreSQL 正在運行" -ForegroundColor Green
Write-Host ""

# 檢查資料庫是否存在
Write-Host "📦 檢查資料庫 'webhook_delivery'..." -ForegroundColor Yellow
$dbCheck = & psql -U postgres -lqt 2>$null | Select-String -Pattern "webhook_delivery"

if ($null -eq $dbCheck) {
    Write-Host "⚠️  資料庫不存在，請先執行以下命令建立:" -ForegroundColor Yellow
    Write-Host "   psql -U postgres -c `"CREATE DATABASE webhook_delivery;`"" -ForegroundColor Cyan
    Write-Host "   psql -U postgres -d webhook_delivery -f src/WebhookDelivery.Database/Migrations/001_InitialSchema.sql" -ForegroundColor Cyan
    Write-Host ""
    $createDb = Read-Host "是否現在建立資料庫？(y/n)"

    if ($createDb -eq "y") {
        Write-Host "正在建立資料庫..." -ForegroundColor Yellow
        & psql -U postgres -c "CREATE DATABASE webhook_delivery;"
        & psql -U postgres -d webhook_delivery -f "src/WebhookDelivery.Database/Migrations/001_InitialSchema.sql"
        Write-Host "✅ 資料庫建立完成" -ForegroundColor Green
    } else {
        exit 1
    }
}

Write-Host "✅ 資料庫存在" -ForegroundColor Green
Write-Host ""

# 啟動所有服務
Write-Host "🎯 啟動所有服務 (6個視窗)..." -ForegroundColor Green
Write-Host ""

$services = @(
    @{Name="SubscriptionApi"; Path="src\WebhookDelivery.SubscriptionApi"; Port=5001; Color="Cyan"}
    @{Name="DeadLetterApi"; Path="src\WebhookDelivery.DeadLetter"; Port=5003; Color="Magenta"}
    @{Name="EventIngestion"; Path="src\WebhookDelivery.EventIngestion"; Port="-"; Color="Yellow"}
    @{Name="Router"; Path="src\WebhookDelivery.Router"; Port="-"; Color="Green"}
    @{Name="Orchestrator"; Path="src\WebhookDelivery.Orchestrator"; Port="-"; Color="Blue"}
    @{Name="Worker"; Path="src\WebhookDelivery.Worker"; Port="-"; Color="White"}
)

foreach ($service in $services) {
    $title = "Webhook - $($service.Name)"

    if ($service.Port -ne "-") {
        Write-Host "  🌐 $($service.Name) (Port: $($service.Port))" -ForegroundColor $service.Color
    } else {
        Write-Host "  ⚙️  $($service.Name)" -ForegroundColor $service.Color
    }

    Start-Process powershell -ArgumentList @(
        "-NoExit",
        "-Command",
        "& {" +
        "`$host.ui.RawUI.WindowTitle = '$title';" +
        "cd '$($service.Path)';" +
        "Write-Host '啟動 $($service.Name)...' -ForegroundColor $($service.Color);" +
        "dotnet run" +
        "}"
    )

    Start-Sleep -Milliseconds 500
}

Write-Host ""
Write-Host "✅ 所有服務已啟動！" -ForegroundColor Green
Write-Host ""
Write-Host "📝 API 端點:" -ForegroundColor Cyan
Write-Host "   • Subscription API: http://localhost:5001/swagger" -ForegroundColor White
Write-Host "   • Dead Letter API:  http://localhost:5003/swagger" -ForegroundColor White
Write-Host ""
Write-Host "⏹️  停止所有服務: 關閉所有 PowerShell 視窗" -ForegroundColor Yellow
Write-Host ""
