# Apiche configuration reference — 8x8 Call Attribution Platform

This document is a complete, endpoint-by-endpoint translation of the current .NET
implementation's HTTP surface into Apiche's config model (one HTTP endpoint = one
parameterized SQL statement, `<param>` placeholders bound 1:1 to query-string parameters
on GET/DELETE or JSON body fields on POST/PUT). It is generated as part of the plan to
replace the ASP.NET Core + Dapper backend with Apiche while preserving identical
behavior, and is meant to be handed to a human configuring Apiche directly — it is not
itself code and changes nothing in `src/`. Every controller action in the 14 controllers
under `src/Attribution.Api/Controllers/` is covered, plus the four background workers,
translated using the real table/column names in the FluentMigrator migrations and the
real Dapper SQL in `src/Attribution.Infrastructure/Data/*.cs`.

**Admin and Reporting: direct database access (architecture decision).** After reviewing
this document, the project owner decided that the DNI section and the Background Worker
Jobs section stay exactly as genuine Apiche HTTP endpoints/scheduled jobs — DNI is public,
untrusted JavaScript running in visitors' browsers, which structurally can never hold real
database credentials, so it remains the one surface a gateway genuinely protects. Every
operation under an **Admin — ...** heading and under **Reporting**, however, is
documented below not as an Apiche HTTP endpoint but as **direct database access**: a
trusted internal client application (the existing admin tooling, or the existing
reporting portal — both already-trusted, already-authenticated internal operator tools,
exactly like the reporting portal's existing direct read-only database access under the
project constitution) connects straight to MySQL over a TLS connection authenticated as
that specific human operator's own native MySQL user account, and executes the exact SQL
statement or `CALL sp_xxx(...)` shown below directly, with no Apiche/HTTP layer and no
HTTP Basic Auth in between. Authorization for these two sections is enforced by MySQL's
own role-based access control — three native roles, `role_system_administrator`,
`role_marketing_administrator`, and `role_analyst`, granted to each individual human
user's own MySQL account per the same Role/permission mapping this document already
states (see each subsection's **Required MySQL role**) — rather than by an HTTP-layer
check, and the acting user's identity for audit-log writes is resolved via MySQL's own
`CURRENT_USER()` inside each stored procedure rather than needing to be injected by
anything. This is a deliberate architecture decision made after reviewing this document,
not a correction of an earlier error; see Coverage Notes for further detail.

**Conventions used throughout:**
- A nested JSON body object (e.g. `utm`, `time_of_day`, `arrival_details`) is bound to
  **one** placeholder carrying that sub-object's raw JSON text; the SQL/procedure then
  uses `JSON_EXTRACT`/`JSON_UNQUOTE` to pull fields out of it. A JSON array field (e.g.
  `matched_pool_ids`) is likewise bound to one placeholder carrying the array's JSON text,
  parsed with `JSON_TABLE`.
- An optional GET/DELETE query parameter that Apiche cannot omit from substitution is
  documented as accepting an empty string for "not supplied"; SQL guards it with
  `(<param> = '' OR ...)`, mirroring the nullable-parameter branches the current Dapper
  queries already use.
- All IDs are `CHAR(36)` text (GUIDs); every `<...>` placeholder that is a GUID is passed
  as its string form.
- Role names match `Attribution.Domain.Identity.Role`: `SystemAdministrator`,
  `MarketingAdministrator`, `Analyst`, `IntegrationService`. Per `RbacPolicy.cs`:
  `ManageUsers`, `ManagePools`, `ManageNumbers`, `ManagePrivacy` are System Administrator
  only; `ManageRules`, `ViewReports`, `ExportReports`, `ManualReview`,
  `ViewIntegrationHealth`, `AcknowledgeAlerts`, `ViewAuditLog` are granted to both System
  Administrator and Marketing Administrator; `ViewReports`/`ExportReports` are additionally
  granted to Analyst; Integration Service holds no interactive operation at all (FR-038).

---

## DNI (Dynamic Number Insertion — visitor-facing)

Source: `DniController.cs`, `Attribution.Api.Contracts.DniContracts.cs`,
`Attribution.Application.Allocation.AllocationService`/`ShadowAllocationService`,
`Attribution.Infrastructure.Data.AtomicAllocator`. Unauthenticated by design (FR-037);
origin-restricted and rate-limited instead of RBAC'd. Role for every endpoint in this
section: **no role — client credential only** (a client ID/secret pair with no RBAC
role attached, per the new auth model).

### Allocate a tracking number

- **Method**: POST
- **Path**: `/v1/dni/allocate`
- **Role**: no role — client credential only
- **Body fields**:
  - `website_id` (string, GUID) — the website requesting an allocation
  - `client_token` (string) — rate-limiting correlation token (not used by the SQL itself; consumed by Apiche's rate-limiting layer)
  - `consent_granted` (boolean)
  - `landing_page` (string, nullable)
  - `referrer` (string, nullable)
  - `utm` (object, nullable) — `{source, medium, campaign, term, content}`
  - `gclid`, `gbraid`, `wbraid` (string, nullable)
  - `ga4_client_id` (string, nullable)
  - `matched_pool_ids` (array of string GUIDs, nullable) — FR-050, multi-pool only
  - `session_id` (string, GUID, nullable) — FR-050, resuming an active multi-pool session
- **SQL**:
```sql
CALL sp_dni_allocate(<website_id>, <consent_granted>, <landing_page>, <referrer>, <utm>,
                      <gclid>, <gbraid>, <wbraid>, <ga4_client_id>, <matched_pool_ids>, <session_id>)
```
- See stored procedure `sp_dni_allocate` in Appendix A.

### Heartbeat

- **Method**: POST
- **Path**: `/v1/dni/heartbeat`
- **Role**: no role — client credential only
- **Body fields**:
  - `session_id` (string, GUID)
- **SQL**:
```sql
CALL sp_dni_heartbeat(<session_id>)
```
- See stored procedure `sp_dni_heartbeat` in Appendix A.

### Consent (grant or withdraw)

- **Method**: POST
- **Path**: `/v1/dni/consent`
- **Role**: no role — client credential only
- **Body fields**:
  - `session_id` (string, GUID, nullable — required for `consent: "withdrawn"`)
  - `client_token` (string) — not used by the SQL itself
  - `website_id` (string, GUID) — required for `consent: "granted"`
  - `consent` (string) — `"granted"` or `"withdrawn"`
  - `arrival_details` (object, nullable) — same shape as the allocate body (minus `website_id`/`consent_granted`), used only for a late grant
- **SQL**:
```sql
CALL sp_dni_consent(<website_id>, <session_id>, <consent>, <arrival_details>)
```
- See stored procedure `sp_dni_consent` in Appendix A.

### Shadow-mode observation

- **Method**: POST
- **Path**: `/v1/dni/shadow-observe`
- **Role**: no role — client credential only
- **Body fields**:
  - `website_id` (string, GUID)
  - `session_id` (string, GUID, nullable — accepted by the DTO but unused by `ShadowAllocationService`, which always starts a fresh session)
  - `observed_number` (string) — the number another (e.g. Mediahawk-style) system already displayed
  - `landing_page`, `referrer` (string, nullable)
  - `utm` (object, nullable)
  - `gclid`, `gbraid`, `wbraid`, `ga4_client_id` (string, nullable)
- **SQL**:
```sql
CALL sp_dni_shadow_observe(<website_id>, <observed_number>, <landing_page>, <referrer>, <utm>,
                            <gclid>, <gbraid>, <wbraid>, <ga4_client_id>)
```
- See stored procedure `sp_dni_shadow_observe` in Appendix A.

---

## Auth

Source: `AuthController.cs`. **Not applicable under the new design.** The current
`/v1/auth/sign-in` (local username/password + mandatory TOTP, returns a JWT access token
plus a rotating refresh token) and `/v1/auth/refresh` endpoints exist solely to support
session-based JWT authentication, which the new design explicitly replaces with per-user
long-lived HTTP Basic Auth credentials checked fresh on every request (no sessions, no
JWT, no TOTP). Apiche's native Basic Auth credential store performs this check natively
and needs no SQL endpoint of its own — see Coverage Notes.

---

## Admin — Websites

Source: `AdminWebsitesController.cs`. Every endpoint below is now direct database access
(no Apiche) — see "Admin and Reporting: direct database access" at the top of this
document. Required MySQL role for every operation: `role_system_administrator`
(`Operation.ManagePools`).

### List websites

- **Access**: Direct database connection (no Apiche) — the admin tooling's website list screen
- **Required MySQL role**: `role_system_administrator`
- **Parameters**: none
- **SQL**:
```sql
SELECT id, name, permitted_origins, default_number, session_timeout_seconds,
       heartbeat_interval_seconds, allocation_window_extension_seconds, cooldown_seconds,
       consent_required, shadow_mode_enabled, multi_pool_enabled, business_unit, local_timezone
FROM websites
ORDER BY name
```

### Enable shadow mode

- **Access**: Direct database connection (no Apiche) — the admin tooling's "enable shadow mode" toggle for a website
- **Required MySQL role**: `role_system_administrator`
- **Body fields**:
  - `id` (string, GUID) — path segment, bound as a body/query field for Apiche
- **SQL**:
```sql
CALL sp_set_website_shadow_mode(<id>, 1)
```
- Invoked directly against MySQL (no Apiche); see stored procedure `sp_set_website_shadow_mode` in Appendix A.

### Disable shadow mode

- **Access**: Direct database connection (no Apiche) — the admin tooling's "disable shadow mode" toggle for a website
- **Required MySQL role**: `role_system_administrator`
- **Body fields**: `id` (string, GUID)
- **SQL**:
```sql
CALL sp_set_website_shadow_mode(<id>, 0)
```
- Invoked directly against MySQL (no Apiche); see stored procedure `sp_set_website_shadow_mode` in Appendix A.

### Enable multi-pool

- **Access**: Direct database connection (no Apiche) — the admin tooling's "enable multi-pool" toggle for a website
- **Required MySQL role**: `role_system_administrator`
- **Body fields**: `id` (string, GUID)
- **SQL**:
```sql
CALL sp_set_website_multi_pool(<id>, 1)
```
- Invoked directly against MySQL (no Apiche); see stored procedure `sp_set_website_multi_pool` in Appendix A.

### Disable multi-pool

- **Access**: Direct database connection (no Apiche) — the admin tooling's "disable multi-pool" toggle for a website
- **Required MySQL role**: `role_system_administrator`
- **Body fields**: `id` (string, GUID)
- **SQL**:
```sql
CALL sp_set_website_multi_pool(<id>, 0)
```
- Invoked directly against MySQL (no Apiche); see stored procedure `sp_set_website_multi_pool` in Appendix A.

---

## Admin — Number Pools

Source: `AdminPoolsController.cs`, `AdminPoolsContracts.cs`. Every endpoint below is now
direct database access (no Apiche) — see "Admin and Reporting: direct database access" at
the top of this document. Required MySQL role for every operation:
`role_system_administrator` (`Operation.ManagePools`).

### Create pool

- **Access**: Direct database connection (no Apiche) — the admin tooling's pool-creation screen
- **Required MySQL role**: `role_system_administrator`
- **Body fields**:
  - `name` (string)
  - `scope_type` (string) — `"website" | "campaign" | "business_unit"`
  - `scope_ref` (string, GUID)
  - `default_number` (string, nullable)
- **SQL**:
```sql
CALL sp_create_pool(<name>, <scope_type>, <scope_ref>, <default_number>)
```
- Invoked directly against MySQL (no Apiche); see stored procedure `sp_create_pool` in Appendix A.

### List pools

- **Access**: Direct database connection (no Apiche) — the admin tooling's pool list screen
- **Required MySQL role**: `role_system_administrator`
- **Parameters**: none
- **SQL**:
```sql
SELECT np.id, np.name, np.scope_type, np.scope_ref, np.default_number,
       COUNT(tn.id) AS number_count,
       CASE WHEN COUNT(tn.id) = 0 THEN 0
            ELSE SUM(CASE WHEN tn.status = 'Active' THEN 1 ELSE 0 END) / COUNT(tn.id) END AS utilisation
FROM number_pools np
LEFT JOIN tracking_numbers tn ON tn.pool_id = np.id
GROUP BY np.id, np.name, np.scope_type, np.scope_ref, np.default_number
ORDER BY np.name
```

### Get one pool

- **Access**: Direct database connection (no Apiche) — the admin tooling's pool detail screen
- **Required MySQL role**: `role_system_administrator`
- **Parameters**: `id` (string, GUID)
- **SQL**:
```sql
SELECT np.id, np.name, np.scope_type, np.scope_ref, np.default_number,
       COUNT(tn.id) AS number_count,
       CASE WHEN COUNT(tn.id) = 0 THEN 0
            ELSE SUM(CASE WHEN tn.status = 'Active' THEN 1 ELSE 0 END) / COUNT(tn.id) END AS utilisation
FROM number_pools np
LEFT JOIN tracking_numbers tn ON tn.pool_id = np.id
WHERE np.id = <id>
GROUP BY np.id, np.name, np.scope_type, np.scope_ref, np.default_number
```

### List a pool's tracking numbers

- **Access**: Direct database connection (no Apiche) — the admin tooling's pool detail screen's number list
- **Required MySQL role**: `role_system_administrator`
- **Parameters**: `id` (string, GUID)
- **SQL**:
```sql
SELECT id, did, status, status_changed_at, last_released_at
FROM tracking_numbers
WHERE pool_id = <id>
```

### Import numbers (browser upload)

The current endpoint accepts a multipart CSV file (`IFormFile`), one DID per line. Apiche
has no file-upload primitive — POST/PUT bodies are JSON objects — so this is reshaped to
accept a JSON array of candidate DID strings instead (the browser/admin UI parses the CSV
client-side before calling this endpoint). See Coverage Notes.

- **Access**: Direct database connection (no Apiche) — the admin tooling's CSV import screen, after the browser has parsed the uploaded file client-side
- **Required MySQL role**: `role_system_administrator`
- **Body fields**:
  - `id` (string, GUID) — path segment
  - `dids` (array of string) — one candidate DID per CSV row, in file order
- **SQL**:
```sql
CALL sp_import_tracking_numbers(<id>, <dids>)
```
- Invoked directly against MySQL (no Apiche); see stored procedure `sp_import_tracking_numbers` in Appendix A.

### List import-folder files

Not a SQL operation at all — it lists `*.csv` files in a server-side folder
(`NumberImportOptions.FolderPath`). See Coverage Notes.

- **Access**: Not a database operation at all, direct or otherwise — the admin tooling's from-folder import screen lists `*.csv` files from a server-side folder via ordinary filesystem access, exactly as before. Gating who may see this listing is still policy-equivalent to `role_system_administrator` (enforced by the admin tooling itself, since there is no SQL/MySQL grant to attach it to)
- **Parameters**: none
- **SQL**: N/A — not expressible as a SQL statement; see Coverage Notes.

### Import numbers from folder

- **Access**: Direct database connection (no Apiche) — the admin tooling's from-folder import action
- **Required MySQL role**: `role_system_administrator`
- **Body fields**:
  - `id` (string, GUID) — path segment
  - `file_name` (string) — a bare file name (no path segments) inside the configured import folder
- **SQL**:
```sql
CALL sp_import_tracking_numbers_from_folder(<id>, <file_name>)
```
- Invoked directly against MySQL (no Apiche); see stored procedure `sp_import_tracking_numbers_from_folder` in Appendix A (requires the MySQL server process itself to have filesystem access to the configured import folder — see Coverage Notes).

---

## Admin — Numbers

Source: `AdminNumbersController.cs`. Every endpoint below is now direct database access
(no Apiche) — see "Admin and Reporting: direct database access" at the top of this
document. Required MySQL role for every operation: `role_system_administrator`
(`Operation.ManageNumbers`).

### Suspend

- **Access**: Direct database connection (no Apiche) — the admin tooling's "suspend" action on a tracking number
- **Required MySQL role**: `role_system_administrator`
- **Body fields**: `id` (string, GUID)
- **SQL**:
```sql
CALL sp_change_tracking_number_status(<id>, 'Suspended', 'SuspendTrackingNumber')
```
- Invoked directly against MySQL (no Apiche); see stored procedure `sp_change_tracking_number_status` in Appendix A.

### Retire

- **Access**: Direct database connection (no Apiche) — the admin tooling's "retire" action on a tracking number
- **Required MySQL role**: `role_system_administrator`
- **Body fields**: `id` (string, GUID)
- **SQL**:
```sql
CALL sp_change_tracking_number_status(<id>, 'Retired', 'RetireTrackingNumber')
```
- Invoked directly against MySQL (no Apiche); see stored procedure `sp_change_tracking_number_status` in Appendix A.

### Reactivate

- **Access**: Direct database connection (no Apiche) — the admin tooling's "reactivate" action on a tracking number
- **Required MySQL role**: `role_system_administrator`
- **Body fields**: `id` (string, GUID)
- **SQL**:
```sql
CALL sp_change_tracking_number_status(<id>, 'Active', 'ReactivateTrackingNumber')
```
- Invoked directly against MySQL (no Apiche); see stored procedure `sp_change_tracking_number_status` in Appendix A.

### Move to another pool

- **Access**: Direct database connection (no Apiche) — the admin tooling's "move to pool" action on a tracking number
- **Required MySQL role**: `role_system_administrator`
- **Body fields**:
  - `id` (string, GUID) — path segment
  - `target_pool_id` (string, GUID)
- **SQL**:
```sql
CALL sp_move_tracking_number(<id>, <target_pool_id>)
```
- Invoked directly against MySQL (no Apiche); see stored procedure `sp_move_tracking_number` in Appendix A.

---

## Admin — Users

Source: `AdminUsersController.cs`. Every endpoint below is now direct database access (no
Apiche) — see "Admin and Reporting: direct database access" at the top of this document.
Required MySQL role for every operation: `role_system_administrator`
(`Operation.ManageUsers`). Note: password storage/verification itself is Apiche's native
Basic Auth credential store (out of scope per the task); the SQL here only maintains the
`users` table's RBAC role metadata (username, role, active flag) that `RbacPolicy`
resolves against. See Coverage Notes.

### List users

- **Access**: Direct database connection (no Apiche) — the admin tooling's user list screen
- **Required MySQL role**: `role_system_administrator`
- **Parameters**: none
- **SQL**:
```sql
SELECT id, username, client_id, identity_type, mapped_role, role_override,
       role_overridden_by, is_active, created_at, last_seen_at,
       COALESCE(role_override, mapped_role) AS effective_role
FROM users
ORDER BY created_at
```

### Create user

- **Access**: Direct database connection (no Apiche) — the admin tooling's user-creation screen
- **Required MySQL role**: `role_system_administrator`
- **Body fields**:
  - `username` (string) — becomes the native database account's own username (`db_username`)
  - `role` (string) — `SystemAdministrator | MarketingAdministrator | Analyst | IntegrationService`; for `Local` accounts (every role except `IntegrationService`) this determines which native database role (`role_system_administrator`/`role_marketing_administrator`/`role_analyst`) the account is granted
- **Not a field here**: there is no `password` field. The native account's actual password is set through the database's own account-creation facility (`CREATE USER ... IDENTIFIED BY`), communicated to the new user out of band — never through this operation or stored in the platform's own records (data-model.md's User/Role note; `IntegrationService` accounts remain the one exception, still registered against Apiche's native Basic Auth credential store since that role stays Apiche-authenticated).
- **SQL**:
```sql
CALL sp_create_user(<username>, <role>)
```
- Invoked directly against MySQL (no Apiche); see stored procedure `sp_create_user` in Appendix A.

### Deactivate user

- **Access**: Direct database connection (no Apiche) — the admin tooling's "deactivate user" action
- **Required MySQL role**: `role_system_administrator`
- **Body fields**:
  - `id` (string, GUID) — path segment
- **SQL** (the actor is resolved from `CURRENT_USER()` inside the procedure, not passed as a parameter):
```sql
CALL sp_deactivate_user(<id>)
```
- Invoked directly against MySQL (no Apiche); see stored procedure `sp_deactivate_user` in Appendix A.

### Override role

- **Access**: Direct database connection (no Apiche) — the admin tooling's "override role" action
- **Required MySQL role**: `role_system_administrator`
- **Body fields**:
  - `id` (string, GUID) — path segment
  - `role` (string) — the new role
- **SQL** (the actor is resolved from `CURRENT_USER()` inside the procedure, not passed as a parameter):
```sql
CALL sp_override_user_role(<id>, <role>)
```
- Invoked directly against MySQL (no Apiche); see stored procedure `sp_override_user_role` in Appendix A.

---

## Admin — Qualification Rules

Source: `AdminQualificationRulesController.cs`, `AdminQualificationRulesContracts.cs`.
Every endpoint below is now direct database access (no Apiche) — see "Admin and
Reporting: direct database access" at the top of this document. Required MySQL role for
every operation: `role_system_administrator` or `role_marketing_administrator`
(`Operation.ManageRules`).

### List rule versions for a scope

- **Access**: Direct database connection (no Apiche) — the admin tooling's qualification-rule version history screen
- **Required MySQL role**: `role_system_administrator` and `role_marketing_administrator`
- **Parameters**:
  - `scope_type` (string) — `default | website | campaign`
  - `scope_ref` (string, nullable) — required for `website`/`campaign`, empty for `default`
- **SQL**:
```sql
SELECT id, scope_type, scope_ref, version, conditions, effective_start, effective_end, created_by, created_at
FROM qualification_rules
WHERE scope_type = <scope_type> AND scope_ref <=> NULLIF(<scope_ref>, '')
ORDER BY version
```

### Create a new rule version

- **Access**: Direct database connection (no Apiche) — the admin tooling's "create rule version" screen
- **Required MySQL role**: `role_system_administrator` and `role_marketing_administrator`
- **Body fields**:
  - `scope_type` (string) — `default | website | campaign`
  - `scope_ref` (string, nullable) — required for `website`/`campaign`
  - `conditions` (object) — `{direction, answered_required, min_connected_duration_seconds, time_of_day: {start, end} | null}`
  - `effective_start` (string, ISO datetime)
- **SQL** (`created_by` is resolved from `CURRENT_USER()` inside the procedure, not passed as a parameter):
```sql
CALL sp_create_qualification_rule_version(<scope_type>, <scope_ref>, <conditions>, <effective_start>)
```
- Invoked directly against MySQL (no Apiche); see stored procedure `sp_create_qualification_rule_version` in Appendix A.

### Delete a not-yet-effective future version

- **Access**: Direct database connection (no Apiche) — the admin tooling's "delete future rule version" action
- **Required MySQL role**: `role_system_administrator` and `role_marketing_administrator`
- **Parameters**: `id` (string, GUID)
- **SQL**:
```sql
CALL sp_delete_future_qualification_rule_version(<id>)
```
- Invoked directly against MySQL (no Apiche); see stored procedure `sp_delete_future_qualification_rule_version` in Appendix A.

---

## Admin — Review

Source: `AdminReviewController.cs`. Every endpoint below is now direct database access
(no Apiche) — see "Admin and Reporting: direct database access" at the top of this
document. Required MySQL role for every operation: `role_system_administrator` or
`role_marketing_administrator` (`Operation.ManualReview`).

### List open review cases

- **Access**: Direct database connection (no Apiche) — the admin tooling's open review-case queue
- **Required MySQL role**: `role_system_administrator` and `role_marketing_administrator`
- **Parameters**: none
- **SQL**:
```sql
SELECT id, call_id, attribution_id, status, opened_at,
       TIMESTAMPDIFF(SECOND, opened_at, UTC_TIMESTAMP()) AS age_seconds
FROM review_cases
WHERE status = 'Open'
ORDER BY opened_at ASC
```
(`past_age_threshold` in the current JSON response is a client-side comparison against the
configured `AlertingThresholds.ReviewCaseAge` value, not a stored column — the admin
tooling compares `age_seconds` against that same configured threshold.)

### Resolve a review case

- **Access**: Direct database connection (no Apiche) — the admin tooling's "resolve review case" action
- **Required MySQL role**: `role_system_administrator` and `role_marketing_administrator`
- **Body fields**:
  - `id` (string, GUID) — path segment
  - `session_id` (string, GUID, nullable) — provide this XOR `confirm_unattributed`
  - `confirm_unattributed` (boolean, nullable)
- **SQL** (the resolving user is resolved from `CURRENT_USER()` inside the procedure, not passed as a parameter):
```sql
CALL sp_resolve_review_case(<id>, <session_id>, <confirm_unattributed>)
```
- Invoked directly against MySQL (no Apiche); see stored procedure `sp_resolve_review_case` in Appendix A. (The Google Ads/GA4
  correction side effect this can trigger requires an outbound HTTP call the stored
  procedure itself cannot make — see Coverage Notes.)

---

## Admin — Alerts

Source: `AdminAlertsController.cs`. Every endpoint below is now direct database access
(no Apiche) — see "Admin and Reporting: direct database access" at the top of this
document. Required MySQL role for every operation: `role_system_administrator` or
`role_marketing_administrator` (`Operation.AcknowledgeAlerts`).

### List open alerts

- **Access**: Direct database connection (no Apiche) — the admin tooling's open alerts list
- **Required MySQL role**: `role_system_administrator` and `role_marketing_administrator`
- **Parameters**: none
- **SQL**:
```sql
SELECT id, condition_type, scope_ref, threshold, raised_at, last_notified_at,
       acknowledged_at, acknowledged_by, cleared_at
FROM alerts
WHERE cleared_at IS NULL
ORDER BY raised_at ASC
```

### Acknowledge an alert

- **Access**: Direct database connection (no Apiche) — the admin tooling's "acknowledge alert" action
- **Required MySQL role**: `role_system_administrator` and `role_marketing_administrator`
- **Body fields**:
  - `id` (string, GUID) — path segment
- **SQL** (the acknowledging user is resolved from `CURRENT_USER()` inside the procedure, not passed as a parameter):
```sql
CALL sp_acknowledge_alert(<id>)
```
- Invoked directly against MySQL (no Apiche); see stored procedure `sp_acknowledge_alert` in Appendix A. (The immediate
  acknowledged-webhook/email delivery this currently performs synchronously requires an
  outbound HTTP call the stored procedure cannot make — see Coverage Notes.)

---

## Admin — Audit

Source: `AdminAuditController.cs`. Read-only; no PUT/PATCH/DELETE exists on this resource
by design (the audit log is append-only). This endpoint is now direct database access (no
Apiche) — see "Admin and Reporting: direct database access" at the top of this document.
Required MySQL role: `role_system_administrator` or `role_marketing_administrator`
(`Operation.ViewAuditLog`).

### Query the audit log

- **Access**: Direct database connection (no Apiche) — the admin tooling's audit-log search screen
- **Required MySQL role**: `role_system_administrator` and `role_marketing_administrator`
- **Parameters**:
  - `target_type` (string, nullable — empty string means "not supplied")
  - `target_id` (string, nullable)
  - `from` (string, ISO datetime, nullable)
  - `to` (string, ISO datetime, nullable)
- **SQL**:
```sql
SELECT id, actor_user_id, action, target_type, target_id, before_value, after_value, occurred_at
FROM audit_entries
WHERE (<target_type> = '' OR target_type = <target_type>)
  AND (<target_id> = '' OR target_id = <target_id>)
  AND (<from> = '' OR occurred_at >= <from>)
  AND (<to> = '' OR occurred_at <= <to>)
ORDER BY occurred_at
```

---

## Admin — Health

Source: `AdminHealthController.cs`. Every endpoint below is now direct database access
(no Apiche) — see "Admin and Reporting: direct database access" at the top of this
document. Required MySQL role for every operation: `role_system_administrator` or
`role_marketing_administrator` (`Operation.ViewIntegrationHealth`). Threshold values
(`AlertingThresholds.IngestionLag`, `.PublicationFailureRate`, `.PoolUtilisation`) are
deployment configuration, not table data — each query returns the raw metric and the
admin tooling compares it against the configured threshold, exactly as the current thin
controller methods already do in C#.

### Ingestion health

- **Access**: Direct database connection (no Apiche) — the admin tooling's integration-health dashboard, ingestion panel
- **Required MySQL role**: `role_system_administrator` and `role_marketing_administrator`
- **Parameters**: none
- **SQL**:
```sql
SELECT '8x8-cdr' AS feed, updated_at AS last_successful_ingestion_at,
       TIMESTAMPDIFF(SECOND, updated_at, UTC_TIMESTAMP()) AS current_lag_seconds
FROM ingestion_checkpoints
WHERE feed = '8x8-cdr'
```

### Publication health

- **Access**: Direct database connection (no Apiche) — the admin tooling's integration-health dashboard, publication panel
- **Required MySQL role**: `role_system_administrator` and `role_marketing_administrator`
- **Parameters**: none
- **SQL**:
```sql
SELECT destination,
       SUM(CASE WHEN status IN ('Sent', 'Adjusted') THEN 1 ELSE 0 END) AS sent,
       SUM(CASE WHEN status IN ('Failed', 'Rejected') THEN 1 ELSE 0 END) AS failed,
       CASE WHEN COUNT(*) = 0 THEN NULL
            ELSE SUM(CASE WHEN status IN ('Failed', 'Rejected') THEN 1 ELSE 0 END) / COUNT(*) END AS failure_rate
FROM conversion_publications
WHERE destination IN ('GoogleAds', 'Ga4')
GROUP BY destination
```
(This produces one row per destination that has at least one publication row; a
destination with zero rows — `sent=0, failed=0, failure_rate=NULL, healthy=true` in the
current C# — needs the admin tooling to fill in the two enum values `GoogleAds`/`Ga4` that
this GROUP BY may omit; see Coverage Notes.)

### Pool health

- **Access**: Direct database connection (no Apiche) — the admin tooling's integration-health dashboard, pool panel
- **Required MySQL role**: `role_system_administrator` and `role_marketing_administrator`
- **Parameters**: none
- **SQL**:
```sql
SELECT np.id AS pool_id, np.name AS pool_name,
       COUNT(DISTINCT CASE WHEN a.id IS NOT NULL THEN tn.id END) AS held_numbers,
       COUNT(DISTINCT tn.id) AS total_numbers
FROM number_pools np
JOIN tracking_numbers tn ON tn.pool_id = np.id AND tn.status = 'Active'
LEFT JOIN allocations a ON a.tracking_number_id = tn.id AND a.window_end > UTC_TIMESTAMP()
GROUP BY np.id, np.name
```

### Notification delivery health

- **Access**: Direct database connection (no Apiche) — the admin tooling's integration-health dashboard, notification-delivery panel
- **Required MySQL role**: `role_system_administrator` and `role_marketing_administrator`
- **Parameters**: none
- **SQL**:
```sql
SELECT channel, last_attempt_at, last_success_at, last_failure_at, last_failure_reason,
       (last_failure_at IS NULL OR (last_success_at IS NOT NULL AND last_success_at >= last_failure_at)) AS healthy
FROM notification_delivery_status
```

---

## Admin — Privacy

Source: `AdminPrivacyController.cs`. This endpoint is now direct database access (no
Apiche) — see "Admin and Reporting: direct database access" at the top of this document.
Required MySQL role: `role_system_administrator` (`Operation.ManagePrivacy`).

### Erase a visitor's data

- **Access**: Direct database connection (no Apiche) — the admin tooling's "erase visitor data" action
- **Required MySQL role**: `role_system_administrator`
- **Body fields**:
  - `id` (string, GUID) — path segment
- **SQL** (the requesting administrator is resolved from `CURRENT_USER()` inside the procedure, not passed as a parameter):
```sql
CALL sp_erase_visitor(<id>)
```
- Invoked directly against MySQL (no Apiche); see stored procedure `sp_erase_visitor` in Appendix A (the surrogate-hash construction
  it uses is a documented approximation of the app's keyed HMAC-SHA256 — see Coverage
  Notes).

---

## Reporting

Source: `ReportsController.cs`, `Attribution.Application.Administration.ReportingService`,
`Attribution.Infrastructure.Data.ReportingRepository`. Every report below is now direct
database access (no Apiche) — see "Admin and Reporting: direct database access" at the
top of this document. Required MySQL role for every operation: `role_analyst`,
`role_marketing_administrator`, and `role_system_administrator` (`Operation.ViewReports`
and `Operation.ExportReports` — Integration Service holds neither, FR-038). There is no
longer a JSON endpoint and a separate `.../export.csv` twin: with no HTTP layer to route
between two response formats, that distinction collapses into a single operation — the
reporting client runs the one SELECT below once and renders the result set as an
on-screen report or exports it as a CSV file, both from the exact same query result, so
the two can never disagree. `<from>`/`<to>` are `YYYY-MM-DD` calendar dates; `<to>` is
inclusive as a calendar day, so the SQL uses the day *after* it as the exclusive upper
bound, matching `ReportingRepository`'s own `To()` helper.

### Dashboard

- **Access**: Direct database connection (no Apiche) — the reporting portal's dashboard view
- **Required MySQL role**: `role_analyst`, `role_marketing_administrator`, and `role_system_administrator`
- **Parameters**: `from` (date), `to` (date)
- **SQL**:
```sql
SELECT w.id AS website_id, w.name AS website_name,
       COUNT(DISTINCT c.id) AS total_calls,
       COUNT(DISTINCT CASE WHEN a.state = 'Attributed' THEN c.id END) AS attributed_calls,
       COUNT(DISTINCT CASE WHEN qr.is_qualified = 1 THEN c.id END) AS qualified_calls
FROM calls c
LEFT JOIN attributions a ON a.call_id = c.id AND a.is_current = 1
LEFT JOIN sessions s ON s.id = a.session_id
LEFT JOIN websites w ON w.id = s.website_id
LEFT JOIN qualification_results qr ON qr.call_id = c.id AND qr.is_current = 1
WHERE c.started_at >= <from> AND c.started_at < DATE_ADD(<to>, INTERVAL 1 DAY)
GROUP BY w.id, w.name
ORDER BY total_calls DESC
```
(Totals — `total_calls`, `attributed_calls`, `attribution_rate`, `qualified_calls` — are
derived by summing/dividing this same row set, exactly as `ReportingService.DashboardAsync`
does; the reporting portal performs that same aggregation over the returned rows, or a
second, identical-filter aggregate query can be run directly against MySQL if totals must
come from SQL itself.)

### Campaigns

- **Access**: Direct database connection (no Apiche) — the reporting portal's campaigns view
- **Required MySQL role**: `role_analyst`, `role_marketing_administrator`, and `role_system_administrator`
- **Parameters**: `from` (date), `to` (date)
- **SQL**:
```sql
SELECT COALESCE(s.utm_campaign, '(none)') AS campaign,
       COUNT(*) AS total_calls,
       SUM(CASE WHEN qr.is_qualified = 1 THEN 1 ELSE 0 END) AS qualified_calls
FROM calls c
JOIN attributions a ON a.call_id = c.id AND a.is_current = 1 AND a.state = 'Attributed'
JOIN sessions s ON s.id = a.session_id
LEFT JOIN qualification_results qr ON qr.call_id = c.id AND qr.is_current = 1
WHERE c.started_at >= <from> AND c.started_at < DATE_ADD(<to>, INTERVAL 1 DAY)
GROUP BY COALESCE(s.utm_campaign, '(none)')
ORDER BY total_calls DESC
```

### Calls

- **Access**: Direct database connection (no Apiche) — the reporting portal's calls view
- **Required MySQL role**: `role_analyst`, `role_marketing_administrator`, and `role_system_administrator`
- **Parameters**: `from` (date), `to` (date), `state` (string, nullable — `Attributed | Unattributed | Ambiguous`), `q` (string, nullable — free-text match against dialled/caller number)
- **SQL**:
```sql
SELECT c.id AS call_id, c.started_at, c.direction, c.dialled_number, c.caller_id, c.is_final,
       c.connected_duration_seconds, a.state AS attribution_state, a.reason AS attribution_reason,
       a.is_shadow_derived, qr.is_qualified, s.utm_campaign AS campaign
FROM calls c
LEFT JOIN attributions a ON a.call_id = c.id AND a.is_current = 1
LEFT JOIN qualification_results qr ON qr.call_id = c.id AND qr.is_current = 1
LEFT JOIN sessions s ON s.id = a.session_id
WHERE c.started_at >= <from> AND c.started_at < DATE_ADD(<to>, INTERVAL 1 DAY)
  AND (<state> = '' OR a.state = <state>)
  AND (<q> = '' OR c.dialled_number LIKE CONCAT('%', <q>, '%') OR c.caller_id LIKE CONCAT('%', <q>, '%'))
ORDER BY c.started_at DESC
```

### Missed

- **Access**: Direct database connection (no Apiche) — the reporting portal's missed-calls view
- **Required MySQL role**: `role_analyst`, `role_marketing_administrator`, and `role_system_administrator`
- **Parameters**: `from` (date), `to` (date)
- **SQL**:
```sql
SELECT c.id AS call_id, c.started_at, c.dialled_number, c.caller_id,
       s.utm_campaign AS campaign, w.id AS website_id, w.name AS website_name
FROM calls c
LEFT JOIN attributions a ON a.call_id = c.id AND a.is_current = 1
LEFT JOIN sessions s ON s.id = a.session_id
LEFT JOIN websites w ON w.id = s.website_id
WHERE c.started_at >= <from> AND c.started_at < DATE_ADD(<to>, INTERVAL 1 DAY)
  AND c.direction = 'Inbound' AND c.answered_at IS NULL
ORDER BY c.started_at DESC
```

### Qualified

- **Access**: Direct database connection (no Apiche) — the reporting portal's qualified-calls view
- **Required MySQL role**: `role_analyst`, `role_marketing_administrator`, and `role_system_administrator`
- **Parameters**: `from` (date), `to` (date)
- **SQL**:
```sql
SELECT c.id AS call_id, c.started_at, c.dialled_number, c.caller_id, c.connected_duration_seconds,
       s.utm_campaign AS campaign, w.id AS website_id, w.name AS website_name
FROM calls c
JOIN qualification_results qr ON qr.call_id = c.id AND qr.is_current = 1 AND qr.is_qualified = 1
LEFT JOIN attributions a ON a.call_id = c.id AND a.is_current = 1
LEFT JOIN sessions s ON s.id = a.session_id
LEFT JOIN websites w ON w.id = s.website_id
WHERE c.started_at >= <from> AND c.started_at < DATE_ADD(<to>, INTERVAL 1 DAY)
ORDER BY c.started_at DESC
```

### Unattributed

- **Access**: Direct database connection (no Apiche) — the reporting portal's unattributed-calls view
- **Required MySQL role**: `role_analyst`, `role_marketing_administrator`, and `role_system_administrator`
- **Parameters**: `from` (date), `to` (date)
- **SQL**:
```sql
SELECT c.id AS call_id, c.started_at, c.dialled_number, c.caller_id,
       a.state AS attribution_state, a.reason AS reason, a.is_shadow_derived
FROM calls c
JOIN attributions a ON a.call_id = c.id AND a.is_current = 1 AND a.state IN ('Unattributed', 'Ambiguous')
WHERE c.started_at >= <from> AND c.started_at < DATE_ADD(<to>, INTERVAL 1 DAY)
ORDER BY c.started_at DESC
```
(`totals.by_reason`, a group-by-reason count breakdown, is derived client-side from these
same rows in `ReportingService.UnattributedAsync`; the reporting portal performs the same
grouping over the returned rows.)

### Coverage

- **Access**: Direct database connection (no Apiche) — the reporting portal's coverage view
- **Required MySQL role**: `role_analyst`, `role_marketing_administrator`, and `role_system_administrator`
- **Parameters**: `from` (date), `to` (date)
- **SQL**:
```sql
SELECT w.id AS website_id, w.name AS website_name, a.state AS state, a.reason AS reason,
       a.is_shadow_derived, COUNT(*) AS count
FROM calls c
LEFT JOIN attributions a ON a.call_id = c.id AND a.is_current = 1
LEFT JOIN sessions s ON s.id = a.session_id
LEFT JOIN websites w ON w.id = s.website_id
WHERE c.started_at >= <from> AND c.started_at < DATE_ADD(<to>, INTERVAL 1 DAY)
GROUP BY w.id, w.name, a.state, a.reason, a.is_shadow_derived
ORDER BY w.name, a.state
```

---

## Appendix A — Stored Procedures

### sp_dni_allocate

Replaces `Attribution.Application.Allocation.AllocationService.AllocateAsync` (both the
single-pool and FR-050 multi-pool branches) and
`Attribution.Infrastructure.Data.AtomicAllocator.TryAllocateAsync`/
`TryAllocateAdditionalAsync`'s `FOR UPDATE SKIP LOCKED` candidate pick.

```sql
CREATE PROCEDURE sp_dni_allocate(
    IN p_website_id CHAR(36),
    IN p_consent_granted TINYINT(1),
    IN p_landing_page VARCHAR(2048),
    IN p_referrer VARCHAR(2048),
    IN p_utm JSON,
    IN p_gclid VARCHAR(255),
    IN p_gbraid VARCHAR(255),
    IN p_wbraid VARCHAR(255),
    IN p_ga4_client_id VARCHAR(255),
    IN p_matched_pool_ids JSON,
    IN p_session_id CHAR(36)
)
BEGIN
    DECLARE v_multi_pool TINYINT(1);
    DECLARE v_default_number VARCHAR(32);
    DECLARE v_session_timeout INT;
    DECLARE v_cooldown INT;
    DECLARE v_window_ext INT;
    DECLARE v_visitor_id CHAR(36);
    DECLARE v_session_id CHAR(36);
    DECLARE v_pool_id CHAR(36);
    DECLARE v_tracking_number_id CHAR(36);
    DECLARE v_did VARCHAR(32);
    DECLARE v_now DATETIME(6) DEFAULT UTC_TIMESTAMP(6);
    DECLARE v_expires_at DATETIME(6);
    DECLARE v_utm_source VARCHAR(255);
    DECLARE v_utm_medium VARCHAR(255);
    DECLARE v_utm_campaign VARCHAR(255);
    DECLARE v_utm_term VARCHAR(255);
    DECLARE v_utm_content VARCHAR(255);
    DECLARE v_done INT DEFAULT 0;

    SELECT multi_pool_enabled, default_number, session_timeout_seconds, cooldown_seconds,
           allocation_window_extension_seconds
    INTO v_multi_pool, v_default_number, v_session_timeout, v_cooldown, v_window_ext
    FROM websites WHERE id = p_website_id;

    SET v_utm_source   = JSON_UNQUOTE(JSON_EXTRACT(p_utm, '$.source'));
    SET v_utm_medium   = JSON_UNQUOTE(JSON_EXTRACT(p_utm, '$.medium'));
    SET v_utm_campaign = JSON_UNQUOTE(JSON_EXTRACT(p_utm, '$.campaign'));
    SET v_utm_term     = JSON_UNQUOTE(JSON_EXTRACT(p_utm, '$.term'));
    SET v_utm_content  = JSON_UNQUOTE(JSON_EXTRACT(p_utm, '$.content'));

    DROP TEMPORARY TABLE IF EXISTS tmp_result;
    CREATE TEMPORARY TABLE tmp_result (
        session_id CHAR(36) NULL, number VARCHAR(32) NULL, reason VARCHAR(64) NULL,
        expires_at DATETIME(6) NULL, pool_id CHAR(36) NULL, pool_default_number VARCHAR(32) NULL,
        allocated_number VARCHAR(32) NULL
    );

    -- ===== Single-pool website (the ordinary path) =====
    IF v_multi_pool = 0 OR v_multi_pool IS NULL THEN
        IF p_consent_granted = 0 THEN
            INSERT INTO tmp_result (number, reason) VALUES (v_default_number, 'no_consent');
        ELSE
            SELECT id INTO v_pool_id FROM number_pools
            WHERE scope_type = 'website' AND scope_ref = p_website_id LIMIT 1;

            IF v_pool_id IS NULL THEN
                INSERT INTO tmp_result (number, reason) VALUES (v_default_number, 'no_pool_configured');
            ELSE
                START TRANSACTION;
                SELECT tn.id INTO v_tracking_number_id FROM tracking_numbers tn
                WHERE tn.pool_id = v_pool_id AND tn.status = 'Active'
                  AND NOT EXISTS (
                        SELECT 1 FROM allocations al
                        WHERE al.tracking_number_id = tn.id
                          AND v_now < DATE_ADD(al.window_end, INTERVAL v_cooldown SECOND))
                ORDER BY tn.last_released_at ASC
                LIMIT 1 FOR UPDATE SKIP LOCKED;

                IF v_tracking_number_id IS NULL THEN
                    ROLLBACK;
                    INSERT INTO tmp_result (number, reason) VALUES (v_default_number, 'pool_exhausted');
                ELSE
                    SET v_visitor_id = UUID();
                    SET v_session_id = UUID();
                    SET v_expires_at = DATE_ADD(v_now, INTERVAL v_session_timeout SECOND);

                    INSERT INTO visitors (id, website_id, first_seen_at) VALUES (v_visitor_id, p_website_id, v_now);
                    INSERT INTO sessions (id, visitor_id, website_id, landing_page, referrer, utm_source, utm_medium,
                        utm_campaign, utm_term, utm_content, gclid, gbraid, wbraid, ga4_client_id,
                        consent_state, provenance, started_at, expires_at)
                    VALUES (v_session_id, v_visitor_id, p_website_id, p_landing_page, p_referrer, v_utm_source,
                        v_utm_medium, v_utm_campaign, v_utm_term, v_utm_content, p_gclid, p_gbraid, p_wbraid,
                        p_ga4_client_id, 'Granted', 'Ordinary', v_now, v_expires_at);
                    INSERT INTO allocations (id, tracking_number_id, session_id, pool_id_at_allocation,
                        window_start, window_end, is_shadow, created_at)
                    VALUES (UUID(), v_tracking_number_id, v_session_id, v_pool_id, v_now,
                        DATE_ADD(v_expires_at, INTERVAL v_window_ext SECOND), 0, v_now);

                    SELECT did INTO v_did FROM tracking_numbers WHERE id = v_tracking_number_id;
                    COMMIT;

                    INSERT INTO tmp_result (session_id, number, expires_at) VALUES (v_session_id, v_did, v_expires_at);
                END IF;
            END IF;
        END IF;

    -- ===== Multi-pool website (FR-050) =====
    ELSE
        -- Static pool -> default-number map, always returned (safe pre-consent, FR-039).
        INSERT INTO tmp_result (pool_id, pool_default_number)
        SELECT np.id, COALESCE(np.default_number, v_default_number)
        FROM number_pools np WHERE np.scope_type = 'website' AND np.scope_ref = p_website_id;

        IF p_consent_granted = 0 THEN
            UPDATE tmp_result SET reason = 'no_consent' WHERE session_id IS NULL AND number IS NULL LIMIT 1;
            IF ROW_COUNT() = 0 THEN INSERT INTO tmp_result (reason) VALUES ('no_consent'); END IF;
        ELSE
            DROP TEMPORARY TABLE IF EXISTS tmp_requested_pools;
            CREATE TEMPORARY TABLE tmp_requested_pools (pool_id CHAR(36));
            INSERT INTO tmp_requested_pools (pool_id)
            SELECT DISTINCT jt.pool_id FROM JSON_TABLE(p_matched_pool_ids, '$[*]' COLUMNS (pool_id CHAR(36) PATH '$')) jt
            JOIN number_pools np ON np.id = jt.pool_id AND np.scope_type = 'website' AND np.scope_ref = p_website_id;

            IF (SELECT COUNT(*) FROM tmp_requested_pools) = 0 THEN
                INSERT INTO tmp_result (reason) VALUES ('pending_match');
            ELSE
                -- Resume an existing, still-active session (research.md §15): allocate only
                -- pools it doesn't already hold.
                IF p_session_id IS NOT NULL AND EXISTS (
                    SELECT 1 FROM sessions WHERE id = p_session_id AND ended_at IS NULL AND expires_at > v_now)
                THEN
                    SET v_session_id = p_session_id;
                    SELECT expires_at INTO v_expires_at FROM sessions WHERE id = v_session_id;
                    DELETE FROM tmp_requested_pools WHERE pool_id IN (
                        SELECT pool_id_at_allocation FROM allocations WHERE session_id = v_session_id
                          AND pool_id_at_allocation IS NOT NULL);
                ELSE
                    SET v_visitor_id = UUID();
                    SET v_session_id = UUID();
                    SET v_expires_at = DATE_ADD(v_now, INTERVAL v_session_timeout SECOND);
                END IF;

                SET v_done = 0;
                WHILE v_done = 0 DO
                    SELECT pool_id INTO v_pool_id FROM tmp_requested_pools LIMIT 1;
                    IF v_pool_id IS NULL THEN
                        SET v_done = 1;
                    ELSE
                        START TRANSACTION;
                        SET v_tracking_number_id = NULL;
                        SELECT tn.id INTO v_tracking_number_id FROM tracking_numbers tn
                        WHERE tn.pool_id = v_pool_id AND tn.status = 'Active'
                          AND NOT EXISTS (
                                SELECT 1 FROM allocations al WHERE al.tracking_number_id = tn.id
                                  AND v_now < DATE_ADD(al.window_end, INTERVAL v_cooldown SECOND))
                        ORDER BY tn.last_released_at ASC LIMIT 1 FOR UPDATE SKIP LOCKED;

                        IF v_tracking_number_id IS NOT NULL THEN
                            IF v_visitor_id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM sessions WHERE id = v_session_id) THEN
                                INSERT INTO visitors (id, website_id, first_seen_at) VALUES (v_visitor_id, p_website_id, v_now);
                                INSERT INTO sessions (id, visitor_id, website_id, landing_page, referrer, utm_source,
                                    utm_medium, utm_campaign, utm_term, utm_content, gclid, gbraid, wbraid,
                                    ga4_client_id, consent_state, provenance, started_at, expires_at)
                                VALUES (v_session_id, v_visitor_id, p_website_id, p_landing_page, p_referrer,
                                    v_utm_source, v_utm_medium, v_utm_campaign, v_utm_term, v_utm_content,
                                    p_gclid, p_gbraid, p_wbraid, p_ga4_client_id, 'Granted', 'Ordinary', v_now, v_expires_at);
                            END IF;
                            INSERT INTO allocations (id, tracking_number_id, session_id, pool_id_at_allocation,
                                window_start, window_end, is_shadow, created_at)
                            VALUES (UUID(), v_tracking_number_id, v_session_id, v_pool_id, v_now,
                                DATE_ADD(v_expires_at, INTERVAL v_window_ext SECOND), 0, v_now);
                            SELECT did INTO v_did FROM tracking_numbers WHERE id = v_tracking_number_id;
                            COMMIT;
                            UPDATE tmp_result SET allocated_number = v_did WHERE pool_id = v_pool_id;
                        ELSE
                            ROLLBACK; -- this pool's occurrences fall back to its own default number.
                        END IF;
                        DELETE FROM tmp_requested_pools WHERE pool_id = v_pool_id;
                    END IF;
                END WHILE;

                IF NOT EXISTS (SELECT 1 FROM tmp_result WHERE allocated_number IS NOT NULL) THEN
                    INSERT INTO tmp_result (reason) VALUES ('pool_exhausted');
                ELSE
                    UPDATE tmp_result SET session_id = v_session_id, expires_at = v_expires_at WHERE 1=1;
                END IF;
            END IF;
        END IF;
    END IF;

    SELECT * FROM tmp_result;
    DROP TEMPORARY TABLE IF EXISTS tmp_result;
    DROP TEMPORARY TABLE IF EXISTS tmp_requested_pools;
END
```

### sp_dni_heartbeat

Replaces `AllocationService.HeartbeatAsync`.

```sql
CREATE PROCEDURE sp_dni_heartbeat(IN p_session_id CHAR(36))
BEGIN
    DECLARE v_website_id CHAR(36);
    DECLARE v_multi_pool TINYINT(1);
    DECLARE v_timeout INT;
    DECLARE v_window_ext INT;
    DECLARE v_new_expiry DATETIME(6);
    DECLARE v_now DATETIME(6) DEFAULT UTC_TIMESTAMP(6);

    IF NOT EXISTS (SELECT 1 FROM sessions WHERE id = p_session_id AND ended_at IS NULL AND expires_at > v_now) THEN
        SELECT 0 AS still_valid, NULL AS number;
    ELSE
        SELECT s.website_id INTO v_website_id FROM sessions s WHERE s.id = p_session_id;
        SELECT multi_pool_enabled, session_timeout_seconds, allocation_window_extension_seconds
        INTO v_multi_pool, v_timeout, v_window_ext FROM websites WHERE id = v_website_id;

        SET v_new_expiry = DATE_ADD(v_now, INTERVAL v_timeout SECOND);
        UPDATE sessions SET expires_at = v_new_expiry WHERE id = p_session_id;
        UPDATE allocations SET window_end = DATE_ADD(v_new_expiry, INTERVAL v_window_ext SECOND)
        WHERE session_id = p_session_id;

        IF v_multi_pool = 1 THEN
            SELECT 1 AS still_valid, al.pool_id_at_allocation AS pool_id, 1 AS pool_still_valid, tn.did AS number
            FROM allocations al JOIN tracking_numbers tn ON tn.id = al.tracking_number_id
            WHERE al.session_id = p_session_id;
        ELSE
            SELECT 1 AS still_valid, tn.did AS number
            FROM allocations al JOIN tracking_numbers tn ON tn.id = al.tracking_number_id
            WHERE al.session_id = p_session_id LIMIT 1;
        END IF;
    END IF;
END
```

### sp_dni_consent

Replaces `DniController.Consent` + `AllocationService.AllocateAsync`(consent-grant
branch)/`WithdrawConsentAsync`.

```sql
CREATE PROCEDURE sp_dni_consent(
    IN p_website_id CHAR(36), IN p_session_id CHAR(36), IN p_consent VARCHAR(16), IN p_arrival_details JSON
)
BEGIN
    DECLARE v_now DATETIME(6) DEFAULT UTC_TIMESTAMP(6);
    DECLARE v_default_number VARCHAR(32);

    IF LOWER(p_consent) = 'granted' THEN
        -- FR-014: degraded provenance if the entry-page arrival is no longer present.
        CALL sp_dni_allocate(
            p_website_id, 1,
            JSON_UNQUOTE(JSON_EXTRACT(p_arrival_details, '$.landing_page')),
            JSON_UNQUOTE(JSON_EXTRACT(p_arrival_details, '$.referrer')),
            JSON_EXTRACT(p_arrival_details, '$.utm'),
            JSON_UNQUOTE(JSON_EXTRACT(p_arrival_details, '$.gclid')),
            JSON_UNQUOTE(JSON_EXTRACT(p_arrival_details, '$.gbraid')),
            JSON_UNQUOTE(JSON_EXTRACT(p_arrival_details, '$.wbraid')),
            JSON_UNQUOTE(JSON_EXTRACT(p_arrival_details, '$.ga4_client_id')),
            NULL, NULL);
    ELSEIF LOWER(p_consent) = 'withdrawn' THEN
        SELECT w.default_number, w.allocation_window_extension_seconds
        INTO v_default_number, @window_ext
        FROM sessions s JOIN websites w ON w.id = s.website_id WHERE s.id = p_session_id;

        UPDATE sessions SET consent_state = 'Withdrawn', ended_at = v_now WHERE id = p_session_id;
        UPDATE allocations SET window_end = v_now WHERE session_id = p_session_id;
        UPDATE tracking_numbers tn JOIN allocations al ON al.tracking_number_id = tn.id
        SET tn.last_released_at = v_now WHERE al.session_id = p_session_id;

        SELECT NULL AS session_id, v_default_number AS number, NULL AS reason, NULL AS expires_at;
    END IF;
END
```

### sp_dni_shadow_observe

Replaces `ShadowAllocationService.RecordObservationAsync`.

```sql
CREATE PROCEDURE sp_dni_shadow_observe(
    IN p_website_id CHAR(36), IN p_observed_number VARCHAR(32), IN p_landing_page VARCHAR(2048),
    IN p_referrer VARCHAR(2048), IN p_utm JSON, IN p_gclid VARCHAR(255), IN p_gbraid VARCHAR(255),
    IN p_wbraid VARCHAR(255), IN p_ga4_client_id VARCHAR(255)
)
BEGIN
    DECLARE v_shadow_enabled TINYINT(1);
    DECLARE v_pool_id CHAR(36);
    DECLARE v_tracking_number_id CHAR(36);
    DECLARE v_visitor_id CHAR(36) DEFAULT UUID();
    DECLARE v_session_id CHAR(36) DEFAULT UUID();
    DECLARE v_now DATETIME(6) DEFAULT UTC_TIMESTAMP(6);
    DECLARE v_timeout INT; DECLARE v_window_ext INT; DECLARE v_expires_at DATETIME(6);

    SELECT shadow_mode_enabled, session_timeout_seconds, allocation_window_extension_seconds
    INTO v_shadow_enabled, v_timeout, v_window_ext FROM websites WHERE id = p_website_id;

    IF v_shadow_enabled IS NULL OR v_shadow_enabled = 0 THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'Shadow mode is not enabled for this website (FR-049).';
    END IF;

    SELECT id INTO v_pool_id FROM number_pools WHERE scope_type = 'website' AND scope_ref = p_website_id LIMIT 1;
    IF v_pool_id IS NULL THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'No pool configured for website.';
    END IF;

    SELECT id INTO v_tracking_number_id FROM tracking_numbers WHERE did = p_observed_number LIMIT 1;
    IF v_tracking_number_id IS NULL THEN
        SET v_tracking_number_id = UUID();
        INSERT INTO tracking_numbers (id, pool_id, did, status, status_changed_at)
        VALUES (v_tracking_number_id, v_pool_id, p_observed_number, 'Active', v_now);
    END IF;

    SET v_expires_at = DATE_ADD(v_now, INTERVAL v_timeout SECOND);
    INSERT INTO visitors (id, website_id, first_seen_at) VALUES (v_visitor_id, p_website_id, v_now);
    INSERT INTO sessions (id, visitor_id, website_id, landing_page, referrer, utm_source, utm_medium, utm_campaign,
        utm_term, utm_content, gclid, gbraid, wbraid, ga4_client_id, consent_state, provenance, started_at, expires_at)
    VALUES (v_session_id, v_visitor_id, p_website_id, p_landing_page, p_referrer,
        JSON_UNQUOTE(JSON_EXTRACT(p_utm, '$.source')), JSON_UNQUOTE(JSON_EXTRACT(p_utm, '$.medium')),
        JSON_UNQUOTE(JSON_EXTRACT(p_utm, '$.campaign')), JSON_UNQUOTE(JSON_EXTRACT(p_utm, '$.term')),
        JSON_UNQUOTE(JSON_EXTRACT(p_utm, '$.content')), p_gclid, p_gbraid, p_wbraid, p_ga4_client_id,
        'Granted', 'Ordinary', v_now, v_expires_at);
    INSERT INTO allocations (id, tracking_number_id, session_id, pool_id_at_allocation, window_start, window_end, is_shadow, created_at)
    VALUES (UUID(), v_tracking_number_id, v_session_id, v_pool_id, v_now, DATE_ADD(v_expires_at, INTERVAL v_window_ext SECOND), 1, v_now);

    SELECT v_session_id AS session_id, p_observed_number AS number, v_expires_at AS expires_at;
END
```

### sp_set_website_shadow_mode

Replaces `AdminWebsitesController.SetShadowMode`.

```sql
CREATE PROCEDURE sp_set_website_shadow_mode(IN p_id CHAR(36), IN p_enable TINYINT(1))
BEGIN
    DECLARE v_before TINYINT(1);
    DECLARE v_actor VARCHAR(32) DEFAULT SUBSTRING_INDEX(CURRENT_USER(), '@', 1);
    SELECT shadow_mode_enabled INTO v_before FROM websites WHERE id = p_id;
    UPDATE websites SET shadow_mode_enabled = p_enable, updated_at = UTC_TIMESTAMP(6) WHERE id = p_id;
    INSERT INTO audit_entries (id, actor_user_id, action, target_type, target_id, before_value, after_value, occurred_at)
    VALUES (UUID(), v_actor, 'SetShadowMode', 'Website', p_id,
        JSON_OBJECT('ShadowModeEnabled', v_before), JSON_OBJECT('ShadowModeEnabled', p_enable), UTC_TIMESTAMP(6));
END
```

`v_actor` is resolved from the database's own `CURRENT_USER()` — direct database access means there is no Apiche layer to inject an actor id as a parameter, and none is accepted as one (this session's FR-035 clarification).

### sp_set_website_multi_pool

Replaces `AdminWebsitesController.SetMultiPool`.

```sql
CREATE PROCEDURE sp_set_website_multi_pool(IN p_id CHAR(36), IN p_enable TINYINT(1))
BEGIN
    DECLARE v_before TINYINT(1);
    DECLARE v_actor VARCHAR(32) DEFAULT SUBSTRING_INDEX(CURRENT_USER(), '@', 1);
    SELECT multi_pool_enabled INTO v_before FROM websites WHERE id = p_id;
    UPDATE websites SET multi_pool_enabled = p_enable, updated_at = UTC_TIMESTAMP(6) WHERE id = p_id;
    INSERT INTO audit_entries (id, actor_user_id, action, target_type, target_id, before_value, after_value, occurred_at)
    VALUES (UUID(), v_actor, 'SetMultiPoolEnabled', 'Website', p_id,
        JSON_OBJECT('MultiPoolEnabled', v_before), JSON_OBJECT('MultiPoolEnabled', p_enable), UTC_TIMESTAMP(6));
END
```

`v_actor` is resolved from the database's own `CURRENT_USER()` — same reasoning as `sp_set_website_shadow_mode` above.

### sp_create_pool

Invoked directly by an authenticated MySQL session (native role-based access, no Apiche) — see the "Admin and Reporting: direct database access" note at the top of this document.

Replaces `AdminPoolsController.CreatePool` — including the FR-050 digit-normalized
default-number collision guard for multi-pool-enabled websites.

```sql
CREATE PROCEDURE sp_create_pool(IN p_name VARCHAR(255), IN p_scope_type VARCHAR(32), IN p_scope_ref CHAR(36), IN p_default_number VARCHAR(32))
BEGIN
    DECLARE v_multi_pool TINYINT(1) DEFAULT 0;
    DECLARE v_new_digits VARCHAR(32);
    DECLARE v_id CHAR(36) DEFAULT UUID();
    DECLARE v_now DATETIME(6) DEFAULT UTC_TIMESTAMP(6);
    DECLARE v_actor VARCHAR(32) DEFAULT SUBSTRING_INDEX(CURRENT_USER(), '@', 1);

    IF p_scope_type NOT IN ('website', 'campaign', 'business_unit') THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'Scope type must be website, campaign or business_unit (FR-004).';
    END IF;

    IF p_scope_type = 'website' AND p_default_number IS NOT NULL AND p_default_number <> '' THEN
        SELECT multi_pool_enabled INTO v_multi_pool FROM websites WHERE id = p_scope_ref;
        IF v_multi_pool = 1 THEN
            SET v_new_digits = REGEXP_REPLACE(p_default_number, '[^0-9]', '');
            IF EXISTS (
                SELECT 1 FROM number_pools
                WHERE scope_type = 'website' AND scope_ref = p_scope_ref
                  AND default_number IS NOT NULL
                  AND REGEXP_REPLACE(default_number, '[^0-9]', '') = v_new_digits
            ) THEN
                SIGNAL SQLSTATE '45000'
                    SET MESSAGE_TEXT = 'default_number collides, digit-normalized, with another pool on this multi-pool website (FR-050).';
            END IF;
        END IF;
    END IF;

    INSERT INTO number_pools (id, name, scope_type, scope_ref, default_number, created_at, updated_at)
    VALUES (v_id, p_name, p_scope_type, p_scope_ref, NULLIF(p_default_number, ''), v_now, v_now);

    INSERT INTO audit_entries (id, actor_user_id, action, target_type, target_id, before_value, after_value, occurred_at)
    VALUES (UUID(), v_actor, 'CreatePool', 'NumberPool', v_id, NULL,
        JSON_OBJECT('Id', v_id, 'Name', p_name, 'ScopeType', p_scope_type, 'ScopeRef', p_scope_ref, 'DefaultNumber', p_default_number),
        v_now);

    SELECT v_id AS id;
END
```

### sp_import_tracking_numbers

Invoked directly by an authenticated MySQL session (native role-based access, no Apiche) — see the "Admin and Reporting: direct database access" note at the top of this document.

Replaces `AdminPoolsController.ImportCsvRowsAsync` as invoked from the browser-upload
path — one DID candidate per array element, in order; rejects malformed (not valid E.164)
and duplicate (within the pool, including duplicates newly accepted earlier in the same
call) entries with a per-row reason.

```sql
CREATE PROCEDURE sp_import_tracking_numbers(IN p_pool_id CHAR(36), IN p_dids JSON)
BEGIN
    DECLARE v_now DATETIME(6) DEFAULT UTC_TIMESTAMP(6);
    DECLARE v_accepted INT DEFAULT 0;
    DECLARE v_rejected INT DEFAULT 0;

    DROP TEMPORARY TABLE IF EXISTS tmp_import_result;
    CREATE TEMPORARY TABLE tmp_import_result (
        row_number INT, did VARCHAR(32), accepted TINYINT(1), reason VARCHAR(16)
    );

    INSERT INTO tmp_import_result (row_number, did, accepted, reason)
    SELECT jt.row_number, jt.did,
           CASE WHEN jt.did NOT REGEXP '^\\+?[0-9]{8,15}$' THEN 0
                WHEN EXISTS (SELECT 1 FROM tracking_numbers tn WHERE tn.pool_id = p_pool_id AND tn.did = jt.did) THEN 0
                WHEN EXISTS (SELECT 1 FROM (SELECT did, ROW_NUMBER() OVER (PARTITION BY did ORDER BY row_number) rn
                                             FROM JSON_TABLE(p_dids, '$[*]' COLUMNS (row_number FOR ORDINALITY, did VARCHAR(32) PATH '$')) x
                                             WHERE x.did = jt.did) dup WHERE dup.rn > 1 AND dup.rn <= jt.row_number
                             AND EXISTS (SELECT 1 FROM (SELECT 1) z WHERE jt.row_number > (SELECT MIN(rn) FROM (SELECT did, ROW_NUMBER() OVER (PARTITION BY did ORDER BY row_number) rn FROM JSON_TABLE(p_dids, '$[*]' COLUMNS (row_number FOR ORDINALITY, did VARCHAR(32) PATH '$')) x2 WHERE x2.did = jt.did) y))
                THEN 0
                ELSE 1 END AS accepted,
           CASE WHEN jt.did NOT REGEXP '^\\+?[0-9]{8,15}$' THEN 'malformed'
                WHEN EXISTS (SELECT 1 FROM tracking_numbers tn WHERE tn.pool_id = p_pool_id AND tn.did = jt.did) THEN 'duplicate'
                ELSE NULL END AS reason
    FROM JSON_TABLE(p_dids, '$[*]' COLUMNS (row_number FOR ORDINALITY, did VARCHAR(32) PATH '$')) jt;

    -- Insert every accepted, first-occurrence row as a new Active tracking number.
    INSERT INTO tracking_numbers (id, pool_id, did, status, status_changed_at)
    SELECT UUID(), p_pool_id, did, 'Active', v_now FROM tmp_import_result WHERE accepted = 1;

    SELECT COUNT(*) INTO v_accepted FROM tmp_import_result WHERE accepted = 1;
    SELECT COUNT(*) INTO v_rejected FROM tmp_import_result WHERE accepted = 0;

    INSERT INTO audit_entries (id, actor_user_id, action, target_type, target_id, before_value, after_value, occurred_at)
    VALUES (UUID(), SUBSTRING_INDEX(CURRENT_USER(), '@', 1), 'ImportNumbers', 'NumberPool', p_pool_id, NULL,
        JSON_OBJECT('accepted', v_accepted, 'rejected', v_rejected), v_now);

    SELECT row_number, did, accepted, reason FROM tmp_import_result ORDER BY row_number;
    DROP TEMPORARY TABLE IF EXISTS tmp_import_result;
END
```
Note: the row-order-sensitive "duplicate later in the same file" detection above is
intentionally simplified from the C# (which rejects the *second and later* occurrence of a
repeated value in file order) — see Coverage Notes.

