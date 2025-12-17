# Webhook Delivery System

端到端的 Webhook 投遞範例專案，涵蓋事件寫入、路由、Saga 協調、工作者執行與死信重入列。技術棧：.NET 8 + PostgreSQL 15。

## 架構概覽
- EventIngestion：事件入口（HTTP），只負責 INSERT events。
- SubscriptionApi：訂閱管理（CRUD、驗證、啟停）。
- Router：依事件類型為每個訂閱建立 saga（Pending）。
- Orchestrator：掌握 saga 狀態與重試/死信邏輯，建立 job。
- Worker：拉取 job，送出 HTTP webhook，回寫 Completed/Failed，租約逾時會被 lease cleaner 重置。
- DeadLetter：查詢/重入列死信（建立全新的 saga）。
- Database：`src/WebhookDelivery.Database/Migrations/001_InitialSchema.sql`

核心保障：idempotency（unique event/subscription + job attempt）、終態保護（Completed/DeadLettered 不可更新）、`SELECT FOR UPDATE SKIP LOCKED` + lease 防重入。

## 快速開始（本機）
1) 準備 PostgreSQL 15（預設帳密：`postgres/5512355123k`，DB=`webhook_delivery`），套用 schema：  
```bash
psql -U postgres -c "CREATE DATABASE webhook_delivery;"
psql -U postgres -d webhook_delivery -f src/WebhookDelivery.Database/Migrations/001_InitialSchema.sql
```

2) 啟動服務（Docker）：  
```bash
docker-compose up -d   # 已暴露 5001/5002/5003
```
若不用 Docker，請在各專案資料夾自行 `dotnet run` 並調整 appsettings 連線字串。

3) 一鍵驗證：  
```powershell
.\smoke-test.ps1 -CallbackUrl "https://httpbin.org/status/200"
```
腳本會建立訂閱、送事件、等待 10 秒並印出 saga/job 狀態。

## 服務端點
- Subscription API: `http://localhost:5001/swagger`
- Event Ingestion: `http://localhost:5002/api/events` (POST)
- Dead Letter API: `http://localhost:5003/swagger`

API Key（可選）：在環境變數或 appsettings 設定 `Security:ApiKey`，啟用後呼叫需帶 `X-Api-Key`，未設定則不驗證。

## 測試
```bash
dotnet test WebhookDelivery.sln
```
整合測試 17 項聚焦 DB 權限與終態保護。

## 專案結構
```
src/
  WebhookDelivery.Core/            # 模型/介面/觀測
  WebhookDelivery.Database/        # SQL schema / roles
  WebhookDelivery.EventIngestion/  # 事件入口
  WebhookDelivery.SubscriptionApi/ # 訂閱 API
  WebhookDelivery.Router/          # 路由 worker
  WebhookDelivery.Orchestrator/    # Saga 協調
  WebhookDelivery.Worker/          # Job worker + lease cleaner
  WebhookDelivery.DeadLetter/      # 死信 API

tests/
  WebhookDelivery.IntegrationTests/

scripts/
  smoke-test.ps1                   # 一鍵驗證流程
```

## 待辦 / 加值方向
- 訂閱 callback 簽章、防重放；結構化 logging/metrics。
- Router 持久化 offset 或改用 CDC/訊息匯流排。
- 部署 CI/CD、自動健康檢查與監控。
