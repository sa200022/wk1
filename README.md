# Webhook Delivery System

端到端 Webhook 投遞系統：可建立訂閱、接收事件、路由成 saga、建立 job 並投遞 webhook，支援重試與死信。
技術棧：.NET 8 + PostgreSQL 15。

## 功能
- 訂閱管理：建立/驗證/啟停
- 事件接收：HTTP 寫入 events（idempotency）
- 路由：為每個有效訂閱建立 saga（Pending）
- 協調：Saga 狀態機、重試/死信邏輯、建立 job
- 投遞：Worker 發送 webhook，回寫 Completed/Failed
- 死信：查詢死信、重入列
- 一鍵啟動 + smoke test

## 快速開始（本機）
1) 準備 PostgreSQL 15（請自行設定帳密，DB=`webhook_delivery`），套用 schema：  
```bash
psql -U postgres -c "CREATE DATABASE webhook_delivery;"
psql -U postgres -d webhook_delivery -f src/WebhookDelivery.Database/Migrations/001_InitialSchema.sql
```

2) 啟動服務（Docker 可選）：  
```bash
docker-compose up -d   # 已暴露 5001/5002/5003
```
不用 Docker：
- 一鍵啟動：`.\start-all-services.ps1 -DbPassword 你的密碼`（會設定 `ConnectionStrings__DefaultConnection` 並開 6 個視窗）
- 或自行啟動：`$env:ConnectionStrings__DefaultConnection="Host=localhost;Port=5432;Database=webhook_delivery;Username=postgres;Password=你的密碼;"` 後在各專案 `dotnet run`

3) 一鍵驗證：  
```powershell
.\smoke-test.ps1 -CallbackUrl "https://httpbin.org/status/200"
```
腳本會建立訂閱、送事件、等待並印出 saga/job 狀態。

如要驗證 DB 權限分離（DEV roles/grants），可跑：`.\verify-db-permissions.ps1`（需要 `psql` 在 PATH 或已安裝 PostgreSQL client tools）。

## 服務端點
- Subscription API: `http://localhost:5001/swagger`
- Event Ingestion: `http://localhost:5002/api/events` (POST)
- Dead Letter API: `http://localhost:5003/swagger`

API Key（可選）：在環境變數或 appsettings 設定 `Security:ApiKey`，啟用後呼叫需帶 `X-Api-Key`。

## 測試
```bash
dotnet test WebhookDelivery.sln
```
整合測試需設定：
- `ConnectionStrings__TestDatabase`（例如 `Host=localhost;Port=5432;Username=postgres;Password=你的密碼;Database=postgres`）

## 架構概覽
- EventIngestion：事件入口（HTTP），只負責 INSERT events。
- SubscriptionApi：訂閱管理（CRUD、驗證、啟停）。
- Router：依事件類型為每個訂閱建立 saga（Pending）。
- Orchestrator：掌握 saga 狀態與重試/死信邏輯，建立 job。
- Worker：拉取 job，送出 HTTP webhook，回寫 Completed/Failed，租約逾時會被 lease cleaner 重置。
- DeadLetter：查詢/重入列死信（建立全新的 saga）。
- Database：`src/WebhookDelivery.Database/Migrations/001_InitialSchema.sql`

核心保障：idempotency（unique event/subscription + job attempt）、終態保護（Completed/DeadLettered 不可更新）、`SELECT FOR UPDATE SKIP LOCKED` + lease 防重入。

## 注意事項
- Router offset 只往前，新訂閱不會補送舊事件。
- 沒前端畫面，為純 API 專案。
