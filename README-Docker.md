# Docker 部署指南

## 📦 檔案結構

```
wk1/
├── docker-compose.yml          # 完整部署配置
├── docker-compose.dev.yml      # 本地開發配置（僅 MySQL）
├── .env.example                # 環境變數範本
├── .dockerignore               # Docker build 排除檔案
└── src/
    ├── WebhookDelivery.EventIngestion/Dockerfile
    ├── WebhookDelivery.SubscriptionApi/Dockerfile
    ├── WebhookDelivery.Router/Dockerfile
    ├── WebhookDelivery.Orchestrator/Dockerfile
    ├── WebhookDelivery.Worker/Dockerfile
    └── WebhookDelivery.DeadLetter/Dockerfile
```

---

## 🚀 快速開始

### 1. 本地開發（僅啟動 MySQL）

```bash
# 啟動 MySQL 容器
docker-compose -f docker-compose.dev.yml up -d

# 查看狀態
docker-compose -f docker-compose.dev.yml ps

# 查看日誌
docker-compose -f docker-compose.dev.yml logs -f mysql

# 停止
docker-compose -f docker-compose.dev.yml down
```

MySQL 連線資訊：
- **Host**: `localhost`
- **Port**: `3306`
- **Database**: `webhook_delivery`
- **Username**: `root`
- **Password**: `test`

然後在 Visual Studio / Rider 中直接運行各個服務。

---

### 2. 完整部署（所有服務 + MySQL）

#### 步驟 1: 準備環境變數

```bash
# 複製環境變數範本
cp .env.example .env

# 編輯 .env 檔案，替換所有密碼
nano .env
```

#### 步驟 2: 建立並啟動所有服務

```bash
# 建立 Docker images 並啟動
docker-compose up -d --build

# 查看所有服務狀態
docker-compose ps

# 查看特定服務日誌
docker-compose logs -f orchestrator
docker-compose logs -f worker

# 查看所有日誌
docker-compose logs -f
```

#### 步驟 3: 驗證服務

```bash
# 檢查 MySQL 健康狀態
docker exec webhook_delivery_mysql mysqladmin ping -h localhost -u root -ptest

# 測試 Subscription API (Swagger)
curl http://localhost:5001/swagger/index.html

# 測試 DeadLetter API (Swagger)
curl http://localhost:5003/swagger/index.html

# 檢查資料庫使用者
docker exec webhook_delivery_mysql mysql -u root -ptest -e "SELECT User, Host FROM mysql.user WHERE User LIKE '%event%' OR User LIKE '%router%' OR User LIKE '%saga%' OR User LIKE '%worker%' OR User LIKE '%dead%' OR User LIKE '%subscription%';"
```

#### 步驟 4: 停止服務

```bash
# 停止所有服務（保留資料）
docker-compose down

# 停止並刪除所有資料（慎用！）
docker-compose down -v
```

---

## 🔧 常用命令

### 查看日誌

```bash
# 即時查看所有日誌
docker-compose logs -f

# 查看特定服務
docker-compose logs -f mysql
docker-compose logs -f orchestrator
docker-compose logs -f worker

# 查看最近 100 行
docker-compose logs --tail=100 orchestrator
```

### 重啟服務

```bash
# 重啟單一服務
docker-compose restart orchestrator

# 重啟所有服務
docker-compose restart
```

### 重建服務

```bash
# 重建特定服務
docker-compose up -d --build orchestrator

# 重建所有服務
docker-compose up -d --build
```

### 進入容器

```bash
# 進入 MySQL 容器
docker exec -it webhook_delivery_mysql bash

# 連接 MySQL
docker exec -it webhook_delivery_mysql mysql -u root -ptest webhook_delivery

# 進入應用程式容器
docker exec -it webhook_delivery_orchestrator bash
```

---

## 📊 監控與除錯

### 檢查服務健康狀態

```bash
# 查看所有容器狀態
docker-compose ps

# 查看特定服務健康檢查
docker inspect webhook_delivery_mysql | grep -A 10 Health
```

### 查看資源使用

```bash
# 查看 CPU / Memory 使用
docker stats

# 查看特定服務
docker stats webhook_delivery_orchestrator webhook_delivery_worker
```

### 資料庫維護