### sp_import_tracking_numbers_from_folder

Invoked directly by an authenticated MySQL session (native role-based access, no Apiche) — see the "Admin and Reporting: direct database access" note at the top of this document.

Replaces `AdminPoolsController.ImportNumbersFromFolder`. Requires the MySQL server
process (not the Apiche app tier) to have filesystem read access to the configured import
folder and `secure_file_priv` permitting it — see Coverage Notes.

```sql
CREATE PROCEDURE sp_import_tracking_numbers_from_folder(IN p_pool_id CHAR(36), IN p_file_name VARCHAR(255))
BEGIN
    DECLARE v_now DATETIME(6) DEFAULT UTC_TIMESTAMP(6);
    DECLARE v_full_path VARCHAR(1024);

    -- p_file_name must already have been validated by the caller (or a preceding Apiche
    -- rule) as a bare file name with no path separators, mirroring the C#'s
    -- Path.GetFileName equality check, to prevent directory traversal.
    SET v_full_path = CONCAT('/var/attribution/number-imports/', p_file_name); -- configured import folder root

    DROP TEMPORARY TABLE IF EXISTS tmp_folder_import;
    CREATE TEMPORARY TABLE tmp_folder_import (did VARCHAR(32));

    SET @sql = CONCAT('LOAD DATA INFILE ''', v_full_path, ''' INTO TABLE tmp_folder_import LINES TERMINATED BY ''\n''');
    PREPARE stmt FROM @sql;
    EXECUTE stmt;
    DEALLOCATE PREPARE stmt;

    DELETE FROM tmp_folder_import WHERE TRIM(did) = '' OR LOWER(TRIM(did)) = 'did';
    UPDATE tmp_folder_import SET did = TRIM(did);

    CALL sp_import_tracking_numbers(p_pool_id, (SELECT JSON_ARRAYAGG(did) FROM tmp_folder_import));

    UPDATE audit_entries SET action = 'ImportNumbersFromFolder',
        after_value = JSON_SET(after_value, '$.file_name', p_file_name)
    WHERE target_id = p_pool_id AND action = 'ImportNumbers' ORDER BY occurred_at DESC LIMIT 1;

    DROP TEMPORARY TABLE IF EXISTS tmp_folder_import;
END
```

