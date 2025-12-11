-- ============================================================================
-- Webhook Delivery System - Initial Schema Migration
-- ============================================================================
-- Description: Creates all required tables for webhook delivery system
-- Version: 001
-- Date: 2025-12-11
-- ============================================================================

-- Set default engine and charset
SET default_storage_engine=InnoDB;
SET NAMES utf8mb4;

-- ============================================================================
-- Table: events
-- Purpose: Immutable append-only log of all domain events
-- ============================================================================
CREATE TABLE IF NOT EXISTS `events` (
    `id` BIGINT NOT NULL AUTO_INCREMENT,
    `event_type` VARCHAR(100) NOT NULL,
    `created_at` DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    `payload` JSON NOT NULL,
    PRIMARY KEY (`id`),
    INDEX `idx_event_created` (`created_at`),
    INDEX `idx_event_type` (`event_type`, `created_at`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- ============================================================================
-- Table: subscriptions
-- Purpose: Configuration layer defining who receives which events
-- ============================================================================
CREATE TABLE IF NOT EXISTS `subscriptions` (
    `id` BIGINT NOT NULL AUTO_INCREMENT,
    `event_type` VARCHAR(100) NOT NULL,
    `callback_url` VARCHAR(500) NOT NULL,
    `active` TINYINT(1) NOT NULL DEFAULT 1,
    `verified` TINYINT(1) NOT NULL DEFAULT 0,
    `created_at` DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    `updated_at` DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6),
    PRIMARY KEY (`id`),
    INDEX `idx_sub_event_type` (`event_type`),
    INDEX `idx_sub_active` (`active`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- ============================================================================
-- Table: webhook_delivery_sagas
-- Purpose: Orchestration layer coordinating webhook delivery state
-- ============================================================================
CREATE TABLE IF NOT EXISTS `webhook_delivery_sagas` (
    `id` BIGINT NOT NULL AUTO_INCREMENT,
    `event_id` BIGINT NOT NULL,
    `subscription_id` BIGINT NOT NULL,
    `status` ENUM('Pending', 'InProgress', 'PendingRetry', 'Completed', 'DeadLettered') NOT NULL DEFAULT 'Pending',
    `attempt_count` INT NOT NULL DEFAULT 0,
    `next_attempt_at` DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    `final_error_code` VARCHAR(100) NULL,
    `created_at` DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    `updated_at` DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6),
    PRIMARY KEY (`id`),
    UNIQUE KEY `uniq_saga_event_subscription` (`event_id`, `subscription_id`),
    INDEX `idx_saga_event` (`event_id`, `subscription_id`),
    INDEX `idx_saga_status_retry` (`status`, `next_attempt_at`),
    INDEX `idx_saga_status` (`status`),
    CONSTRAINT `fk_saga_event` FOREIGN KEY (`event_id`) REFERENCES `events` (`id`),
    CONSTRAINT `fk_saga_subscription` FOREIGN KEY (`subscription_id`) REFERENCES `subscriptions` (`id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- ============================================================================
-- Table: webhook_delivery_jobs
-- Purpose: Individual atomic delivery attempts
-- ============================================================================
CREATE TABLE IF NOT EXISTS `webhook_delivery_jobs` (
    `id` BIGINT NOT NULL AUTO_INCREMENT,
    `saga_id` BIGINT NOT NULL,
    `status` ENUM('Pending', 'Leased', 'Completed', 'Failed') NOT NULL DEFAULT 'Pending',
    `lease_until` DATETIME(6) NULL,
    `attempt_at` DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    `response_status` INT NULL,
    `error_code` VARCHAR(100) NULL,
    PRIMARY KEY (`id`),
    UNIQUE KEY `uniq_job_saga_attempt` (`saga_id`, `attempt_at`),
    INDEX `idx_job_saga` (`saga_id`),
    INDEX `idx_job_status_lease` (`status`, `lease_until`),
    CONSTRAINT `fk_job_saga` FOREIGN KEY (`saga_id`) REFERENCES `webhook_delivery_sagas` (`id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- ============================================================================
-- Table: dead_letters
-- Purpose: Permanent storage for failed delivery sagas
-- ============================================================================
CREATE TABLE IF NOT EXISTS `dead_letters` (
    `id` BIGINT NOT NULL AUTO_INCREMENT,
    `saga_id` BIGINT NOT NULL,
    `event_id` BIGINT NOT NULL,
    `subscription_id` BIGINT NOT NULL,
    `final_error_code` VARCHAR(100) NULL,
    `failed_at` DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    `payload_snapshot` JSON NOT NULL,
    PRIMARY KEY (`id`),
    INDEX `idx_dead_saga` (`saga_id`),
    INDEX `idx_dead_event` (`event_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- ============================================================================
-- Migration Complete
-- ============================================================================
