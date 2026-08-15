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
SET @pool_id     = '00000000-0000-0000-0000-000000000002';

INSERT INTO websites
    (id, name, permitted_origins, default_number, session_timeout_seconds, heartbeat_interval_seconds,
     allocation_window_extension_seconds, cooldown_seconds, consent_required, shadow_mode_enabled,
     local_timezone, created_at, updated_at)
-- Short session_timeout/heartbeat/extension/cooldown values (vs. a realistic ~30-minute
-- production setting) so repeated manual clicking through the demo pages doesn't exhaust
-- the small dummy pool for 30 minutes at a time.
VALUES
    (@website_id, 'Dev Test Website', 'http://localhost:4173\nhttp://localhost:3000', '+441632960000',
     120, 60, 60, 15, 1, 0, 'Europe/London', UTC_TIMESTAMP(), UTC_TIMESTAMP());

INSERT INTO number_pools (id, name, scope_type, scope_ref, created_at, updated_at)
VALUES (@pool_id, 'Dev Test Pool', 'website', @website_id, UTC_TIMESTAMP(), UTC_TIMESTAMP());

-- A small pool of dummy DIDs so allocation has more than one number to hand out across
-- concurrent manual-test sessions. The business's real tracking numbers are UK (not US),
-- so these use Ofcom's 01632 960000-960999 range, reserved specifically for fictional use
-- in dramas/testing and guaranteed never to be a real allocated number.
INSERT INTO tracking_numbers (id, pool_id, did, status, status_changed_at)
VALUES
    ('00000000-0000-0000-0000-000000000003', @pool_id, '+441632960001', 'Active', UTC_TIMESTAMP()),
    ('00000000-0000-0000-0000-000000000004', @pool_id, '+441632960002', 'Active', UTC_TIMESTAMP()),
    ('00000000-0000-0000-0000-000000000005', @pool_id, '+441632960003', 'Active', UTC_TIMESTAMP()),
    ('00000000-0000-0000-0000-000000000006', @pool_id, '+441632960004', 'Active', UTC_TIMESTAMP()),
    ('00000000-0000-0000-0000-000000000007', @pool_id, '+441632960005', 'Active', UTC_TIMESTAMP());

SELECT @website_id AS website_id, @pool_id AS pool_id;
SELECT did, status FROM tracking_numbers WHERE pool_id = @pool_id;