```bash
# 備份資料庫
docker exec webhook_delivery_mysql mysqldump -u root -ptest webhook_delivery > backup_$(date +%Y%m%d_%H%M%S).sql

# 還原資料庫
cat backup.sql | docker exec -i webhook_delivery_mysql mysql -u root -ptest webhook_delivery

# 查看 Saga 狀態分佈
docker exec webhook_delivery_mysql mysql -u root -ptest webhook_delivery -e "SELECT status, COUNT(*) as count FROM webhook_delivery_sagas GROUP BY status;"

# 查看 Job 狀態分佈
docker exec webhook_delivery_mysql mysql -u root -ptest webhook_delivery -e "SELECT status, COUNT(*) as count FROM webhook_delivery_jobs GROUP BY status;"
```

---

## 🔐 安全性設定

### 生產環境建議

1. **強密碼**
   ```bash
   # 使用 openssl 生成隨機密碼
   openssl rand -base64 32
   ```

2. **SSL/TLS 連線**
   ```yaml
   # 在 docker-compose.yml 中更新連線字串
   ConnectionStrings__DefaultConnection: "Server=mysql;Port=3306;Database=webhook_delivery;Uid=xxx;Pwd=xxx;SslMode=Required;CharSet=utf8mb4;"
   ```

3. **限制網路存取**
   ```yaml
   # 移除不必要的 port 暴露
   # 使用內部網路
   ```

4. **使用 Docker Secrets**
   ```bash
   # 建立 secret
   echo "my_secret_password" | docker secret create db_password -

   # 在 docker-compose.yml 中使用
   secrets:
     - db_password
   ```

---

## 🐛 常見問題

### Q: 容器無法啟動

```bash
# 查看詳細錯誤
docker-compose logs [service_name]

# 檢查 Docker daemon
docker info

# 清理舊的容器和 images
docker system prune -a
```

### Q: MySQL 初始化失敗

```bash
# 刪除 volume 重新初始化
docker-compose down -v
docker-compose up -d mysql

# 查看初始化日誌
docker-compose logs mysql
```

### Q: 服務無法連接到 MySQL

```bash
# 確認 MySQL 已就緒
docker-compose ps mysql

# 檢查網路連接
docker network inspect webhook_delivery_network

# 從服務容器測試連線
docker exec webhook_delivery_orchestrator ping mysql
```

### Q: 效能問題

```bash
# 增加 MySQL 記憶體配置
# 在 docker-compose.yml 的 mysql 服務加入：
command: --innodb-buffer-pool-size=1G --max-connections=500

# 調整服務並行數
# 使用 docker-compose scale（需調整配置支援多實例）
```

---

## 📈 擴展部署

### 多 Worker 實例

```bash
# 啟動 3 個 worker 實例
docker-compose up -d --scale worker=3
```

### 使用 Docker Swarm

```bash
# 初始化 Swarm
docker swarm init

# 部署 stack
docker stack deploy -c docker-compose.yml webhook_delivery

# 查看服務
docker service ls

# 擴展服務
docker service scale webhook_delivery_worker=5
```

### 使用 Kubernetes

參考 `k8s/` 資料夾中的 Kubernetes manifests（如有）。

---

## 📝 環境變數說明

| 變數名稱 | 說明 | 預設值 |
|---------|------|--------|
| `MYSQL_ROOT_PASSWORD` | MySQL root 密碼 | `root_password_change_me` |
| `DB_PASSWORD_EVENT_INGEST` | event_ingest_writer 密碼 | `dev_password_event_ingest` |
| `DB_PASSWORD_ROUTER_WORKER` | router_worker 密碼 | `dev_password_router` |
| `DB_PASSWORD_SAGA_ORCHESTRATOR` | saga_orchestrator 密碼 | `dev_password_orchestrator` |
| `DB_PASSWORD_JOB_WORKER` | job_worker 密碼 | `dev_password_worker` |
| `DB_PASSWORD_DEAD_LETTER_OPERATOR` | dead_letter_operator 密碼 | `dev_password_deadletter` |
| `DB_PASSWORD_SUBSCRIPTION_ADMIN` | subscription_admin 密碼 | `dev_password_subscription` |

---

## 🎯 下一步

- 設定 CI/CD pipeline 自動建立 Docker images
- 配置 Prometheus + Grafana 監控
- 設定 ELK Stack 集中式日誌
- 實作 Kubernetes Helm Charts
- 配置 Auto-scaling 策略

---

**最後更新**: 2025-12-11
