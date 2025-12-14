# PostgreSQL 本地設定指南

## 1. 安裝 PostgreSQL

### 下載與安裝
1. 前往: https://www.postgresql.org/download/windows/
2. 下載 PostgreSQL 15 或 16 (推薦 15)
3. 執行安裝程式
4. **重要**: 記住你設定的 `postgres` 使用者密碼！

### 安裝選項
- Port: `5432` (預設)
- Locale: `Chinese (Simplified), China` 或 `C`
- 勾選 pgAdmin 4 (圖形化管理工具)

---

## 2. 驗證安裝

開啟 PowerShell 或 CMD:

```powershell
# 檢查 PostgreSQL 是否安裝
psql --version
# 應該看到: psql (PostgreSQL) 15.x

# 測試連線 (會要求輸入密碼)
psql -U postgres
```

---

## 3. 建立資料庫

在 psql 中執行:

```sql
-- 建立資料庫
CREATE DATABASE webhook_delivery;

-- 確認建立成功
\l
-- 應該看到 webhook_delivery 在列表中

-- 連線到新資料庫
\c webhook_delivery
```

---

## 4. 執行 Schema Migration

### 方式 A: 使用 psql 命令列

```powershell
# 在專案根目錄執行
cd c:\Users\DogbearZ\Documents\GitHub\wk1

# 執行 SQL 腳本
psql -U postgres -d webhook_delivery -f src/WebhookDelivery.Database/Migrations/001_InitialSchema.sql
```

### 方式 B: 使用 pgAdmin 4 (圖形化)

1. 開啟 pgAdmin 4
2. 連線到 PostgreSQL 伺服器
3. 展開 `Databases` → `webhook_delivery`
4. 右鍵點擊 `webhook_delivery` → `Query Tool`
5. 開啟 `src/WebhookDelivery.Database/Migrations/001_InitialSchema.sql`
6. 執行 (F5)

---

## 5. 驗證資料表建立

```sql
-- 列出所有資料表
\dt

-- 應該看到:
-- events
-- subscriptions
-- webhook_delivery_sagas
-- webhook_delivery_jobs
-- dead_letters
```

---

## 6. 更新服務連線字串

所有服務的 `appsettings.Development.json` 已經準備好，預設密碼是 `5512355123k`

如果你的密碼不同，需要修改以下檔案:

- `src/WebhookDelivery.EventIngestion/appsettings.Development.json`
- `src/WebhookDelivery.SubscriptionApi/appsettings.Development.json`
- `src/WebhookDelivery.Router/appsettings.Development.json`
- `src/WebhookDelivery.Orchestrator/appsettings.Development.json`
- `src/WebhookDelivery.Worker/appsettings.Development.json`
- `src/WebhookDelivery.DeadLetter/appsettings.Development.json`

連線字串格式:
```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Port=5432;Database=webhook_delivery;Username=postgres;Password=你的密碼"
  }
}
```

---

## 7. 建立資料庫角色 (權限隔離)

**注意**: 開發環境可以暫時跳過這步，所有服務都用 `postgres` 超級使用者。

如果要完整設定權限，執行:

```powershell
psql -U postgres -d webhook_delivery -f src/WebhookDelivery.Database/Scripts/002_DatabaseRoles_Development.sql
```

---

## 8. 啟動服務

### 方式 A: 使用 Visual Studio / Rider

1. 開啟 `WebhookDelivery.sln`
2. 設定多個啟動專案
3. 同時運行所有服務

### 方式 B: 命令列逐一啟動

```powershell
# 在不同的終端視窗中執行

# Terminal 1: Subscription API
cd src\WebhookDelivery.SubscriptionApi
dotnet run

# Terminal 2: Event Ingestion
cd src\WebhookDelivery.EventIngestion
dotnet run

# Terminal 3: Router
cd src\WebhookDelivery.Router
dotnet run

# Terminal 4: Orchestrator
cd src\WebhookDelivery.Orchestrator
dotnet run

# Terminal 5: Worker
cd src\WebhookDelivery.Worker
dotnet run

# Terminal 6: Dead Letter API
cd src\WebhookDelivery.DeadLetter
dotnet run
```

---

## 9. 測試服務

### Subscription API (Port 5001)
```powershell
# Swagger UI
start http://localhost:5001/swagger
```

### Dead Letter API (Port 5003)
```powershell
# Swagger UI
start http://localhost:5003/swagger
```

---

## 10. 常見問題

### Q: psql 命令找不到？

A: 需要將 PostgreSQL 的 bin 目錄加入 PATH:
```
C:\Program Files\PostgreSQL\15\bin
```

### Q: 連線被拒絕？

A: 確認 PostgreSQL 服務正在運行:
```powershell
# 檢查服務狀態
Get-Service -Name postgresql*

# 啟動服務
Start-Service postgresql-x64-15
```

### Q: 密碼錯誤？

A: 重設 postgres 密碼:
```sql
ALTER USER postgres WITH PASSWORD '新密碼';
```

---

## 11. 快速重置資料庫

如果需要重新開始:

```sql
-- 在 psql 中執行
DROP DATABASE webhook_delivery;
CREATE DATABASE webhook_delivery;

-- 然後重新執行 Schema Migration
```

---

**下一步**: 完成安裝後，執行步驟 3-8 即可啟動完整系統！
