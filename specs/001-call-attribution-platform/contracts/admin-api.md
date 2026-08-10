# Contract: Administration API

All endpoints require a platform-issued JWT (post-OIDC-federation or break-glass, FR-046) and enforce RBAC per FR-038. Every state-changing call writes an Audit Entry (FR-035) with actor, action, target, before/after values.

## Number pools & numbers (FR-001–FR-007)

| Method | Path | Notes |
|---|---|---|
| POST | `/v1/admin/pools` | scope_type + scope_ref required (FR-004) |
| GET | `/v1/admin/pools/{id}` | includes current utilisation for the FR-034 warning |
| POST | `/v1/admin/pools/{id}/numbers/import` | multipart CSV upload; response lists per-row accept/reject with reason (FR-002) |
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
| GET/POST | `/v1/admin/users` | federated users are provisioned on first sign-in, not created here; this creates/edits break-glass accounts and role overrides |
| POST | `/v1/admin/users/{id}/role-override` | `{ "role": "..." }` — audited (FR-046) |

## Integration health (FR-034)

| Method | Path | Notes |
|---|---|---|
| GET | `/v1/admin/health/ingestion` | last successful ingest time, current lag, per-feed checkpoint |
| GET | `/v1/admin/health/publication` | success/failure counts per destination |
| GET | `/v1/admin/health/pools` | per-pool utilisation vs. warning threshold |

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
| GET | `/v1/admin/audit?target_type=&target_id=&from=&to=` | read-only; no PUT/PATCH/DELETE exists on this resource by design |
