-- ============================================================================
-- Webhook Delivery System - Database Roles (Development Version, PostgreSQL)
-- ============================================================================
-- Description: Creates minimal-privilege roles for each component (DEV only)
-- Date: 2025-12-12
-- WARNING: Use strong passwords in production and tighten grants as needed.
-- ============================================================================

-- Helper: create role if missing
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'event_ingest_writer') THEN
        CREATE ROLE event_ingest_writer LOGIN PASSWORD 'dev_password_event_ingest';
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'router_worker') THEN
        CREATE ROLE router_worker LOGIN PASSWORD 'dev_password_router';
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'saga_orchestrator') THEN
        CREATE ROLE saga_orchestrator LOGIN PASSWORD 'dev_password_orchestrator';
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'job_worker') THEN
        CREATE ROLE job_worker LOGIN PASSWORD 'dev_password_worker';
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'dead_letter_operator') THEN
        CREATE ROLE dead_letter_operator LOGIN PASSWORD 'dev_password_deadletter';
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'subscription_admin') THEN
        CREATE ROLE subscription_admin LOGIN PASSWORD 'dev_password_subscription';
    END IF;
END$$;

-- Connect + schema usage for all roles
GRANT CONNECT ON DATABASE webhook_delivery TO
    event_ingest_writer, router_worker, saga_orchestrator,
    job_worker, dead_letter_operator, subscription_admin;
GRANT USAGE ON SCHEMA public TO
    event_ingest_writer, router_worker, saga_orchestrator,
    job_worker, dead_letter_operator, subscription_admin;
GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA public TO
    event_ingest_writer, router_worker, saga_orchestrator,
    job_worker, dead_letter_operator, subscription_admin;

-- event_ingest_writer: append-only events, read subscriptions
GRANT SELECT, INSERT ON events TO event_ingest_writer;
GRANT SELECT ON subscriptions TO event_ingest_writer;

-- router_worker: read events/subscriptions, insert sagas
GRANT SELECT ON events, subscriptions TO router_worker;
GRANT SELECT, INSERT ON webhook_delivery_sagas TO router_worker;

-- saga_orchestrator: manage sagas + jobs
GRANT SELECT, UPDATE ON webhook_delivery_sagas TO saga_orchestrator;
GRANT SELECT, INSERT, UPDATE ON webhook_delivery_jobs TO saga_orchestrator;
GRANT SELECT ON events, subscriptions TO saga_orchestrator;

-- job_worker: update jobs only (no saga updates)
GRANT SELECT, UPDATE ON webhook_delivery_jobs TO job_worker;
GRANT SELECT ON webhook_delivery_sagas TO job_worker;

-- dead_letter_operator: create dead letters and requeue sagas
GRANT SELECT ON events, subscriptions, webhook_delivery_sagas TO dead_letter_operator;
GRANT INSERT ON webhook_delivery_sagas, dead_letters TO dead_letter_operator;

-- subscription_admin: manage subscriptions table
GRANT SELECT, INSERT, UPDATE ON subscriptions TO subscription_admin;

-- Default privileges for future tables/sequences (dev convenience)
ALTER DEFAULT PRIVILEGES IN SCHEMA public
    GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO saga_orchestrator, subscription_admin;
ALTER DEFAULT PRIVILEGES IN SCHEMA public
    GRANT USAGE, SELECT ON SEQUENCES TO saga_orchestrator, subscription_admin;
