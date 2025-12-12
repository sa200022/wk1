# Database Permissions Matrix

?¬æ?ä»¶å?ç¾©ä? Webhook Delivery System ?„è??™åº«æ¬Šé??©é™£ï¼Œåš´?¼éµå¾ªæ?å°æ??å??‡ã€?

## æ¬Šé?ç¸½è¦½è¡?

| è§’è‰² Role | events | subscriptions | sagas | jobs | dead_letters |
|-----------|--------|---------------|-------|------|--------------|
| **event_ingest_writer** | SELECT, INSERT | SELECT | ??| ??| ??|
| **router_worker** | SELECT | SELECT | SELECT, INSERT | ??| ??|
| **saga_orchestrator** â­?| SELECT | SELECT | SELECT, INSERT, **UPDATE** | SELECT, INSERT, UPDATE | SELECT, INSERT |
| **job_worker** | SELECT | SELECT | SELECT | SELECT, INSERT, UPDATE | ??|
| **dead_letter_operator** | SELECT | SELECT | SELECT, INSERT | ??| SELECT |
| **subscription_admin** | ??| SELECT, INSERT, UPDATE | ??| ??| ??|

**â­?CRITICAL**: `saga_orchestrator` ??*?¯ä?**?¯ä»¥ UPDATE sagas ?„è??²ï?

---

## è§’è‰²è©³ç´°èªªæ?

### 1. event_ingest_writer (äº‹ä»¶å¯«å…¥?å?)

**?¨é€?*: Event Ingestion Service
**è²¬ä»»**: ?ªèƒ½è¿½å?äº‹ä»¶ï¼Œä??¯ä¿®??

