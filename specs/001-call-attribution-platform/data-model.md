# Phase 1 Data Model: Call Attribution Platform

**Feature**: 001-call-attribution-platform | **Date**: 2026-08-10

Derived from spec.md's Key Entities section and the Functional Requirements that constrain each entity's fields, relationships and lifecycle. Field lists are the attributes the spec's requirements and acceptance scenarios actually need to be provable — not a full column-level schema, which is an implementation task.

## Website

Configuration root for a tracked property.

| Field | Notes |
|---|---|
| id | |
| name | |
| permitted_origins | list — FR-037 origin restriction |
| default_number | FR-007 fallback |
| session_timeout_seconds | default 1800 (FR-012) |
| heartbeat_interval_seconds | default 300 (FR-012) |
| allocation_window_extension_seconds | default 1800 — FR-018, configurable per website |
| cooldown_seconds | default = allocation_window_extension_seconds; MUST be ≥ it (FR-006) |
| consent_required | |
| shadow_mode_enabled | default false (FR-049) |
| business_unit | for pool scoping (FR-004) |
| local_timezone | used to evaluate a qualification rule's time-of-day condition (FR-023), independent of the canonical storage timezone |
| created_at, updated_at | |

**Validation**: `cooldown_seconds >= allocation_window_extension_seconds` (reject otherwise, FR-006).

## Number Pool

| Field | Notes |
|---|---|
| id | |
| name | |
| scope_type | website \| campaign \| business_unit (FR-004) |
| scope_ref | id of the website/campaign/business-unit this pool is scoped to |
| default_number | overrides Website.default_number if set (FR-007) |
| created_at, updated_at | |

**Relationships**: one Website may use multiple Number Pools (via scope); one Number Pool holds many Tracking Numbers.

## Tracking Number

| Field | Notes |
|---|---|
| id | |
| pool_id | FK Number Pool — **exactly one pool at a time** (FR-004); moving a number updates this FK, does not delete history |
| did | the 8x8 number itself |
| status | active \| suspended \| retired (FR-005) |
| status_changed_at | |
| last_released_at | used by the allocation query's ordering (research.md §2) |

**State transitions**: `active → suspended → active` (reversible), `active|suspended → retired` (terminal). Suspending/retiring MUST NOT touch an in-progress Allocation on that number (FR-005) — only future allocation eligibility changes.

**Validation**: suspended/retired numbers excluded from allocation candidate queries; historical Allocation/Attribution rows referencing this number are never deleted or reassigned by a status change or a pool move.

## Allocation

The binding of one Tracking Number to one Session for a bounded window — the sole evidence used for attribution (FR-019).

| Field | Notes |
|---|---|
| id | |
| tracking_number_id | FK |
| session_id | FK |
| pool_id_at_allocation | snapshot — pool the number belonged to at allocation time, independent of later moves (FR-004) |
| window_start | moment the number was first displayed |
| window_end | session end + allocation_window_extension_seconds (FR-018), or session end at consent withdrawal (FR-039) |
| is_shadow | true if FR-049 shadow-observed rather than platform-allocated |
| created_at | |

**Invariants**: for a given `tracking_number_id`, no two Allocation rows' `[window_start, window_end)` may overlap in ordinary (non-shadow) operation — enforced at allocation time by the cooldown check (FR-006); shadow-mode Allocations are exempt from this invariant and instead feed the ambiguity path (FR-021, FR-049) when they do overlap.

## Visitor

| Field | Notes |
|---|---|
| id | anonymous, device/browser-scoped |
| website_id | FK — a visitor is identified across sessions on **one** website |
| first_seen_at | |
| de_identified_at | nullable — set at the 14-month retention threshold, or immediately on an FR-039 erasure request |

**FR-039 erasure has no separate persisted "request" entity**: `POST /v1/admin/privacy/visitors/{id}/erase` (contracts/admin-api.md) performs the same de-identification transformation the 14-month sweep eventually would, synchronously, for one visitor regardless of age — completing trivially within FR-039's 30-day bar rather than needing a queued request/status workflow the spec never actually asks for. A call still under an open manual review case is left untouched by either path (FR-040's own carve-out), to be picked up once the review resolves.

## Session

| Field | Notes |
|---|---|
| id | |
| visitor_id | FK |
| website_id | FK |
| landing_page, referrer | captured at first page view (FR-014) |
| utm_source, utm_medium, utm_campaign, utm_term, utm_content | FR-014 |
| gclid, gbraid, wbraid | FR-015, nullable |
| ga4_client_id | FR-015, nullable — required for GA4 publication (FR-026) |
| consent_state | pending \| granted \| withdrawn (FR-039) |
| provenance | ordinary \| degraded — degraded when consent arrived after arrival details were no longer recoverable (FR-014) |
| started_at | at consent grant if consent was pending at arrival (FR-039), else at first page view |
| expires_at | rolling: `last_activity + session_timeout_seconds` (FR-012) |
| ended_at | nullable — set on timeout or consent withdrawal |
| de_identified_at | nullable — set at the 14-month retention threshold, alongside the utm_*/gclid/gbraid/wbraid/ga4_client_id/landing_page/referrer fields above being nulled (FR-040) |

