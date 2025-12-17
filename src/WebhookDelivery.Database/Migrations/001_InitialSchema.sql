-- ============================================================================
-- Webhook Delivery System - Initial Schema Migration (PostgreSQL)
-- ============================================================================
-- Description: Creates all required tables for webhook delivery system
-- Version: 001
-- Date: 2025-12-11
-- Target: PostgreSQL 15+ (tested with pgAdmin / psql)
-- ============================================================================

-- Drop tables in dependency order (idempotent for local dev/testing)
DROP TABLE IF EXISTS dead_letters CASCADE;
DROP TABLE IF EXISTS webhook_delivery_jobs CASCADE;
DROP TABLE IF EXISTS webhook_delivery_sagas CASCADE;
DROP TABLE IF EXISTS router_offsets CASCADE;
DROP TABLE IF EXISTS subscriptions CASCADE;
DROP TABLE IF EXISTS events CASCADE;
DROP TYPE IF EXISTS saga_status_enum;
DROP TYPE IF EXISTS job_status_enum;

-- ============================================================================
-- Table: events
-- Purpose: Immutable append-only log of all domain events
-- ============================================================================
CREATE TABLE IF NOT EXISTS events (
    id BIGSERIAL PRIMARY KEY,
    external_event_id VARCHAR(255) NULL,
    event_type VARCHAR(100) NOT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
    payload JSONB NOT NULL,
    CONSTRAINT uniq_event_external_id UNIQUE (external_event_id)
);
CREATE INDEX IF NOT EXISTS idx_event_created ON events (created_at);
CREATE INDEX IF NOT EXISTS idx_event_type ON events (event_type, created_at);

-- ============================================================================
-- Table: subscriptions
-- Purpose: Configuration layer defining who receives which events
-- ============================================================================
CREATE TABLE IF NOT EXISTS subscriptions (
    id BIGSERIAL PRIMARY KEY,
    event_type VARCHAR(100) NOT NULL,
    callback_url VARCHAR(500) NOT NULL,
    active BOOLEAN NOT NULL DEFAULT TRUE,
    verified BOOLEAN NOT NULL DEFAULT FALSE,
    created_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP
);
CREATE INDEX IF NOT EXISTS idx_sub_event_type ON subscriptions (event_type);
CREATE INDEX IF NOT EXISTS idx_sub_active ON subscriptions (active);

-- ============================================================================
-- Table: router_offsets
-- Purpose: Persist router progress (last processed event id)
-- ============================================================================
CREATE TABLE IF NOT EXISTS router_offsets (
    id INT PRIMARY KEY DEFAULT 1,
    last_processed_event_id BIGINT NOT NULL DEFAULT 0
);

-- ============================================================================
-- Table: webhook_delivery_sagas
-- Purpose: Orchestration layer coordinating webhook delivery state
-- ============================================================================
CREATE TYPE saga_status_enum AS ENUM ('Pending', 'InProgress', 'PendingRetry', 'Completed', 'DeadLettered');

CREATE TABLE IF NOT EXISTS webhook_delivery_sagas (
    id BIGSERIAL PRIMARY KEY,
    event_id BIGINT NOT NULL REFERENCES events(id),
    subscription_id BIGINT NOT NULL REFERENCES subscriptions(id),
    status saga_status_enum NOT NULL DEFAULT 'Pending',
    attempt_count INT NOT NULL DEFAULT 0,
    next_attempt_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
    final_error_code VARCHAR(100) NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP
);
CREATE UNIQUE INDEX IF NOT EXISTS idx_saga_event_active
    ON webhook_delivery_sagas (event_id, subscription_id)
    WHERE status <> 'DeadLettered';
CREATE INDEX IF NOT EXISTS idx_saga_event ON webhook_delivery_sagas (event_id, subscription_id);
CREATE INDEX IF NOT EXISTS idx_saga_status_retry ON webhook_delivery_sagas (status, next_attempt_at);
CREATE INDEX IF NOT EXISTS idx_saga_status ON webhook_delivery_sagas (status);

-- ============================================================================
-- Table: webhook_delivery_jobs
-- Purpose: Individual atomic delivery attempts
-- ============================================================================
CREATE TYPE job_status_enum AS ENUM ('Pending', 'Leased', 'Completed', 'Failed');

CREATE TABLE IF NOT EXISTS webhook_delivery_jobs (
    id BIGSERIAL PRIMARY KEY,
    saga_id BIGINT NOT NULL REFERENCES webhook_delivery_sagas(id),
    status job_status_enum NOT NULL DEFAULT 'Pending',
    lease_until TIMESTAMPTZ NULL,
    attempt_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
    response_status INT NULL,
    error_code VARCHAR(100) NULL,
    CONSTRAINT uniq_job_saga_attempt UNIQUE (saga_id, attempt_at)
);
CREATE INDEX IF NOT EXISTS idx_job_saga ON webhook_delivery_jobs (saga_id);
CREATE INDEX IF NOT EXISTS idx_job_status_lease ON webhook_delivery_jobs (status, lease_until);

-- ============================================================================
-- Table: dead_letters
-- Purpose: Permanent storage for failed delivery sagas
-- ============================================================================
CREATE TABLE IF NOT EXISTS dead_letters (
    id BIGSERIAL PRIMARY KEY,
    saga_id BIGINT NOT NULL REFERENCES webhook_delivery_sagas(id),
    event_id BIGINT NOT NULL REFERENCES events(id),
    subscription_id BIGINT NOT NULL REFERENCES subscriptions(id),
    final_error_code VARCHAR(100) NULL,
    failed_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
    payload_snapshot JSONB NOT NULL
);
CREATE INDEX IF NOT EXISTS idx_dead_saga ON dead_letters (saga_id);
CREATE INDEX IF NOT EXISTS idx_dead_event ON dead_letters (event_id);

-- ============================================================================
-- Migration Complete
-- ============================================================================
