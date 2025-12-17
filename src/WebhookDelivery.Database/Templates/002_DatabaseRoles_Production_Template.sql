-- ============================================================================
-- Webhook Delivery System - Database Roles (Production Template, PostgreSQL)
-- ============================================================================
-- IMPORTANT:
-- 1) Replace all placeholder passwords before running.
-- 2) Enforce TLS/SSL via Postgres server config + pg_hba.conf (not via SQL).
-- 3) Restrict network access at the firewall / security group level.
-- 4) Consider using secrets manager + rotation.
-- ============================================================================

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'event_ingest_writer') THEN
        CREATE ROLE event_ingest_writer LOGIN PASSWORD 'REPLACE_ME_EVENT_INGEST';
    END IF;

    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'router_worker') THEN
        CREATE ROLE router_worker LOGIN PASSWORD 'REPLACE_ME_ROUTER_WORKER';
    END IF;

    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'saga_orchestrator') THEN
        CREATE ROLE saga_orchestrator LOGIN PASSWORD 'REPLACE_ME_ORCHESTRATOR';
    END IF;

    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'job_worker') THEN
        CREATE ROLE job_worker LOGIN PASSWORD 'REPLACE_ME_JOB_WORKER';
    END IF;

    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'dead_letter_operator') THEN
        CREATE ROLE dead_letter_operator LOGIN PASSWORD 'REPLACE_ME_DEAD_LETTER';
    END IF;

    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'subscription_admin') THEN
        CREATE ROLE subscription_admin LOGIN PASSWORD 'REPLACE_ME_SUBSCRIPTION_ADMIN';
    END IF;
END$$;

-- Database + schema usage
GRANT CONNECT ON DATABASE webhook_delivery TO
    event_ingest_writer, router_worker, saga_orchestrator,
    job_worker, dead_letter_operator, subscription_admin;
GRANT USAGE ON SCHEMA public TO
    event_ingest_writer, router_worker, saga_orchestrator,
    job_worker, dead_letter_operator, subscription_admin;
GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA public TO
    event_ingest_writer, router_worker, saga_orchestrator,
    job_worker, dead_letter_operator, subscription_admin;

-- event_ingest_writer: append-only events
GRANT SELECT, INSERT ON events TO event_ingest_writer;
GRANT SELECT ON subscriptions TO event_ingest_writer;

-- router_worker: read events/subscriptions, insert sagas, update router offset only
GRANT SELECT ON events, subscriptions TO router_worker;
GRANT SELECT, INSERT ON webhook_delivery_sagas TO router_worker;
GRANT SELECT, INSERT, UPDATE ON router_offsets TO router_worker;

-- saga_orchestrator: manage sagas + jobs + dead letters
GRANT SELECT, UPDATE ON webhook_delivery_sagas TO saga_orchestrator;
GRANT SELECT, INSERT, UPDATE ON webhook_delivery_jobs TO saga_orchestrator;
GRANT SELECT ON events, subscriptions TO saga_orchestrator;
GRANT SELECT, INSERT ON dead_letters TO saga_orchestrator;

-- job_worker: update jobs only
GRANT SELECT, UPDATE ON webhook_delivery_jobs TO job_worker;
GRANT SELECT ON webhook_delivery_sagas, subscriptions, events TO job_worker;

-- dead_letter_operator: read + requeue
GRANT SELECT ON events, subscriptions, webhook_delivery_sagas, dead_letters TO dead_letter_operator;
GRANT INSERT ON webhook_delivery_sagas, dead_letters TO dead_letter_operator;

-- subscription_admin: manage subscriptions
GRANT SELECT, INSERT, UPDATE ON subscriptions TO subscription_admin;
