# Contract: Administration API

All endpoints require a platform-issued JWT (FR-046) and enforce RBAC per FR-038. Every state-changing call writes an Audit Entry (FR-035) with actor, action, target, before/after values.

## Authentication (FR-046)

| Method | Path | Notes |
|---|---|---|
| POST | `/v1/auth/sign-in` | Unauthenticated — `{ username, password, totp_code }` → `{ access_token, expires_at, refresh_token }`. The platform's sole interactive sign-in path — local username/password plus mandatory TOTP MFA for every role. Every sign-in attempt, success or failure, is audited. |
| POST | `/v1/auth/refresh` | Unauthenticated — `{ refresh_token }` → `{ access_token, expires_at, refresh_token }`, rotating the refresh token on each use. Refused (401) if the account has been deactivated or the refresh token is unknown/expired, which is what bounds a deactivated account's access loss to one refresh interval (SC-016). |

## Number pools & numbers (FR-001–FR-007)

| Method | Path | Notes |
|---|---|---|
| POST | `/v1/admin/pools` | scope_type + scope_ref required (FR-004) |
| GET | `/v1/admin/pools/{id}` | includes current utilisation for the FR-034 warning |
| POST | `/v1/admin/pools/{id}/numbers/import` | multipart CSV upload; response lists per-row accept/reject with reason (FR-002) |
| GET | `/v1/admin/numbers/import-folder/files` | lists CSV files currently in the configured server-side import folder — `[{ file_name, size_bytes, modified_at }]` (FR-051) |
| POST | `/v1/admin/pools/{id}/numbers/import-from-folder` | `{ "file_name": "string" }` — reads that file from the configured folder and applies the identical per-row accept/reject logic as `/numbers/import`; 400 if `file_name` is not a bare name resolving inside the folder (FR-051) |
| POST | `/v1/admin/numbers/{id}/suspend` \| `/retire` \| `/reactivate` | does not touch an in-progress Allocation (FR-005) |
| POST | `/v1/admin/numbers/{id}/move` | `{ "target_pool_id": "string" }` — rejects if number not currently active-and-unheld in a way that would violate exactly-one-pool (FR-004) |

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
| POST | `/v1/admin/users` | `{ "username", "password", "role" }` → creates a local account, generating a fresh TOTP secret returned once as an `otpauth://` provisioning URI for the administrator to hand to whoever will hold it |
| POST | `/v1/admin/users/{id}/deactivate` | audited; rejected with 409 if this would leave zero active System Administrator accounts (FR-046) |
| POST | `/v1/admin/users/{id}/role-override` | `{ "role": "..." }` — audited (FR-046) |

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
