# Docker 部署（PostgreSQL 版本）

本文件已全面改用 PostgreSQL，舊版 MySQL 指令已移除。

## 目錄結構
- `docker-compose.yml`：完整服務（PostgreSQL + 所有應用）
- `docker-compose.dev.yml`：僅資料庫（PostgreSQL）
- `.env.example`：環境變數樣板
- `src/*/Dockerfile`：各服務的映像檔定義

## 快速開始
### 只啟動資料庫（本地開發）
```bash
docker-compose -f docker-compose.dev.yml up -d
docker-compose -f docker-compose.dev.yml logs -f postgres
docker-compose -f docker-compose.dev.yml down
```
連線資訊：
- Host: `localhost`
- Port: `5432`
- Database: `webhook_delivery`
- User: `postgres`
- Password: `5512355123k`

### 完整服務
```bash
cp .env.example .env
docker-compose up -d --build
docker-compose ps
docker-compose logs -f orchestrator
docker-compose logs -f worker
```
停止服務：
```bash
docker-compose down          # 保留資料
docker-compose down -v       # 連同 volume 一併移除
```

## 常用指令
- 查看日誌：`docker-compose logs -f [service]`
- 重啟服務：`docker-compose restart [service]`
- 重新建置：`docker-compose up -d --build [service]`
- 進入資料庫容器：`docker exec -it webhook_delivery_postgres psql -U postgres -d webhook_delivery`

## 資料庫維運
- 備份：
  ```bash
  docker exec webhook_delivery_postgres pg_dump -U postgres webhook_delivery > backup_$(date +%Y%m%d_%H%M%S).sql
  ```
- 還原：
  ```bash
  cat backup.sql | docker exec -i webhook_delivery_postgres psql -U postgres -d webhook_delivery
  ```
- 查詢常用統計：
  ```bash
  docker exec webhook_delivery_postgres psql -U postgres -d webhook_delivery -c "SELECT status, COUNT(*) FROM webhook_delivery_sagas GROUP BY status;"
  docker exec webhook_delivery_postgres psql -U postgres -d webhook_delivery -c "SELECT status, COUNT(*) FROM webhook_delivery_jobs GROUP BY status;"
  ```

## 健康檢查與監控
- 檢視狀態：`docker-compose ps`
- 查看健康檢查：`docker inspect webhook_delivery_postgres | grep -A 5 Health`
- 即時資源：`docker stats`

## 環境變數（摘錄）
| 變數 | 說明 | 預設值 |
| --- | --- | --- |
| `POSTGRES_PASSWORD` | postgres 密碼 | `5512355123k` |
| `POSTGRES_USER` | DB 超級使用者 | `postgres` |
| `POSTGRES_DB` | 預設資料庫 | `webhook_delivery` |
| `DB_PASSWORD_EVENT_INGEST` | event_ingest_writer 密碼 | `dev_password_event_ingest` |
| `DB_PASSWORD_ROUTER_WORKER` | router_worker 密碼 | `dev_password_router` |
| `DB_PASSWORD_SAGA_ORCHESTRATOR` | saga_orchestrator 密碼 | `dev_password_orchestrator` |
| `DB_PASSWORD_JOB_WORKER` | job_worker 密碼 | `dev_password_worker` |
| `DB_PASSWORD_DEAD_LETTER_OPERATOR` | dead_letter_operator 密碼 | `dev_password_deadletter` |
| `DB_PASSWORD_SUBSCRIPTION_ADMIN` | subscription_admin 密碼 | `dev_password_subscription` |

## 後續建議
- 為 CI/CD 加入映像檔建置與掃描
- 監控可加上 Prometheus/Grafana
- 日誌可串接 ELK 或 Loki
- 若要上 Kubernetes，可將 compose 內容轉為 Helm chart