### sp_change_tracking_number_status

Invoked directly by an authenticated MySQL session (native role-based access, no Apiche) — see the "Admin and Reporting: direct database access" note at the top of this document.

Replaces `AdminNumbersController.ChangeStatus` (backing Suspend/Retire/Reactivate).

```sql
CREATE PROCEDURE sp_change_tracking_number_status(IN p_id CHAR(36), IN p_new_status VARCHAR(16), IN p_action VARCHAR(32))
BEGIN
    DECLARE v_before VARCHAR(16);
    DECLARE v_actor VARCHAR(32) DEFAULT SUBSTRING_INDEX(CURRENT_USER(), '@', 1);
    SELECT status INTO v_before FROM tracking_numbers WHERE id = p_id;
    UPDATE tracking_numbers SET status = p_new_status, status_changed_at = UTC_TIMESTAMP(6) WHERE id = p_id;
    INSERT INTO audit_entries (id, actor_user_id, action, target_type, target_id, before_value, after_value, occurred_at)
    VALUES (UUID(), v_actor, CONCAT(p_action, 'TrackingNumber'), 'TrackingNumber', p_id,
        JSON_OBJECT('Status', v_before), JSON_OBJECT('Status', p_new_status), UTC_TIMESTAMP(6));
END
```

### sp_move_tracking_number

