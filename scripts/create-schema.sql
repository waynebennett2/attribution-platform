-- Schema for the Call Attribution Platform database (spec 001-call-attribution-platform).
--
-- Exported from FluentMigrator migration M202608100001_InitialSchema (the actual source
-- of truth: src/Attribution.Infrastructure/Data/Migrations/M202608100001_InitialSchema.cs)
-- by applying it to a throwaway database and dumping the resulting schema with
-- mysqldump --no-data. Contains only CREATE TABLE statements — no data.
--
-- Usage: create an empty database, then run this against it, e.g.
--   mysql -h <your-host> -P 3306 -u <your-user> -p<your-password> <your-database> < create-schema.sql
--
-- After this, scripts/seed-dev-data.sql can be run the same way to load sample
-- website/pool/tracking-number test data.
--
-- If the application's own schema (the migration file above) changes in future, this
-- export will be stale — regenerate it rather than hand-editing this file.

/*!40101 SET @OLD_CHARACTER_SET_CLIENT=@@CHARACTER_SET_CLIENT */;
/*!40101 SET @OLD_CHARACTER_SET_RESULTS=@@CHARACTER_SET_RESULTS */;
/*!40101 SET @OLD_COLLATION_CONNECTION=@@COLLATION_CONNECTION */;
/*!50503 SET NAMES utf8mb4 */;
/*!40103 SET @OLD_TIME_ZONE=@@TIME_ZONE */;
/*!40103 SET TIME_ZONE='+00:00' */;
/*!40014 SET @OLD_UNIQUE_CHECKS=@@UNIQUE_CHECKS, UNIQUE_CHECKS=0 */;
/*!40014 SET @OLD_FOREIGN_KEY_CHECKS=@@FOREIGN_KEY_CHECKS, FOREIGN_KEY_CHECKS=0 */;
/*!40101 SET @OLD_SQL_MODE=@@SQL_MODE, SQL_MODE='NO_AUTO_VALUE_ON_ZERO' */;
/*!40111 SET @OLD_SQL_NOTES=@@SQL_NOTES, SQL_NOTES=0 */;
DROP TABLE IF EXISTS `alerts`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `alerts` (
  `id` varchar(36) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL,
  `condition_type` varchar(32) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL,
  `scope_ref` varchar(255) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci DEFAULT NULL,
  `threshold` varchar(64) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL,
  `raised_at` datetime(6) NOT NULL,
  `last_notified_at` datetime(6) NOT NULL,
  `acknowledged_at` datetime(6) DEFAULT NULL,
  `acknowledged_by` varchar(36) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci DEFAULT NULL,
  `cleared_at` datetime(6) DEFAULT NULL,
  PRIMARY KEY (`id`),
  KEY `IX_alerts_condition_scope_open` (`condition_type`,`scope_ref`,`cleared_at`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
/*!40101 SET character_set_client = @saved_cs_client */;
DROP TABLE IF EXISTS `allocations`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `allocations` (
  `id` varchar(36) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL,
  `tracking_number_id` varchar(36) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL,
  `session_id` varchar(36) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL,
  `pool_id_at_allocation` varchar(36) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci DEFAULT NULL,
  `window_start` datetime(6) NOT NULL,
  `window_end` datetime(6) NOT NULL,
  `is_shadow` tinyint(1) NOT NULL DEFAULT '0',
  `created_at` datetime(6) NOT NULL,
  PRIMARY KEY (`id`),
  KEY `FK_allocations_session` (`session_id`),
  KEY `IX_allocations_number_window` (`tracking_number_id`,`window_start`),
  CONSTRAINT `FK_allocations_number` FOREIGN KEY (`tracking_number_id`) REFERENCES `tracking_numbers` (`id`),
  CONSTRAINT `FK_allocations_session` FOREIGN KEY (`session_id`) REFERENCES `sessions` (`id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
/*!40101 SET character_set_client = @saved_cs_client */;
DROP TABLE IF EXISTS `attributions`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `attributions` (
  `id` varchar(36) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL,
  `call_id` varchar(36) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL,
  `session_id` varchar(36) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci DEFAULT NULL,
  `allocation_id` varchar(36) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci DEFAULT NULL,
  `state` varchar(16) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL,
  `reason` varchar(255) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci DEFAULT NULL,
  `is_shadow_derived` tinyint(1) NOT NULL DEFAULT '0',
  `is_current` tinyint(1) NOT NULL DEFAULT '1',
  `superseded_reason` varchar(255) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci DEFAULT NULL,
  `decided_at` datetime(6) NOT NULL,
  PRIMARY KEY (`id`),
  KEY `FK_attributions_session` (`session_id`),
  KEY `FK_attributions_allocation` (`allocation_id`),
  KEY `IX_attributions_call_current` (`call_id`,`is_current`),
  CONSTRAINT `FK_attributions_allocation` FOREIGN KEY (`allocation_id`) REFERENCES `allocations` (`id`),
  CONSTRAINT `FK_attributions_call` FOREIGN KEY (`call_id`) REFERENCES `calls` (`id`),
  CONSTRAINT `FK_attributions_session` FOREIGN KEY (`session_id`) REFERENCES `sessions` (`id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
/*!40101 SET character_set_client = @saved_cs_client */;
DROP TABLE IF EXISTS `audit_entries`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `audit_entries` (
  `id` varchar(36) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL,
  `actor_user_id` varchar(36) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL,
  `action` varchar(128) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL,
  `target_type` varchar(64) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL,
  `target_id` varchar(64) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL,
  `before_value` longtext CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci,
  `after_value` longtext CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL,
  `occurred_at` datetime(6) NOT NULL,
  PRIMARY KEY (`id`),
  KEY `IX_audit_entries_target` (`target_type`,`target_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
/*!40101 SET character_set_client = @saved_cs_client */;
DROP TABLE IF EXISTS `call_legs`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `call_legs` (
  `id` varchar(36) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL,
  `call_id` varchar(36) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci DEFAULT NULL,
  `source_call_record_id` varchar(128) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL,
  `source_leg_id` varchar(128) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL,
  `sequence_or_role` varchar(64) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci DEFAULT NULL,
  `started_at` datetime(6) DEFAULT NULL,
  `ended_at` datetime(6) DEFAULT NULL,
  PRIMARY KEY (`id`),
  UNIQUE KEY `IX_call_legs_source` (`source_call_record_id`,`source_leg_id`),
  KEY `FK_call_legs_call` (`call_id`),
  CONSTRAINT `FK_call_legs_call` FOREIGN KEY (`call_id`) REFERENCES `calls` (`id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
/*!40101 SET character_set_client = @saved_cs_client */;
DROP TABLE IF EXISTS `calls`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `calls` (
  `id` varchar(36) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL,
  `source_record_id` varchar(128) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL,
  `direction` varchar(16) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL,
  `dialled_number` varchar(32) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL,
  `caller_id` varchar(64) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci DEFAULT NULL,
  `started_at` datetime(6) NOT NULL,
  `answered_at` datetime(6) DEFAULT NULL,
  `ended_at` datetime(6) DEFAULT NULL,
  `connected_duration_seconds` int DEFAULT NULL,
  `disposition` varchar(32) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci DEFAULT NULL,
  `is_final` tinyint(1) NOT NULL DEFAULT '0',
  `ingested_at` datetime(6) NOT NULL,
  `updated_at` datetime(6) NOT NULL,
  `de_identified_at` datetime(6) DEFAULT NULL,
  PRIMARY KEY (`id`),
  UNIQUE KEY `IX_calls_source_record_id` (`source_record_id`),
  KEY `IX_calls_dialled_number_started_at` (`dialled_number`,`started_at`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
/*!40101 SET character_set_client = @saved_cs_client */;
DROP TABLE IF EXISTS `conversion_publications`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `conversion_publications` (
  `id` varchar(36) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL,
  `qualification_result_id` varchar(36) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL,
  `destination` varchar(16) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL,
  `idempotency_key` varchar(255) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL,
  `status` varchar(16) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL,
  `skipped_reason` varchar(255) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci DEFAULT NULL,
  `attempt_count` int NOT NULL DEFAULT '0',
  `external_id` varchar(255) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci DEFAULT NULL,
  `last_error` longtext CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci,
  `correction` longtext CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci,
  `sent_at` datetime(6) DEFAULT NULL,
  `corrected_at` datetime(6) DEFAULT NULL,
  `de_identified_at` datetime(6) DEFAULT NULL,
  PRIMARY KEY (`id`),
  UNIQUE KEY `IX_conversion_publications_idempotency_key` (`idempotency_key`),
  KEY `FK_conversion_publications_result` (`qualification_result_id`),
  CONSTRAINT `FK_conversion_publications_result` FOREIGN KEY (`qualification_result_id`) REFERENCES `qualification_results` (`id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
/*!40101 SET character_set_client = @saved_cs_client */;
DROP TABLE IF EXISTS `ingestion_checkpoints`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `ingestion_checkpoints` (
  `id` varchar(36) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL,
  `feed` varchar(32) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL,
  `position` varchar(255) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL,
  `updated_at` datetime(6) NOT NULL,
  PRIMARY KEY (`id`),
  UNIQUE KEY `IX_ingestion_checkpoints_feed` (`feed`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
/*!40101 SET character_set_client = @saved_cs_client */;
DROP TABLE IF EXISTS `notification_delivery_status`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `notification_delivery_status` (
  `channel` varchar(16) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL,
  `last_attempt_at` datetime(6) NOT NULL,
  `last_success_at` datetime(6) DEFAULT NULL,
  `last_failure_at` datetime(6) DEFAULT NULL,
  `last_failure_reason` varchar(255) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci DEFAULT NULL,
  PRIMARY KEY (`channel`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
/*!40101 SET character_set_client = @saved_cs_client */;
DROP TABLE IF EXISTS `number_pools`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `number_pools` (
  `id` varchar(36) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL,
  `name` varchar(255) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL,
  `scope_type` varchar(32) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL,
  `scope_ref` varchar(36) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL,
  `default_number` varchar(32) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci DEFAULT NULL,
  `created_at` datetime(6) NOT NULL,
  `updated_at` datetime(6) NOT NULL,
  PRIMARY KEY (`id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
/*!40101 SET character_set_client = @saved_cs_client */;
DROP TABLE IF EXISTS `qualification_results`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `qualification_results` (
  `id` varchar(36) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL,
  `call_id` varchar(36) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL,
  `attribution_id` varchar(36) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL,
  `qualification_rule_id` varchar(36) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL,
  `is_qualified` tinyint(1) NOT NULL,
  `is_current` tinyint(1) NOT NULL DEFAULT '1',
  `superseded_reason` varchar(255) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci DEFAULT NULL,
  `decided_at` datetime(6) NOT NULL,
  PRIMARY KEY (`id`),
  KEY `FK_qualification_results_attribution` (`attribution_id`),
  KEY `FK_qualification_results_rule` (`qualification_rule_id`),
  KEY `IX_qualification_results_call_current` (`call_id`,`is_current`),
  CONSTRAINT `FK_qualification_results_attribution` FOREIGN KEY (`attribution_id`) REFERENCES `attributions` (`id`),
  CONSTRAINT `FK_qualification_results_call` FOREIGN KEY (`call_id`) REFERENCES `calls` (`id`),
  CONSTRAINT `FK_qualification_results_rule` FOREIGN KEY (`qualification_rule_id`) REFERENCES `qualification_rules` (`id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
/*!40101 SET character_set_client = @saved_cs_client */;
DROP TABLE IF EXISTS `qualification_rules`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `qualification_rules` (
  `id` varchar(36) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL,
  `scope_type` varchar(16) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL,
  `scope_ref` varchar(255) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci DEFAULT NULL,
  `version` int NOT NULL,
  `conditions` longtext CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL,
  `effective_start` datetime(6) NOT NULL,
  `effective_end` datetime(6) DEFAULT NULL,
  `created_by` varchar(36) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL,
  `created_at` datetime(6) NOT NULL,
  PRIMARY KEY (`id`),
  UNIQUE KEY `IX_qualification_rules_scope_version` (`scope_type`,`scope_ref`,`version`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
/*!40101 SET character_set_client = @saved_cs_client */;
DROP TABLE IF EXISTS `review_cases`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `review_cases` (
  `id` varchar(36) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL,
  `call_id` varchar(36) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL,
  `attribution_id` varchar(36) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL,
  `status` varchar(16) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL,
  `opened_at` datetime(6) NOT NULL,
  `age_alert_raised_at` datetime(6) DEFAULT NULL,
  `resolved_by` varchar(36) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci DEFAULT NULL,
  `resolved_at` datetime(6) DEFAULT NULL,
  `resolution` longtext CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci,
  PRIMARY KEY (`id`),
  KEY `FK_review_cases_call` (`call_id`),
  KEY `FK_review_cases_attribution` (`attribution_id`),
  KEY `IX_review_cases_status_opened` (`status`,`opened_at`),
  CONSTRAINT `FK_review_cases_attribution` FOREIGN KEY (`attribution_id`) REFERENCES `attributions` (`id`),
  CONSTRAINT `FK_review_cases_call` FOREIGN KEY (`call_id`) REFERENCES `calls` (`id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
/*!40101 SET character_set_client = @saved_cs_client */;
DROP TABLE IF EXISTS `sessions`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `sessions` (
  `id` varchar(36) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL,
  `visitor_id` varchar(36) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL,
  `website_id` varchar(36) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL,
  `landing_page` varchar(2048) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci DEFAULT NULL,
  `referrer` varchar(2048) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci DEFAULT NULL,
  `utm_source` varchar(255) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci DEFAULT NULL,
  `utm_medium` varchar(255) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci DEFAULT NULL,
  `utm_campaign` varchar(255) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci DEFAULT NULL,
  `utm_term` varchar(255) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci DEFAULT NULL,
  `utm_content` varchar(255) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci DEFAULT NULL,
  `gclid` varchar(255) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci DEFAULT NULL,
  `gbraid` varchar(255) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci DEFAULT NULL,
  `wbraid` varchar(255) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci DEFAULT NULL,
  `ga4_client_id` varchar(255) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci DEFAULT NULL,
  `consent_state` varchar(16) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL,
  `provenance` varchar(16) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL DEFAULT 'ordinary',
  `started_at` datetime(6) NOT NULL,
  `expires_at` datetime(6) NOT NULL,
  `ended_at` datetime(6) DEFAULT NULL,
  `de_identified_at` datetime(6) DEFAULT NULL,
  PRIMARY KEY (`id`),
  KEY `FK_sessions_visitor` (`visitor_id`),
  KEY `FK_sessions_website` (`website_id`),
  CONSTRAINT `FK_sessions_visitor` FOREIGN KEY (`visitor_id`) REFERENCES `visitors` (`id`),
  CONSTRAINT `FK_sessions_website` FOREIGN KEY (`website_id`) REFERENCES `websites` (`id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
/*!40101 SET character_set_client = @saved_cs_client */;
DROP TABLE IF EXISTS `tracking_numbers`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `tracking_numbers` (
  `id` varchar(36) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL,
  `pool_id` varchar(36) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL,
  `did` varchar(32) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL,
  `status` varchar(16) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL,
  `status_changed_at` datetime(6) NOT NULL,
  `last_released_at` datetime(6) DEFAULT NULL,
  PRIMARY KEY (`id`),
  UNIQUE KEY `IX_tracking_numbers_did` (`did`),
  KEY `IX_tracking_numbers_pool_status` (`pool_id`,`status`,`last_released_at`),
  CONSTRAINT `FK_tracking_numbers_pool` FOREIGN KEY (`pool_id`) REFERENCES `number_pools` (`id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
/*!40101 SET character_set_client = @saved_cs_client */;
DROP TABLE IF EXISTS `users`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `users` (
  `id` varchar(36) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL,
  `subject_ref` varchar(255) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci DEFAULT NULL,
  `username` varchar(255) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci DEFAULT NULL,
  `client_id` varchar(255) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci DEFAULT NULL,
  `identity_type` varchar(24) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL,
  `mapped_role` varchar(32) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL,
  `role_override` varchar(32) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci DEFAULT NULL,
  `role_overridden_by` varchar(36) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci DEFAULT NULL,
  `password_hash` varchar(255) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci DEFAULT NULL,
  `totp_secret` varchar(64) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci DEFAULT NULL,
  `mfa_required` tinyint(1) NOT NULL DEFAULT '0',
  `is_active` tinyint(1) NOT NULL DEFAULT '1',
  `created_at` datetime(6) NOT NULL,
  `last_seen_at` datetime(6) DEFAULT NULL,
  PRIMARY KEY (`id`),
  UNIQUE KEY `IX_users_subject_ref` (`subject_ref`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
/*!40101 SET character_set_client = @saved_cs_client */;
DROP TABLE IF EXISTS `versioninfo`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `versioninfo` (
  `Version` bigint NOT NULL,
  `AppliedOn` datetime DEFAULT NULL,
  `Description` varchar(1024) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci DEFAULT NULL,
  UNIQUE KEY `UC_Version` (`Version`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
/*!40101 SET character_set_client = @saved_cs_client */;
DROP TABLE IF EXISTS `visitors`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `visitors` (
  `id` varchar(36) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL,
  `website_id` varchar(36) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL,
  `first_seen_at` datetime(6) NOT NULL,
  `de_identified_at` datetime(6) DEFAULT NULL,
  PRIMARY KEY (`id`),
  KEY `FK_visitors_website` (`website_id`),
  CONSTRAINT `FK_visitors_website` FOREIGN KEY (`website_id`) REFERENCES `websites` (`id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
/*!40101 SET character_set_client = @saved_cs_client */;
DROP TABLE IF EXISTS `websites`;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!50503 SET character_set_client = utf8mb4 */;
CREATE TABLE `websites` (
  `id` varchar(36) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL,
  `name` varchar(255) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL,
  `permitted_origins` longtext CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL,
  `default_number` varchar(32) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL,
  `session_timeout_seconds` int NOT NULL DEFAULT '1800',
  `heartbeat_interval_seconds` int NOT NULL DEFAULT '300',
  `allocation_window_extension_seconds` int NOT NULL DEFAULT '1800',
  `cooldown_seconds` int NOT NULL DEFAULT '1800',
  `consent_required` tinyint(1) NOT NULL DEFAULT '1',
  `shadow_mode_enabled` tinyint(1) NOT NULL DEFAULT '0',
  `business_unit` varchar(255) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci DEFAULT NULL,
  `local_timezone` varchar(64) CHARACTER SET utf8mb3 COLLATE utf8mb3_general_ci NOT NULL DEFAULT 'UTC',
  `created_at` datetime(6) NOT NULL,
  `updated_at` datetime(6) NOT NULL,
  PRIMARY KEY (`id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
/*!40101 SET character_set_client = @saved_cs_client */;
/*!40103 SET TIME_ZONE=@OLD_TIME_ZONE */;

/*!40101 SET SQL_MODE=@OLD_SQL_MODE */;
/*!40014 SET FOREIGN_KEY_CHECKS=@OLD_FOREIGN_KEY_CHECKS */;
/*!40014 SET UNIQUE_CHECKS=@OLD_UNIQUE_CHECKS */;
/*!40101 SET CHARACTER_SET_CLIENT=@OLD_CHARACTER_SET_CLIENT */;
/*!40101 SET CHARACTER_SET_RESULTS=@OLD_CHARACTER_SET_RESULTS */;
/*!40101 SET COLLATION_CONNECTION=@OLD_COLLATION_CONNECTION */;
/*!40111 SET SQL_NOTES=@OLD_SQL_NOTES */;

