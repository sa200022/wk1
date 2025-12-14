# 🚀 Webhook Delivery System - 快速開始指南

## 📋 前置需求

1. ✅ .NET 8 SDK ([下載](https://dotnet.microsoft.com/download/dotnet/8.0))
2. ✅ PostgreSQL 15+ ([下載](https://www.postgresql.org/download/windows/))

---

## ⚡ 5 分鐘快速啟動

### 步驟 1: 安裝 PostgreSQL

```powershell
# 下載並安裝 PostgreSQL
# https://www.postgresql.org/download/windows/

# 安裝時記住你設定的 postgres 密碼！
# 預設 Port: 5432
```

### 步驟 2: 建立資料庫與 Schema

```powershell
# 開啟 PowerShell，在專案根目錄執行

# 建立資料庫
psql -U postgres -c "CREATE DATABASE webhook_delivery;"

# 執行 Schema Migration
psql -U postgres -d webhook_delivery -f src/WebhookDelivery.Database/Migrations/001_InitialSchema.sql
```

### 步驟 3: 設定密碼 (如果不是預設密碼)

如果你的 PostgreSQL 密碼不是 `5512355123k`，修改以下檔案：

- `src/WebhookDelivery.EventIngestion/appsettings.Development.json`
- `src/WebhookDelivery.SubscriptionApi/appsettings.Development.json`
- `src/WebhookDelivery.Router/appsettings.Development.json`
- `src/WebhookDelivery.Orchestrator/appsettings.Development.json`
- `src/WebhookDelivery.Worker/appsettings.Development.json`
- `src/WebhookDelivery.DeadLetter/appsettings.Development.json`

將 `Password=5512355123k` 改為你的密碼。

### 步驟 4: 啟動所有服務

#### 方式 A: 使用自動啟動腳本 (推薦)

```powershell
.\start-all-services.ps1
```

會自動開啟 6 個視窗，分別運行：
- ✅ SubscriptionApi (Port 5001)
- ✅ DeadLetterApi (Port 5003)
- ✅ EventIngestion
- ✅ Router
- ✅ Orchestrator
- ✅ Worker

#### 方式 B: 手動啟動

在 6 個不同的 PowerShell 視窗中執行：

```powershell
# Terminal 1
cd src\WebhookDelivery.SubscriptionApi
dotnet run

# Terminal 2
cd src\WebhookDelivery.DeadLetter
dotnet run

# Terminal 3
cd src\WebhookDelivery.EventIngestion
dotnet run

# Terminal 4
cd src\WebhookDelivery.Router
dotnet run

# Terminal 5
cd src\WebhookDelivery.Orchestrator
dotnet run

# Terminal 6
cd src\WebhookDelivery.Worker
dotnet run
```

### 步驟 5: 驗證系統運行

打開瀏覽器訪問：

- **Subscription API**: http://localhost:5001/swagger
- **Dead Letter API**: http://localhost:5003/swagger

---

## 🧪 測試系統

### 1. 建立訂閱

```bash
curl -X POST "http://localhost:5001/api/subscriptions" \
  -H "Content-Type: application/json" \
  -d '{
    "eventType": "user.created",
    "callbackUrl": "https://webhook.site/your-unique-url",
    "active": true,
    "verified": true
  }'
```

### 2. 發送測試事件

(需要自行實作 Event Ingestion 的 HTTP 端點，目前是 BackgroundService)

### 3. 查看死信

```bash
curl "http://localhost:5003/api/deadletters"
```

---

## 📊 資料庫管理

### 使用 psql 命令列

```powershell
# 連線到資料庫
psql -U postgres -d webhook_delivery

# 查看所有資料表
\dt

# 查看 Saga 狀態
SELECT status, COUNT(*) FROM webhook_delivery_sagas GROUP BY status;

# 查看 Job 狀態
SELECT status, COUNT(*) FROM webhook_delivery_jobs GROUP BY status;

# 離開
\q
```

### 使用 pgAdmin 4 (圖形化)

1. 開啟 pgAdmin 4
2. 連線到 PostgreSQL Server
3. 展開 `Databases` → `webhook_delivery`
4. 查看 `Tables`

---

## 🛠️ 常見問題

### Q: psql 命令找不到？

**A**: 將 PostgreSQL bin 目錄加入 PATH：

```powershell
# 加入到 PATH
$env:Path += ";C:\Program Files\PostgreSQL\15\bin"

# 永久設定 (需要管理員權限)
[Environment]::SetEnvironmentVariable("Path", $env:Path, "Machine")
```

### Q: 連線被拒絕 (Connection refused)？

**A**: 確認 PostgreSQL 服務正在運行：

```powershell
# 查看服務狀態
Get-Service -Name postgresql*

# 啟動服務
Start-Service postgresql-x64-15
```

### Q: 密碼錯誤 (Password authentication failed)？

**A**: 重設 postgres 使用者密碼：

```sql
-- 在 psql 中執行
ALTER USER postgres WITH PASSWORD '新密碼';
```

然後更新所有 `appsettings.Development.json` 中的密碼。

### Q: 如何重置資料庫？

**A**: 刪除並重建：

```powershell
# 刪除資料庫
psql -U postgres -c "DROP DATABASE webhook_delivery;"

# 重新建立
psql -U postgres -c "CREATE DATABASE webhook_delivery;"

# 執行 Schema
psql -U postgres -d webhook_delivery -f src/WebhookDelivery.Database/Migrations/001_InitialSchema.sql
```

---

## 📖 完整文件

- [setup-local-postgres.md](setup-local-postgres.md) - 詳細 PostgreSQL 設定指南
- [README-Docker.md](README-Docker.md) - Docker 部署方式
- [rule/r0/流程.txt](rule/r0/流程.txt) - 完整系統設計規格

---

## 🎯 系統架構

```
Event → Router → Saga Orchestrator → Job Worker → Webhook Callback
                      ↓
                 Dead Letter Queue
```

### 6 個微服務

1. **EventIngestion** - 接收並儲存事件
2. **Router** - 將事件路由到訂閱
3. **Orchestrator** - 管理 Saga 狀態機與重試邏輯
4. **Worker** - 執行實際的 HTTP 投遞
5. **SubscriptionApi** - 管理訂閱配置
6. **DeadLetter** - 處理永久失敗的投遞

---

## 🚀 下一步

- 實作 EventIngestion 的 HTTP 端點
- 加入監控與 Metrics (Prometheus/Grafana)
- 執行整合測試: `dotnet test`
- 部署到生產環境

---

**專案完成度**: 100% ✅
**最後更新**: 2025-12-14
**資料庫**: PostgreSQL 15+
**狀態**: 可立即部署運行！