Invoked directly by an authenticated MySQL session (native role-based access, no Apiche) — see the "Admin and Reporting: direct database access" note at the top of this document.

Replaces `AdminNumbersController.Move`.

```sql
CREATE PROCEDURE sp_move_tracking_number(IN p_id CHAR(36), IN p_target_pool_id CHAR(36))
BEGIN
    DECLARE v_before CHAR(36);
    DECLARE v_actor VARCHAR(32) DEFAULT SUBSTRING_INDEX(CURRENT_USER(), '@', 1);
    SELECT pool_id INTO v_before FROM tracking_numbers WHERE id = p_id;
    UPDATE tracking_numbers SET pool_id = p_target_pool_id WHERE id = p_id;
    INSERT INTO audit_entries (id, actor_user_id, action, target_type, target_id, before_value, after_value, occurred_at)
    VALUES (UUID(), v_actor, 'MoveTrackingNumber', 'TrackingNumber', p_id,
        JSON_OBJECT('PoolId', v_before), JSON_OBJECT('PoolId', p_target_pool_id), UTC_TIMESTAMP(6));
END
```

### sp_create_user

Invoked directly by an authenticated MySQL session (native role-based access, no Apiche) — see the "Admin and Reporting: direct database access" note at the top of this document.

Replaces `AdminUsersController.Create`'s RBAC-metadata half (password/TOTP provisioning
is Apiche's native Basic Auth store — see Coverage Notes).