**Relationships**: one Session has zero-or-one active Allocation at a time (FR-010); a Session may have historical Allocations if it re-allocates after a mid-session change (e.g., consent-withdrawal-then-none, since re-grant would be a new session per FR-039's "create the session" language).

## Call

| Field | Notes |
|---|---|
| id | |
| source_record_id | 8x8's own CDR identifier — natural key for idempotent upsert (FR-017) |
| direction | inbound \| outbound (only inbound is attributed/qualified — Assumptions) |
| dialled_number | the tracking number that was rung |
| caller_id | nullable (withheld/anonymous, Edge Cases) |
| started_at, answered_at, ended_at | nullable except started_at |
| connected_duration_seconds | authoritative from 8x8, re-derived on restatement (FR-045) |
| disposition | answered \| missed \| ... |
| is_final | false while 8x8 still reports the call as in-progress; re-ingestion of a non-final call re-derives attribution/qualification (FR-045) |
| ingested_at, updated_at | |
| de_identified_at | nullable — set at the 14-month threshold, when `caller_id` is overwritten in place with a stable HMAC surrogate rather than nulled, preserving the "same caller across calls" join FR-019's evidence chain and SC-014's report reconciliation depend on (FR-040, research.md §10) |

**Relationships**: one Call has many Call Legs; one Call has zero-or-one current Attribution (plus superseded history, FR-045) and zero-or-one current Qualification Result (plus superseded history).

## Call Leg

| Field | Notes |
|---|---|
| id | |
| call_id | FK |
| source_leg_id | 8x8 natural key |
| sequence_or_role | used to reconstruct the call journey (FR-017) |
| started_at, ended_at | |

**Idempotency**: upsert on `(call_id, source_leg_id)`; legs may arrive before their parent CDR (Edge Cases) — held/attached once the parent Call row exists.

## Attribution

The decision linking a Call to a Session.

| Field | Notes |
|---|---|
| id | |
| call_id | FK |
| session_id | nullable FK — null when state is unattributed/ambiguous |
| allocation_id | nullable FK — the specific Allocation matched (FR-019) |
| state | attributed \| unattributed \| ambiguous (FR-018, FR-020, FR-021) |
| reason | required when state ≠ attributed |
| is_shadow_derived | FR-049 — distinguishes shadow-mode ambiguity from ordinary-operation ambiguity |
| is_current | superseded rows kept for FR-045 history, only one `is_current=true` row per call |
| superseded_reason | nullable — why a prior Attribution was replaced |
| decided_at | |

**State machine**: `(none) → attributed | unattributed | ambiguous`, then only `→ attributed | unattributed | ambiguous` again via FR-045 re-derivation or FR-036 manual review resolution, each transition appending a new row and flipping `is_current`, never mutating a prior row in place.

## Qualification Rule

Versioned, scoped condition set (research.md §14).

| Field | Notes |
|---|---|
| id | |
| scope_type | default \| website \| campaign (FR-024) |
| scope_ref | nullable — id of the website/campaign when scope_type ≠ default |
| version | monotonic per scope |
| conditions | direction, answered required, min_connected_duration_seconds, time_of_day_window (all FR-023) |
| effective_start, effective_end | nullable end = "still current"; MUST be contiguous/non-overlapping with adjacent versions in the same scope (FR-024) |
| created_by, created_at | audit trail |

**Validation**: within one `(scope_type, scope_ref)`, `effective_start` of version N+1 MUST equal `effective_end` of version N exactly — reject any configuration creating a gap or overlap (FR-024).

**Resolution rule** (applied at qualification time, not stored): for a Call's website/campaign, use the most specific in-force rule — a matching `website`/`campaign` scope row wins over `default` (FR-024).

## Qualification Result

| Field | Notes |
|---|---|
| id | |
| call_id | FK |
| attribution_id | FK — qualification only runs against an attributed call |
| qualification_rule_id | which version+scope judged it (FR-024) |
| is_qualified | |
| is_current | superseded rows retained as history (FR-045) |
| superseded_reason | nullable |
| decided_at | |

**Invariant**: a rule change never mutates an existing `is_current=true` row (FR-024); only a source restatement (FR-045) or a manual review resolution that changes attribution can supersede one.

## Conversion Publication

One attempt to report one qualified call to one destination.

| Field | Notes |
|---|---|
| id | |
| qualification_result_id | FK |
| destination | google_ads \| ga4 |
| idempotency_key | stable per (call, destination, publish episode) — FR-027. A new episode begins each time a call is re-qualified after having been unqualified; retries within the same episode reuse the key, a genuine retract-then-requalify gets a new one. |
| status | pending \| sent \| failed \| rejected \| retracted \| adjusted \| skipped |
| skipped_reason | nullable — e.g. "no GCLID" (Google Ads) or "no GA4 client id" (FR-026) |
| attempt_count | |
| external_id | destination's own conversion identifier, once known |
| last_error | nullable |
| correction | nullable structured field: {type: retract\|adjust\|unpropagatable, reason, destination_accepted} (FR-044) |
| sent_at, corrected_at | nullable |
| de_identified_at | nullable — set at the same 14-month threshold as the Call it belongs to; `external_id` (the destination's own conversion/click identifier) is overwritten in place with a stable HMAC surrogate (FR-040) |

**Idempotency**: the outbox worker (research.md §3) writes this row in the same transaction as the Qualification Result; the destination call is only made once `status` transitions from `pending`, and retries reuse the same `idempotency_key` so a crash-and-retry cannot double-publish (FR-027).

## Ingestion Checkpoint

| Field | Notes |
|---|---|
| id | |
| feed | cdr \| call_legs |
| position | source-provided cursor/watermark |
| updated_at | |

One row per feed; advanced only after a batch is durably persisted, so a restart resumes without skip or reprocess (FR-016).

## User / Role

| Field | Notes |
|---|---|
| id | |
| subject_ref | nullable — IdP subject, null for break-glass and integration-service accounts (FR-046) |
| username | nullable — break-glass accounts only |
| client_id | nullable — integration-service accounts only |
| identity_type | federated \| break_glass \| integration_service |
| mapped_role | System Administrator \| Marketing Administrator \| Analyst \| Integration Service |
| role_override | nullable — administrator-set override of the mapped role, audited (FR-046) |
| role_overridden_by | nullable — who applied the override |
| password_hash, totp_secret | nullable — break-glass accounts only; a federated account never has either, since FR-046 requires the platform to never store a password for federated users |
| mfa_required | true for break_glass (FR-046) |
| is_active | |
| created_at, last_seen_at | |

**Constraint**: default of 2 `break_glass` rows, configurable (FR-046); `integration_service` identity_type is barred from interactive session issuance (FR-038). No HTTP endpoint exists for federated sign-in itself — that path is the identity provider's own SSO flow redirecting back with an already-established session, which this repository has no live provider to exercise. The one interactive sign-in surface actually implemented is break-glass (`POST /v1/auth/break-glass/sign-in`, contracts/admin-api.md).

## Alert

| Field | Notes |
|---|---|
| id | |
| condition_type | ingestion_lag \| publication_failure_rate \| allocation_failure_rate \| pool_utilisation \| review_case_age (FR-047) |
| scope_ref | e.g. which pool, which destination |
| threshold | |
| raised_at | |
| last_notified_at | |
| acknowledged_at, acknowledged_by | nullable |
| cleared_at | nullable |

**Invariant**: one open Alert row per `(condition_type, scope_ref)` while firing — repeat notifications update `last_notified_at` on the existing row rather than creating a new Alert (FR-047).

**Known simplification**: `allocation_failure_rate` is never evaluated — no allocation-attempt log exists anywhere in the schema (only successful Allocation rows are persisted, never failed attempts), so there is no failure signal to read for this specific condition. Documented rather than fabricated.

## Notification Delivery Status

Not part of spec.md's Key Entities — an operational-visibility addition, not a business entity. One row per delivery channel (`email`, `webhook`), upserted on every `AlertingWorker` send attempt.

| Field | Notes |
|---|---|
| channel | email \| webhook (primary key) |
| last_attempt_at | |
| last_success_at | nullable |
| last_failure_at | nullable |
| last_failure_reason | nullable |

**Rationale**: FR-047's "failure to deliver a notification MUST NOT suppress the underlying condition on the integration health view of FR-034" implies the reverse also needs to hold — a stuck delivery pipeline should itself be visible on the health view (`GET /v1/admin/health/notifications`, contracts/admin-api.md), independent of whether the alert conditions it would have notified about are themselves healthy.

## Audit Entry

| Field | Notes |
|---|---|
| id | |
| actor_user_id | |
| action | |
| target_type, target_id | |
| before_value, after_value | |
| occurred_at | |

**Invariant**: append-only; no UPDATE/DELETE path exists at any role (FR-035) — enforced at the database-grant level, not just application logic, so that even a compromised admin session cannot alter history.

**Retention**: purged after 7 years (FR-040's default, configurable), the one category FR-040 has purge without a de-identification step first — an audit entry has no "identifier to mask", only whether it still needs to exist.

## Review Case

| Field | Notes |
|---|---|
| id | |
| call_id | FK |
| attribution_id | the ambiguous/disputed Attribution row prompting review |
| status | open \| resolved |
| opened_at | |
| age_alert_raised_at | nullable — FR-036's 48-hour default threshold |
| resolved_by, resolved_at | nullable |
| resolution | the session chosen, or "confirmed unattributed" |

**Relationships**: resolving a Review Case creates a new Attribution row (superseding the ambiguous one) and, if publication was already made on the superseded result, a Conversion Publication correction row (FR-044).
