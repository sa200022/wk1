-- ============================================================================
-- Webhook Delivery System - Database Roles and Permissions
-- ============================================================================
-- Description: Creates minimal-privilege database roles for each component
-- Version: 002
-- Date: 2025-12-11
-- ============================================================================

-- ============================================================================
-- Role: event_ingest_writer
-- Purpose: Event ingestion service - can only write events
-- ============================================================================

-- Create user (replace password in production)
-- CREATE USER IF NOT EXISTS 'event_ingest_writer'@'%' IDENTIFIED BY 'CHANGE_ME_IN_PRODUCTION';

-- Grant minimal permissions
-- GRANT SELECT ON webhook_delivery.events TO 'event_ingest_writer'@'%';
-- GRANT INSERT ON webhook_delivery.events TO 'event_ingest_writer'@'%';
-- GRANT SELECT ON webhook_delivery.subscriptions TO 'event_ingest_writer'@'%';

-- Explicitly deny dangerous operations
-- REVOKE UPDATE, DELETE ON webhook_delivery.events FROM 'event_ingest_writer'@'%';
-- REVOKE ALL PRIVILEGES ON webhook_delivery.webhook_delivery_sagas FROM 'event_ingest_writer'@'%';
-- REVOKE ALL PRIVILEGES ON webhook_delivery.webhook_delivery_jobs FROM 'event_ingest_writer'@'%';
-- REVOKE ALL PRIVILEGES ON webhook_delivery.dead_letters FROM 'event_ingest_writer'@'%';


-- ============================================================================
-- Role: router_worker
-- Purpose: Routing worker - creates sagas from events
-- ============================================================================

-- CREATE USER IF NOT EXISTS 'router_worker'@'%' IDENTIFIED BY 'CHANGE_ME_IN_PRODUCTION';

-- GRANT SELECT ON webhook_delivery.events TO 'router_worker'@'%';
-- GRANT SELECT ON webhook_delivery.subscriptions TO 'router_worker'@'%';
-- GRANT SELECT, INSERT ON webhook_delivery.webhook_delivery_sagas TO 'router_worker'@'%';

-- Deny job and saga status manipulation
-- REVOKE UPDATE, DELETE ON webhook_delivery.webhook_delivery_sagas FROM 'router_worker'@'%';
-- REVOKE ALL PRIVILEGES ON webhook_delivery.webhook_delivery_jobs FROM 'router_worker'@'%';


-- ============================================================================
-- Role: saga_orchestrator
-- Purpose: Saga orchestrator - manages saga state and creates jobs
-- ============================================================================

-- CREATE USER IF NOT EXISTS 'saga_orchestrator'@'%' IDENTIFIED BY 'CHANGE_ME_IN_PRODUCTION';

-- GRANT SELECT ON webhook_delivery.events TO 'saga_orchestrator'@'%';
-- GRANT SELECT ON webhook_delivery.subscriptions TO 'saga_orchestrator'@'%';
-- GRANT SELECT, INSERT, UPDATE ON webhook_delivery.webhook_delivery_sagas TO 'saga_orchestrator'@'%';
-- GRANT SELECT, INSERT, UPDATE ON webhook_delivery.webhook_delivery_jobs TO 'saga_orchestrator'@'%';
-- GRANT SELECT, INSERT ON webhook_delivery.dead_letters TO 'saga_orchestrator'@'%';

-- Explicitly deny destructive operations
-- REVOKE DELETE ON webhook_delivery.webhook_delivery_sagas FROM 'saga_orchestrator'@'%';
-- REVOKE UPDATE, DELETE ON webhook_delivery.events FROM 'saga_orchestrator'@'%';
-- REVOKE UPDATE, DELETE ON webhook_delivery.subscriptions FROM 'saga_orchestrator'@'%';


-- ============================================================================
-- Role: job_worker
-- Purpose: Job workers - execute HTTP delivery attempts
-- ============================================================================

-- CREATE USER IF NOT EXISTS 'job_worker'@'%' IDENTIFIED BY 'CHANGE_ME_IN_PRODUCTION';

-- GRANT SELECT ON webhook_delivery.events TO 'job_worker'@'%';
-- GRANT SELECT ON webhook_delivery.subscriptions TO 'job_worker'@'%';
-- GRANT SELECT ON webhook_delivery.webhook_delivery_sagas TO 'job_worker'@'%';
-- GRANT SELECT, UPDATE ON webhook_delivery.webhook_delivery_jobs TO 'job_worker'@'%';

-- Workers must never modify sagas, events, or subscriptions
-- REVOKE INSERT, UPDATE, DELETE ON webhook_delivery.webhook_delivery_sagas FROM 'job_worker'@'%';
-- REVOKE ALL PRIVILEGES ON webhook_delivery.events FROM 'job_worker'@'%';
-- REVOKE ALL PRIVILEGES ON webhook_delivery.subscriptions FROM 'job_worker'@'%';
-- REVOKE ALL PRIVILEGES ON webhook_delivery.dead_letters FROM 'job_worker'@'%';


-- ============================================================================
-- Role: dead_letter_operator
-- Purpose: Operator API for requeue operations
-- ============================================================================

-- CREATE USER IF NOT EXISTS 'dead_letter_operator'@'%' IDENTIFIED BY 'CHANGE_ME_IN_PRODUCTION';

-- GRANT SELECT ON webhook_delivery.dead_letters TO 'dead_letter_operator'@'%';
-- GRANT SELECT ON webhook_delivery.events TO 'dead_letter_operator'@'%';
-- GRANT SELECT ON webhook_delivery.subscriptions TO 'dead_letter_operator'@'%';
-- GRANT INSERT ON webhook_delivery.webhook_delivery_sagas TO 'dead_letter_operator'@'%';

-- Must never modify existing sagas or dead letters
-- REVOKE UPDATE, DELETE ON webhook_delivery.webhook_delivery_sagas FROM 'dead_letter_operator'@'%';
-- REVOKE UPDATE, DELETE ON webhook_delivery.dead_letters FROM 'dead_letter_operator'@'%';


-- ============================================================================
-- Role: subscription_admin
-- Purpose: Admin API for subscription management
-- ============================================================================

-- CREATE USER IF NOT EXISTS 'subscription_admin'@'%' IDENTIFIED BY 'CHANGE_ME_IN_PRODUCTION';

-- GRANT SELECT, INSERT, UPDATE ON webhook_delivery.subscriptions TO 'subscription_admin'@'%';

-- Should not touch runtime state
-- REVOKE ALL PRIVILEGES ON webhook_delivery.webhook_delivery_sagas FROM 'subscription_admin'@'%';
-- REVOKE ALL PRIVILEGES ON webhook_delivery.webhook_delivery_jobs FROM 'subscription_admin'@'%';
-- REVOKE ALL PRIVILEGES ON webhook_delivery.dead_letters FROM 'subscription_admin'@'%';


-- ============================================================================
-- Notes:
-- - All CREATE USER and GRANT statements are commented out by default
-- - Uncomment and configure in your deployment environment
-- - Replace all passwords with strong secrets from your secret management system
-- - Consider using certificate-based authentication in production
-- ============================================================================