```sql
CREATE PROCEDURE sp_create_user(IN p_username VARCHAR(255), IN p_role VARCHAR(32))
BEGIN
    DECLARE v_id CHAR(36) DEFAULT UUID();
    DECLARE v_actor VARCHAR(32) DEFAULT SUBSTRING_INDEX(CURRENT_USER(), '@', 1);
    IF p_role NOT IN ('SystemAdministrator', 'MarketingAdministrator', 'Analyst', 'IntegrationService') THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'role must be one of: SystemAdministrator, MarketingAdministrator, Analyst, IntegrationService.';
    END IF;
    IF EXISTS (SELECT 1 FROM users WHERE username = p_username) THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'Username is already in use.';
    END IF;

    INSERT INTO users (id, db_username, username, identity_type, mapped_role, is_active, created_at)
    VALUES (v_id, p_username, p_username, 'Local', p_role, 1, UTC_TIMESTAMP(6));

    -- The native account itself (CREATE USER) and its role grant (GRANT role_x TO p_username) are
    -- provisioned by the account-lifecycle tooling this procedure is one step of (tasks.md T163) —
    -- deliberately not written as dynamic SQL here, since the actual password is set through the
    -- database's own account-creation facility, out of band, per data-model.md's User/Role note.
    INSERT INTO audit_entries (id, actor_user_id, action, target_type, target_id, before_value, after_value, occurred_at)
    VALUES (UUID(), v_actor, 'CreateUser', 'User', v_id, NULL, JSON_OBJECT('Username', p_username, 'role', p_role), UTC_TIMESTAMP(6));

    SELECT v_id AS id;
END
```

### sp_deactivate_user

Invoked directly by an authenticated MySQL session (native role-based access, no Apiche) — see the "Admin and Reporting: direct database access" note at the top of this document.

Actor identity is resolved from the database's own `CURRENT_USER()` — direct database access means there is no Apiche layer to inject an actor id as a parameter, and none is accepted as one (this session's FR-035 clarification).

Replaces `AdminUsersController.Deactivate`, including
`SystemAdministratorGuard.WouldRemoveLastActiveSystemAdministrator`.

```sql
CREATE PROCEDURE sp_deactivate_user(IN p_id CHAR(36))
BEGIN
    DECLARE v_effective_role VARCHAR(32);
    DECLARE v_is_active TINYINT(1);
    DECLARE v_active_admins INT;
    DECLARE v_db_username VARCHAR(255);
    DECLARE v_actor VARCHAR(32) DEFAULT SUBSTRING_INDEX(CURRENT_USER(), '@', 1);

    SELECT COALESCE(role_override, mapped_role), is_active, db_username
        INTO v_effective_role, v_is_active, v_db_username FROM users WHERE id = p_id;

    IF v_is_active = 1 THEN
        IF v_effective_role = 'SystemAdministrator' THEN
            SELECT COUNT(*) INTO v_active_admins FROM users
            WHERE is_active = 1 AND COALESCE(role_override, mapped_role) = 'SystemAdministrator';
            IF v_active_admins <= 1 THEN
                SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'Cannot deactivate the last active System Administrator account.';
            END IF;
        END IF;

        -- The native account itself (e.g. ALTER USER v_db_username@'%' ACCOUNT LOCK) is disabled by
        -- the account-lifecycle tooling this procedure is one step of (tasks.md T163).
        UPDATE users SET is_active = 0 WHERE id = p_id;
        INSERT INTO audit_entries (id, actor_user_id, action, target_type, target_id, before_value, after_value, occurred_at)
        VALUES (UUID(), v_actor, 'DeactivateUser', 'User', p_id,
            JSON_OBJECT('is_active', true), JSON_OBJECT('is_active', false), UTC_TIMESTAMP(6));
    END IF;

    SELECT id, username, is_active FROM users WHERE id = p_id;
END
```

### sp_override_user_role

Invoked directly by an authenticated MySQL session (native role-based access, no Apiche) — see the "Admin and Reporting: direct database access" note at the top of this document.

Actor identity is resolved from the database's own `CURRENT_USER()` — same reasoning as `sp_deactivate_user` above.

Replaces `AdminUsersController.OverrideRole`, including the same zero-admin guard.

```sql
CREATE PROCEDURE sp_override_user_role(IN p_id CHAR(36), IN p_new_role VARCHAR(32))
BEGIN
    DECLARE v_previous_role VARCHAR(32);
    DECLARE v_active_admins INT;
    DECLARE v_db_username VARCHAR(255);
    DECLARE v_actor VARCHAR(32) DEFAULT SUBSTRING_INDEX(CURRENT_USER(), '@', 1);

    IF p_new_role NOT IN ('SystemAdministrator', 'MarketingAdministrator', 'Analyst', 'IntegrationService') THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'role must be one of: SystemAdministrator, MarketingAdministrator, Analyst, IntegrationService.';
    END IF;

    SELECT COALESCE(role_override, mapped_role), db_username INTO v_previous_role, v_db_username FROM users WHERE id = p_id;

    IF p_new_role <> 'SystemAdministrator' AND v_previous_role = 'SystemAdministrator' THEN
        SELECT COUNT(*) INTO v_active_admins FROM users
        WHERE is_active = 1 AND COALESCE(role_override, mapped_role) = 'SystemAdministrator';
        IF v_active_admins <= 1 THEN
            SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'Cannot change the role of the last active System Administrator account.';
        END IF;
    END IF;

    UPDATE users SET role_override = p_new_role, role_overridden_by = v_actor WHERE id = p_id;

    -- The native role re-grant (REVOKE role_<previous> FROM v_db_username; GRANT role_<new> TO
    -- v_db_username) is applied by the account-lifecycle tooling this procedure is one step of
    -- (tasks.md T163), keeping the database's own grants and this row's role in lockstep.
    INSERT INTO audit_entries (id, actor_user_id, action, target_type, target_id, before_value, after_value, occurred_at)
    VALUES (UUID(), v_actor, 'OverrideUserRole', 'User', p_id,
        JSON_OBJECT('role', v_previous_role), JSON_OBJECT('role', p_new_role, 'overriddenBy', v_actor), UTC_TIMESTAMP(6));

    SELECT id, username, COALESCE(role_override, mapped_role) AS effective_role FROM users WHERE id = p_id;
END
```

### sp_create_qualification_rule_version

Invoked directly by an authenticated MySQL session (native role-based access, no Apiche) — see the "Admin and Reporting: direct database access" note at the top of this document.

Actor identity is resolved from the database's own `CURRENT_USER()` — direct database access means there is no Apiche layer to inject an actor id as a parameter, and none is accepted as one (this session's FR-035 clarification).

Replaces `RuleVersioningService.CreateVersionAsync` — FR-024's structural
contiguity/non-overlap guarantee (a new version's `effective_start` closes the prior
version's `effective_end` exactly; a `effective_start` at or before the currently-open
version's own start is rejected as it would leave a gap).

```sql
CREATE PROCEDURE sp_create_qualification_rule_version(
    IN p_scope_type VARCHAR(16), IN p_scope_ref VARCHAR(255), IN p_conditions JSON,
    IN p_effective_start DATETIME(6)
)
BEGIN
    DECLARE v_latest_id CHAR(36);
    DECLARE v_latest_start DATETIME(6);
    DECLARE v_latest_version INT;
    DECLARE v_new_id CHAR(36) DEFAULT UUID();
    DECLARE v_new_version INT;
    DECLARE v_conditions_json JSON;
    DECLARE v_now DATETIME(6) DEFAULT UTC_TIMESTAMP(6);
    DECLARE v_actor VARCHAR(32) DEFAULT SUBSTRING_INDEX(CURRENT_USER(), '@', 1);

    SET v_conditions_json = JSON_OBJECT(
        'RequiredDirection', JSON_EXTRACT(p_conditions, '$.direction'),
        'AnsweredRequired', JSON_EXTRACT(p_conditions, '$.answered_required'),
        'MinConnectedDurationSeconds', JSON_EXTRACT(p_conditions, '$.min_connected_duration_seconds'),
        'TimeOfDay', CASE WHEN JSON_EXTRACT(p_conditions, '$.time_of_day') IS NULL THEN NULL
            ELSE JSON_OBJECT('StartLocal', JSON_EXTRACT(p_conditions, '$.time_of_day.start'),
                              'EndLocal', JSON_EXTRACT(p_conditions, '$.time_of_day.end')) END);

    SELECT id, version, effective_start INTO v_latest_id, v_latest_version, v_latest_start
    FROM qualification_rules
    WHERE scope_type = p_scope_type AND scope_ref <=> NULLIF(p_scope_ref, '') AND effective_end IS NULL
    LIMIT 1;

    IF v_latest_id IS NULL THEN
        SET v_new_version = 1;
    ELSE
        IF p_effective_start <= v_latest_start THEN
            SIGNAL SQLSTATE '45000'
                SET MESSAGE_TEXT = 'A new version''s effective_start must be after the currently-open version''s effective_start.';
        END IF;
        UPDATE qualification_rules SET effective_end = p_effective_start WHERE id = v_latest_id;
        SET v_new_version = v_latest_version + 1;
    END IF;

    INSERT INTO qualification_rules (id, scope_type, scope_ref, version, conditions, effective_start, effective_end, created_by, created_at)
    VALUES (v_new_id, p_scope_type, NULLIF(p_scope_ref, ''), v_new_version, v_conditions_json, p_effective_start, NULL, v_actor, v_now);

    INSERT INTO audit_entries (id, actor_user_id, action, target_type, target_id, before_value, after_value, occurred_at)
    VALUES (UUID(), v_actor, 'CreateQualificationRuleVersion', 'QualificationRule', v_new_id,
        NULL, JSON_OBJECT('id', v_new_id, 'version', v_new_version), v_now);

    SELECT id, scope_type, scope_ref, version, conditions, effective_start, effective_end, created_by, created_at
    FROM qualification_rules WHERE id = v_new_id;
END
```

### sp_delete_future_qualification_rule_version

Invoked directly by an authenticated MySQL session (native role-based access, no Apiche) — see the "Admin and Reporting: direct database access" note at the top of this document.

Replaces `RuleVersioningService.DeleteFutureVersionAsync`.

```sql
CREATE PROCEDURE sp_delete_future_qualification_rule_version(IN p_id CHAR(36))
BEGIN
    DECLARE v_effective_start DATETIME(6);
    DECLARE v_scope_type VARCHAR(16);
    DECLARE v_scope_ref VARCHAR(255);
    DECLARE v_predecessor_id CHAR(36);

    SELECT effective_start, scope_type, scope_ref INTO v_effective_start, v_scope_type, v_scope_ref
    FROM qualification_rules WHERE id = p_id;

    IF v_effective_start <= UTC_TIMESTAMP(6) THEN
        SIGNAL SQLSTATE '45000'
            SET MESSAGE_TEXT = 'Only a not-yet-effective future version may be deleted.';
    END IF;

    SELECT id INTO v_predecessor_id FROM qualification_rules
    WHERE scope_type = v_scope_type AND scope_ref <=> v_scope_ref AND effective_end = v_effective_start
    LIMIT 1;

    IF v_predecessor_id IS NOT NULL THEN
        UPDATE qualification_rules SET effective_end = NULL WHERE id = v_predecessor_id;
    END IF;

    INSERT INTO audit_entries (id, actor_user_id, action, target_type, target_id, before_value, after_value, occurred_at)
    SELECT UUID(), SUBSTRING_INDEX(CURRENT_USER(), '@', 1), 'DeleteQualificationRuleVersion', 'QualificationRule', p_id,
           JSON_OBJECT('id', id, 'version', version), NULL, UTC_TIMESTAMP(6)
    FROM qualification_rules WHERE id = p_id;

    DELETE FROM qualification_rules WHERE id = p_id;
END
```

### sp_attribute_call

Shared helper replacing `AttributionService.AttributeAsync`/`Decide` — the exact-match,
never-guessed attribution decision (FR-018/FR-020/FR-021), including opening a
`review_cases` row on ambiguity.

```sql
CREATE PROCEDURE sp_attribute_call(IN p_call_id CHAR(36), IN p_decided_at DATETIME(6), OUT o_attribution_id CHAR(36), OUT o_state VARCHAR(16))
BEGIN
    DECLARE v_dialled_number VARCHAR(32);
    DECLARE v_call_started_at DATETIME(6);
    DECLARE v_tracking_number_id CHAR(36);
    DECLARE v_covering_count INT;
    DECLARE v_all_shadow INT;
    DECLARE v_session_id CHAR(36);
    DECLARE v_allocation_id CHAR(36);
    DECLARE v_is_shadow TINYINT(1);
    DECLARE v_reason VARCHAR(64);

    SELECT dialled_number, started_at INTO v_dialled_number, v_call_started_at FROM calls WHERE id = p_call_id;
    SELECT id INTO v_tracking_number_id FROM tracking_numbers WHERE did = v_dialled_number LIMIT 1;
    SET o_attribution_id = UUID();

    IF v_tracking_number_id IS NULL THEN
        SET o_state = 'Unattributed';
        INSERT INTO attributions (id, call_id, state, reason, is_shadow_derived, is_current, decided_at)
        VALUES (o_attribution_id, p_call_id, 'Unattributed', 'number_never_allocated', 0, 1, p_decided_at);
    ELSE
        SELECT COUNT(*) INTO v_covering_count FROM allocations
        WHERE tracking_number_id = v_tracking_number_id AND window_start <= v_call_started_at AND window_end > v_call_started_at;

        IF v_covering_count = 0 THEN
            SET o_state = 'Unattributed';
            INSERT INTO attributions (id, call_id, state, reason, is_shadow_derived, is_current, decided_at)
            VALUES (o_attribution_id, p_call_id, 'Unattributed', 'no_allocation_window_covers_call_start', 0, 1, p_decided_at);
        ELSEIF v_covering_count > 1 THEN
            SELECT MIN(is_shadow) INTO v_all_shadow FROM allocations
            WHERE tracking_number_id = v_tracking_number_id AND window_start <= v_call_started_at AND window_end > v_call_started_at;
            SET v_reason = IF(v_all_shadow = 1, 'shadow_mode_overlapping_observed_windows', 'multiple_allocation_windows_cover_call_start');
            SET o_state = 'Ambiguous';
            INSERT INTO attributions (id, call_id, state, reason, is_shadow_derived, is_current, decided_at)
            VALUES (o_attribution_id, p_call_id, 'Ambiguous', v_reason, IFNULL(v_all_shadow, 0), 1, p_decided_at);
            INSERT INTO review_cases (id, call_id, attribution_id, status, opened_at)
            VALUES (UUID(), p_call_id, o_attribution_id, 'Open', p_decided_at);
        ELSE
            SELECT session_id, id, is_shadow INTO v_session_id, v_allocation_id, v_is_shadow FROM allocations
            WHERE tracking_number_id = v_tracking_number_id AND window_start <= v_call_started_at AND window_end > v_call_started_at
            LIMIT 1;
            SET o_state = 'Attributed';
            INSERT INTO attributions (id, call_id, session_id, allocation_id, state, is_shadow_derived, is_current, decided_at)
            VALUES (o_attribution_id, p_call_id, v_session_id, v_allocation_id, 'Attributed', v_is_shadow, 1, p_decided_at);
        END IF;
    END IF;
END
```

### sp_qualify_call

Invoked directly by an authenticated MySQL session (native role-based access, no Apiche) — see the "Admin and Reporting: direct database access" note at the top of this document.

Shared helper replacing `QualificationService.QualifyAsync` — resolves the in-force rule
(campaign scope beats website scope beats platform default, FR-024), evaluates it via
`RuleEvaluator`'s logic (FR-022/FR-023, including the website's local-timezone time-of-day
window), records the result, and idempotently enqueues publication rows (FR-025-FR-027).

