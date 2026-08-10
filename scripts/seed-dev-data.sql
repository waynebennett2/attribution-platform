-- Manual/exploratory testing seed data for User Story 1 (DNI allocation).
--
-- The admin API (pools/numbers management) requires a JWT, which requires a real OIDC
-- identity provider that isn't wired up in this environment yet — so for now, seeding
-- directly via SQL is the practical path to get a Website + Number Pool + Tracking
-- Number to allocate from. The /v1/dni/* endpoints themselves are deliberately
-- unauthenticated (FR-037), so once this data exists you can call them directly.
--
-- Usage: mysql -h 127.0.0.1 -P 3306 -u attribution -pattribution_dev attribution < scripts/seed-dev-data.sql
-- (matches the credentials in docker-compose.yml / appsettings.Development.json)

SET @website_id = '00000000-0000-0000-0000-000000000001';
SET @pool_id     = '00000000-0000-0000-0000-000000000002';
SET @number_id   = '00000000-0000-0000-0000-000000000003';

INSERT INTO websites
    (id, name, permitted_origins, default_number, session_timeout_seconds, heartbeat_interval_seconds,
     allocation_window_extension_seconds, cooldown_seconds, consent_required, shadow_mode_enabled,
     local_timezone, created_at, updated_at)
VALUES
    (@website_id, 'Dev Test Website', 'http://localhost:4173\nhttp://localhost:3000', '+15550000000',
     1800, 300, 1800, 1800, 1, 0, 'UTC', UTC_TIMESTAMP(), UTC_TIMESTAMP());

INSERT INTO number_pools (id, name, scope_type, scope_ref, created_at, updated_at)
VALUES (@pool_id, 'Dev Test Pool', 'website', @website_id, UTC_TIMESTAMP(), UTC_TIMESTAMP());

INSERT INTO tracking_numbers (id, pool_id, did, status, status_changed_at)
VALUES (@number_id, @pool_id, '+15551234567', 'Active', UTC_TIMESTAMP());

SELECT @website_id AS website_id, @pool_id AS pool_id, @number_id AS tracking_number_id;
