-- ============================================================================
-- Webhook Delivery System - Database Roles (Production Template)
-- ============================================================================
-- Description: Production-ready database roles with placeholder passwords
-- Version: 002-PROD
-- Date: 2025-12-11
-- ============================================================================
-- IMPORTANT:
-- 1. Replace all ${PASSWORD_*} placeholders with secrets from your vault
-- 2. Consider using certificate-based authentication instead of passwords
-- 3. Restrict hosts to specific IPs/networks (replace '%' with '10.0.0.0/255.255.255.0')
-- 4. Enable SSL/TLS for all database connections
-- 5. Rotate credentials regularly
-- ============================================================================

-- ============================================================================
-- Role 1: event_ingest_writer
-- ============================================================================

CREATE USER IF NOT EXISTS 'event_ingest_writer'@'%'
    IDENTIFIED BY '${PASSWORD_EVENT_INGEST_WRITER}'
    REQUIRE SSL;

GRANT SELECT, INSERT ON webhook_delivery.events TO 'event_ingest_writer'@'%';
GRANT SELECT ON webhook_delivery.subscriptions TO 'event_ingest_writer'@'%';


-- ============================================================================
-- Role 2: router_worker
-- ============================================================================

CREATE USER IF NOT EXISTS 'router_worker'@'%'
    IDENTIFIED BY '${PASSWORD_ROUTER_WORKER}'
    REQUIRE SSL;

GRANT SELECT ON webhook_delivery.events TO 'router_worker'@'%';
GRANT SELECT ON webhook_delivery.subscriptions TO 'router_worker'@'%';
GRANT SELECT, INSERT ON webhook_delivery.webhook_delivery_sagas TO 'router_worker'@'%';


-- ============================================================================
-- Role 3: saga_orchestrator (CRITICAL - Only role that can update sagas)
-- ============================================================================

CREATE USER IF NOT EXISTS 'saga_orchestrator'@'%'
    IDENTIFIED BY '${PASSWORD_SAGA_ORCHESTRATOR}'
    REQUIRE SSL;

GRANT SELECT ON webhook_delivery.events TO 'saga_orchestrator'@'%';
GRANT SELECT ON webhook_delivery.subscriptions TO 'saga_orchestrator'@'%';
GRANT SELECT, INSERT, UPDATE ON webhook_delivery.webhook_delivery_sagas TO 'saga_orchestrator'@'%';
GRANT SELECT, INSERT, UPDATE ON webhook_delivery.webhook_delivery_jobs TO 'saga_orchestrator'@'%';
GRANT SELECT, INSERT ON webhook_delivery.dead_letters TO 'saga_orchestrator'@'%';


-- ============================================================================
-- Role 4: job_worker (MUST NEVER modify sagas)
-- ============================================================================

CREATE USER IF NOT EXISTS 'job_worker'@'%'
    IDENTIFIED BY '${PASSWORD_JOB_WORKER}'
    REQUIRE SSL;

GRANT SELECT ON webhook_delivery.events TO 'job_worker'@'%';
GRANT SELECT ON webhook_delivery.subscriptions TO 'job_worker'@'%';
GRANT SELECT ON webhook_delivery.webhook_delivery_sagas TO 'job_worker'@'%';
GRANT SELECT, INSERT, UPDATE ON webhook_delivery.webhook_delivery_jobs TO 'job_worker'@'%';


-- ============================================================================
-- Role 5: dead_letter_operator
-- ============================================================================

CREATE USER IF NOT EXISTS 'dead_letter_operator'@'%'
    IDENTIFIED BY '${PASSWORD_DEAD_LETTER_OPERATOR}'
    REQUIRE SSL;

GRANT SELECT ON webhook_delivery.dead_letters TO 'dead_letter_operator'@'%';
GRANT SELECT ON webhook_delivery.events TO 'dead_letter_operator'@'%';
GRANT SELECT ON webhook_delivery.subscriptions TO 'dead_letter_operator'@'%';
GRANT SELECT ON webhook_delivery.webhook_delivery_sagas TO 'dead_letter_operator'@'%';
GRANT INSERT ON webhook_delivery.webhook_delivery_sagas TO 'dead_letter_operator'@'%';


-- ============================================================================
-- Role 6: subscription_admin
-- ============================================================================

CREATE USER IF NOT EXISTS 'subscription_admin'@'%'
    IDENTIFIED BY '${PASSWORD_SUBSCRIPTION_ADMIN}'
    REQUIRE SSL;

GRANT SELECT, INSERT, UPDATE ON webhook_delivery.subscriptions TO 'subscription_admin'@'%';


-- ============================================================================
-- Apply changes
-- ============================================================================

FLUSH PRIVILEGES;


-- ============================================================================
-- Security Checklist:
-- ============================================================================
-- [ ] All ${PASSWORD_*} placeholders replaced with strong secrets
-- [ ] Secrets stored in vault (e.g., AWS Secrets Manager, HashiCorp Vault)
-- [ ] SSL/TLS enabled for all connections
-- [ ] Host restrictions configured (replace '%' with specific IPs/networks)
-- [ ] Certificate-based auth configured (recommended)
-- [ ] Password rotation policy defined (e.g., every 90 days)
-- [ ] Audit logging enabled
-- [ ] Connection limits configured per role
-- [ ] Database firewall rules configured
-- ============================================================================
