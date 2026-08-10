# Quickstart: Validating the Call Attribution Platform

**Feature**: 001-call-attribution-platform | **Date**: 2026-08-10

This is a validation guide, not a build guide — it proves the feature works end-to-end against the acceptance bars in spec.md. Full setup, environment configuration and CI wiring are implementation tasks; see `data-model.md` for schema details and `contracts/` for the exact request/response shapes referenced below.

## Prerequisites

- .NET 8 SDK
- Docker (for local MySQL 8.0+ and Testcontainers-backed integration tests)
- Node.js (for the `client/dni-script` package and its Playwright tests)
- A sandbox/test credential set for: Analytics for 8x8 Work, Google Ads (test account), GA4 (test property) — real credentials are not required to validate attribution/qualification logic, only to validate the publication path end-to-end

## 1. Local environment

```bash
docker compose up -d mysql          # MySQL 8.0+, per research.md §2's FOR UPDATE SKIP LOCKED requirement
dotnet run --project src/Attribution.Infrastructure -- migrate   # FluentMigrator, applies schema from data-model.md
dotnet run --project src/Attribution.Api
dotnet run --project src/Attribution.Workers
```

Seed a Website, a Number Pool with a handful of active Tracking Numbers, and the default Qualification Rule (FR-022) via `/v1/admin/*` (contracts/admin-api.md) or a seed script — a minimum viable environment for every scenario below needs at least one Website with `permitted_origins` including the test host.

## 2. Story 1 — DNI allocation and session capture (SC-003, SC-004, SC-013)

1. Load a test page with UTM parameters and a `gclid` in the URL, consent granted.
2. Confirm `POST /v1/dni/allocate` (contracts/dni-api.md) returns a number from the seeded pool, and that every configured phone-number instance on the page is replaced with it (FR-008).
3. Navigate to a second page (and, on an SPA fixture, trigger an in-app route change) — confirm the number is unchanged and no second allocation occurs (FR-010).
4. Reload with consent withheld — confirm the default number displays, no `session_id` is created, and nothing is stored client-side (SC-013); then grant consent on the same page and confirm a session now appears with the original arrival details (FR-039).
5. Run the DNI client's Playwright suite (`client/dni-script/tests`) headless in CI for the multi-tab, post-load-DOM-mutation, and script-blocked-fallback scenarios that cannot be validated manually.

**Pass bar**: matches User Story 1's Acceptance Scenarios 1–6 exactly.

## 3. Story 2 — Deterministic attribution (SC-001, SC-002)

Seed the exact call set SC-001 specifies: one call inside the allocation window, one after session expiry but inside the FR-018 extension, one after the window closed, one to a never-allocated number, one to a suspended number, one spanning a daylight-saving transition, one placed across midnight. Feed each as a synthetic Call Detail Record through the ingestion path (or directly via a test seam into the attribution service).

**Pass bar**: 100% of the seeded calls land in the state the tester independently knows is correct, with a stored evidence chain (matched number, session, allocation window) for every attributed one (SC-001). Re-ingest the identical batch three times and confirm zero change in any report total (SC-002).

## 4. Story 3 — Qualification (User Story 3 Acceptance Scenarios)

Feed attributed calls at 45s and 75s connected duration against the default rule; confirm the 45s call is not qualified and the 75s call is, each recording the rule version applied. Publish a new rule version for a specific website scope with a different threshold (contracts/admin-api.md's `POST /v1/admin/qualification-rules`); confirm previously-judged calls keep their original result and version reference, and only calls after the new version's `effective_start` are judged by it.

## 5. Story 5 — Publication (SC-007, SC-015)

Qualify one call whose session carries a `gclid` and `ga4_client_id`, and one whose session carries neither. Confirm the first is published exactly once to both Google Ads and GA4 (contracts/alert-webhook.md's destinations), the second is skipped at both with a recorded reason (FR-025, FR-026), and re-running publication for either produces no second conversion (FR-027). Resolve a review case that unqualifies an already-published call; confirm the Google Ads conversion is retracted and the GA4 divergence is recorded as unpropagatable (SC-015).

## 6. Story 6 — Administration and audit (SC-006, SC-016, SC-017)

Perform one action of each administrative type (role change, number suspension, rule publication) and confirm all three appear in `GET /v1/admin/audit` with actor/target/before/after (SC-006); attempt to alter an audit entry and confirm it's refused and itself logged. Disable a signed-in test user at the identity provider and confirm their next request is refused without waiting for session expiry (SC-016). Induce a stalled ingestion, a failing publication destination, a pool crossing its utilisation warning, and a review case left open past 48 hours; confirm each raises a webhook + email alert within 15 minutes, repeats without duplicating, and clears on resolution (SC-017).

## 7. Retention and erasure (SC-014, SC-019)

Run the Retention worker against a seeded historical dataset spanning the 14-month and 25-month thresholds; confirm identifiers are gone but report totals for that period reconcile identically before and after (SC-014). Submit a data-subject erasure request against a seeded visitor and confirm completion — and the visitor's identified data being gone — within the 30-day SC-019 bar (run this as an accelerated/simulated-clock test, not a literal 30-day wait).