**?è¨±?ä?**:
- ??`INSERT` events (?ªèƒ½è¿½å??°ä?ä»?
- ??`SELECT` events (è®€?–ç”¨?¼å»??
- ??`SELECT` subscriptions (è®€?–è??±é?ç½?

**ç¦æ­¢?ä?**:
- ??`UPDATE` events (äº‹ä»¶ä¸å¯è®?
- ??`DELETE` events (äº‹ä»¶ä¸å¯?ªé™¤)
- ??ä»»ä? sagas ?ä? (è·¯ç”±ä¸æ˜¯å®ƒç?è²¬ä»»)
- ??ä»»ä? jobs ?ä?
- ??ä»»ä? dead_letters ?ä?

**å®‰å…¨æª¢æŸ¥é»?*:
```sql
-- ?‰è©²å¤±æ? (æ²’æ?æ¬Šé?)
UPDATE events SET payload = '{}' WHERE id = 1;  -- ???‰è©²å¤±æ?
INSERT INTO webhook_delivery_sagas (...);        -- ???‰è©²å¤±æ?
```

---

### 2. router_worker (è·¯ç”± Worker)

**?¨é€?*: Routing Worker Service
**è²¬ä»»**: ?ºæ???(event, subscription) çµ„å?å»ºç? saga

**?è¨±?ä?**:
- ??`SELECT` events (è®€?–å?è·¯ç”±?„ä?ä»?
- ??`SELECT` subscriptions (?¥è©¢ active è¨‚é–±)
- ??`SELECT` sagas (æª¢æŸ¥?¯å¦å·²è·¯??
- ??`INSERT` sagas (å»ºç???sagaï¼Œå†ªç­?

**ç¦æ­¢?ä?**:
- ??`UPDATE` sagas (ä¸èƒ½ä¿®æ”¹ saga ?€?‹ï?)
- ??`DELETE` sagas
- ??ä»»ä? jobs ?ä? (ä¸èƒ½å»ºç? job)
- ??ä¿®æ”¹ events ??subscriptions

**å®‰å…¨æª¢æŸ¥é»?*:
```sql
-- ?‰è©²?å?
INSERT INTO webhook_delivery_sagas (...) ON CONFLICT (event_id, subscription_id) DO UPDATE SET event_id = EXCLUDED.event_id RETURNING id;  -- ??

-- ?‰è©²å¤±æ?
UPDATE webhook_delivery_sagas SET status = 'InProgress' WHERE id = 1;  -- ??
INSERT INTO webhook_delivery_jobs (...);  -- ??
```

---

### 3. saga_orchestrator â­?(Saga ç·¨æ???

**?¨é€?*: Saga Orchestrator Service
**è²¬ä»»**: **?¯ä?**?¯ä»¥?´æ–° saga ?€?‹ç?æ¨¡ç?ï¼Œæ§?¶æ•´?‹æ??æ?ç¨?

**?è¨±?ä?**:
- ??`SELECT` events (è®€??payload ?¨æ–¼ dead letter å¿«ç…§)
- ??`SELECT` subscriptions (è®€?–è??±è?è¨?
- ??`SELECT`, `INSERT`, **`UPDATE`** sagas â­?**?¯ä???UPDATE**
- ??`SELECT`, `INSERT`, `UPDATE` jobs (å»ºç??‡è???job çµæ?)
- ??`SELECT`, `INSERT` dead_letters (å»ºç?æ­»ä¿¡è¨˜é?)

**ç¦æ­¢?ä?**:
- ??`DELETE` sagas (saga æ°¸ä??ªé™¤)
- ??`UPDATE` events (äº‹ä»¶ä¸å¯è®?
- ??`UPDATE` subscriptions (ä¸è?è²¬è??±ç®¡??
- ??`UPDATE` dead_letters (dead letter ä¸å¯è®?

**?œéµ?¹æ€?*:
- ?? **çµ‚æ­¢?€?‹ä?è­?*: ç¨‹å?ç¢¼å±¤ç´šç?æ­¢æ›´??`Completed` ??`DeadLettered` ??saga
- ?? **?ªç???*: ?è??•ç??Œä???job çµæ?ä¸æ?å½±éŸ¿ saga ?€??

**å®‰å…¨æª¢æŸ¥é»?*:
```sql
-- ?‰è©²?å? (?¯ä??¯ä»¥?šé€™å€‹ç?è§’è‰²)
UPDATE webhook_delivery_sagas SET status = 'InProgress' WHERE id = 1;  -- ??
INSERT INTO webhook_delivery_jobs (...);  -- ??
INSERT INTO dead_letters (...);  -- ??

-- ?‰è©²å¤±æ?
DELETE FROM webhook_delivery_sagas WHERE id = 1;  -- ??
UPDATE events SET payload = '{}' WHERE id = 1;  -- ??
```

---

### 4. job_worker (Job Worker)

**?¨é€?*: Job Worker Service + Lease Reset Cleaner
**è²¬ä»»**: ?·è? HTTP ?•é?ï¼Œå??±ç???

**?è¨±?ä?**:
- ??`SELECT` events (è®€??payload ?¨æ–¼?•é?)
- ??`SELECT` subscriptions (è®€??callback_url)
- ??`SELECT` sagas (è®€??saga è³‡è?)
- ??`SELECT`, `INSERT`, `UPDATE` jobs (?–å??æ›´??job ?€??

**ç¦æ­¢?ä?** (CRITICAL):
- ??`INSERT` sagas (**ç¦æ­¢å»ºç? saga**)
- ??`UPDATE` sagas (**ç¦æ­¢ä¿®æ”¹ saga ?€?‹ï?**)
- ??`DELETE` sagas
- ??ä»»ä? events å¯«å…¥?ä?
- ??ä»»ä? subscriptions å¯«å…¥?ä?
- ??ä»»ä? dead_letters ?ä?

**å®‰å…¨æª¢æŸ¥é»?*:
```sql
-- ?‰è©²?å?
UPDATE webhook_delivery_jobs SET status = 'Completed', response_status = 200 WHERE id = 1;  -- ??

-- ?‰è©²å¤±æ? (CRITICAL)
UPDATE webhook_delivery_sagas SET status = 'Completed' WHERE id = 1;  -- ??çµ•å?ç¦æ­¢ï¼?
INSERT INTO webhook_delivery_sagas (...);  -- ??
UPDATE webhook_delivery_sagas SET attempt_count = attempt_count + 1 WHERE id = 1;  -- ??
```

---

### 5. dead_letter_operator (æ­»ä¿¡?ä???

**?¨é€?*: Dead Letter Service (Requeue API)
**è²¬ä»»**: è®€?–æ­»ä¿¡ï?å»ºç???saga ?¨æ–¼ requeue

**?è¨±?ä?**:
- ??`SELECT` dead_letters (è®€?–æ­»ä¿¡è???
- ??`SELECT` events (è®€?–ä?ä»¶è?è¨?
- ??`SELECT` subscriptions (è®€?–è??±è?è¨?
- ??`SELECT` sagas (æª¢æŸ¥?€??
- ??`INSERT` sagas (å»ºç?**??* saga ?¨æ–¼ requeue)

**ç¦æ­¢?ä?**:
- ??`UPDATE` sagas (ä¸èƒ½ä¿®æ”¹??sagaï¼?
- ??`DELETE` sagas
- ??`UPDATE` dead_letters (dead letter ä¸å¯è®?
- ??`DELETE` dead_letters
- ??ä»»ä? jobs ?ä?

**?œéµ?Ÿå?**:
- ?? **Requeue å¿…é?å»ºç???saga**: ä¸å¯ä¿®æ”¹?Šç? `DeadLettered` saga
- ?? ??saga ??`status = Pending`, `attempt_count = 0`

**å®‰å…¨æª¢æŸ¥é»?*:
```sql
-- ?‰è©²?å? (å»ºç???saga)
INSERT INTO webhook_delivery_sagas (event_id, subscription_id, status, attempt_count, next_attempt_at)
VALUES (1, 1, 'Pending', 0, NOW());  -- ??

-- ?‰è©²å¤±æ?
UPDATE webhook_delivery_sagas SET status = 'Pending' WHERE id = 1;  -- ??
UPDATE dead_letters SET final_error_code = NULL WHERE id = 1;  -- ??
```

---

### 6. subscription_admin (è¨‚é–±ç®¡ç???

**?¨é€?*: Subscription API
**è²¬ä»»**: è¨‚é–±??CRUD ?ä?

**?è¨±?ä?**:
- ??`SELECT`, `INSERT`, `UPDATE` subscriptions

**ç¦æ­¢?ä?**:
- ??ä»»ä? events ?ä? (ä¸è?è²¬ä?ä»?
- ??ä»»ä? sagas ?ä? (ä¸è?è²¬é?è¡Œæ??€??
- ??ä»»ä? jobs ?ä?
- ??ä»»ä? dead_letters ?ä?

**å®‰å…¨æª¢æŸ¥é»?*:
```sql
-- ?‰è©²?å?
UPDATE subscriptions SET active = 0 WHERE id = 1;  -- ??

-- ?‰è©²å¤±æ?
SELECT * FROM webhook_delivery_sagas;  -- ??
UPDATE events SET payload = '{}' WHERE id = 1;  -- ??
```

---

## æ¬Šé?é©—è??³æœ¬

### İÒİä??£á????£¹£á?

```sql
SELECT rolname FROM pg_roles
WHERE rolname IN (
    'event_ingest_writer',
    'router_worker',
    'saga_orchestrator',
    'job_worker',
    'dead_letter_operator',
    'subscription_admin'
);
```

### ©c¤GŞv?ºÀ?·ê???????

```sql
SELECT grantee, table_name, privilege_type
FROM information_schema.role_table_grants
WHERE grantee IN (
    'event_ingest_writer',
    'router_worker',
    'saga_orchestrator',
    'job_worker',
    'dead_letter_operator',
    'subscription_admin'
)
ORDER BY grantee, table_name, privilege_type;
```
```

### é©—è? saga_orchestrator ?¯å”¯ä¸€??UPDATE sagas ?„è???

```sql
-- ?™å€‹æŸ¥è©¢æ?è©²åªè¿”å? saga_orchestrator
SELECT DISTINCT Grantee
FROM information_schema.TABLE_PRIVILEGES
WHERE TABLE_SCHEMA = 'webhook_delivery'
  AND TABLE_NAME = 'webhook_delivery_sagas'
  AND PRIVILEGE_TYPE = 'UPDATE';

-- ?æ?çµæ?: 'saga_orchestrator'@'%'
```

---

## å®‰å…¨æ¸…å–®

- [ ] ?€?‰å?ç¢¼ä½¿?¨å¼·å¯†ç¢¼ (?³å? 32 å­—å?ï¼Œéš¨æ©Ÿç???
- [ ] ?Ÿç”¢?°å?å¯†ç¢¼å­˜æ–¼å¯†é‘°ç®¡ç?ç³»çµ± (Vault, AWS Secrets Manager)
- [ ] ?Ÿç”¨ SSL/TLS ??? (`REQUIRE SSL`)
- [ ] ?åˆ¶???ä¾†æ? IP (å°?`%` ?¹ç‚º?¹å? IP/ç¶²æ®µ)
- [ ] å®šæ?è¼ªæ?å¯†ç¢¼ (å»ºè­°æ¯?90 å¤?
- [ ] ?Ÿç”¨è³‡æ?åº«ç¨½?¸æ—¥èª?
- [ ] è¨­å?????¸é???(?²æ­¢ DoS)
- [ ] é©—è? `job_worker` **çµ•å??¡æ?** UPDATE sagas
- [ ] é©—è??ªæ? `saga_orchestrator` ?¯ä»¥ UPDATE sagas

---

## å¸¸è??é?

### Q: ?ºä?éº?worker ä¸èƒ½ UPDATE sagasï¼?
**A**: ?™æ˜¯?¸å??¶æ??Ÿå?ï¼å???worker ?¯ä»¥ä¿®æ”¹ sagaï¼Œæ??´å??€?‹æ??„å–®ä¸€è²¬ä»»ï¼Œå??´ï?
- ä½µç™¼è¡ç?
- ?€?‹ä?ä¸€??
- ?¡æ?ä¿è??ªç???
- ??»¥è¿½è¹¤?€?‹è???

### Q: Requeue ?ºä?éº¼è?å»ºç???sagaï¼?
**A**:
- ä¿æ? DeadLettered saga ä¸å¯è®Šï??¨æ–¼ç¨½æ ¸ï¼?
- ?¿å??´å?çµ‚æ­¢?€?‹ä?è­?
- ??saga ?æ–°è¨ˆç? attempt_count ?‡é?è©¦æ???

### Q: å¦‚ä?æ¸¬è©¦æ¬Šé??¯å¦æ­?¢ºï¼?
**A**: ä½¿ç”¨?„è??²ç?å¸³è??·è?ä¸è©²?‰æ??ç??ä?ï¼Œç¢ºèªæ?å¤±æ???

---

**?€å¾Œæ›´??*: 2025-12-11
**?ˆæœ¬**: 1.0

