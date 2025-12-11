# Database Permissions Matrix

本文件定義了 Webhook Delivery System 的資料庫權限矩陣，嚴格遵循最小權限原則。

## 權限總覽表

| 角色 Role | events | subscriptions | sagas | jobs | dead_letters |
|-----------|--------|---------------|-------|------|--------------|
| **event_ingest_writer** | SELECT, INSERT | SELECT | ❌ | ❌ | ❌ |
| **router_worker** | SELECT | SELECT | SELECT, INSERT | ❌ | ❌ |
| **saga_orchestrator** ⭐ | SELECT | SELECT | SELECT, INSERT, **UPDATE** | SELECT, INSERT, UPDATE | SELECT, INSERT |
| **job_worker** | SELECT | SELECT | SELECT | SELECT, INSERT, UPDATE | ❌ |
| **dead_letter_operator** | SELECT | SELECT | SELECT, INSERT | ❌ | SELECT |
| **subscription_admin** | ❌ | SELECT, INSERT, UPDATE | ❌ | ❌ | ❌ |

**⭐ CRITICAL**: `saga_orchestrator` 是**唯一**可以 UPDATE sagas 的角色！

---

## 角色詳細說明

### 1. event_ingest_writer (事件寫入服務)

**用途**: Event Ingestion Service
**責任**: 只能追加事件，不可修改

**允許操作**:
- ✅ `INSERT` events (只能追加新事件)
- ✅ `SELECT` events (讀取用於去重)
- ✅ `SELECT` subscriptions (讀取訂閱配置)

**禁止操作**:
- ❌ `UPDATE` events (事件不可變)
- ❌ `DELETE` events (事件不可刪除)
- ❌ 任何 sagas 操作 (路由不是它的責任)
- ❌ 任何 jobs 操作
- ❌ 任何 dead_letters 操作

**安全檢查點**:
```sql
-- 應該失敗 (沒有權限)
UPDATE events SET payload = '{}' WHERE id = 1;  -- ❌ 應該失敗
INSERT INTO webhook_delivery_sagas (...);        -- ❌ 應該失敗
```

---

### 2. router_worker (路由 Worker)

**用途**: Routing Worker Service
**責任**: 為每個 (event, subscription) 組合建立 saga

**允許操作**:
- ✅ `SELECT` events (讀取待路由的事件)
- ✅ `SELECT` subscriptions (查詢 active 訂閱)
- ✅ `SELECT` sagas (檢查是否已路由)
- ✅ `INSERT` sagas (建立新 saga，冪等)

**禁止操作**:
- ❌ `UPDATE` sagas (不能修改 saga 狀態！)
- ❌ `DELETE` sagas
- ❌ 任何 jobs 操作 (不能建立 job)
- ❌ 修改 events 或 subscriptions

**安全檢查點**:
```sql
-- 應該成功
INSERT INTO webhook_delivery_sagas (...) ON DUPLICATE KEY UPDATE id = LAST_INSERT_ID(id);  -- ✅

-- 應該失敗
UPDATE webhook_delivery_sagas SET status = 'InProgress' WHERE id = 1;  -- ❌
INSERT INTO webhook_delivery_jobs (...);  -- ❌
```

---

### 3. saga_orchestrator ⭐ (Saga 編排器)

**用途**: Saga Orchestrator Service
**責任**: **唯一**可以更新 saga 狀態的模組，控制整個投遞流程

**允許操作**:
- ✅ `SELECT` events (讀取 payload 用於 dead letter 快照)
- ✅ `SELECT` subscriptions (讀取訂閱資訊)
- ✅ `SELECT`, `INSERT`, **`UPDATE`** sagas ⭐ **唯一可 UPDATE**
- ✅ `SELECT`, `INSERT`, `UPDATE` jobs (建立與讀取 job 結果)
- ✅ `SELECT`, `INSERT` dead_letters (建立死信記錄)

**禁止操作**:
- ❌ `DELETE` sagas (saga 永不刪除)
- ❌ `UPDATE` events (事件不可變)
- ❌ `UPDATE` subscriptions (不負責訂閱管理)
- ❌ `UPDATE` dead_letters (dead letter 不可變)

**關鍵特性**:
- 🔒 **終止狀態保護**: 程式碼層級禁止更新 `Completed` 或 `DeadLettered` 的 saga
- 🔒 **冪等性**: 重複處理同一個 job 結果不會影響 saga 狀態

**安全檢查點**:
```sql
-- 應該成功 (唯一可以做這個的角色)
UPDATE webhook_delivery_sagas SET status = 'InProgress' WHERE id = 1;  -- ✅
INSERT INTO webhook_delivery_jobs (...);  -- ✅
INSERT INTO dead_letters (...);  -- ✅

-- 應該失敗
DELETE FROM webhook_delivery_sagas WHERE id = 1;  -- ❌
UPDATE events SET payload = '{}' WHERE id = 1;  -- ❌
```

---

### 4. job_worker (Job Worker)

**用途**: Job Worker Service + Lease Reset Cleaner
**責任**: 執行 HTTP 投遞，回報結果

**允許操作**:
- ✅ `SELECT` events (讀取 payload 用於投遞)
- ✅ `SELECT` subscriptions (讀取 callback_url)
- ✅ `SELECT` sagas (讀取 saga 資訊)
- ✅ `SELECT`, `INSERT`, `UPDATE` jobs (取得、更新 job 狀態)

**禁止操作** (CRITICAL):
- ❌ `INSERT` sagas (**禁止建立 saga**)
- ❌ `UPDATE` sagas (**禁止修改 saga 狀態！**)
- ❌ `DELETE` sagas
- ❌ 任何 events 寫入操作
- ❌ 任何 subscriptions 寫入操作
- ❌ 任何 dead_letters 操作

