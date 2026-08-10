# Phase 0 Research: Call Attribution Platform

**Feature**: 001-call-attribution-platform | **Date**: 2026-08-10

Every item below resolves a technical unknown left open by the constitution (which fixes language, database and layering, but explicitly defers data-access technology to this plan) or by the spec's functional requirements. No NEEDS CLARIFICATION markers remain in the Technical Context.

## 1. Data access: Dapper vs EF Core

- **Decision**: Dapper as the sole data-access technology, used only from the Infrastructure layer.
- **Rationale**: Two operations are correctness-critical and latency-sensitive in a way that benefits from hand-written, reviewable SQL rather than ORM-generated queries: atomic number allocation (FR-003, SC-004's 300ms bar) and idempotent CDR/Call Leg upserts (FR-017, FR-045). Both need explicit `INSERT ... ON DUPLICATE KEY UPDATE` / `SELECT ... FOR UPDATE SKIP LOCKED` control that EF Core would fight rather than help with. Using one data-access technology for the whole application (rather than mixing Dapper for hot paths and EF Core for CRUD) keeps Infrastructure simple and avoids two change-tracking mental models in one codebase, consistent with the "monolith-first" architecture style in the constitution.
- **Alternatives considered**: EF Core alone — idiomatic for the CRUD-heavy admin surface (pools, numbers, rules, users) but its change-tracker and generated SQL make the atomic-allocation and idempotent-upsert paths harder to reason about and test at the SQL level. EF Core + Dapper hybrid — gets the best of both but adds a second ORM/migration story for no benefit at this schema's size (~16 entities); rejected as unnecessary complexity for a monolith-first build.

## 2. Atomic number allocation on MySQL

- **Decision**: `SELECT id FROM tracking_numbers WHERE pool_id = @pool AND status = 'active' AND id NOT IN (<currently-held>) ORDER BY last_released_at LIMIT 1 FOR UPDATE SKIP LOCKED`, followed by an `UPDATE` inserting the allocation row in the same transaction.
- **Rationale**: `FOR UPDATE SKIP LOCKED` (MySQL 8.0.1+) lets concurrent allocation requests each grab a different available number without blocking on each other or double-allocating, satisfying FR-003's "no two concurrent sessions can ever hold the same number" guarantee under the SC-004 peak of ~57 requests/minute. This is why Storage is pinned to MySQL 8.0+ rather than an unversioned "MySQL" in Technical Context.
- **Alternatives considered**: Application-level distributed lock (e.g., Redis) — adds an operational dependency and a second source of truth for a load level (≤57 req/min) the database handles natively; rejected as premature. Optimistic concurrency (retry on conflict) — workable but adds latency variance under contention that a direct locked read avoids; rejected in favor of the simpler pessimistic approach given the write is already short-lived.

## 3. Idempotent ingestion and outbox-based publication

- **Decision**: CDR/Call Leg ingestion upserts on 8x8's own record identifier (natural key) with `INSERT ... ON DUPLICATE KEY UPDATE`, guarded by the ingestion checkpoint (FR-016). Publication to Google Ads/GA4 goes through an outbox table written in the same transaction as the qualification decision; a worker drains the outbox and records the idempotency key (FR-027) before calling out, so a crash between "sent" and "recorded" cannot double-publish on retry.
- **Rationale**: Directly implements Principle IV (idempotent, auditable operations) and FR-017/FR-027/FR-045 without introducing a message broker — at this scale (~57 allocation-path requests/minute, hourly CDR batches) a database-backed outbox is sufficient and keeps the deployment footprint to "API + workers + MySQL," matching the constitution's monolith-first guidance.
- **Alternatives considered**: A message queue (RabbitMQ/SQS) between qualification and publication — gives stronger decoupling but is unjustified infrastructure for this volume and reintroduces an idempotency problem (consumer-side dedup) the outbox table already solves at the database layer; deferred as a future extraction point if scale grows, per the constitution's "structured to allow future extraction" guidance.

## 4. Background worker architecture

- **Decision**: A separate `Attribution.Workers` host running four `IHostedService` loops — Ingestion (8x8 polling, FR-016), Publication (outbox drain, FR-025–FR-028), Alerting (FR-047 threshold evaluation), Retention (FR-040 purge/de-identify, FR-039 erasure) — deployed and scaled independently from the request/response API.
- **Rationale**: FR-043 requires no single point of failure in the visitor-facing allocation path; running ingestion/publication in-process with the API would couple their failure modes and resource contention (a slow 8x8 poll must never delay a DNI allocation request). A dedicated worker host can scale to zero or many instances independently and is simple to reason about — one loop, one responsibility, one crash-restart boundary.
- **Alternatives considered**: In-process `IHostedService` inside the API — simplest to deploy but violates the isolation FR-043 asks for. A full job-scheduling platform (Hangfire, Quartz.NET clustered) — adds a UI and storage model beyond what four fixed, always-on loops need; a plain hosted-service loop with the outbox/checkpoint tables already providing durability is sufficient.

## 5. Federated SSO and break-glass accounts

- **Decision**: Standard OpenID Connect authorization-code flow against the customer's identity provider; on successful federation the platform issues its own short-lived JWT carrying the mapped role (FR-046), rather than passing the IdP's token through to downstream calls. Break-glass accounts (2 by default, FR-046) are local username/password + TOTP MFA records, checked only when OIDC discovery/token endpoints are unreachable.
- **Rationale**: Satisfies the constitution's JWT-everywhere constraint (Principle VI) while still delegating authentication to the customer's IdP as FR-046 requires; issuing a platform-owned token also lets the group-to-role mapping and any in-platform override be baked into every subsequent request without re-querying the IdP.
- **Alternatives considered**: Passing the IdP's own token straight through to the API — simpler, but couples every service to the specific claims shape of whichever IdP a given customer runs, and cannot carry the in-platform role override FR-046 requires; rejected.

## 6. Google Ads offline conversions integration

- **Decision**: Google Ads API's `ConversionUploadService` (click conversions, keyed on GCLID/GBRAID/WBRAID) for initial upload, and its adjustment/retraction endpoint for the corrections FR-044 requires.
- **Rationale**: This is the only Google Ads surface that accepts an offline (non-web-hit) conversion tied to a stored click identifier, and it is the one Google surface documented to support later retraction/adjustment — the capability FR-044 depends on existing at all.
- **Alternatives considered**: None materially different — Google Ads offers no second API for this use case; the only real decision is how failures are retried, covered by the outbox/idempotency design in §3.

## 7. GA4 Measurement Protocol integration

- **Decision**: Server-side Measurement Protocol POST, keyed on the GA4 client identifier captured at session time (FR-015, FR-026).
- **Rationale**: The Measurement Protocol is Google's only server-to-server path for GA4 events and is explicitly named in the spec's input description; it has no retraction endpoint, which is why FR-044 records GA4 corrections as "unpropagatable" rather than attempting one.
- **Alternatives considered**: None — this is the single documented integration surface for the requirement as stated.

## 8. Analytics for 8x8 Work ingestion

- **Decision**: Scheduled polling (default hourly, FR-016) against the Analytics for 8x8 Work API for Call Detail Records and Call Legs, authenticated per 8x8's own credential model, with an ingestion checkpoint (last successfully consumed position) persisted per feed so a restart resumes without gap or duplication.
- **Rationale**: The spec's own description mandates polling (no 8x8-side webhook/push capability is asserted), and FR-016 explicitly requires cadence changes to leave attribution outcomes unaffected — only achievable if matching always replays stored allocation windows rather than depending on ingestion timing, which the checkpoint-plus-idempotent-upsert design in §3 already guarantees.
- **Alternatives considered**: Real-time/streaming ingestion — not offered by the stated source (Analytics for 8x8 Work is polled), and Real-time dashboards are explicitly Out of Scope; rejected as solving a problem the spec doesn't have.

## 9. DNI client testing

- **Decision**: Playwright for the constitution-mandated browser-level tests of the insertion client, covering multi-page and SPA replacement, session stickiness across tabs, post-load DOM mutation (MutationObserver-driven replacement), consent grant/withdrawal, and default-number fallback.
- **Rationale**: These are exactly the scenarios the constitution calls out as unenforceable by server-side tests alone; Playwright drives real browser engines (not a DOM simulation), supports multi-tab scenarios (Edge Cases: "same visitor opens several concurrent tabs"), and integrates cleanly into the same CI pipeline as the xUnit suites.
- **Alternatives considered**: Cypress — comparable capability but weaker native multi-tab/multi-context support, which the concurrent-tabs edge case specifically needs; Playwright's built-in multi-context API is a better fit.

## 10. Retention and de-identification

- **Decision**: A scheduled Retention worker loop (§4) that, per data category, either hard-deletes (visitor/session identifiers at 14 months) or replaces identifying fields with a stable HMAC-derived, non-reversible surrogate (call/attribution/publication records, de-identified in place at the 14-month mark, retained a further 11 months to 25 total), skipping any row still referenced by an open manual review case (FR-040) or awaiting an erasure request's 30-day completion window (SC-019).
- **Rationale**: A stable surrogate (rather than deleting the linking key outright) is what FR-040 requires to keep historical reports and audit trails internally consistent after identifiers are gone — a keyed HMAC of the original identifier produces the same surrogate every time the same source value is re-encountered within a category, preserving joins without being reversible without the key.
- **Alternatives considered**: Hard delete with orphaned foreign keys — breaks FR-019's evidence-chain requirement and SC-014's "reports still reconcile" bar; rejected. Reversible encryption — technically satisfies "non-reversible" in spirit but not in fact, since the key's existence makes it recoverable; rejected as not meeting the letter of FR-040.

## 11. Rate limiting for the visitor-facing endpoints

- **Decision**: Fixed-window counters keyed on (origin, client token) held in-process per instance with periodic reconciliation, or a shared store (e.g., MySQL-backed counter table, reusing the existing database rather than adding Redis) if horizontal scale-out makes per-instance counting too permissive in practice; defaults of 600 req/min per origin and 10 req/min per client (FR-037).
- **Rationale**: At the platform's stated scale (~57 req/min system-wide peak, §Technical Context Scale/Scope), even a per-instance approximation comfortably enforces the FR-037 defaults without a new infrastructure dependency; a shared MySQL counter table is a low-effort upgrade path if the deployment later runs enough API instances for per-instance skew to matter.
- **Alternatives considered**: Redis-backed sliding-window rate limiting — the standard answer at larger scale, but an unjustified dependency addition against the stated ~630-number, ~250-concurrent-session sizing; noted as the first thing to revisit if Scale/Scope grows materially beyond what's stated in the spec.

## 12. Consent event/callback contract (FR-039)

- **Decision**: A single global JS contract published by the DNI client: `window.__attributionConsent = { granted: boolean }` readable synchronously on load, plus a `window.addEventListener('attribution:consent-change', handler)` custom event any CMP or custom consent tool dispatches on later changes. The DNI client reads the global on load and subscribes to the event for the life of the page view.
- **Rationale**: A single, versioned, minimal-surface contract is what makes FR-039's "any site's consent mechanism is wired to fire it, rather than a bespoke per-site adapter" true in practice — one thing for every deploying customer's consent tooling to call, documented once.
- **Alternatives considered**: A cookie/localStorage convention instead of a JS event — harder to subscribe to for real-time change notification (would require polling), which conflicts with FR-039's "acted on immediately" requirement; rejected.

## 13. Schema migrations

- **Decision**: FluentMigrator, C#-authored, versioned migrations run as a startup/deploy step against MySQL.
- **Rationale**: With Dapper chosen over EF Core (§1), there's no ORM-provided migration tooling; FluentMigrator gives the same "migrations as code, reviewed in PRs" workflow the constitution's CI/CD gate expects, without pulling in a full ORM just for its migration runner.
- **Alternatives considered**: DbUp (plain SQL script runner) — simpler but loses the up/down, C#-testable migration authoring FluentMigrator offers; rejected as a minor ergonomics regression for no scale benefit. Flyway — mature but Java-centric tooling in an otherwise pure .NET deployment; rejected to avoid a second runtime dependency.

## 14. Qualification rule representation

- **Decision**: A qualification rule version is stored as a small structured condition set (direction, answered, minimum connected duration, website/campaign scope, time-of-day window) in relational columns/a constrained JSON document — not as arbitrary executable code — evaluated by one internal rule-evaluator function shared by every rule.
- **Rationale**: FR-023/FR-024 require rules to be configurable without code change and safely versioned; a constrained condition set keeps evaluation deterministic, unit-testable (Principle V) and auditable (every field of every version is inspectable), whereas an embedded scripting/expression language would reopen the "no fuzzy/heuristic logic" concern Principle I is guarding against in a different guise.
- **Alternatives considered**: A generic rule-expression DSL (e.g., stored JsonLogic/CEL expressions) — more flexible for future condition types, but unjustified against the five condition dimensions FR-023 actually names; deferred until a real requirement for open-ended conditions appears.
