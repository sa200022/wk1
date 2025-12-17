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
1) 準備 PostgreSQL 15（請自行設定帳密，DB=`webhook_delivery`），套用 schema：  
```bash
psql -U postgres -c "CREATE DATABASE webhook_delivery;"
psql -U postgres -d webhook_delivery -f src/WebhookDelivery.Database/Migrations/001_InitialSchema.sql
```

2) 啟動服務（Docker）：  
```bash
docker-compose up -d   # 已暴露 5001/5002/5003
```
若不用 Docker，請用環境變數提供連線字串（因為 repo 不硬編碼密碼）：
- 一鍵啟動：`.\start-all-services.ps1`（會設定 `ConnectionStrings__DefaultConnection` 並開 6 個視窗）
- 或自行啟動：`$env:ConnectionStrings__DefaultConnection="Host=localhost;Port=5432;Database=webhook_delivery;Username=postgres;Password=...;"` 後在各專案 `dotnet run`

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

## 環境變數重點
| 變數 | 說明 | 範例 |
| --- | --- | --- |
| `POSTGRES_PASSWORD` | Postgres 管理者密碼 | `change_me_in_production` |
| `DB_PASSWORD_EVENT_INGEST` | event_ingest_writer 密碼 | `dev_password_event_ingest` |
| `DB_PASSWORD_ROUTER_WORKER` | router_worker 密碼 | `dev_password_router` |
| `DB_PASSWORD_SAGA_ORCHESTRATOR` | saga_orchestrator 密碼 | `dev_password_orchestrator` |
| `DB_PASSWORD_JOB_WORKER` | job_worker 密碼 | `dev_password_worker` |
| `DB_PASSWORD_DEAD_LETTER_OPERATOR` | dead_letter_operator 密碼 | `dev_password_deadletter` |
| `DB_PASSWORD_SUBSCRIPTION_ADMIN` | subscription_admin 密碼 | `dev_password_subscription` |
| `API_KEY` | （可選）HTTP API 金鑰，啟用時呼叫需帶 `X-Api-Key` | 空值代表停用 |
| `Worker__WebhookSigningKey` | （可選）Worker 發送 webhook 時的 HMAC-SHA256 簽章金鑰 | 空值代表不簽章 |

## Webhook 簽章（可選）
當設定 `Worker__WebhookSigningKey` 後，Worker 會對送出的 JSON body 做 HMAC-SHA256，並加上 header `X-Webhook-Signature`（hex）。
接收端可用同一把 key 重新計算並比對簽章，用來防止 payload 被竄改（仍建議搭配 HTTPS）。

## 測試
```bash
dotnet test WebhookDelivery.sln
```
整合測試 17 項聚焦 DB 權限與終態保護（需要本機 PostgreSQL）。若未設定測試 DB 連線或無法連線，會自動 Skip。

整合測試需設定：
- `ConnectionStrings__TestDatabase`（例如 `Host=localhost;Port=5432;Username=postgres;Password=...;Database=postgres`）

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
