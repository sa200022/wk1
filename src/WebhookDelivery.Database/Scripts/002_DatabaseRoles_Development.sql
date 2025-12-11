-- ============================================================================
-- Webhook Delivery System - Database Roles (Development Version)
-- ============================================================================
-- Description: Creates minimal-privilege database roles for each component
-- Version: 002-DEV
-- Date: 2025-12-11
-- WARNING: This is for DEVELOPMENT ONLY. Use strong passwords in production!
-- ============================================================================

-- ============================================================================
-- Role 1: event_ingest_writer
-- Purpose: Event ingestion service - can only write events (append-only)
-- Permissions: INSERT events, SELECT events/subscriptions (read-only)
-- ============================================================================

CREATE USER IF NOT EXISTS 'event_ingest_writer'@'%' IDENTIFIED BY 'dev_password_event_ingest';

-- Allow: INSERT events (append-only)
GRANT SELECT, INSERT ON webhook_delivery.events TO 'event_ingest_writer'@'%';
GRANT SELECT ON webhook_delivery.subscriptions TO 'event_ingest_writer'@'%';

-- Deny: Everything else
-- (MySQL REVOKE doesn't work if privileges weren't granted, so we rely on NOT granting them)


-- ============================================================================
-- Role 2: router_worker
-- Purpose: Routing worker - creates sagas from events
-- Permissions: SELECT events/subscriptions, INSERT sagas (idempotent)
-- ============================================================================

CREATE USER IF NOT EXISTS 'router_worker'@'%' IDENTIFIED BY 'dev_password_router';

-- Allow: Read events and subscriptions, create sagas
GRANT SELECT ON webhook_delivery.events TO 'router_worker'@'%';
GRANT SELECT ON webhook_delivery.subscriptions TO 'router_worker'@'%';
GRANT SELECT, INSERT ON webhook_delivery.webhook_delivery_sagas TO 'router_worker'@'%';

-- Explicitly DENY saga status updates (router must not update sagas)
-- Note: In MySQL, we can't explicitly deny, but we ensure UPDATE is not granted


-- ============================================================================
-- Role 3: saga_orchestrator
-- Purpose: Saga orchestrator - THE ONLY component allowed to update saga state
-- Permissions: SELECT/INSERT/UPDATE sagas, CREATE jobs, INSERT dead_letters
-- ============================================================================

CREATE USER IF NOT EXISTS 'saga_orchestrator'@'%' IDENTIFIED BY 'dev_password_orchestrator';

-- Allow: Read events/subscriptions
GRANT SELECT ON webhook_delivery.events TO 'saga_orchestrator'@'%';
GRANT SELECT ON webhook_delivery.subscriptions TO 'saga_orchestrator'@'%';

-- Allow: Full saga state control (THE ONLY role with UPDATE permission)
GRANT SELECT, INSERT, UPDATE ON webhook_delivery.webhook_delivery_sagas TO 'saga_orchestrator'@'%';

-- Allow: Create and read jobs
GRANT SELECT, INSERT, UPDATE ON webhook_delivery.webhook_delivery_jobs TO 'saga_orchestrator'@'%';

-- Allow: Insert dead letter records
GRANT SELECT, INSERT ON webhook_delivery.dead_letters TO 'saga_orchestrator'@'%';

-- Deny: DELETE operations (sagas are never deleted)
-- Deny: Modify events/subscriptions (read-only)


-- ============================================================================
-- Role 4: job_worker
-- Purpose: Job workers - execute HTTP delivery attempts
-- Permissions: SELECT events/subscriptions/sagas, UPDATE jobs only
-- CRITICAL: Workers MUST NEVER modify sagas!
-- ============================================================================

CREATE USER IF NOT EXISTS 'job_worker'@'%' IDENTIFIED BY 'dev_password_worker';

-- Allow: Read events, subscriptions, sagas (for delivery context)
GRANT SELECT ON webhook_delivery.events TO 'job_worker'@'%';
GRANT SELECT ON webhook_delivery.subscriptions TO 'job_worker'@'%';
GRANT SELECT ON webhook_delivery.webhook_delivery_sagas TO 'job_worker'@'%';

-- Allow: Acquire and update jobs (status, lease, response)
GRANT SELECT, INSERT, UPDATE ON webhook_delivery.webhook_delivery_jobs TO 'job_worker'@'%';

-- CRITICAL: Workers MUST NOT modify sagas
-- (Ensured by NOT granting INSERT/UPDATE/DELETE on webhook_delivery_sagas)


-- ============================================================================
-- Role 5: dead_letter_operator
-- Purpose: Operator API for requeue operations
-- Permissions: SELECT dead_letters, INSERT new sagas (requeue only)
-- ============================================================================

CREATE USER IF NOT EXISTS 'dead_letter_operator'@'%' IDENTIFIED BY 'dev_password_deadletter';

-- Allow: Read dead letters and events
GRANT SELECT ON webhook_delivery.dead_letters TO 'dead_letter_operator'@'%';
GRANT SELECT ON webhook_delivery.events TO 'dead_letter_operator'@'%';
GRANT SELECT ON webhook_delivery.subscriptions TO 'dead_letter_operator'@'%';
GRANT SELECT ON webhook_delivery.webhook_delivery_sagas TO 'dead_letter_operator'@'%';

-- Allow: Create NEW sagas for requeue (but cannot update existing ones)
GRANT INSERT ON webhook_delivery.webhook_delivery_sagas TO 'dead_letter_operator'@'%';

-- Deny: UPDATE/DELETE sagas (can only create new ones)
-- Deny: Modify dead_letters (append-only)


-- ============================================================================
-- Role 6: subscription_admin
-- Purpose: Admin API for subscription management
-- Permissions: SELECT/INSERT/UPDATE subscriptions only
-- ============================================================================

CREATE USER IF NOT EXISTS 'subscription_admin'@'%' IDENTIFIED BY 'dev_password_subscription';

-- Allow: Full subscription management
GRANT SELECT, INSERT, UPDATE ON webhook_delivery.subscriptions TO 'subscription_admin'@'%';

-- Deny: Runtime state (sagas, jobs, dead_letters)
-- (Ensured by NOT granting access)


-- ============================================================================
-- Apply all permission changes
-- ============================================================================

FLUSH PRIVILEGES;


-- ============================================================================
-- Verification queries (run these to verify permissions)
-- ============================================================================

-- Show all users
-- SELECT User, Host FROM mysql.user WHERE User LIKE '%event%' OR User LIKE '%router%' OR User LIKE '%saga%' OR User LIKE '%worker%' OR User LIKE '%dead%' OR User LIKE '%subscription%';

-- Show grants for a specific user
-- SHOW GRANTS FOR 'event_ingest_writer'@'%';
-- SHOW GRANTS FOR 'router_worker'@'%';
-- SHOW GRANTS FOR 'saga_orchestrator'@'%';
-- SHOW GRANTS FOR 'job_worker'@'%';
-- SHOW GRANTS FOR 'dead_letter_operator'@'%';
-- SHOW GRANTS FOR 'subscription_admin'@'%';
