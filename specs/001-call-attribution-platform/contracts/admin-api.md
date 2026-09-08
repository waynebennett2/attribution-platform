# Contract: Administration API

> **Updated 2026-09-08, twice.** First to HTTP Basic Auth (plan.md's "Architecture Migration" addendum, research.md §20) — now superseded again, same day: after reviewing `apiche-config.md`, the project owner moved every operation below off Apiche entirely, onto **direct database access** (research.md §22, constitution amendment 2.0.0). **These are no longer HTTP endpoints.** Each row below is retained as a stable operation catalog — what the operation does, its role requirement, its parameters — but "Method"/"Path" now identifies the operation for cross-reference against `apiche-config.md` (whose Admin sections give the exact `CALL sp_xxx(...)` or `SELECT` a trusted internal client runs directly against MySQL), not a literal route a client sends an HTTP request to. Every state-changing call still writes an Audit Entry (FR-035) with actor, action, target, before/after values — the actor now comes from the database's own authenticated session (`CURRENT_USER()`), not a request field.

## Authentication (direct database access — supersedes both the JWT/TOTP and the Basic Auth designs previously described here)

There is no HTTP layer, no sign-in endpoint, and no request to carry a header on. Each interactive human connects directly to the database as their own individual native database account:

| Credential | Used by | Notes |
|---|---|---|
| Individual native database account, mapped to a database role | System Administrator, Marketing Administrator, Analyst | One account per human (data-model.md's User entity, `db_username`), granted membership in `role_system_administrator` / `role_marketing_administrator` / `role_analyst`. The database engine itself checks the credential and enforces the role's grants on every operation — no session, no MFA challenge, no Apiche involvement. Deactivating the account (disabling it at the database level) removes access on the very next operation attempted with it (SC-016, immediate). |
| API key (Basic Auth password against Apiche) | Integration Service | Unaffected by this change — Integration Service is neither an Admin nor a Reporting operation, and remains authenticated via Apiche exactly as before (research.md §20); barred from every interactive-only operation (FR-038). |

Every failed authentication attempt against a human or Integration Service credential is written to the audit log, preserving FR-046's "every sign-in attempt, successful or failed, MUST be audited" intent now that there is no discrete sign-in event to anchor it to — for `local` accounts this means every refused database connection attempt.

## Number pools & numbers (FR-001–FR-007)

| Method | Path | Notes |
|---|---|---|
| GET | `/v1/admin/pools` | lists every pool across every scope — camelCase `scopeType`/`scopeRef`/`defaultNumber` (ASP.NET Core's default JSON policy on the ad-hoc response shape) alongside literal `number_count`/`utilisation` |
| POST | `/v1/admin/pools` | scope_type + scope_ref required (FR-004); rejects a `default_number` that collides, digit-normalized, with another pool's `default_number` scoped to the same website while that website has `multi_pool_enabled` (FR-050) |
| GET | `/v1/admin/pools/{id}` | includes current utilisation for the FR-034 warning; same mixed camelCase/snake_case shape as the list above |
| GET | `/v1/admin/pools/{id}/numbers` | lists the pool's individual Tracking Numbers — `[{ id, did, status, status_changed_at, last_released_at }]` |
| POST | `/v1/admin/pools/{id}/numbers/import` | multipart CSV upload; response lists per-row accept/reject with reason (FR-002) |
| GET | `/v1/admin/numbers/import-folder/files` | lists CSV files currently in the configured server-side import folder — `[{ file_name, size_bytes, modified_at }]` (FR-051) |
| POST | `/v1/admin/pools/{id}/numbers/import-from-folder` | `{ "file_name": "string" }` — reads that file from the configured folder and applies the identical per-row accept/reject logic as `/numbers/import`; 400 if `file_name` is not a bare name resolving inside the folder (FR-051) |
| POST | `/v1/admin/numbers/{id}/suspend` \| `/retire` \| `/reactivate` | does not touch an in-progress Allocation (FR-005) |
| POST | `/v1/admin/numbers/{id}/move` | `{ "target_pool_id": "string" }` — rejects if number not currently active-and-unheld in a way that would violate exactly-one-pool (FR-004) |

## Websites (FR-004, FR-049, FR-050)

| Method | Path | Notes |
|---|---|---|
| GET | `/v1/admin/websites` | lists every website — includes `shadowModeEnabled`/`multiPoolEnabled` (camelCase, ad-hoc response shape) alongside its other configuration. There is no create endpoint yet: a Website is currently provisioned directly against the database, not through this API. |
| POST | `/v1/admin/websites/{id}/shadow-mode/enable` \| `/disable` | FR-049 parallel-run toggle, audited |
| POST | `/v1/admin/websites/{id}/multi-pool/enable` \| `/disable` | FR-050 multi-pool DNI toggle, audited |

## Qualification rules (FR-022–FR-024, FR-033)

| Method | Path | Notes |
|---|---|---|
| GET | `/v1/admin/qualification-rules?scope_type=&scope_ref=` | lists versions for a scope |
| POST | `/v1/admin/qualification-rules` | `{ scope_type, scope_ref?, conditions: {...}, effective_start }` — server computes/validates contiguity against the prior version in scope, 400 on gap/overlap (FR-024) |
| DELETE | `/v1/admin/qualification-rules/{id}` | only permitted on a not-yet-effective future version; a live/past version cannot be deleted (FR-024's "never alter... already judged") |

## Users & roles (FR-032, FR-046)

| Method | Path | Notes |
|---|---|---|
| GET | `/v1/admin/users` | lists every local account (System Administrator, Marketing Administrator, Analyst) and its effective role |
| POST | `/v1/admin/users` | `{ "username", "role" }` → creates a native database account for `username` (the database's own account-creation facility sets the actual password, communicated to the new user out of band — never through this operation or stored in this platform's own records), grants it the matching database role, and records the mapping (2026-09-08, twice: no TOTP secret is issued — MFA is dropped under the direct-database-access model, research.md §22) |
| POST | `/v1/admin/users/{id}/deactivate` | audited; rejected with 409 if this would leave zero active System Administrator accounts (FR-046) |
| POST | `/v1/admin/users/{id}/role-override` | `{ "role": "..." }` — audited; rejected with 409 if this would leave zero active System Administrator accounts, same guard as `/deactivate` (FR-046) |

## Integration health (FR-034)

| Method | Path | Notes |
|---|---|---|
| GET | `/v1/admin/health/ingestion` | last successful ingest time, current lag, per-feed checkpoint |
| GET | `/v1/admin/health/publication` | success/failure counts per destination |
| GET | `/v1/admin/health/pools` | per-pool utilisation vs. warning threshold |
| GET | `/v1/admin/health/notifications` | per-channel (email/webhook) last delivery attempt/success/failure — FR-047's delivery-failure surfacing, distinct from whether the alert conditions themselves are healthy |

## Alerts (FR-047)

| Method | Path | Notes |
|---|---|---|
| GET | `/v1/admin/alerts?status=open` | |
| POST | `/v1/admin/alerts/{id}/acknowledge` | audited; stops repeat notification, does not clear the underlying condition (FR-047) |

## Manual review (FR-036)

| Method | Path | Notes |
|---|---|---|
| GET | `/v1/admin/review-cases?status=open` | includes age, flags cases past the 48h default threshold |
| POST | `/v1/admin/review-cases/{id}/resolve` | `{ "session_id": "string" }` (or `"confirm_unattributed": true`) — creates a superseding Attribution row, propagates any already-published correction under FR-044 |

## Audit log (FR-035)

| Method | Path | Notes |
|---|---|---|
| GET | `/v1/admin/audit?target_type=&target_id=&from=&to=` | read-only; no PUT/PATCH/DELETE exists on this resource by design; every filter is independently optional |

## Privacy (FR-039)

| Method | Path | Notes |
|---|---|---|
| POST | `/v1/admin/privacy/visitors/{id}/erase` | erases one visitor's data on request — synchronous, completing well within the 30-day SC-019 bar rather than a queued request/status workflow (data-model.md's Visitor section); a call still under an open manual review case is left untouched, per FR-040's own carve-out |