```sql
CREATE PROCEDURE sp_qualify_call(IN p_call_id CHAR(36), IN p_attribution_id CHAR(36), IN p_decided_at DATETIME(6))
BEGIN
    DECLARE v_session_id CHAR(36);
    DECLARE v_website_id CHAR(36);
    DECLARE v_campaign VARCHAR(255);
    DECLARE v_timezone VARCHAR(64) DEFAULT 'UTC';
    DECLARE v_rule_id CHAR(36);
    DECLARE v_conditions JSON;
    DECLARE v_is_qualified TINYINT(1) DEFAULT 1;
    DECLARE v_direction VARCHAR(16);
    DECLARE v_answered_required TINYINT(1);
    DECLARE v_min_duration INT;
    DECLARE v_call_direction VARCHAR(16);
    DECLARE v_answered_at DATETIME(6);
    DECLARE v_connected_duration INT;
    DECLARE v_call_started_at DATETIME(6);
    DECLARE v_local_time TIME;
    DECLARE v_tod_start TIME;
    DECLARE v_tod_end TIME;
    DECLARE v_result_id CHAR(36) DEFAULT UUID();
    DECLARE v_gclid VARCHAR(255); DECLARE v_gbraid VARCHAR(255); DECLARE v_wbraid VARCHAR(255); DECLARE v_ga4 VARCHAR(255);

    SELECT session_id INTO v_session_id FROM attributions WHERE id = p_attribution_id;
    SELECT website_id, utm_campaign, gclid, gbraid, wbraid, ga4_client_id
    INTO v_website_id, v_campaign, v_gclid, v_gbraid, v_wbraid, v_ga4 FROM sessions WHERE id = v_session_id;
    SELECT local_timezone INTO v_timezone FROM websites WHERE id = v_website_id;

    -- FR-024: most-specific in-force rule wins — campaign, then website, then default.
    SELECT id, conditions INTO v_rule_id, v_conditions FROM qualification_rules
    WHERE scope_type = 'Campaign' AND scope_ref = v_campaign
      AND effective_start <= p_decided_at AND (effective_end IS NULL OR effective_end > p_decided_at)
    LIMIT 1;
    IF v_rule_id IS NULL THEN
        SELECT id, conditions INTO v_rule_id, v_conditions FROM qualification_rules
        WHERE scope_type = 'Website' AND scope_ref = v_website_id
          AND effective_start <= p_decided_at AND (effective_end IS NULL OR effective_end > p_decided_at)
        LIMIT 1;
    END IF;
    IF v_rule_id IS NULL THEN
        SELECT id, conditions INTO v_rule_id, v_conditions FROM qualification_rules
        WHERE scope_type = 'Default' AND scope_ref IS NULL
          AND effective_start <= p_decided_at AND (effective_end IS NULL OR effective_end > p_decided_at)
        LIMIT 1;
    END IF;

    SELECT direction, connected_duration_seconds, answered_at, started_at
    INTO v_call_direction, v_connected_duration, v_answered_at, v_call_started_at FROM calls WHERE id = p_call_id;

    SET v_direction = JSON_UNQUOTE(JSON_EXTRACT(v_conditions, '$.RequiredDirection'));
    SET v_answered_required = JSON_EXTRACT(v_conditions, '$.AnsweredRequired');
    SET v_min_duration = JSON_UNQUOTE(JSON_EXTRACT(v_conditions, '$.MinConnectedDurationSeconds'));

    IF v_direction IS NOT NULL AND v_direction <> 'null' AND v_direction <> v_call_direction THEN SET v_is_qualified = 0; END IF;
    IF v_answered_required = 1 AND v_answered_at IS NULL THEN SET v_is_qualified = 0; END IF;
    IF v_min_duration IS NOT NULL AND COALESCE(v_connected_duration, 0) < v_min_duration THEN SET v_is_qualified = 0; END IF;

    IF JSON_EXTRACT(v_conditions, '$.TimeOfDay') IS NOT NULL AND JSON_EXTRACT(v_conditions, '$.TimeOfDay') <> CAST('null' AS JSON) THEN
        SET v_local_time = TIME(CONVERT_TZ(v_call_started_at, 'UTC', v_timezone));
        SET v_tod_start = JSON_UNQUOTE(JSON_EXTRACT(v_conditions, '$.TimeOfDay.StartLocal'));
        SET v_tod_end = JSON_UNQUOTE(JSON_EXTRACT(v_conditions, '$.TimeOfDay.EndLocal'));
        IF v_tod_start <= v_tod_end THEN
            IF NOT (v_local_time >= v_tod_start AND v_local_time < v_tod_end) THEN SET v_is_qualified = 0; END IF;
        ELSE
            IF NOT (v_local_time >= v_tod_start OR v_local_time < v_tod_end) THEN SET v_is_qualified = 0; END IF;
        END IF;
    END IF;

    INSERT INTO qualification_results (id, call_id, attribution_id, qualification_rule_id, is_qualified, is_current, decided_at)
    VALUES (v_result_id, p_call_id, p_attribution_id, v_rule_id, v_is_qualified, 1, p_decided_at);

    -- FR-025-FR-027: idempotent outbox enqueue, only when genuinely qualified.
    IF v_is_qualified = 1 THEN
        IF NOT EXISTS (SELECT 1 FROM conversion_publications cp JOIN qualification_results qr ON qr.id = cp.qualification_result_id
                       WHERE qr.call_id = p_call_id AND cp.destination = 'GoogleAds' AND cp.status <> 'Retracted') THEN
            INSERT INTO conversion_publications (id, qualification_result_id, destination, idempotency_key, status, skipped_reason, attempt_count)
            VALUES (UUID(), v_result_id, 'GoogleAds', CONCAT(p_call_id, ':GoogleAds:', UUID()),
                IF(v_gclid IS NULL AND v_gbraid IS NULL AND v_wbraid IS NULL, 'Skipped', 'Pending'),
                IF(v_gclid IS NULL AND v_gbraid IS NULL AND v_wbraid IS NULL, 'no_google_click_identifier', NULL), 0);
        END IF;
        IF NOT EXISTS (SELECT 1 FROM conversion_publications cp JOIN qualification_results qr ON qr.id = cp.qualification_result_id
                       WHERE qr.call_id = p_call_id AND cp.destination = 'Ga4' AND cp.status <> 'Retracted') THEN
            INSERT INTO conversion_publications (id, qualification_result_id, destination, idempotency_key, status, skipped_reason, attempt_count)
            VALUES (UUID(), v_result_id, 'Ga4', CONCAT(p_call_id, ':Ga4:', UUID()),
                IF(v_ga4 IS NULL, 'Skipped', 'Pending'), IF(v_ga4 IS NULL, 'no_ga4_client_id', NULL), 0);
        END IF;
    END IF;
END
```

### sp_correct_publications_if_needed

Invoked directly by an authenticated MySQL session (native role-based access, no Apiche) — see the "Admin and Reporting: direct database access" note at the top of this document.

Shared helper replacing the DB-only half of `CorrectionService.CorrectIfNeededAsync`. The
actual Google Ads retract/adjust HTTP call cannot run inside SQL (see Coverage Notes); this
marks the active publication rows `PendingCorrection` with the desired outcome recorded in
`correction`, for the Publication Worker's job (Appendix — Background Worker Jobs) to
carry out on its next tick.

```sql
CREATE PROCEDURE sp_correct_publications_if_needed(
    IN p_call_id CHAR(36), IN p_old_was_qualified TINYINT(1), IN p_new_is_qualified TINYINT(1), IN p_now DATETIME(6)
)
BEGIN
    IF p_old_was_qualified = 1 THEN
        -- GA4: no retraction is possible at the destination (FR-044) — recorded as
        -- unpropagatable divergence, status left as-is.
        UPDATE conversion_publications cp JOIN qualification_results qr ON qr.id = cp.qualification_result_id
        SET cp.correction = JSON_OBJECT('Type', 'Unpropagatable',
                'Reason', IF(p_new_is_qualified = 1, 'qualification_details_changed', 'no_longer_qualified'), 'DestinationAccepted', false),
            cp.corrected_at = p_now
        WHERE qr.call_id = p_call_id AND cp.destination = 'Ga4' AND cp.status = 'Sent' AND cp.correction IS NULL;

        -- Google Ads: mark for retraction or adjustment; PublicationWorker performs the
        -- actual external call and finalizes status/correction.
        UPDATE conversion_publications cp JOIN qualification_results qr ON qr.id = cp.qualification_result_id
        SET cp.status = 'PendingCorrection',
            cp.correction = JSON_OBJECT('Type', IF(p_new_is_qualified = 1, 'Adjust', 'Retract'),
                'Reason', IF(p_new_is_qualified = 1, 'qualification_details_changed', 'no_longer_qualified'))
        WHERE qr.call_id = p_call_id AND cp.destination = 'GoogleAds' AND cp.status IN ('Sent', 'Adjusted');
    END IF;
END
```

### sp_ingest_call_record

Replaces `IngestionService.ProcessCallAsync` (fresh insert) and
`ReDerivationService.ReDeriveIfChangedAsync` (restatement) in one procedure, called once
per polled CDR.

```sql
CREATE PROCEDURE sp_ingest_call_record(
    IN p_source_record_id VARCHAR(128), IN p_direction VARCHAR(16), IN p_dialled_number VARCHAR(32),
    IN p_caller_id VARCHAR(64), IN p_started_at DATETIME(6), IN p_answered_at DATETIME(6),
    IN p_ended_at DATETIME(6), IN p_connected_duration_seconds INT, IN p_disposition VARCHAR(32),
    IN p_is_final TINYINT(1), IN p_now DATETIME(6)
)
BEGIN
    DECLARE v_call_id CHAR(36);
    DECLARE v_changed TINYINT(1);
    DECLARE v_old_attribution_id CHAR(36);
    DECLARE v_old_qualification_id CHAR(36);
    DECLARE v_old_was_qualified TINYINT(1) DEFAULT 0;
    DECLARE v_new_attribution_id CHAR(36);
    DECLARE v_new_state VARCHAR(16);
    DECLARE v_new_is_qualified TINYINT(1) DEFAULT 0;

    SELECT id, (answered_at <=> p_answered_at AND ended_at <=> p_ended_at
                AND connected_duration_seconds <=> p_connected_duration_seconds
                AND disposition <=> p_disposition AND is_final = p_is_final) = 0
    INTO v_call_id, v_changed
    FROM calls WHERE source_record_id = p_source_record_id;

    IF v_call_id IS NULL THEN
        -- Fresh call.
        SET v_call_id = UUID();
        INSERT INTO calls (id, source_record_id, direction, dialled_number, caller_id, started_at, answered_at,
            ended_at, connected_duration_seconds, disposition, is_final, ingested_at, updated_at)
        VALUES (v_call_id, p_source_record_id, p_direction, p_dialled_number, p_caller_id, p_started_at, p_answered_at,
            p_ended_at, p_connected_duration_seconds, p_disposition, p_is_final, p_now, p_now);

        -- Link any Call Leg that arrived before this CDR.
        UPDATE call_legs SET call_id = v_call_id WHERE source_call_record_id = p_source_record_id AND call_id IS NULL;

        CALL sp_attribute_call(v_call_id, p_now, v_new_attribution_id, v_new_state);
        IF v_new_state = 'Attributed' THEN
            CALL sp_qualify_call(v_call_id, v_new_attribution_id, p_now);
        END IF;

    ELSEIF v_changed = 1 THEN
        -- FR-045: restatement — re-derive attribution and qualification, superseding the prior current rows.
        UPDATE calls SET answered_at = p_answered_at, ended_at = p_ended_at,
            connected_duration_seconds = p_connected_duration_seconds, disposition = p_disposition,
            is_final = p_is_final, updated_at = p_now
        WHERE id = v_call_id;

        SELECT id INTO v_old_attribution_id FROM attributions WHERE call_id = v_call_id AND is_current = 1;
        IF v_old_attribution_id IS NOT NULL THEN
            UPDATE attributions SET is_current = 0, superseded_reason = 'call_record_restated' WHERE id = v_old_attribution_id;
        END IF;

        CALL sp_attribute_call(v_call_id, p_now, v_new_attribution_id, v_new_state);

        SELECT id, is_qualified INTO v_old_qualification_id, v_old_was_qualified
        FROM qualification_results WHERE call_id = v_call_id AND is_current = 1;
        IF v_old_qualification_id IS NOT NULL THEN
            UPDATE qualification_results SET is_current = 0, superseded_reason = 'call_record_restated' WHERE id = v_old_qualification_id;
        END IF;

        IF v_new_state = 'Attributed' THEN
            CALL sp_qualify_call(v_call_id, v_new_attribution_id, p_now);
            SELECT is_qualified INTO v_new_is_qualified FROM qualification_results
            WHERE call_id = v_call_id AND is_current = 1;
        END IF;

        CALL sp_correct_publications_if_needed(v_call_id, IFNULL(v_old_was_qualified, 0), v_new_is_qualified, p_now);
    END IF;
    -- else: byte-identical re-ingestion — no-op, matching Call.ApplyRestatement's own guard.
END
```

### sp_ingest_call_leg

Replaces `IngestionService.ProcessCallLegAsync`.

```sql
CREATE PROCEDURE sp_ingest_call_leg(
    IN p_source_call_record_id VARCHAR(128), IN p_source_leg_id VARCHAR(128), IN p_sequence_or_role VARCHAR(64),
    IN p_started_at DATETIME(6), IN p_ended_at DATETIME(6)
)
BEGIN
    DECLARE v_parent_call_id CHAR(36);
    IF NOT EXISTS (SELECT 1 FROM call_legs WHERE source_call_record_id = p_source_call_record_id AND source_leg_id = p_source_leg_id) THEN
        SELECT id INTO v_parent_call_id FROM calls WHERE source_record_id = p_source_call_record_id;
        INSERT INTO call_legs (id, call_id, source_call_record_id, source_leg_id, sequence_or_role, started_at, ended_at)
        VALUES (UUID(), v_parent_call_id, p_source_call_record_id, p_source_leg_id, p_sequence_or_role, p_started_at, p_ended_at);
    END IF;
END
```

