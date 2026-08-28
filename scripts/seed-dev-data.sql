-- Manual/exploratory testing seed data for User Story 1 (DNI allocation).
--
-- The admin API (pools/numbers management) requires a JWT, which requires a real OIDC
-- identity provider that isn't wired up in this environment yet — so for now, seeding
-- directly via SQL is the practical path to get a Website + Number Pool + Tracking
-- Number to allocate from. The /v1/dni/* endpoints themselves are deliberately
-- unauthenticated (FR-037), so once this data exists you can call them directly.
--
-- Usage: mysql -h <your-host> -P 3306 -u <your-user> -p<your-password> <your-database> < seed-dev-data.sql
-- (local dev default credentials, if you're running the bundled docker-compose.yml
-- instead of your own server, are in appsettings.Development.json)

SET @website_id = '00000000-0000-0000-0000-000000000001';
SET @pool_a_id  = '00000000-0000-0000-0000-000000000002'; -- Protea Haven
SET @pool_b_id  = '00000000-0000-0000-0000-000000000009'; -- Jacaranda Ridge
SET @pool_c_id  = '00000000-0000-0000-0000-000000000012'; -- Silver Tree

INSERT INTO websites
    (id, name, permitted_origins, default_number, session_timeout_seconds, heartbeat_interval_seconds,
     allocation_window_extension_seconds, cooldown_seconds, consent_required, shadow_mode_enabled,
     multi_pool_enabled, local_timezone, created_at, updated_at)
-- Short session_timeout/heartbeat/extension/cooldown values (vs. a realistic ~30-minute
-- production setting) so repeated manual clicking through the demo pages doesn't exhaust
-- the small dummy pool for 30 minutes at a time.
-- multi_pool_enabled=1 (FR-050): care-homes.html shows three care homes, each with its own
-- number pool below, so each gets an independently allocated tracking number instead of the
-- whole site sharing one.
VALUES
    (@website_id, 'Dev Test Website', 'http://localhost:4173\nhttp://localhost:3000', '+441632960000',
     120, 60, 60, 15, 1, 0, 1, 'Europe/London', UTC_TIMESTAMP(), UTC_TIMESTAMP());

-- One pool per care home on care-homes.html, each with its own default_number (matched
-- locally by the DNI client against that card's own displayed number, per FR-050) and its
-- own small set of dummy DIDs to allocate from. The business's real tracking numbers are UK
-- (not US), so these use Ofcom's 01632 960000-960999 range, reserved specifically for
-- fictional use in dramas/testing and guaranteed never to be a real allocated number.
INSERT INTO number_pools (id, name, scope_type, scope_ref, default_number, created_at, updated_at)
VALUES
    (@pool_a_id, 'Protea Haven Pool', 'website', @website_id, '+441632960010', UTC_TIMESTAMP(), UTC_TIMESTAMP()),
    (@pool_b_id, 'Jacaranda Ridge Pool', 'website', @website_id, '+441632960011', UTC_TIMESTAMP(), UTC_TIMESTAMP()),
    (@pool_c_id, 'Silver Tree Pool', 'website', @website_id, '+441632960012', UTC_TIMESTAMP(), UTC_TIMESTAMP());

INSERT INTO tracking_numbers (id, pool_id, did, status, status_changed_at)
VALUES
    ('00000000-0000-0000-0000-000000000003', @pool_a_id, '+441632960001', 'Active', UTC_TIMESTAMP()),
    ('00000000-0000-0000-0000-000000000004', @pool_a_id, '+441632960002', 'Active', UTC_TIMESTAMP()),
    ('00000000-0000-0000-0000-000000000005', @pool_a_id, '+441632960003', 'Active', UTC_TIMESTAMP()),
    ('00000000-0000-0000-0000-000000000006', @pool_a_id, '+441632960004', 'Active', UTC_TIMESTAMP()),
    ('00000000-0000-0000-0000-000000000007', @pool_a_id, '+441632960005', 'Active', UTC_TIMESTAMP()),
    ('00000000-0000-0000-0000-000000000010', @pool_b_id, '+441632960006', 'Active', UTC_TIMESTAMP()),
    ('00000000-0000-0000-0000-000000000011', @pool_b_id, '+441632960007', 'Active', UTC_TIMESTAMP()),
    ('00000000-0000-0000-0000-000000000013', @pool_c_id, '+441632960008', 'Active', UTC_TIMESTAMP()),
    ('00000000-0000-0000-0000-000000000014', @pool_c_id, '+441632960009', 'Active', UTC_TIMESTAMP());

-- FR-022: the platform default qualification rule. A deployment needs exactly one
-- open-ended (effective_end IS NULL) Default-scope rule at all times, or no attributed
-- call can ever be qualified. The conditions JSON must match QualificationConditions'
-- System.Text.Json shape (Attribution.Domain.Qualification.QualificationConditions) —
-- RequiredDirection is the CallDirection enum's underlying int (Inbound = 0).
INSERT INTO qualification_rules
    (id, scope_type, scope_ref, version, conditions, effective_start, effective_end, created_by, created_at)
VALUES
    ('00000000-0000-0000-0000-000000000008', 'Default', NULL, 1,
     '{"RequiredDirection":0,"AnsweredRequired":true,"MinConnectedDurationSeconds":60,"TimeOfDay":null}',
     '2020-01-01 00:00:00.000000', NULL, 'seed', UTC_TIMESTAMP());

SELECT @website_id AS website_id, @pool_a_id AS pool_a_id, @pool_b_id AS pool_b_id, @pool_c_id AS pool_c_id;
SELECT pool_id, did, status FROM tracking_numbers WHERE pool_id IN (@pool_a_id, @pool_b_id, @pool_c_id) ORDER BY pool_id, did;