**安全檢查點**:
```sql
-- 應該成功
UPDATE webhook_delivery_jobs SET status = 'Completed', response_status = 200 WHERE id = 1;  -- ✅

-- 應該失敗 (CRITICAL)
UPDATE webhook_delivery_sagas SET status = 'Completed' WHERE id = 1;  -- ❌ 絕對禁止！
INSERT INTO webhook_delivery_sagas (...);  -- ❌
UPDATE webhook_delivery_sagas SET attempt_count = attempt_count + 1 WHERE id = 1;  -- ❌
```

---

### 5. dead_letter_operator (死信操作員)

**用途**: Dead Letter Service (Requeue API)
**責任**: 讀取死信，建立新 saga 用於 requeue

**允許操作**:
- ✅ `SELECT` dead_letters (讀取死信記錄)
- ✅ `SELECT` events (讀取事件資訊)
- ✅ `SELECT` subscriptions (讀取訂閱資訊)
- ✅ `SELECT` sagas (檢查狀態)
- ✅ `INSERT` sagas (建立**新** saga 用於 requeue)

**禁止操作**:
- ❌ `UPDATE` sagas (不能修改舊 saga！)
- ❌ `DELETE` sagas
- ❌ `UPDATE` dead_letters (dead letter 不可變)
- ❌ `DELETE` dead_letters
- ❌ 任何 jobs 操作

**關鍵原則**:
- 🔒 **Requeue 必須建立新 saga**: 不可修改舊的 `DeadLettered` saga
- 🔒 新 saga 的 `status = Pending`, `attempt_count = 0`

**安全檢查點**:
```sql
-- 應該成功 (建立新 saga)
INSERT INTO webhook_delivery_sagas (event_id, subscription_id, status, attempt_count, next_attempt_at)
VALUES (1, 1, 'Pending', 0, NOW());  -- ✅

-- 應該失敗
UPDATE webhook_delivery_sagas SET status = 'Pending' WHERE id = 1;  -- ❌
UPDATE dead_letters SET final_error_code = NULL WHERE id = 1;  -- ❌
```

---

### 6. subscription_admin (訂閱管理員)

**用途**: Subscription API
**責任**: 訂閱的 CRUD 操作

**允許操作**:
- ✅ `SELECT`, `INSERT`, `UPDATE` subscriptions

**禁止操作**:
- ❌ 任何 events 操作 (不負責事件)
- ❌ 任何 sagas 操作 (不負責運行時狀態)
- ❌ 任何 jobs 操作
- ❌ 任何 dead_letters 操作

**安全檢查點**:
```sql
-- 應該成功
UPDATE subscriptions SET active = 0 WHERE id = 1;  -- ✅

-- 應該失敗
SELECT * FROM webhook_delivery_sagas;  -- ❌
UPDATE events SET payload = '{}' WHERE id = 1;  -- ❌
```

---

## 權限驗證腳本

### 驗證所有使用者

```sql
SELECT User, Host FROM mysql.user
WHERE User IN (
    'event_ingest_writer',
    'router_worker',
    'saga_orchestrator',
    'job_worker',
    'dead_letter_operator',
    'subscription_admin'
);
```

### 檢查特定角色的權限

```sql
SHOW GRANTS FOR 'event_ingest_writer'@'%';
SHOW GRANTS FOR 'router_worker'@'%';
SHOW GRANTS FOR 'saga_orchestrator'@'%';
SHOW GRANTS FOR 'job_worker'@'%';
SHOW GRANTS FOR 'dead_letter_operator'@'%';
SHOW GRANTS FOR 'subscription_admin'@'%';
```

### 驗證 saga_orchestrator 是唯一可 UPDATE sagas 的角色

```sql
-- 這個查詢應該只返回 saga_orchestrator
SELECT DISTINCT Grantee
FROM information_schema.TABLE_PRIVILEGES
WHERE TABLE_SCHEMA = 'webhook_delivery'
  AND TABLE_NAME = 'webhook_delivery_sagas'
  AND PRIVILEGE_TYPE = 'UPDATE';

-- 預期結果: 'saga_orchestrator'@'%'
```

---

## 安全清單

- [ ] 所有密碼使用強密碼 (至少 32 字元，隨機生成)
- [ ] 生產環境密碼存於密鑰管理系統 (Vault, AWS Secrets Manager)
- [ ] 啟用 SSL/TLS 連線 (`REQUIRE SSL`)
- [ ] 限制連線來源 IP (將 `%` 改為特定 IP/網段)
- [ ] 定期輪換密碼 (建議每 90 天)
- [ ] 啟用資料庫稽核日誌
- [ ] 設定連線數限制 (防止 DoS)
- [ ] 驗證 `job_worker` **絕對無法** UPDATE sagas
- [ ] 驗證只有 `saga_orchestrator` 可以 UPDATE sagas

---

## 常見問題

### Q: 為什麼 worker 不能 UPDATE sagas？
**A**: 這是核心架構原則！如果 worker 可以修改 saga，會破壞狀態機的單一責任，導致：
- 併發衝突
- 狀態不一致
- 無法保證冪等性
- 難以追蹤狀態轉換

### Q: Requeue 為什麼要建立新 saga？
**A**:
- 保持 DeadLettered saga 不可變（用於稽核）
- 避免破壞終止狀態保護
- 新 saga 重新計算 attempt_count 與重試時間

### Q: 如何測試權限是否正確？
**A**: 使用各角色的帳號執行不該有權限的操作，確認會失敗。

---

**最後更新**: 2025-12-11
**版本**: 1.0