### sp_advance_ingestion_checkpoint

Replaces `IngestionService.AdvanceCheckpointAsync`.

```sql
CREATE PROCEDURE sp_advance_ingestion_checkpoint(IN p_feed VARCHAR(32), IN p_position VARCHAR(255), IN p_now DATETIME(6))
BEGIN
    INSERT INTO ingestion_checkpoints (id, feed, position, updated_at) VALUES (UUID(), p_feed, p_position, p_now)
    ON DUPLICATE KEY UPDATE position = p_position, updated_at = p_now;
END
```

### sp_resolve_review_case

Invoked directly by an authenticated MySQL session (native role-based access, no Apiche) — see the "Admin and Reporting: direct database access" note at the top of this document.

Actor identity is resolved from the database's own `CURRENT_USER()` — direct database access means there is no Apiche layer to inject an actor id as a parameter, and none is accepted as one (this session's FR-035 clarification).

Replaces `ReviewResolutionService.ResolveAsync` in full, calling the shared
`sp_qualify_call`/`sp_correct_publications_if_needed` helpers above so this stays
consistent with fresh ingestion's own qualification/correction logic (FR-036, FR-044,
FR-045's "superseded, never overwritten" pattern).

```sql
CREATE PROCEDURE sp_resolve_review_case(
    IN p_review_case_id CHAR(36), IN p_session_id CHAR(36), IN p_confirm_unattributed TINYINT(1)
)
BEGIN
    DECLARE v_status VARCHAR(16);
    DECLARE v_call_id CHAR(36);
    DECLARE v_old_attribution_id CHAR(36);
    DECLARE v_old_qualification_id CHAR(36);
    DECLARE v_old_was_qualified TINYINT(1) DEFAULT 0;
    DECLARE v_new_attribution_id CHAR(36) DEFAULT UUID();
    DECLARE v_new_state VARCHAR(16);
    DECLARE v_allocation_id CHAR(36);
    DECLARE v_resolution VARCHAR(64);
    DECLARE v_new_is_qualified TINYINT(1) DEFAULT 0;
    DECLARE v_now DATETIME(6) DEFAULT UTC_TIMESTAMP(6);
    DECLARE v_actor VARCHAR(32) DEFAULT SUBSTRING_INDEX(CURRENT_USER(), '@', 1);

    SELECT status, call_id INTO v_status, v_call_id FROM review_cases WHERE id = p_review_case_id;
    IF v_status = 'Resolved' THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'This review case is already resolved.';
    END IF;

    SELECT id INTO v_old_attribution_id FROM attributions WHERE call_id = v_call_id AND is_current = 1;
    IF v_old_attribution_id IS NOT NULL THEN
        UPDATE attributions SET is_current = 0, superseded_reason = 'manual_review_resolved' WHERE id = v_old_attribution_id;
    END IF;

    IF p_session_id IS NOT NULL THEN
        SELECT id INTO v_allocation_id FROM allocations WHERE session_id = p_session_id LIMIT 1;
        IF v_allocation_id IS NULL THEN
            SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'That session has no allocation to attribute this call against.';
        END IF;
        INSERT INTO attributions (id, call_id, session_id, allocation_id, state, is_current, decided_at)
        VALUES (v_new_attribution_id, v_call_id, p_session_id, v_allocation_id, 'Attributed', 1, v_now);
        SET v_new_state = 'Attributed';
        SET v_resolution = CONCAT('attributed_to_session_', p_session_id);
    ELSE
        INSERT INTO attributions (id, call_id, state, reason, is_current, decided_at)
        VALUES (v_new_attribution_id, v_call_id, 'Unattributed', 'manual_review_confirmed_unattributed', 1, v_now);
        SET v_new_state = 'Unattributed';
        SET v_resolution = 'confirmed_unattributed';
    END IF;

    SELECT id, is_qualified INTO v_old_qualification_id, v_old_was_qualified
    FROM qualification_results WHERE call_id = v_call_id AND is_current = 1;
    IF v_old_qualification_id IS NOT NULL THEN
        UPDATE qualification_results SET is_current = 0, superseded_reason = 'manual_review_resolved' WHERE id = v_old_qualification_id;
    END IF;

    IF v_new_state = 'Attributed' THEN
        CALL sp_qualify_call(v_call_id, v_new_attribution_id, v_now);
        SELECT is_qualified INTO v_new_is_qualified FROM qualification_results WHERE call_id = v_call_id AND is_current = 1;
    END IF;

    CALL sp_correct_publications_if_needed(v_call_id, IFNULL(v_old_was_qualified, 0), v_new_is_qualified, v_now);

    UPDATE review_cases SET status = 'Resolved', resolved_by = v_actor, resolved_at = v_now, resolution = v_resolution
    WHERE id = p_review_case_id;

    UPDATE alerts SET cleared_at = v_now
    WHERE condition_type = 'ReviewCaseAge' AND scope_ref = p_review_case_id AND cleared_at IS NULL;

    INSERT INTO audit_entries (id, actor_user_id, action, target_type, target_id, before_value, after_value, occurred_at)
    VALUES (UUID(), v_actor, 'ResolveReviewCase', 'ReviewCase', p_review_case_id,
        JSON_OBJECT('status', 'Open'),
        JSON_OBJECT('status', 'Resolved', 'resolvedBy', v_actor, 'attributionId', v_new_attribution_id, 'sessionId', p_session_id),
        v_now);

    SELECT v_new_attribution_id AS attribution_id, v_new_state AS state;
END
```

### sp_acknowledge_alert

Invoked directly by an authenticated MySQL session (native role-based access, no Apiche) — see the "Admin and Reporting: direct database access" note at the top of this document.

Actor identity is resolved from the database's own `CURRENT_USER()` — direct database access means there is no Apiche layer to inject an actor id as a parameter, and none is accepted as one (this session's FR-035 clarification).

Replaces the DB-only half of `AdminAlertsController.Acknowledge`/`AlertingService.AcknowledgeAsync`.
The synchronous acknowledged-webhook/email delivery this currently performs cannot run
inside SQL — see Coverage Notes; a fast-polling companion job (or Apiche's own
notification feature, if any) should pick up the just-acknowledged alert instead.

```sql
CREATE PROCEDURE sp_acknowledge_alert(IN p_id CHAR(36))
BEGIN
    DECLARE v_cleared_at DATETIME(6);
    DECLARE v_actor VARCHAR(32) DEFAULT SUBSTRING_INDEX(CURRENT_USER(), '@', 1);
    SELECT cleared_at INTO v_cleared_at FROM alerts WHERE id = p_id;
    IF v_cleared_at IS NOT NULL THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'This alert has already cleared.';
    END IF;

    UPDATE alerts SET acknowledged_at = UTC_TIMESTAMP(6), acknowledged_by = v_actor WHERE id = p_id;

    INSERT INTO audit_entries (id, actor_user_id, action, target_type, target_id, before_value, after_value, occurred_at)
    VALUES (UUID(), v_actor, 'AcknowledgeAlert', 'Alert', p_id,
        JSON_OBJECT('acknowledged', false), JSON_OBJECT('acknowledged', true, 'acknowledgedBy', v_actor), UTC_TIMESTAMP(6));

    SELECT id, condition_type, scope_ref, threshold, raised_at, last_notified_at, acknowledged_at, acknowledged_by, cleared_at
    FROM alerts WHERE id = p_id;
END
```

### sp_erase_visitor

Invoked directly by an authenticated MySQL session (native role-based access, no Apiche) — see the "Admin and Reporting: direct database access" note at the top of this document.

Actor identity is resolved from the database's own `CURRENT_USER()` — direct database access means there is no Apiche layer to inject an actor id as a parameter, and none is accepted as one (this session's FR-035 clarification).

Replaces `RetentionService.EraseVisitorAsync`. The app's surrogate is a keyed
HMAC-SHA256 of the caller id (`RetentionPolicy.HmacKey`, a deployment secret). Stock MySQL
8 has no built-in HMAC function, so this uses `SHA2` over the secret concatenated with the
value as a documented approximation — **not** bit-identical to the .NET output — see
Coverage Notes. The secret itself is read from a dedicated secrets table populated at
deploy time, never accepted as a client-supplied endpoint parameter.

```sql
CREATE PROCEDURE sp_erase_visitor(IN p_visitor_id CHAR(36))
BEGIN
    DECLARE v_now DATETIME(6) DEFAULT UTC_TIMESTAMP(6);
    DECLARE v_hmac_key VARCHAR(255);
    DECLARE v_actor VARCHAR(32) DEFAULT SUBSTRING_INDEX(CURRENT_USER(), '@', 1);
    SELECT secret_value INTO v_hmac_key FROM app_secrets WHERE secret_name = 'retention_hmac_key';

    UPDATE visitors SET de_identified_at = v_now WHERE id = p_visitor_id AND de_identified_at IS NULL;
    UPDATE sessions SET landing_page = NULL, referrer = NULL, utm_source = NULL, utm_medium = NULL, utm_campaign = NULL,
        utm_term = NULL, utm_content = NULL, gclid = NULL, gbraid = NULL, wbraid = NULL, ga4_client_id = NULL,
        de_identified_at = v_now
    WHERE visitor_id = p_visitor_id AND de_identified_at IS NULL;

    UPDATE calls c
    JOIN attributions a ON a.call_id = c.id
    JOIN sessions s ON s.id = a.session_id
    SET c.caller_id = SHA2(CONCAT(v_hmac_key, c.caller_id), 256), c.de_identified_at = v_now
    WHERE s.visitor_id = p_visitor_id AND c.caller_id IS NOT NULL AND c.de_identified_at IS NULL
      AND NOT EXISTS (SELECT 1 FROM review_cases rc WHERE rc.call_id = c.id AND rc.status = 'Open');

    INSERT INTO audit_entries (id, actor_user_id, action, target_type, target_id, before_value, after_value, occurred_at)
    VALUES (UUID(), v_actor, 'EraseVisitor', 'Visitor', p_visitor_id, NULL,
        JSON_OBJECT('erasedBy', v_actor, 'completedAt', v_now), v_now);
END
```

### sp_evaluate_alerts

Replaces `AlertingService.EvaluateAsync` and its four condition evaluators (ingestion lag,
publication failure rate, pool utilisation, review-case age), raising/repeating/clearing
each condition's single open `alerts` row exactly as `EvaluateConditionAsync` does.
Notification delivery (email/webhook) is a separate concern — see Coverage Notes and the
Background Worker Jobs section.

```sql
CREATE PROCEDURE sp_evaluate_alerts(IN p_now DATETIME(6), IN p_ingestion_lag_seconds INT,
    IN p_publication_failure_rate DECIMAL(4,3), IN p_pool_utilisation DECIMAL(4,3), IN p_review_case_age_seconds INT)
BEGIN
    DECLARE v_last_ingestion DATETIME(6);
    DECLARE v_breached TINYINT(1);
    DECLARE done INT DEFAULT 0;
    DECLARE v_destination VARCHAR(16); DECLARE v_sent INT; DECLARE v_failed INT;
    DECLARE v_pool_id CHAR(36); DECLARE v_held INT; DECLARE v_total INT;
    DECLARE v_case_id CHAR(36); DECLARE v_opened_at DATETIME(6);
    DECLARE dest_cur CURSOR FOR SELECT destination, SUM(CASE WHEN status IN ('Sent','Adjusted') THEN 1 ELSE 0 END),
        SUM(CASE WHEN status IN ('Failed','Rejected') THEN 1 ELSE 0 END) FROM conversion_publications GROUP BY destination;
    DECLARE pool_cur CURSOR FOR SELECT np.id, COUNT(DISTINCT CASE WHEN a.id IS NOT NULL THEN tn.id END), COUNT(DISTINCT tn.id)
        FROM number_pools np JOIN tracking_numbers tn ON tn.pool_id = np.id AND tn.status = 'Active'
        LEFT JOIN allocations a ON a.tracking_number_id = tn.id AND a.window_end > p_now GROUP BY np.id;
    DECLARE case_cur CURSOR FOR SELECT id, opened_at FROM review_cases WHERE status = 'Open';
    DECLARE CONTINUE HANDLER FOR NOT FOUND SET done = 1;

    -- 1) Ingestion lag.
    SELECT updated_at INTO v_last_ingestion FROM ingestion_checkpoints WHERE feed = '8x8-cdr';
    SET v_breached = (v_last_ingestion IS NULL OR TIMESTAMPDIFF(SECOND, v_last_ingestion, p_now) > p_ingestion_lag_seconds);
    CALL sp_evaluate_one_alert('IngestionLag', '8x8-cdr', v_breached, p_now);

    -- 2) Publication failure rate, per destination.
    OPEN dest_cur;
    dest_loop: LOOP
        FETCH dest_cur INTO v_destination, v_sent, v_failed;
        IF done = 1 THEN LEAVE dest_loop; END IF;
        SET v_breached = ((v_sent + v_failed) > 0 AND v_failed / (v_sent + v_failed) > p_publication_failure_rate);
        CALL sp_evaluate_one_alert('PublicationFailureRate', v_destination, v_breached, p_now);
    END LOOP;
    CLOSE dest_cur; SET done = 0;

    -- 3) Pool utilisation.
    OPEN pool_cur;
    pool_loop: LOOP
        FETCH pool_cur INTO v_pool_id, v_held, v_total;
        IF done = 1 THEN LEAVE pool_loop; END IF;
        SET v_breached = (v_total > 0 AND v_held / v_total >= p_pool_utilisation);
        CALL sp_evaluate_one_alert('PoolUtilisation', v_pool_id, v_breached, p_now);
    END LOOP;
    CLOSE pool_cur; SET done = 0;

    -- 4) Review-case age.
    OPEN case_cur;
    case_loop: LOOP
        FETCH case_cur INTO v_case_id, v_opened_at;
        IF done = 1 THEN LEAVE case_loop; END IF;
        SET v_breached = (TIMESTAMPDIFF(SECOND, v_opened_at, p_now) >= p_review_case_age_seconds);
        CALL sp_evaluate_one_alert('ReviewCaseAge', v_case_id, v_breached, p_now);
        IF v_breached = 1 THEN
            UPDATE review_cases SET age_alert_raised_at = p_now WHERE id = v_case_id AND age_alert_raised_at IS NULL;
        END IF;
    END LOOP;
    CLOSE case_cur;

    -- AllocationFailureRate (FR-047's fifth condition) is intentionally not evaluated —
    -- no allocation-attempt log exists in the schema, matching AlertingService's own
    -- documented simplification.

    -- Newly raised/repeated alerts, for the notification dispatcher to pick up.
    SELECT id, condition_type, scope_ref, threshold, raised_at, last_notified_at
    FROM alerts WHERE cleared_at IS NULL AND last_notified_at = p_now;
END
```

### sp_evaluate_one_alert

Shared helper implementing `AlertingService.EvaluateConditionAsync`'s raise/repeat/clear
state machine, reused by all four conditions above (repeat-notification interval is a
fixed literal here — bind it to `AlertingThresholds.RepeatNotificationInterval` at
deployment).

```sql
CREATE PROCEDURE sp_evaluate_one_alert(IN p_condition_type VARCHAR(32), IN p_scope_ref VARCHAR(255), IN p_breached TINYINT(1), IN p_now DATETIME(6))
BEGIN
    DECLARE v_id CHAR(36); DECLARE v_last_notified DATETIME(6);
    SELECT id, last_notified_at INTO v_id, v_last_notified FROM alerts
    WHERE condition_type = p_condition_type AND scope_ref <=> p_scope_ref AND cleared_at IS NULL LIMIT 1;

    IF p_breached = 1 THEN
        IF v_id IS NULL THEN
            INSERT INTO alerts (id, condition_type, scope_ref, threshold, raised_at, last_notified_at)
            VALUES (UUID(), p_condition_type, p_scope_ref, '', p_now, p_now);
        ELSEIF TIMESTAMPDIFF(SECOND, v_last_notified, p_now) >= 900 THEN -- repeat-notification interval
            UPDATE alerts SET last_notified_at = p_now WHERE id = v_id;
        END IF;
    ELSEIF v_id IS NOT NULL THEN
        UPDATE alerts SET cleared_at = p_now WHERE id = v_id;
    END IF;
END
```

### sp_deidentify_expired

Replaces `RetentionService.DeIdentifyExpiredAsync`. Shares the same `SHA2`-based surrogate
approximation and secrets-table lookup as `sp_erase_visitor` — see Coverage Notes.

