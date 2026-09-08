# Contract: Administration API

> **Updated 2026-09-08** — see plan.md's "Architecture Migration" addendum and research.md §20. All endpoints require HTTP Basic Auth, checked fresh on every request against a per-user long-lived credential with a role attached, and enforce RBAC per FR-038. Every state-changing call writes an Audit Entry (FR-035) with actor, action, target, before/after values. This supersedes the JWT-based auth model previously described here; `spec.md`'s FR-046/SC-016 were formally amended to match in the 2026-09-08 `/speckit.clarify` session.

## Authentication (Basic Auth — supersedes FR-046's JWT/TOTP design, research.md §20)

There is no sign-in or refresh endpoint. Every request to any endpoint below carries an `Authorization: Basic <base64(username:password)>` header, verified independently each time:

| Credential | Used by | Notes |
|---|---|---|
| Per-user username/password | System Administrator, Marketing Administrator, Analyst | Long-lived, stored only as a salted hash (data-model.md's User entity). Checked, and the row's role enforced, on every request — no session, no MFA challenge. Deactivating the account (`is_active = false`) removes access on the very next request (SC-016, strengthened from "within one refresh interval" to immediate). |
| API key (as the Basic Auth password) | Integration Service | Fixed identifying username + API key; barred from every interactive-only endpoint (FR-038). |

Every failed Basic Auth check against a human or integration credential is written to the audit log, preserving FR-046's "every sign-in attempt, successful or failed, MUST be audited" intent now that there is no discrete sign-in event to anchor it to.

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
| POST | `/v1/admin/users` | `{ "username", "password", "role" }` → creates a local account with the given Basic Auth username/password (stored as a salted hash) and role (2026-09-08: no TOTP secret is issued — MFA is dropped under the Basic Auth model, research.md §20) |
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
