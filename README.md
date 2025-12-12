# Webhook Delivery System

基於 Saga 模式的高可靠性 Webhook 投遞系統,實作嚴格的狀態機與冪等性保證。

## 系統架構

```
Event → Routing → Saga Orchestrator → Job Worker → HTTP Delivery
                      ↓ (失敗)
                 Dead Letter → Requeue
```

## 核心模組

### Phase 1: 資料庫 Schema
- **檔案**: `src/WebhookDelivery.Database/Migrations/001_InitialSchema.sql`
- **內容**: 5 個核心資料表 (events, subscriptions, sagas, jobs, dead_letters)
- **特性**: InnoDB 引擎, UTC 時戳, 唯一鍵保證冪等

### Phase 2: Event Ingestion Service
- **檔案**: `src/WebhookDelivery.EventIngestion/`
- **責任**: 只負責 INSERT events,不可修改 saga/job/dead_letters
- **權限**: event_ingest_writer 角色

### Phase 3: Subscription API
- **檔案**: `src/WebhookDelivery.SubscriptionApi/`
- **責任**: 純配置管理,不執行流程控制
- **驗證**: 強制 HTTPS callback_url, 驗證流程後才能啟用

### Phase 4: Routing Worker
- **檔案**: `src/WebhookDelivery.Router/`
- **責任**: 為每個事件建立 saga (Event → Saga)
- **冪等**: 透過 unique(event_id, subscription_id) 保證

### Phase 5: Saga Orchestrator ⭐ 核心
- **檔案**: `src/WebhookDelivery.Orchestrator/`
- **責任**:
  - 唯一可修改 saga 狀態的模組
  - 建立 job
  - 處理重試邏輯 (指數退避)
  - 決定何時 dead-letter
- **狀態機**: Pending → InProgress → PendingRetry → Completed/DeadLettered

### Phase 6: Job Workers
- **檔案**: `src/WebhookDelivery.Worker/`
- **責任**:
  - 執行 HTTP 投遞
  - 回報結果 (Completed/Failed)
  - **禁止**: 修改 saga, 建立 job, 決定重試邏輯
- **併發控制**: `SELECT FOR UPDATE SKIP LOCKED`
- **租約機制**: Lease Reset Cleaner 自動重置過期 job

### Phase 7: Dead Letter System
- **檔案**: `src/WebhookDelivery.DeadLetter/`
- **責任**:
  - 保存永久失敗的 saga
  - Requeue 建立「全新 saga」(不修改舊 saga)
  - 保留 payload 快照

### Phase 8: 可觀測性
- **檔案**: `src/WebhookDelivery.Core/Observability/`
- **內容**:
  - Correlation ID 追蹤
  - 各階段 metrics 定義
  - 整合測試框架

## 關鍵設計原則

### 1. 冪等性 (Idempotency)
- 所有寫入都能安全重試
- 使用 DB unique key 強制 (非記憶體狀態)
- 範例:
  - `unique(event_id, subscription_id)` → 路由冪等
  - `unique(saga_id, attempt_at)` → job 建立冪等

### 2. 責任分層 (Separation of Concerns)
- **Event Layer**: 只能追加,不可修改
- **Subscription Layer**: 純配置,不做流程控制
- **Saga Layer**: 唯一的狀態控制核心
- **Job Layer**: 只做投遞,不做決策
- **Dead Letter Layer**: 只做保存與重新佇列

### 3. 終止狀態保護 (Terminal State Protection)
- Completed/DeadLettered 狀態不可再被修改
- Requeue 必須建立新 saga,不修改舊 saga

### 4. 併發安全 (Concurrency Safety)
- `SELECT FOR UPDATE SKIP LOCKED` 避免鎖競爭
- Row-level locking (禁用 table-level lock)
- Lease-based 租約機制

## 資料庫權限模型

| 角色 | 允許操作 | 禁止操作 |
|------|---------|---------|
| event_ingest_writer | INSERT events | 修改 saga/job |
| router_worker | INSERT saga | 修改 saga status, 建立 job |
| saga_orchestrator | UPDATE saga, CREATE job | DELETE saga, 修改 event |
| job_worker | UPDATE job | 修改 saga |
| dead_letter_operator | INSERT saga (requeue) | UPDATE 舊 saga |

## 執行環境

- **Runtime**: .NET 8.0 (C#)
- **Database**: PostgreSQL 15
- **Time Standard**: UTC
- **Serialization**: JSON (RFC8259)

## 重試策略

- **演算法**: 指數退避 `base_delay * (2^(attempt_count - 1))`
- **預設**: base_delay = 30 秒, max_retry = 5 次
- **上限**: 單次延遲最多 1 小時

## 測試場景

整合測試涵蓋:
1. 重複事件寫入 → 不生成重複 event
2. 重複路由 → 不生成重複 saga
3. Worker 當機 → Lease cleaner 自動重置
4. 重試直到 dead letter
5. Requeue 建立新 saga

## 專案結構

```
src/
├── WebhookDelivery.Core/              # 共用模型與介面
├── WebhookDelivery.Database/          # Schema 與 migrations
├── WebhookDelivery.EventIngestion/    # 事件寫入服務
├── WebhookDelivery.SubscriptionApi/   # 訂閱管理 API
├── WebhookDelivery.Router/            # 路由 worker
├── WebhookDelivery.Orchestrator/      # Saga 編排器 ⭐
├── WebhookDelivery.Worker/            # Job workers
└── WebhookDelivery.DeadLetter/        # 死信系統

tests/
└── WebhookDelivery.IntegrationTests/  # 整合測試

rule/r0/
├── 流程.txt      # 8 階段實作流程
├── r01.txt      # Saga 規格
├── r02.txt      # Job & Dead Letter 規格
├── r03.txt      # 系統基礎原則
└── r04.txt      # Event & Subscription 規格
```

## 下一步

1. 實作 repository 的具體查詢邏輯
2. 設定 dependency injection 與組態檔
3. 實作完整的整合測試
4. 加入 metrics 與 logging 輸出
5. 部署與監控設定

## 參考規格

所有實作嚴格遵循 `rule/r0/` 中的規格文件:
- **r01.txt**: Delivery Saga 核心狀態機
- **r02.txt**: Job 執行模型與死信處理
- **r03.txt**: 冪等性、併發與權限原則
- **r04.txt**: 事件與訂閱路由規則