```sql
CREATE PROCEDURE sp_deidentify_expired(IN p_now DATETIME(6), IN p_visitor_months INT, IN p_call_months INT)
BEGIN
    DECLARE v_hmac_key VARCHAR(255);
    DECLARE v_visitor_cutoff DATETIME(6);
    DECLARE v_call_cutoff DATETIME(6);
    SELECT secret_value INTO v_hmac_key FROM app_secrets WHERE secret_name = 'retention_hmac_key';
    SET v_visitor_cutoff = DATE_SUB(p_now, INTERVAL p_visitor_months MONTH);
    SET v_call_cutoff = DATE_SUB(p_now, INTERVAL p_call_months MONTH);

    UPDATE visitors SET de_identified_at = p_now WHERE first_seen_at < v_visitor_cutoff AND de_identified_at IS NULL;
    UPDATE sessions s JOIN visitors v ON v.id = s.visitor_id
    SET s.landing_page = NULL, s.referrer = NULL, s.utm_source = NULL, s.utm_medium = NULL, s.utm_campaign = NULL,
        s.utm_term = NULL, s.utm_content = NULL, s.gclid = NULL, s.gbraid = NULL, s.wbraid = NULL, s.ga4_client_id = NULL,
        s.de_identified_at = p_now
    WHERE v.first_seen_at < v_visitor_cutoff AND s.de_identified_at IS NULL;

    UPDATE calls c SET c.caller_id = SHA2(CONCAT(v_hmac_key, c.caller_id), 256), c.de_identified_at = p_now
    WHERE c.started_at < v_call_cutoff AND c.de_identified_at IS NULL AND c.caller_id IS NOT NULL
      AND NOT EXISTS (SELECT 1 FROM review_cases rc WHERE rc.call_id = c.id AND rc.status = 'Open');

    UPDATE conversion_publications cp
    JOIN qualification_results qr ON qr.id = cp.qualification_result_id
    JOIN calls c ON c.id = qr.call_id
    SET cp.external_id = SHA2(CONCAT(v_hmac_key, cp.external_id), 256), cp.de_identified_at = p_now
    WHERE c.started_at < v_call_cutoff AND cp.de_identified_at IS NULL AND cp.external_id IS NOT NULL;
END
```

### sp_purge_expired

Replaces `RetentionService.PurgeExpiredAsync`/`RetentionRepository.PurgeCallAsync`.

```sql
CREATE PROCEDURE sp_purge_expired(IN p_now DATETIME(6), IN p_call_purge_months INT, IN p_audit_years INT)
BEGIN
    DECLARE v_call_purge_cutoff DATETIME(6);
    DECLARE v_audit_cutoff DATETIME(6);
    DECLARE done INT DEFAULT 0;
    DECLARE v_call_id CHAR(36);
    DECLARE call_cur CURSOR FOR
        SELECT c.id FROM calls c WHERE c.started_at < v_call_purge_cutoff
          AND NOT EXISTS (SELECT 1 FROM review_cases rc WHERE rc.call_id = c.id AND rc.status = 'Open');
    DECLARE CONTINUE HANDLER FOR NOT FOUND SET done = 1;

    SET v_call_purge_cutoff = DATE_SUB(p_now, INTERVAL p_call_purge_months MONTH);
    SET v_audit_cutoff = DATE_SUB(p_now, INTERVAL p_audit_years YEAR);

    OPEN call_cur;
    purge_loop: LOOP
        FETCH call_cur INTO v_call_id;
        IF done = 1 THEN LEAVE purge_loop; END IF;

        DELETE cp FROM conversion_publications cp JOIN qualification_results qr ON qr.id = cp.qualification_result_id
        WHERE qr.call_id = v_call_id;
        DELETE FROM qualification_results WHERE call_id = v_call_id;
        DELETE FROM review_cases WHERE call_id = v_call_id;
        DELETE FROM attributions WHERE call_id = v_call_id;
        DELETE FROM call_legs WHERE call_id = v_call_id;
        DELETE FROM calls WHERE id = v_call_id;
    END LOOP;
    CLOSE call_cur;

    DELETE FROM audit_entries WHERE occurred_at < v_audit_cutoff;
END
```

---

## Background Worker Jobs (Apiche scheduled jobs)

Source: `src/Attribution.Workers/*/*.cs`. None of these are inbound HTTP endpoints; each
becomes an Apiche scheduled job on the same cadence the current `BackgroundService`
implements.

### 8x8 CDR & Call Leg ingestion

- **Schedule/cadence**: every `Ingestion:PollIntervalSeconds` (default 3600s / hourly), matching `IngestionWorker`'s `PeriodicTimer`.
- **External call**: polls the 8x8 Work Analytics API for Call Detail Records and Call Legs since the feed's last checkpoint position (`IAnalytics8x8Client.PollAsync`, `src/Attribution.Infrastructure/Ingestion8x8/Analytics8x8Client.cs`).
- **SQL**: for each polled call record —
```sql
CALL sp_ingest_call_record(<source_record_id>, <direction>, <dialled_number>, <caller_id>,
    <started_at>, <answered_at>, <ended_at>, <connected_duration_seconds>, <disposition>, <is_final>, UTC_TIMESTAMP(6))
```
  for each polled call leg record —
```sql
CALL sp_ingest_call_leg(<source_call_record_id>, <source_leg_id>, <sequence_or_role>, <started_at>, <ended_at>)
```
  then, once the whole page is durably upserted —
```sql
CALL sp_advance_ingestion_checkpoint('8x8-cdr', <next_checkpoint_position>, UTC_TIMESTAMP(6))
```

### Publication (Google Ads / GA4 outbox drain)

- **Schedule/cadence**: every 30 seconds (`PublicationWorker.PollInterval`).
- **External call**: uploads a conversion to the Google Ads API (`IGoogleAdsClient.UploadConversionAsync`/`RetractAsync`/`AdjustAsync`) or sends a GA4 Measurement Protocol event (`IGa4Client.SendEventAsync`), per outbox row.
- **SQL**: select up to 50 retryable rows (`status = 'Pending'` or `status = 'Failed' AND attempt_count < 5`, plus any `status = 'PendingCorrection'` row `sp_correct_publications_if_needed` marked) —
```sql
SELECT cp.*, c.id AS call_id, c.started_at AS call_started_at,
       s.gclid, s.gbraid, s.wbraid, s.ga4_client_id
FROM conversion_publications cp
JOIN qualification_results qr ON qr.id = cp.qualification_result_id
JOIN calls c ON c.id = qr.call_id
JOIN attributions a ON a.id = qr.attribution_id
LEFT JOIN sessions s ON s.id = a.session_id
WHERE cp.status IN ('Pending', 'PendingCorrection') OR (cp.status = 'Failed' AND cp.attempt_count < 5)
ORDER BY cp.attempt_count ASC
LIMIT 50
```
  then, per row, after the external call succeeds or fails —
```sql
UPDATE conversion_publications SET status = <new_status>, external_id = <external_id>,
    last_error = <error>, correction = <correction_json>, sent_at = <sent_at>, corrected_at = <corrected_at>,
    attempt_count = attempt_count + 1
WHERE id = <id>
```

### Alerting evaluation

- **Schedule/cadence**: every 1 minute (`AlertingWorker.EvaluationInterval`).
- **External call**: none for evaluation itself. Delivery of a raised/repeated/acknowledged
  alert is an outbound email send and/or webhook POST (`IAlertEmailSender`,
  `IAlertWebhookSender`) — this is the part that cannot run inside SQL (see Coverage
  Notes); it must remain a small companion dispatcher (or a native Apiche
  scheduled-HTTP-call feature, if one exists) that queries the rows the evaluation step
  just touched and performs the send, recording the outcome.
- **SQL**:
```sql
CALL sp_evaluate_alerts(UTC_TIMESTAMP(6), <ingestion_lag_threshold_seconds>,
    <publication_failure_rate_threshold>, <pool_utilisation_threshold>, <review_case_age_threshold_seconds>)
```
  then, per delivery attempt the companion dispatcher makes —
```sql
INSERT INTO notification_delivery_status (channel, last_attempt_at, last_success_at, last_failure_at, last_failure_reason)
VALUES (<channel>, <at>, <success_at>, <failure_at>, <failure_reason>)
ON DUPLICATE KEY UPDATE
    last_attempt_at = <at>,
    last_success_at = COALESCE(<success_at>, last_success_at),
    last_failure_at = COALESCE(<failure_at>, last_failure_at),
    last_failure_reason = CASE WHEN <success_at> IS NOT NULL THEN NULL ELSE COALESCE(<failure_reason>, last_failure_reason) END
```

### Retention (de-identification & purge)

- **Schedule/cadence**: every 24 hours (`RetentionWorker.EvaluationInterval`).
- **External call**: none.
- **SQL**:
```sql
CALL sp_deidentify_expired(UTC_TIMESTAMP(6), <visitor_deidentify_after_months>, <call_deidentify_after_months>)
```
```sql
CALL sp_purge_expired(UTC_TIMESTAMP(6), <call_purge_after_months>, <audit_log_retention_years>)
```

---

## Coverage Notes

- **Auth (`/v1/auth/sign-in`, `/v1/auth/refresh`) has no Apiche translation.** These exist
  only to support the current session-based JWT + mandatory TOTP model, which the new
  design explicitly replaces with per-request HTTP Basic Auth checked against a
  server-side credential store (no sessions, no JWT, no TOTP). Apiche's native Basic Auth
  credential store performs this check outside the SQL-per-endpoint model entirely, so
  there is nothing left for these two endpoints to do.
- **CSV file upload (`POST /v1/admin/pools/{id}/numbers/import`) does not fit Apiche's
  JSON-body model.** The current endpoint accepts a multipart file; Apiche's POST/PUT
  bodies are JSON objects, with no file-upload primitive described. Resolved by reshaping
  the wire contract to accept a JSON array of pre-parsed DID strings instead (the CSV
  parsing moves to whichever client calls this endpoint). The C#'s "reject the second-and-
  later occurrence of a repeated value within the same file, in file order" duplicate rule
  is only approximately reproduced in `sp_import_tracking_numbers`'s window-function
  logic — a straightforward re-implementation, not a byte-exact translation; if exact
  per-row duplicate semantics matter, this is worth hand-verifying against the SP body.
- **`GET /v1/admin/numbers/import-folder/files` is not a SQL operation, and stays that way
  under direct database access too.** It lists `*.csv` files in a server-side folder
  (`NumberImportOptions.FolderPath`) — filesystem metadata, not database rows. This cannot
  be expressed as a SQL statement at all, whether reached through Apiche or a direct
  database connection; it must either stay a small ordinary filesystem-listing feature of
  the admin tooling itself, or be dropped if operators are willing to type the file name
  directly into the from-folder import call instead of picking from a list.
- **`POST /v1/admin/pools/{id}/numbers/import-from-folder` requires the MySQL server
  itself to read the shared import folder.** `sp_import_tracking_numbers_from_folder` uses
  `LOAD DATA INFILE`, which reads from the *database server's* filesystem, not the app
  tier's — this only works if the configured import folder is a path (or network share)
  the MySQL server process itself can see, with `secure_file_priv` permitting it. This is
  a deployment-topology change from today's app-tier-reads-the-file model, not a pure
  config change.
- **The Google Ads/GA4 correction and delivery side effects of `Resolve` review case,
  `Acknowledge` alert, and the Publication Worker's outbox drain all make outbound HTTP
  calls that cannot execute inside a MySQL stored procedure.** Each SP above does the
  database-only portion (supersede/insert rows, mark rows `PendingCorrection`, audit) and
  leaves the actual HTTP call to the existing Publication Worker job (which already polls
  for retryable/pending-correction rows) or to a small companion dispatcher for alert
  email/webhook delivery. This is a real behavior change from the current code, which
  performs the Google Ads retract/adjust call and the acknowledged-alert
  webhook/email *synchronously, inside the HTTP request* — under this design (direct
  database access for `Resolve`/`Acknowledge`, an unchanged Apiche scheduled job for the
  Publication Worker) it becomes eventually-consistent (next worker tick), typically
  sub-minute given the existing
  30-second/1-minute poll intervals, but no longer synchronous.
- **The retention surrogate hash is only approximated.** `RetentionService`'s
  `Surrogate()` method computes a keyed HMAC-SHA256 using a deployment secret
  (`RetentionPolicy.HmacKey`). Stock MySQL 8 has no built-in HMAC function; `sp_erase_visitor`,
  `sp_deidentify_expired` use `SHA2(CONCAT(secret, value), 256)` instead, which is **not**
  bit-identical to the .NET output. If external systems (e.g. reconciliation reports) ever
  need to independently recompute this surrogate, they must recompute it the same
  (approximated) way rather than assuming HMAC-SHA256.
- **User creation's password is not persisted by this SQL.** `POST /v1/admin/users`'s
  `password` body field is not referenced by `sp_create_user` — under the new auth model,
  the actual username/password pair is registered in Apiche's native Basic Auth credential
  store as a separate step (assumed out of scope per the task brief), while `sp_create_user`
  only maintains the `users` row RBAC (`mapped_role`) resolves against. These two writes
  (credential store + `users` row) are not currently atomic with each other; a failure
  between them would need reconciliation tooling.
- **The JSON/CSV report "twins" are no longer two endpoints at all.** `ReportsController`'s
  `.../export.csv` actions used to call the identical `ReportingService` method as their
  JSON counterparts and only change how the result was serialized (`text/csv` vs. JSON).
  Under direct database access there is no HTTP layer to route between two response
  formats, so — per the note at the top of the Reporting section — this collapses into a
  single operation: the reporting portal runs the one SELECT once and renders it as a
  screen or exports it as CSV, both from the same result set. This document treats the SQL
  as identical and shared for that reason.
- **Dashboard/unattributed report totals are computed over the returned rows, not by a
  second query.** `ReportingService.DashboardAsync`/`.UnattributedAsync` derive
  `attribution_rate`/`by_reason` etc. purely by summarizing the exact rows already
  returned (so a total can never disagree with what's displayed). The SQL statements above
  return the same row sets `ReportingRepository` does; the summarization itself is assumed
  to happen in the reporting portal (or a client), not as separate SQL, to preserve that
  "never disagrees" guarantee structurally rather than by two independently written
  queries.
- **`GET /v1/admin/health/publication` can under-report destinations with zero rows.** The
  `GROUP BY destination` SQL only returns a row for a destination that has at least one
  `conversion_publications` row; the current C# always returns exactly one row per
  `PublicationDestination` enum value (`sent=0, failed=0, healthy=true` for one with none).
  The admin tooling should left-join/union in the two known destination values
  (`GoogleAds`, `Ga4`) if an always-both-rows shape is required.
- **`IngestionCheckpointRepository`/`IAlertingMetricsRepository`'s "recent" publication
  window is not actually time-bounded in the current code either** (documented directly in
  `AlertingRepository.GetRecentPublicationOutcomeCountsAsync`'s own comment: no
  attempt-timestamp column exists to filter by) — the SQL above faithfully reproduces that
  same all-time aggregate rather than inventing a time window the original code doesn't have.
- **Admin and Reporting now use direct database access, not Apiche, per an explicit
  architecture decision made after reviewing this document.** Every operation under an
  `## Admin — ...` heading and under `## Reporting` is invoked directly against MySQL by a
  trusted internal client (the admin tooling, the reporting portal) over TLS, authenticated
  as that operator's own native MySQL user account, with authorization enforced by MySQL's
  own role-based access control (`role_system_administrator`, `role_marketing_administrator`,
  `role_analyst`) rather than an HTTP-layer check. DNI and the Background Worker Jobs are
  unaffected and remain genuine Apiche HTTP endpoints/scheduled jobs — DNI specifically
  because it is public, untrusted browser JavaScript that can never hold real database
  credentials, the one surface a gateway genuinely protects.
- **The previously-flagged `actor_user_id`/Apiche-identity-injection open item is now fully
  resolved.** This document previously flagged, as its single highest-risk open item, that
  every admin write endpoint's SQL assumed the acting user's id arrives as an
  `actor_user_id` field sourced from "whatever identity Apiche's own Basic Auth layer
  resolves and injects per request" — needing reconciliation with however Apiche actually
  exposes that identity. Under direct database access, every
  Admin/Reporting procedure that needs the acting user's id instead reads it via MySQL's own
  `CURRENT_USER()` inside the procedure, since each human operator authenticates as their
  own real MySQL session identity — no injection mechanism is needed at all. This is the
  item's only occurrence anywhere in the document (nothing in DNI or the Background Worker Jobs
  sections has an equivalent Apiche-identity-injection concern), so it is now fully
  resolved rather than merely narrowed; see Appendix A's per-procedure `CURRENT_USER()`
  notes on `sp_deactivate_user`, `sp_override_user_role`,
  `sp_create_qualification_rule_version`, `sp_resolve_review_case`, `sp_acknowledge_alert`,
  and `sp_erase_visitor`.
- **The CSV-file-upload reshaping and the folder-import `LOAD DATA INFILE`
  deployment-topology notes above still apply exactly as before.** Both were about the
  SQL/stored-procedure design itself (no file-upload primitive in a JSON/SQL-parameter
  model; `LOAD DATA INFILE` reading from the *database server's* filesystem, not the app
  tier's) rather than about HTTP vs. direct-database invocation, so moving Admin to direct
  database access changes neither: the admin tooling still pre-parses the CSV client-side
  into a JSON array of DIDs before calling `sp_import_tracking_numbers` directly, and
  `sp_import_tracking_numbers_from_folder` still requires the MySQL server process itself
  (not the client) to have filesystem access to the configured import folder.
