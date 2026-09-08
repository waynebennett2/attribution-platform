---

description: "Task list template for feature implementation"
---

# Tasks: Call Attribution Platform

**Input**: Design documents from `/specs/001-call-attribution-platform/`
**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md), [data-model.md](./data-model.md), [contracts/](./contracts/), [quickstart.md](./quickstart.md)

**Tests**: Included. The constitution's Principle V (Test-First for Business Logic, NON-NEGOTIABLE) requires unit tests before/alongside Domain and Application layer logic, and its Testing constraint requires browser-level automated tests for the DNI client — these are not optional for this feature.

**Organization**: Tasks are grouped by user story (spec.md priorities P1–P6) to enable independent implementation and testing of each story.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependency on an incomplete task)
- **[Story]**: Maps the task to spec.md's user stories (US1–US6)
- File paths follow plan.md's Project Structure

## Path Conventions

Per plan.md: `src/Attribution.{Api,Application,Domain,Infrastructure,Workers}/`, `client/dni-script/`, `tests/Attribution.{UnitTests,IntegrationTests,Contract}/`.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Repository and toolchain initialization per plan.md's Project Structure.

- [X] T001 Create the .NET 8 solution and the five backend projects (`Attribution.Api`, `Attribution.Application`, `Attribution.Domain`, `Attribution.Infrastructure`, `Attribution.Workers`) in `src/` per plan.md Project Structure, with project references respecting the layering in Constitution Principle II (Api → Application → Domain ← Infrastructure)
- [X] T002 [P] Create the `client/dni-script` package skeleton (`package.json`, `src/`, `tests/`) per plan.md Project Structure
- [X] T003 [P] Create the three test projects (`Attribution.UnitTests`, `Attribution.IntegrationTests`, `Attribution.Contract`) in `tests/` referencing xUnit
- [X] T004 [P] Add Dapper, MySqlConnector and FluentMigrator package references to `Attribution.Infrastructure`, and `Microsoft.Extensions.Hosting` to `Attribution.Workers`, per research.md §1, §3, §4, §13
- [X] T005 [P] Add Testcontainers.MySql to `Attribution.IntegrationTests` and Playwright (`@playwright/test`) to `client/dni-script`
- [X] T006 [P] Add `.editorconfig` and `dotnet format` configuration at the repository root, and ESLint/Prettier configuration in `client/dni-script`
- [X] T007 [P] Add `docker-compose.yml` at the repository root with a MySQL 8.0+ service, per quickstart.md §1
- [X] T008 Configure the CI pipeline (dotnet build, xUnit unit + integration tests, Playwright tests, static analysis) as a merge gate per the constitution's CI/CD constraint

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Schema, cross-cutting infrastructure and auth that every user story depends on.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

### Tests for Foundational

> Numbered T108–T112 (continuing the sequence rather than renumbering T012 onward) but ordered here because Constitution Principle V (NON-NEGOTIABLE) requires these tests before the Domain/Application logic they cover (T013–T019).

- [X] T108 [P] Unit tests for RBAC role-permission decision logic (FR-038) in `tests/Attribution.UnitTests/Identity/RbacDecisionTests.cs`
- [X] T109 [P] Unit tests for JWT validation, 5-minute expiry and silent-refresh logic (FR-046) in `tests/Attribution.UnitTests/Identity/JwtValidationTests.cs`
- [X] T110 [P] Unit tests for the audit-entry append-only invariant — write succeeds, update/delete attempts rejected and recorded (FR-035) — in `tests/Attribution.UnitTests/Audit/AuditImmutabilityTests.cs`
- [X] T111 [P] Unit tests for rate-limit policy evaluation (600/min per origin, 10/min per client, FR-037) in `tests/Attribution.UnitTests/RateLimiting/RateLimitPolicyTests.cs`
- [X] T112 [P] Unit tests for User/Role domain rules — role assignment, override recording (FR-032, FR-046) — in `tests/Attribution.UnitTests/Identity/UserRoleTests.cs`

### Implementation for Foundational

- [X] T009 Configure the FluentMigrator runner project and migration-execution entry point in `src/Attribution.Infrastructure/Data/Migrations/` per research.md §13
- [X] T010 Author the baseline schema migration covering every entity in data-model.md (Website, Number Pool, Tracking Number, Allocation, Visitor, Session, Call, Call Leg, Attribution, Qualification Rule, Qualification Result, Conversion Publication, Ingestion Checkpoint, User, Role, Alert, Audit Entry, Review Case) in `src/Attribution.Infrastructure/Data/Migrations/` (depends on T009)
- [X] T011 [P] Implement the Dapper connection-factory and base repository pattern in `src/Attribution.Infrastructure/Data/` (depends on T009)
- [X] T012 [P] Implement the Website domain entity and repository in `src/Attribution.Domain/Websites/Website.cs`, `src/Attribution.Infrastructure/Data/WebsiteRepository.cs` (depends on T011)
- [X] T013 [P] Implement the User/Role domain entities and repository in `src/Attribution.Domain/Identity/User.cs`, `src/Attribution.Infrastructure/Data/UserRepository.cs` (depends on T011)
- [X] T014 Implement 5-minute JWT access-token issuance in `src/Attribution.Infrastructure/Identity/` per research.md §5, FR-046 (depends on T013) — superseded 2026-08-17: federation dropped, see T140–T143
- [X] T015 [P] Implement local account authentication (username/password + TOTP MFA, unlimited accounts) in `src/Attribution.Infrastructure/Identity/` per FR-046 (depends on T013) — superseded 2026-08-17: was break-glass-only (default 2 accounts), now the sole interactive sign-in path, see T140–T143
- [X] T016 Implement JWT validation and RBAC middleware enforcing FR-038 on every route in `src/Attribution.Api/Middleware/` (depends on T014, T015)
- [X] T017 [P] Implement the Audit Entry domain entity and an append-only repository (no UPDATE/DELETE grant at the database level) in `src/Attribution.Domain/Audit/AuditEntry.cs`, `src/Attribution.Infrastructure/Data/AuditRepository.cs` per FR-035 (depends on T011)
- [X] T018 Implement audit-logging middleware that writes an Audit Entry (actor, action, target, before/after) for every state-changing admin request in `src/Attribution.Api/Middleware/` (depends on T016, T017)
- [X] T019 [P] Implement rate-limiting middleware (600 req/min per origin, 10 req/min per client, per-website configurable) in `src/Attribution.Api/Middleware/` per FR-037, research.md §11
- [X] T020 [P] Implement structured logging, metrics and health-check infrastructure across `src/Attribution.Api/` and `src/Attribution.Workers/` per FR-041
- [X] T021 [P] Implement the `Attribution.Workers` host with four `IHostedService` loop stubs (Ingestion, Publication, Alerting, Retention) in `src/Attribution.Workers/` per research.md §4
- [X] T022 [P] Implement the outbox table writer helper in `src/Attribution.Infrastructure/Data/` per research.md §3 (depends on T010)

**Checkpoint**: Foundation ready — user story implementation can now begin.

---

## Phase 3: User Story 1 - Visitor sees a tracked number and the session is recorded (Priority: P1) 🎯 MVP

**Goal**: Allocate a visitor a tracking number from the pool configured for their website, replace every configured phone number occurrence on the page with it, and record the session (arrival details, click identifiers) and the allocation's evidence window.

**Independent Test**: Load a configured website with UTM parameters and a Google click identifier in the URL, confirm a pool number is displayed in place of every configured number, navigate several pages (including in-app route changes), confirm the number does not change, and confirm a single session record exists holding the arrival details and the allocation window.

### Tests for User Story 1

- [X] T023 [P] [US1] Unit tests for atomic number allocation (`FOR UPDATE SKIP LOCKED` dequeue, no double-allocation) in `tests/Attribution.UnitTests/Allocation/AllocationServiceTests.cs`
- [X] T024 [P] [US1] Unit tests for session timeout/heartbeat expiry logic in `tests/Attribution.UnitTests/Sessions/SessionServiceTests.cs`
- [X] T025 [P] [US1] Integration test for `POST /v1/dni/allocate` against a MySQL Testcontainer, including pool-exhausted fallback (FR-011) in `tests/Attribution.IntegrationTests/Dni/AllocateEndpointTests.cs`
- [X] T026 [P] [US1] Contract test validating request/response shapes against `contracts/dni-api.md` in `tests/Attribution.Contract/DniApiContractTests.cs`
- [X] T027 [P] [US1] Playwright test: number replacement (displayed text + click-to-call targets) on a multi-page site in `client/dni-script/tests/replacement.spec.ts`
- [X] T028 [P] [US1] Playwright test: SPA in-app route change and post-load DOM mutation replacement in `client/dni-script/tests/spa-replacement.spec.ts`
- [X] T029 [P] [US1] Playwright test: session stickiness across concurrent tabs in `client/dni-script/tests/multi-tab.spec.ts`
- [X] T030 [P] [US1] Playwright test: consent gating, grant-after-refusal, withdrawal, and the active pre-consent default-number DOM write in `client/dni-script/tests/consent.spec.ts`
- [X] T031 [P] [US1] Playwright test: script-blocked fallback leaves the static default number in place in `client/dni-script/tests/fallback.spec.ts`

### Implementation for User Story 1

- [X] T032 [P] [US1] Implement Number Pool and Tracking Number domain entities and repositories (exactly-one-pool-at-a-time move semantics, FR-004) in `src/Attribution.Domain/Pools/`, `src/Attribution.Infrastructure/Data/` (depends on T012)
- [X] T033 [P] [US1] Implement Visitor and Session domain entities and repositories in `src/Attribution.Domain/Sessions/`, `src/Attribution.Infrastructure/Data/` (depends on T012)
- [X] T034 [US1] Implement the Allocation domain entity and the atomic `FOR UPDATE SKIP LOCKED` allocation repository query in `src/Attribution.Domain/Sessions/Allocation.cs`, `src/Attribution.Infrastructure/Data/AllocationRepository.cs` per research.md §2, FR-003 (depends on T032, T033)
- [X] T035 [US1] Implement `AllocationService` (allocate, heartbeat, release, suspended-number-in-progress exception FR-005, consent-withdrawal immediate release FR-018/FR-039) in `src/Attribution.Application/Allocation/AllocationService.cs` per FR-006, FR-007, FR-010, FR-012 (depends on T034)
- [X] T036 [US1] Implement `POST /v1/dni/allocate`, `/heartbeat`, `/consent` in `src/Attribution.Api/Controllers/DniController.cs` per contracts/dni-api.md (depends on T035)
- [X] T037 [P] [US1] Implement number pool CRUD and bulk CSV import endpoints (per-row accept/reject reasons, FR-002) in `src/Attribution.Api/Controllers/AdminPoolsController.cs` per contracts/admin-api.md, FR-001, FR-004, FR-005 (depends on T032)
- [X] T038 [P] [US1] Implement the DNI client's allocation and heartbeat calls, with quick-backoff retry on a failed heartbeat before its next scheduled interval, in `client/dni-script/src/allocation.js` per contracts/dni-api.md, FR-012
- [X] T039 [P] [US1] Implement the DNI client's DOM replacement — digit-normalized text matching, `tel:` links and configurable marker-attribute click-to-call targets, MutationObserver for post-load numbers, main-document scope only (no iframe/shadow-DOM traversal) — in `client/dni-script/src/replace.js` per FR-008, FR-009, FR-011
- [X] T040 [P] [US1] Implement the DNI client's consent contract (read `window.__attributionConsent`, subscribe to `attribution:consent-change`, trigger active default-number write pre-consent) in `client/dni-script/src/consent.js` per contracts/consent-contract.md, FR-039
- [X] T041 [US1] Wire landing page/referrer/UTM/GCLID/GBRAID/WBRAID/GA4-client-id capture, with degraded-provenance handling for late consent, into session creation in `src/Attribution.Application/Allocation/AllocationService.cs` per FR-013, FR-014, FR-015 (depends on T035)

### Optional Shadow Mode for User Story 1 (FR-049)

> Numbered T113–T118, continuing the sequence. FR-049's shadow mode had no task coverage until this remediation; it supports the optional, later, report-level comparison against Mediahawk described in spec.md's Assumptions — never a live integration.

- [X] T113 [P] [US1] Unit tests for shadow-mode allocation recording and overlapping-observed-window ambiguity tolerance, distinguished from ordinary-operation ambiguity, in `tests/Attribution.UnitTests/Allocation/ShadowModeTests.cs` per FR-049
- [X] T114 [P] [US1] Playwright test: shadow mode leaves the page's displayed numbers untouched while still recording the observed number in `client/dni-script/tests/shadow-mode.spec.ts` per FR-049
- [X] T115 [US1] Implement shadow-mode allocation recording (observe the displayed number, hold an allocation window without allocating, tolerate overlapping windows as ambiguous) in `src/Attribution.Application/Allocation/ShadowAllocationService.cs` per FR-049 (depends on T034, T051)
- [X] T116 [US1] Implement the per-website shadow-mode toggle endpoint (configuration only, no code change) in `src/Attribution.Api/Controllers/AdminWebsitesController.cs` per FR-049 (depends on T012)
- [X] T117 [P] [US1] Implement the DNI client's observe-only shadow mode (reads the displayed number, does not replace it, reports it via `POST /v1/dni/shadow-observe`) in `client/dni-script/src/shadow.js` per FR-049, contracts/dni-api.md (depends on T038)

### Multi-pool DNI Matching for User Story 1 (FR-050)

> Numbered T124–T139, continuing the sequence. FR-050 was added via `/speckit-clarify` and worked into contracts/dni-api.md, data-model.md and research.md §15 by `/speckit-plan` after User Story 1 was otherwise complete — none of this subsection's tasks are implemented yet, unlike every `[X]` task above. Per-website opt-in (`multi_pool_enabled`, default off); a website that never enables it is unaffected by any task here. T139 and the T133 validation clause were added by a `/speckit-analyze` pass that found FR-050's first draft silent on two things: pool-default-number uniqueness and cross-website `matched_pool_ids` scoping — both now closed in spec.md, data-model.md and contracts/dni-api.md before any of this subsection was implemented.

#### Tests for Multi-pool DNI Matching

- [X] T124 [P] [US1] Unit tests for allocating one Tracking Number per requested `pool_id` (atomic per pool, no double-allocation within a pool, FR-003 applied per pool) in `tests/Attribution.UnitTests/Allocation/MultiPoolAllocationTests.cs`
- [X] T125 [P] [US1] Unit tests for a Session holding multiple concurrent Allocations — distinct-`pool_id_at_allocation` invariant, and a later page view's newly-matched pool being added to an existing session's allocation set rather than starting a new session (research.md §15) — in `tests/Attribution.UnitTests/Sessions/MultiPoolSessionTests.cs`
- [X] T126 [P] [US1] Integration test for `POST /v1/dni/allocate`'s multi-pool response shapes — pre-consent/pre-match `pools` map, `matched_pool_ids` → `allocations`, per-pool exhaustion falling back to that pool's own `default_number` while other matched pools allocate normally, cross-website pool-id scoping, and session growth via a resumed `session_id` — against the project's shared MySQL database (TestSupport.TestDatabase, matching every other integration test already in this suite, not a Testcontainer) in `tests/Attribution.IntegrationTests/Dni/MultiPoolAllocateEndpointTests.cs`
- [X] T127 [P] [US1] Integration test for `POST /v1/dni/heartbeat`'s batched multi-allocation response — one call keeps every active allocation alive, response reports validity and number per allocation — in `tests/Attribution.IntegrationTests/Dni/MultiPoolHeartbeatEndpointTests.cs`
- [X] T128 [P] [US1] Extend the contract test suite to cover the multi-pool `dni-api.md` shapes, including asserting a `multi_pool_enabled = false` website's `/allocate` and `/heartbeat` responses stay byte-for-byte identical to today's, in `tests/Attribution.Contract/DniApiContractTests.cs`
- [X] T129 [P] [US1] Playwright test: three static numbers on one page, each belonging to a different pool, each independently replaced with its own allocated number from one page load, using a new `client/dni-script/tests/fixtures/multi-pool.html` fixture (quickstart.md §2a steps 2–5) in `client/dni-script/tests/multi-pool-matching.spec.js` (`.js`, matching every other Playwright spec in this suite — the project has no TypeScript setup)
- [X] T130 [P] [US1] Playwright test: navigating to a second page matching a pool not present on the first keeps the session's existing allocations and gains one more for the newly-matched pool, rather than starting a new session (quickstart.md §2a step 7) in `client/dni-script/tests/multi-pool-session-growth.spec.js`, using a new `client/dni-script/tests/fixtures/multi-pool-growth-start.html` lead-in fixture (same `website_id` as `multi-pool.html`, showing only one of its three pools) so the second page-load's growth is observable

#### Implementation for Multi-pool DNI Matching

- [X] T131 [US1] Add `multi_pool_enabled` to the Website domain entity and its migration, and a per-website toggle endpoint in `src/Attribution.Domain/Websites/Website.cs`, `src/Attribution.Infrastructure/Data/Migrations/`, `src/Attribution.Api/Controllers/AdminWebsitesController.cs` per FR-050, data-model.md (depends on T012, T116)
- [X] T139 [US1] Extend the pool create/update validation in `AdminPoolsController` (built at T037) to reject a `default_number` that collides, digit-normalized, with another pool's `default_number` scoped to the same website whenever that website has `multi_pool_enabled` in `src/Attribution.Api/Controllers/AdminPoolsController.cs` per data-model.md's Number Pool validation, FR-050 (depends on T037, T131)
- [X] T132 [US1] Extend the Allocation domain entity and repository to support multiple concurrently active rows per `session_id` (distinct-`pool_id_at_allocation` invariant, data-model.md) in `src/Attribution.Domain/Sessions/Allocation.cs`, `src/Attribution.Infrastructure/Data/AllocationRepository.cs` (depends on T034, T131)
- [X] T133 [US1] Extend `AllocationService` with the pool→number map lookup and matched-pool-ids allocation (one Tracking Number per requested pool, per-pool exhaustion fallback to that pool's own `default_number`), first filtering `matched_pool_ids` down to pools actually scoped to the request's `website_id` and silently dropping the rest (research.md §15's cross-website pool validation) in `src/Attribution.Application/Allocation/AllocationService.cs` per research.md §15 (depends on T035, T132)
- [X] T134 [US1] Implement session-growth handling: a page view whose matched pools include one the session doesn't yet hold allocates only the new pool(s) and adds them to the existing session, rather than reallocating or starting a new session, in `src/Attribution.Application/Allocation/AllocationService.cs` (depends on T133)
- [X] T135 [US1] Extend `POST /v1/dni/allocate` and `/heartbeat` in `src/Attribution.Api/Controllers/DniController.cs` for the multi-pool response shapes per contracts/dni-api.md, preserving byte-for-byte unchanged behavior for a `multi_pool_enabled = false` website (depends on T036, T133)
- [X] T136 [P] [US1] Implement the DNI client's pool-map fetch and local per-pool digit-normalized matching, reusing `replace.js`'s existing matching logic against each pool's `default_number` in turn, in `client/dni-script/src/allocation.js`, `client/dni-script/src/replace.js` per FR-050 (depends on T038, T039)
- [X] T137 [US1] Implement the DNI client's matched-pool-ids allocate call, per-pool DOM replacement, per-pool default-number fallback on exhaustion, and handling of the heartbeat's batched `allocations` array in `client/dni-script/src/index.js`, `client/dni-script/src/allocation.js` (depends on T136)
- [X] T138 [P] [US1] Create the multi-pool Playwright fixture — three locations, three distinct default numbers, one page — in `client/dni-script/tests/fixtures/multi-pool.html` per quickstart.md §2a (feeds T129 directly; T130 also needed `multi-pool-growth-start.html`, a same-`website_id` lead-in page showing only one of the three, to make session growth on navigating into `multi-pool.html` observable)

**Checkpoint**: User Story 1 is fully functional and independently testable.

---

## Phase 4: User Story 2 - An inbound call is deterministically attributed to the session that generated it (Priority: P2)

**Goal**: Poll 8x8 Call Detail Records and Call Legs on schedule, and attribute each inbound call to the session that held the dialled number using only exact DID + time-window matching — never guessing.

**Independent Test**: Seed known allocation records, feed a set of call records containing matched, unmatched and deliberately conflicting cases, then confirm each call lands in the expected state with retrievable evidence, and confirm that re-feeding the identical call records changes nothing.

### Tests for User Story 2

- [X] T042 [P] [US2] Unit tests for strict DID + window attribution matching (attributed/unattributed/ambiguous) in `tests/Attribution.UnitTests/Attribution/AttributionServiceTests.cs`
- [X] T043 [P] [US2] Unit tests for idempotent CDR/Call Leg upsert and checkpoint advancement (FR-017) in `tests/Attribution.UnitTests/Ingestion/IngestionTests.cs`
- [X] T044 [P] [US2] Unit tests for FR-045 re-derivation on restated or in-progress call records (rule-version-at-call-time preserved, superseded history retained) in `tests/Attribution.UnitTests/Attribution/ReDerivationTests.cs`
- [X] T045 [P] [US2] Integration test covering SC-001's full seeded-call scenario set (in-window, expired-but-in-extension, closed-window, never-allocated, suspended-number, DST transition, midnight-crossing) against a MySQL Testcontainer in `tests/Attribution.IntegrationTests/Attribution/SeededCallAttributionTests.cs`
- [X] T046 [P] [US2] Integration test: re-ingesting an identical batch three times produces zero change in any report total (SC-002) in `tests/Attribution.IntegrationTests/Ingestion/IdempotentReingestionTests.cs`

### Implementation for User Story 2

- [X] T047 [P] [US2] Implement Call and Call Leg domain entities and repositories (idempotent upsert on source natural keys, orphaned-leg handling) in `src/Attribution.Domain/Calls/`, `src/Attribution.Infrastructure/Data/` per FR-017 (depends on T011)
- [X] T048 [P] [US2] Implement the Attribution domain entity and repository (attributed/unattributed/ambiguous state machine, superseded-history rows) in `src/Attribution.Domain/Calls/Attribution.cs`, `src/Attribution.Infrastructure/Data/AttributionRepository.cs` (depends on T011)
- [X] T049 [P] [US2] Implement the Ingestion Checkpoint domain entity and repository in `src/Attribution.Domain/Calls/IngestionCheckpoint.cs`, `src/Attribution.Infrastructure/Data/` (depends on T011)
- [X] T050 [US2] Implement the Analytics for 8x8 Work client (authentication, CDR/Call Leg polling) in `src/Attribution.Infrastructure/Ingestion8x8/` per research.md §8 (depends on T049)
- [X] T051 [US2] Implement `AttributionService` (exact DID + window matching, unattributed/ambiguous classification, evidence storage per FR-019) in `src/Attribution.Application/Attribution/AttributionService.cs` per FR-018–FR-021 (depends on T048)
- [X] T052 [US2] Implement Review Case creation on ambiguous attribution in `src/Attribution.Application/Attribution/AttributionService.cs`, `src/Attribution.Domain/Audit/ReviewCase.cs` per FR-021, FR-036 (depends on T051)
- [X] T053 [US2] Implement re-derivation on restated/in-progress calls (FR-045: update in place, re-derive against rule version at call time, retain superseded history, idempotent on unchanged records) in `src/Attribution.Application/Attribution/ReDerivationService.cs` (depends on T051)
- [X] T054 [US2] Implement the `IngestionWorker` loop (poll on FR-016's configurable cadence, idempotent upsert per FR-017, checkpoint advance, orphaned-Call-Leg handling) in `src/Attribution.Workers/IngestionWorker/` per research.md §3, §8 (depends on T050, T051, T053)
- [X] T055 [P] [US2] Implement the replay/backfill command (operator-specified period, no duplicate records/attributions, safe alongside live ingestion) in `src/Attribution.Application/Attribution/BackfillService.cs` per FR-042 (depends on T054)

**Checkpoint**: User Stories 1 and 2 both work independently.

---

## Phase 5: User Story 3 - Attributed calls are qualified into marketing conversions (Priority: P3)

**Goal**: Evaluate every attributed call against the qualification rule in force (default, or the most specific website/campaign override) to decide whether it's a marketing conversion, with every rule version and its scope permanently recorded against the calls it judged.

**Independent Test**: With the default rule active, feed attributed calls just under and just over the 60-second threshold plus unanswered calls, confirm correct qualification; then publish a new rule version, confirm new calls use it and previously judged calls retain their original result and rule version.

### Tests for User Story 3

- [X] T056 [P] [US3] Unit tests for default-rule evaluation at the 60-second boundary (45s not qualified, 75s qualified) in `tests/Attribution.UnitTests/Qualification/QualificationServiceTests.cs`
- [X] T057 [P] [US3] Unit tests for rule versioning, most-specific-scope resolution, and effective-period contiguity validation (reject gap/overlap, FR-024) in `tests/Attribution.UnitTests/Qualification/RuleVersioningTests.cs`
- [X] T058 [P] [US3] Unit tests for the time-of-day condition evaluated in the website's local timezone, not the canonical storage timezone, in `tests/Attribution.UnitTests/Qualification/TimeOfDayConditionTests.cs`
- [X] T059 [P] [US3] Integration test: publishing a new rule version leaves previously-judged calls' results and rule-version references unchanged (SC-011) in `tests/Attribution.IntegrationTests/Qualification/RuleChangeHistoryTests.cs`

### Implementation for User Story 3

- [X] T060 [P] [US3] Implement Qualification Rule and Qualification Result domain entities and repositories in `src/Attribution.Domain/Qualification/`, `src/Attribution.Infrastructure/Data/` (depends on T011)
- [X] T061 [US3] Implement the rule condition evaluator (direction, answered, duration, website/campaign scope, website-local-timezone time-of-day) in `src/Attribution.Domain/Qualification/RuleEvaluator.cs` per FR-022, FR-023 (depends on T060)
- [X] T062 [US3] Implement rule-version effective-period contiguity validation (reject any configuration creating a gap or overlap) in `src/Attribution.Application/Qualification/RuleVersioningService.cs` per FR-024 (depends on T060)
- [X] T063 [US3] Implement `QualificationService` (most-specific-scope resolution, judge attributed calls, record rule version and scope applied) in `src/Attribution.Application/Qualification/QualificationService.cs` per FR-022–FR-024 (depends on T061, T062)
- [X] T064 [US3] Wire qualification into the ingestion re-derivation pipeline in `src/Attribution.Application/Attribution/ReDerivationService.cs` (depends on T053, T063)
- [X] T065 [US3] Implement qualification-rule management endpoints (create/list/delete-future-version-only) in `src/Attribution.Api/Controllers/AdminQualificationRulesController.cs` per contracts/admin-api.md, FR-033 (depends on T062)

**Checkpoint**: User Stories 1, 2 and 3 all work independently.

---

## Phase 6: User Story 4 - Marketing reports on call performance and exports the data (Priority: P4)

**Goal**: Serve executive-dashboard, campaign-performance, call-detail-search, missed/qualified/unattributed report data (and matching CSV exports) that reconcile exactly against underlying call records, filtered to what the requesting role permits.

**Independent Test**: With a known dataset loaded, sign in as each reporting role, confirm every report renders the expected figures that reconcile against the underlying call records, confirm an Analyst cannot reach administrative functions, and confirm each report exports a CSV whose contents match what was displayed.

### Tests for User Story 4

- [X] T066 [P] [US4] Integration test: dashboard/campaign/call-detail/missed/qualified/unattributed report totals reconcile against underlying call records in `tests/Attribution.IntegrationTests/Reporting/ReportReconciliationTests.cs`
- [X] T067 [P] [US4] Integration test: CSV export contains the same rows, values, filters and period as the report it was generated from (FR-030) in `tests/Attribution.IntegrationTests/Reporting/CsvExportTests.cs`
- [X] T068 [P] [US4] Integration test: an Analyst-only user is refused on number pool/rule/user management, and the attempt is recorded (FR-031, FR-038) in `tests/Attribution.IntegrationTests/Reporting/RoleRestrictionTests.cs`
- [X] T069 [P] [US4] Integration test: the FR-048 coverage breakdown reconciles exactly with underlying call records (SC-018 evidence) in `tests/Attribution.IntegrationTests/Reporting/CoverageBreakdownTests.cs`

### Implementation for User Story 4

- [X] T070 [US4] Implement `ReportingService` (dashboard, campaign performance, call detail search, missed, qualified, unattributed, FR-048 coverage) query layer in `src/Attribution.Application/Administration/ReportingService.cs` per FR-029, FR-048 (depends on T048, T063)
- [X] T071 [US4] Implement `GET /v1/reports/*` endpoints in `src/Attribution.Api/Controllers/ReportsController.cs` per contracts/reporting-api.md (depends on T070)
- [X] T072 [US4] Implement `GET /v1/reports/*/export.csv` endpoints reusing the report query results (FR-030) in `src/Attribution.Api/Controllers/ReportsController.cs` (depends on T071)
- [X] T073 [US4] Implement role-based report/export filtering (FR-031) in `src/Attribution.Api/Middleware/` or `ReportingService` (depends on T070, T016)
- [X] T118 [US4] Flag shadow-derived attributions and report shadow-mode ambiguity separately from ordinary ambiguity per FR-049 in `src/Attribution.Application/Administration/ReportingService.cs` (depends on T070, T115)

**Checkpoint**: User Stories 1–4 all work independently.

---

## Phase 7: User Story 5 - Qualified calls are published to Google Ads and GA4 (Priority: P5)

**Goal**: Publish every qualified call to Google Ads (offline conversion) and GA4 (Measurement Protocol) exactly once, skip with a recorded reason where the needed identifier is missing, and propagate later corrections as far as each destination permits.

**Independent Test**: Qualify a set of calls where some originating sessions carry a Google click identifier and some do not, then confirm the former are published to both destinations exactly once, the latter are handled per the documented rule without error, and forced failures are retried without producing duplicates.

### Tests for User Story 5

- [X] T074 [P] [US5] Unit tests for idempotency-key generation, scoped per publish episode (a genuine retract-then-requalify gets a new key, FR-027) in `tests/Attribution.UnitTests/Publication/IdempotencyKeyTests.cs`
- [X] T075 [P] [US5] Unit tests for GCLID-missing (Google Ads) and GA4-client-id-missing (GA4) skip-with-reason handling in `tests/Attribution.UnitTests/Publication/SkipReasonTests.cs`
- [X] T076 [P] [US5] Integration test: a qualified call is published exactly once to both destinations, and retries/reprocessing produce no duplicate (SC-002, FR-027) in `tests/Attribution.IntegrationTests/Publication/PublicationIdempotencyTests.cs`
- [X] T077 [P] [US5] Integration test: FR-044 correction propagation — Google Ads retract/adjust succeeds, GA4 is recorded as unpropagatable — both audited in `tests/Attribution.IntegrationTests/Publication/CorrectionPropagationTests.cs`

### Implementation for User Story 5

- [X] T078 [P] [US5] Implement the Conversion Publication domain entity and repository in `src/Attribution.Domain/Publication/ConversionPublication.cs`, `src/Attribution.Infrastructure/Data/` (depends on T011)
- [X] T079 [P] [US5] Implement the Google Ads offline-conversions client (upload, retract, adjust) in `src/Attribution.Infrastructure/GoogleAds/` per research.md §6, FR-025
- [X] T080 [P] [US5] Implement the GA4 Measurement Protocol client in `src/Attribution.Infrastructure/GA4/` per research.md §7
- [X] T081 [US5] Implement `PublicationService` (write the outbox row in the same transaction as the qualification decision, per-episode idempotency key) in `src/Attribution.Application/Publication/PublicationService.cs` per FR-025–FR-028, research.md §3 (depends on T078)
- [X] T082 [US5] Implement the `PublicationWorker` loop (drain the outbox, retry with backoff, record every attempt's outcome) in `src/Attribution.Workers/PublicationWorker/` per FR-027, FR-028 (depends on T079, T080, T081)
- [X] T083 [US5] Implement correction propagation (FR-044: Google Ads retraction/adjustment, GA4 unpropagatable recording, idempotent repeated correction) in `src/Attribution.Application/Publication/CorrectionService.cs` (depends on T081)
- [X] T084 [US5] Wire qualification-change triggers (manual review resolution, FR-045 re-derivation) to `CorrectionService` in `src/Attribution.Application/Qualification/QualificationService.cs`, `ReDerivationService.cs` (depends on T063, T083) — the FR-045 re-derivation trigger; the manual-review-resolution trigger lands with T096 (US6), which already depends on T083 for exactly this reason

**Checkpoint**: User Stories 1–5 all work independently.

---

## Phase 8: User Story 6 - Administrators configure the platform and everything they do is auditable (Priority: P6)

**Goal**: Give administrators user/pool/rule management, integration health visibility, a manual review workflow, and threshold-based alerting — with every administrative action captured in an immutable audit log.

**Independent Test**: Perform one action of every administrative type, confirm each appears in the audit log with actor, action, target, before and after values and timestamp, confirm the log cannot be edited or deleted, and confirm resolving a manual review case updates the call and is itself audited.

### Tests for User Story 6

- [X] T085 [P] [US6] Integration test: every administrative action type appears in the audit log with actor/target/before/after (SC-006) in `tests/Attribution.IntegrationTests/Administration/AuditLogTests.cs`
- [X] T086 [P] [US6] Integration test: audit entry modification/deletion attempts are refused and the attempt is itself recorded (FR-035) in `tests/Attribution.IntegrationTests/Administration/AuditImmutabilityTests.cs`
- [X] T087 [P] [US6] Integration test: an unhealthy ingestion/publication/pool condition raises an alert that is delivered, repeats without duplicating, and clears (FR-034, FR-047, SC-017), including a review case aged past 48 hours in `tests/Attribution.IntegrationTests/Administration/AlertingTests.cs`
- [X] T088 [P] [US6] Integration test: resolving a manual review case updates attribution evidence, produces no duplicate conversion, and is audited (FR-036) in `tests/Attribution.IntegrationTests/Administration/ReviewResolutionTests.cs`
- [X] T089 [P] [US6] Integration test: the Integration Service role is refused interactive sign-in while system-to-system access still works (FR-038) in `tests/Attribution.IntegrationTests/Administration/IntegrationServiceAccessTests.cs`
- [X] T090 [P] [US6] Integration test: a deactivated user's next refresh-token exchange is refused within the 5-minute refresh interval (SC-016), and a local account can sign in with username/password/TOTP (FR-046) in `tests/Attribution.IntegrationTests/Administration/AccountAccessTests.cs` — superseded 2026-08-17: was FederationRevocationTests.cs testing IdP revocation + break-glass; see T140–T143

### Implementation for User Story 6

- [X] T091 [P] [US6] Implement the Alert domain entity and repository (one open alert per condition invariant, FR-047) in `src/Attribution.Domain/Audit/Alert.cs`, `src/Attribution.Infrastructure/Data/` (depends on T011)
- [X] T092 [US6] Implement `AlertingService` (threshold evaluation: ingestion lag, publication failure rate, allocation failure rate, pool utilisation, review-case age) in `src/Attribution.Application/Administration/AlertingService.cs` per FR-047 (depends on T091)
- [X] T093 [US6] Implement the `AlertingWorker` loop plus email/webhook delivery and delivery-failure surfacing in `src/Attribution.Workers/AlertingWorker/` per contracts/alert-webhook.md, FR-047 (depends on T092)
- [X] T094 [US6] Implement user/role management and role-override endpoints in `src/Attribution.Api/Controllers/AdminUsersController.cs` per contracts/admin-api.md, FR-032, FR-046 (depends on T013) — extended 2026-08-17 by T141 (deactivate endpoint, last-System-Administrator guard)
- [X] T095 [US6] Implement integration-health endpoints (ingestion, publication, pool utilisation) in `src/Attribution.Api/Controllers/AdminHealthController.cs` per contracts/admin-api.md, FR-034
- [X] T096 [US6] Implement the manual-review-case resolution endpoint (update attribution, propagate any already-published correction, audit) in `src/Attribution.Api/Controllers/AdminReviewController.cs` per contracts/admin-api.md, FR-036 (depends on T052, T083)
- [X] T097 [US6] Implement the alert-acknowledgement endpoint in `src/Attribution.Api/Controllers/AdminAlertsController.cs` per contracts/admin-api.md, FR-047 (depends on T091)
- [X] T098 [US6] Implement the read-only audit-log query endpoint in `src/Attribution.Api/Controllers/AdminAuditController.cs` per contracts/admin-api.md, FR-035 (depends on T017)
- [X] T099 [US6] Enforce Integration Service system-to-system-only access (deny interactive sign-in) in `src/Attribution.Api/Middleware/` per FR-038 (depends on T016)

### Sign-in rework: local-only authentication + server-side folder import (2026-08-17)

> Numbered T140–T146, continuing the sequence. Superseded via `/speckit-clarify`: federated SSO (2026-08-09 decision, T014/T015/T090/T094) is dropped entirely — it was never exercised in this codebase (no HTTP endpoint for federated sign-in was ever built, per data-model.md's User/Role note) — in favor of local username/password + mandatory TOTP MFA as the platform's sole interactive sign-in method, for as many accounts as there are users rather than a capped break-glass pair. A rotating refresh token (T140) replaces the federated "silent refresh against the still-active IdP browser session" mechanism that had no local-account equivalent. T144–T146 add FR-051's server-side folder CSV import, a new capability threaded through spec.md, research.md §16, data-model.md and contracts/admin-api.md by the same `/speckit-plan` pass.

- [X] T140 Add refresh-token issuance, rotation and validation (opaque token, stored server-side as a hash with an expiry) to `ITokenIssuer`/`JwtTokenIssuer` and a new `POST /v1/auth/refresh` endpoint in `src/Attribution.Infrastructure/Identity/`, `src/Attribution.Api/Controllers/AuthController.cs` per research.md §5, FR-046 (depends on T014)
- [X] T141 Remove `IdentityType.Federated` and the unused `GroupRoleMapper`; rename the break-glass-only authentication path to the platform's sole local sign-in path (`BreakGlassAuthenticator` → `LocalAuthenticator`, `POST /v1/auth/break-glass/sign-in` → `POST /v1/auth/sign-in`, `User.CreateBreakGlass` → `User.CreateLocal`) in `src/Attribution.Domain/Identity/`, `src/Attribution.Infrastructure/Identity/`, `src/Attribution.Api/Controllers/AuthController.cs` per FR-046 (depends on T015)
- [X] T142 Add a deactivate-user endpoint and a guard rejecting any action that would leave zero active System Administrator accounts in `src/Attribution.Api/Controllers/AdminUsersController.cs` per contracts/admin-api.md, FR-046 (depends on T094, T141)
- [X] T143 Update `FederationRevocationTests.cs` → `AccountAccessTests.cs` and `UserRoleTests.cs` to cover local sign-in, refresh-token rotation, deactivation-refuses-refresh, and the last-System-Administrator guard, replacing every federation/break-glass-specific scenario in `tests/Attribution.IntegrationTests/Administration/`, `tests/Attribution.UnitTests/Identity/` per SC-016, FR-046 (depends on T140, T142)
- [X] T144 Add the configured server-side import-folder path and a shared CSV row-validation helper factored out of the existing multipart-upload logic in `src/Attribution.Api/Controllers/AdminPoolsController.cs`, `src/Attribution.Api/appsettings.json` per research.md §16, FR-051 (depends on T037)
- [X] T145 Implement `GET /v1/admin/numbers/import-folder/files` and `POST /v1/admin/pools/{id}/numbers/import-from-folder` (path-traversal-safe file-name resolution, identical per-row accept/reject reporting, audited with the imported file name) in `src/Attribution.Api/Controllers/AdminPoolsController.cs` per contracts/admin-api.md, FR-051 (depends on T144)
- [X] T146 [P] Integration test: the import-folder file listing and folder-triggered import produce identical per-row results to the multipart-upload path, re-triggering an already-imported file rejects its numbers as duplicates rather than re-adding them, and a path-traversal file name is rejected (FR-051) in `tests/Attribution.IntegrationTests/Administration/FolderImportTests.cs` (depends on T145)

### Admin list endpoints for the separate reporting/admin UI (2026-08-17)

> Numbered T147–T149, continuing the sequence. Discovered while starting the separate admin+reporting UI codebase (attribution-ui): the admin API had fetch-by-id and per-action endpoints for pools, websites and tracking numbers, but no way to list any of them — an admin UI could not show a Pools or Websites screen without every id already known. Additive only; no existing endpoint's shape changed.

- [X] T147 [US6] Add `INumberPoolRepository.GetAllAsync`/`IWebsiteRepository.GetAllAsync` and `GET /v1/admin/pools` (list, reusing `GetPool`'s per-pool summary shape) and `GET /v1/admin/websites` (list) in `src/Attribution.Domain/Pools/INumberPoolRepository.cs`, `src/Attribution.Domain/Websites/IWebsiteRepository.cs`, `src/Attribution.Infrastructure/Data/NumberPoolRepository.cs`, `src/Attribution.Infrastructure/Data/WebsiteRepository.cs`, `src/Attribution.Api/Controllers/AdminPoolsController.cs`, `src/Attribution.Api/Controllers/AdminWebsitesController.cs` per contracts/admin-api.md
- [X] T148 [US6] Add `GET /v1/admin/pools/{id}/numbers`, listing a pool's individual Tracking Numbers (id, did, status, status_changed_at, last_released_at) via the already-existing `ITrackingNumberRepository.GetByPoolAsync` in `src/Attribution.Api/Controllers/AdminPoolsController.cs` per contracts/admin-api.md
- [X] T149 [P] [US6] Integration test: `GET /v1/admin/pools` includes a just-created pool, `GET /v1/admin/pools/{id}/numbers` returns the numbers a CSV import just added, `GET /v1/admin/websites` returns a seeded website's `shadowModeEnabled`/`multiPoolEnabled` flags in `tests/Attribution.IntegrationTests/Administration/AdminListEndpointsTests.cs` (depends on T147, T148)
- [X] T150 [US6] Add a configurable `AdminUi` CORS policy (`Cors:AdminUiOrigins`, GET/POST/DELETE/OPTIONS) and apply it via `[EnableCors("AdminUi")]` to every `/v1/auth/*`, `/v1/reports/*` and `/v1/admin/*` controller, distinct from the existing origin-open `DniClient` policy (which stays scoped to the unauthenticated `/v1/dni/*` endpoints only) — the attribution-ui repo genuinely runs on a different origin from this API in any real deployment, unlike the DNI script which is embedded on an arbitrary customer site, in `src/Attribution.Api/Program.cs`, every `src/Attribution.Api/Controllers/*Controller.cs` except `DniController.cs`, `src/Attribution.Api/appsettings.json`, `src/Attribution.Api/appsettings.Development.json` — verified end to end against a live `attribution-ui` dev server (CORS preflight, sign-in, and every list/report endpoint's JSON shape checked by hand against the UI's TypeScript types)
- [X] T151 [US6] Add a `dotnet Attribution.Api.dll seed-admin <username> <password>` bootstrap command (exits before starting Kestrel), provisioning one System Administrator local account through the same `IUserRepository`/`LocalAuthenticator` path `POST /v1/admin/users` uses — discovered while writing the deployment pack: a brand-new deployment has no account to sign in with and therefore no way to reach that endpoint at all, in `src/Attribution.Api/Program.cs` per FR-046, spec.md Assumptions (depends on T140, T141)
- [X] T152 Add a `dotnet Attribution.Api.dll migrate` command (applies pending FluentMigrator migrations via the existing `MigrationRunner`, then exits before starting Kestrel) as the explicit, re-runnable deploy-time counterpart to the existing `Migrations:RunOnStartup` convenience toggle (off by default outside Development) — discovered while writing the deployment pack: a real deployment needs a step it can invoke once before the very first start and again before each upgrade that ships a new migration, rather than relying on an implicit and usually-disabled startup check, in `src/Attribution.Api/Program.cs` per research.md §13, spec.md Assumptions (depends on T009)
- [X] T153 Register `Attribution.Api` and `Attribution.Workers` for Windows Service hosting via `AddWindowsService()` (`Microsoft.Extensions.Hosting.WindowsServices`, pinned to 8.0.1 to avoid a package-downgrade conflict with the existing `Microsoft.Extensions.Hosting` 8.0.1 reference) — a no-op everywhere except when actually started by the Windows Service Control Manager, so `net stop`/`net start` and Windows' own crash recovery work correctly. Written for the Windows Server manual-install deployment pack's `sc.exe create "Attribution Workers" ...`, where Attribution.Workers runs as a genuine service with no console; harmless (and not relied upon, since IIS manages its lifecycle instead) for the Api, in `src/Attribution.Api/Program.cs`, `src/Attribution.Workers/Program.cs`, `src/Attribution.Api/Attribution.Api.csproj`, `src/Attribution.Workers/Attribution.Workers.csproj` per spec.md Assumptions (depends on T004)

**Checkpoint**: All six user stories are independently functional.

---

## Phase 9: Polish & Cross-Cutting Concerns

**Purpose**: Retention/erasure, documentation, and platform-wide hardening that spans every user story.

- [X] T122 [P] Integration test: run the retention purge against a seeded historical dataset spanning the 14/25-month thresholds, confirming identifiers are gone and report totals for that period reconcile identically before/after (SC-014) in `tests/Attribution.IntegrationTests/Retention/RetentionIntegrityTests.cs`
- [X] T100 [P] Implement the `RetentionWorker` loop (14/25-month tiered de-identification via a stable HMAC surrogate, 7-year audit retention, open-review-case exception) in `src/Attribution.Workers/RetentionWorker/` per FR-040, research.md §10 (depends on T122)
- [X] T123 [P] Integration test: submit a seeded erasure request and confirm completion, with the visitor's data gone, within a simulated 30-day window (SC-019) in `tests/Attribution.IntegrationTests/Retention/ErasureSlaTests.cs`
- [X] T101 [P] Implement the data-subject erasure request endpoint, completing within the 30-day SC-019 bar in `src/Attribution.Api/Controllers/AdminPrivacyController.cs` per FR-039, SC-019 (depends on T123)
- [X] T102 [P] Generate and publish OpenAPI documentation for every versioned endpoint per Constitution Principle III
- [X] T103 [P] Propagate a correlation ID through structured logs end-to-end (allocation → attribution → qualification → publication) per FR-041
- [X] T104 Run every quickstart.md validation scenario end-to-end against a seeded environment
- [X] T105 [P] Security hardening pass: TLS enforcement, secret-scanning, dependency audit per Constitution Principle VI
- [X] T106 [P] Performance/load test: DNI allocation at SC-004's peak (≈7 allocations/min + ≈50 heartbeats/min, 300ms p95) in `tests/Attribution.IntegrationTests/Performance/AllocationLoadTests.cs`
- [X] T119 [P] Document and provision a multi-instance deployment topology (N API + N worker instances behind a load balancer, health-check-based instance removal) in `docker-compose.yml` / deployment docs per FR-043
- [X] T120 Integration test: rerun SC-004's load test against 2+ concurrently running API instances with no shared in-process state, confirming identical latency/correctness in `tests/Attribution.IntegrationTests/Performance/HorizontalScaleTest.cs` per FR-043, SC-005 (depends on T119, T106)
- [X] T121 Failover test: terminate one API instance mid-load and confirm zero failed allocation requests per SC-005 (depends on T119)
- [X] T107 Reconcile plan.md/data-model.md/contracts/ against any drift discovered during implementation

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — start immediately.
- **Foundational (Phase 2)**: Depends on Setup completion — BLOCKS all user stories.
- **User Stories (Phase 3–8)**: All depend on Foundational completion.
  - US1 (Phase 3) has no dependency on other stories.
  - US2 (Phase 4) depends only on Foundational — does not require US1's endpoints, though it reuses US1's Allocation table as read-only matching input once both exist.
  - US3 (Phase 5) depends on US2's Attribution existing to have something to qualify (T063 reads Attribution rows) — build US2 first if working sequentially.
  - US4 (Phase 6) depends on US2 and US3 for data to report on (T070 reads Attribution and Qualification Result).
  - US5 (Phase 7) depends on US3 for qualified calls to publish (T081 reads Qualification Result).
  - US6 (Phase 8) depends on US2 (Review Case creation, T052) and US5 (correction propagation, T083) for its resolution endpoint (T096).
- **Polish (Phase 9)**: Depends on all desired user stories being complete.

### Within Each User Story

- Tests are written first and MUST fail before implementation begins (Constitution Principle V).
- Domain entities/repositories before Application services.
- Application services before Api controllers and Worker loops.
- Story checkpoint reached only once its independent test (spec.md) passes.

### Parallel Opportunities

- All Setup tasks marked [P] can run in parallel.
- All Foundational tasks marked [P] can run in parallel once their listed dependency is met.
- Once Foundational completes, US1 and US2 can be staffed in parallel (US2 doesn't require US1's endpoints, only the Foundational schema). US3–US6 follow the dependency chain above.
- All tests marked [P] within a story can run in parallel.
- DNI client tasks (`client/dni-script/`) and backend tasks never touch the same file, so they can always run in parallel with each other.

---

## Parallel Example: User Story 1

```bash
# Launch all tests for User Story 1 together:
Task: "Unit tests for atomic number allocation in tests/Attribution.UnitTests/Allocation/AllocationServiceTests.cs"
Task: "Unit tests for session timeout/heartbeat expiry in tests/Attribution.UnitTests/Sessions/SessionServiceTests.cs"
Task: "Integration test for POST /v1/dni/allocate in tests/Attribution.IntegrationTests/Dni/AllocateEndpointTests.cs"
Task: "Contract test against contracts/dni-api.md in tests/Attribution.Contract/DniApiContractTests.cs"
Task: "Playwright replacement test in client/dni-script/tests/replacement.spec.ts"
Task: "Playwright SPA replacement test in client/dni-script/tests/spa-replacement.spec.ts"
Task: "Playwright multi-tab test in client/dni-script/tests/multi-tab.spec.ts"
Task: "Playwright consent test in client/dni-script/tests/consent.spec.ts"
Task: "Playwright fallback test in client/dni-script/tests/fallback.spec.ts"

# Launch independent Domain models for User Story 1 together:
Task: "Number Pool + Tracking Number entities in src/Attribution.Domain/Pools/"
Task: "Visitor + Session entities in src/Attribution.Domain/Sessions/"
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup
2. Complete Phase 2: Foundational (CRITICAL — blocks all stories)
3. Complete Phase 3: User Story 1
4. **STOP and VALIDATE**: run User Story 1's Independent Test and the quickstart.md §2 scenarios
5. Deploy/demo if ready — this alone proves DNI delivery and session capture, independent of any call data

### Incremental Delivery

1. Setup + Foundational → foundation ready
2. US1 → validate independently → demo (MVP)
3. US2 → validate independently (SC-001 seeded calls) → demo
4. US3 → validate independently → demo
5. US4 → validate independently → demo (this is how SC-018's coverage evidence becomes visible)
6. US5 → validate independently → demo (closes the loop to Google Ads/GA4)
7. US6 → validate independently → demo (production-readiness: admin, audit, alerting)
8. Polish (Phase 9) → retention/erasure, hardening, load test, quickstart.md full run

### Parallel Team Strategy

With multiple developers, after Foundational completes:

- Developer A: US1 (DNI + session capture)
- Developer B: US2 (ingestion + attribution) — independent of US1's endpoints
- Once US2 lands: Developer A or C picks up US3 (qualification), then US4/US5 in parallel (both only need US3)
- US6 last, since T096 needs both US2's Review Case and US5's CorrectionService

---

## Notes

- [P] tasks touch different files with no dependency on an incomplete task.
- [Story] labels map every user-story-phase task to spec.md for traceability.
- Constitution Principle V requires tests to fail before their corresponding implementation task begins — do not skip the test tasks even though they're not separately called out as "TDD."
- Commit after each task or logical group, per this repository's git workflow.
- Stop at any checkpoint to validate a story independently before continuing.
- Avoid: vague tasks, same-file conflicts marked [P], cross-story dependencies that break independent testability beyond what's declared above.

---

# Migration to Apiche (2026-09-08)

Everything above this line documents the completed .NET/ASP.NET Core build (T001–T153, all `[X]`) and is retained as history — it is also the behavioral oracle the tasks below translate from (research.md §18). Nothing above is being redone; the platform's actual runtime behavior is not changing. What follows replaces *how* that behavior is delivered, per `plan.md`'s "Architecture Migration" addendum: the backend becomes Apiche (one HTTP endpoint = one parameterized SQL statement or `CALL sp_xxx(...)`), multi-step business logic moves into MySQL stored procedures, background workers become Apiche scheduled jobs, and authentication becomes Basic Auth throughout (research.md §17–§21; `spec.md`'s 2026-09-08 clarifications). `client/dni-script` and the reporting portal are unaffected and none of their tasks are repeated here.

**Source of truth for this section**: `specs/001-call-attribution-platform/apiche-config.md` — every stored procedure, endpoint and worker job named below is fully specified there (method, path, parameters, SQL/procedure body, Appendix A). Tasks below are the *build and cutover* work; they do not redesign anything `apiche-config.md` already decided.

**Numbering**: continues from T153. T208–T210 below are appended out of numeric sequence (added by the 2026-09-08 `/speckit-analyze` remediation pass, after T154–T207 already existed) but execute **first**, ahead of Phase 10 — renumbering the 54 already-cross-referenced tasks in Phases 10–18 to slot them in numerically was judged riskier than a documented out-of-sequence insertion.

## Phase 9.5: Pre-Migration Blocking Validation (2026-09-08 remediation)

**Purpose**: Close two gaps a `/speckit-analyze` pass found before any real migration work begins: an unconfirmed foundational security assumption (T208) and two missing pre-cutover verification gates (T209, T210).

**⚠️ CRITICAL**: T208 BLOCKS every task in Phases 10–18 that writes or registers an Apiche-invoked stored procedure — do not proceed past this task on an unconfirmed assumption.

- [ ] T208 *(scope narrowed 2026-09-08, same day — see research.md §22)* Confirm, from Apiche's actual vendor documentation (not inferred from its own description), whether `<parameter_name>` substitution uses genuine parameterized/prepared-statement binding or literal string interpolation into the SQL text. **This now only matters for the 4 DNI endpoints** (`sp_dni_allocate`/`heartbeat`/`consent`/`shadow_observe`) and the background jobs — Admin and Reporting no longer pass through Apiche's substitution mechanism at all, since they moved to direct database access (research.md §22), which removes 40 of the original 44 endpoints from this finding's blast radius. If the substitution mechanism is unconfirmed or confirmed unsafe, do not proceed with T166–T168, T176–T177 or the job configs in T180/T193/T200 until either Apiche is confirmed to escape/sanitize substituted values safely, or an input-sanitization layer is added to those specific endpoints/jobs. Record the finding in `research.md` §17 once resolved.
- [ ] T209 [P] Design and add an explicit website-identity binding check to every DNI stored procedure (`sp_dni_allocate`, `sp_dni_heartbeat`, `sp_dni_consent`, `sp_dni_shadow_observe`): the procedure must verify the Basic-Auth-authenticated Client ID/secret's own website matches the `website_id` parameter before proceeding, rejecting a mismatch the same way FR-050 already requires an out-of-scope `matched_pool_id` to be silently dropped — otherwise one website's valid DNI credential could operate against a different website's pools. Fold this into T166's procedure bodies rather than writing it separately (depends on T161)
- [ ] T210 [P] Add a load-test task re-running the equivalent of the retired stack's `AllocationLoadTests.cs`/`HorizontalScaleTest.cs` (T106/T120) against the migrated Apiche + MySQL-stored-procedure stack, proving SC-004 (300ms/95th-percentile allocation latency) and SC-005 (99.9% availability) hold under the new backend **before** T205's traffic cutover — not merely monitored after it — in `tests/Attribution.ContractTests/Performance/`
- [ ] T211 [P] Run a dedicated security review of the migration's new attack surface — T208's injection-safety finding (now DNI-scoped), T209's website-identity binding, the DNI/Integration-Service Basic Auth credential store (T161), and the native MySQL role/grant setup (T162): confirm `role_analyst` genuinely cannot execute a System-Administrator-only or Marketing-Administrator-only procedure, no privilege is ever granted to `PUBLIC`, and a deactivated database account is actually locked rather than merely flagged — as its own gate on SC-012 ("no outstanding high/critical finding at launch"), distinct from and in addition to T203's behavioral-parity comparison; schedule immediately before T205's cutover

**Checkpoint**: T208's answer is known and acted on; T209, T210 and T211 are scheduled into Phase 12 (T166), and Phase 18 before T205, respectively.

## Phase 10: Migration Setup

**Purpose**: Stand up the Apiche + MySQL toolchain alongside the still-running .NET system, so the migration can proceed without an outage.

- [ ] T154 Provision an Apiche instance pointed at the existing MySQL database (same connection the .NET app currently uses) and confirm it can load a trivial config entry end-to-end
- [ ] T155 [P] Create `db/schema/` and `db/procedures/` directories with a versioned-filename convention (e.g. `NNN_description.sql`) per research.md §13
- [ ] T156 [P] Create the `tests/Attribution.SqlTests` project — a MySqlConnector-based test harness that calls a named stored procedure directly against the shared remote MySQL database (per this project's standing test-database convention) and asserts on result sets and resulting table state, seeded/torn down with randomized identifiers per test (research.md §18)
- [ ] T157 [P] Create the `apiche/` directory (`endpoints/dni/`, `jobs/`, `auth/` — narrowed 2026-09-08, no `admin/`/`reports/` subdirectories, since those moved to direct database access) and `db/access-roles/` per plan.md's Target Project Structure
- [ ] T158 Add a CI stage that runs `Attribution.SqlTests` against the shared MySQL database and validates `apiche/` config loads cleanly, running alongside (not yet replacing) the existing dotnet build/test stage, so both pipelines stay green during migration

**Checkpoint**: Apiche and the SQL test harness exist and are wired into CI, with zero impact on the running .NET system.

---

## Phase 11: Migration Foundational (Blocking Prerequisites)

**Purpose**: Schema translation, the DNI/Integration-Service credential store, native database role setup for Admin/Reporting (2026-09-08, same day — replaces this phase's original actor-identity-injection concern, research.md §22), and a stored-procedure error convention.

**⚠️ CRITICAL**: No per-story migration work (Phase 12+) can begin until this phase is complete.

- [ ] T159 Translate the three existing FluentMigrator migrations (`M202608100001_InitialSchema`, `M202608170001_LocalAuthAndFolderImport`, `M202608170002_MultiPoolWebsites`) into equivalent versioned plain-SQL files in `db/schema/`, preserving identical table/column definitions (data-model.md is unchanged) — apply against a scratch copy of the shared database first and diff the resulting schema against the live one to confirm byte-for-byte equivalence
- [ ] T160 Define and document the stored-procedure error-signaling convention (`SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = '<code>: <detail>'`) that every procedure in Appendix A uses for a rejected/invalid request; for Apiche-invoked procedures (DNI, background jobs) confirm how Apiche surfaces a signaled error as an HTTP status + body, and for direct-access procedures (Admin, Reporting) confirm how the calling application surfaces it from the raw database driver exception
- [ ] T161 *(narrowed 2026-09-08, same day)* Configure Apiche's native Basic Auth credential store for the two credential shapes it still governs — per-website DNI Client ID/secret (no role) and Integration Service API key — per research.md §20 and `contracts/dni-api.md`. Interactive human credentials are no longer configured here at all (see T162).
- [ ] T162 *(redefined 2026-09-08, same day — replaces the original actor-identity-injection task, research.md §22)* Create the three native MySQL roles (`role_system_administrator`, `role_marketing_administrator`, `role_analyst`) in `db/access-roles/`, each granted `EXECUTE`/`SELECT` on exactly the procedures/views its Apiche-era Role column already specified (`apiche-config.md`'s existing per-endpoint assignments, reused unchanged as the permission mapping), and confirm the account-provisioning flow: `sp_create_user` creates a native database account and grants it the matching role, `sp_deactivate_user` disables the account, `sp_override_user_role` re-grants it. Audit actor identity is resolved via `CURRENT_USER()` inside each procedure — no injection mechanism is needed, which is what makes this task safe to build directly rather than escalate first (contrast with T208, which stays a genuine open blocker for the 4 DNI endpoints).
- [ ] T163 [P] Write `sp_create_user`, `sp_deactivate_user`, `sp_override_user_role` in `db/procedures/` per `apiche-config.md` Appendix A — each now performing the native `CREATE USER`/`GRANT`/`ALTER USER ... ACCOUNT LOCK`/`REVOKE`+`GRANT` operations from T162 in addition to the platform's own `users` row bookkeeping — using `UserRoleTests.cs` and the zero-System-Administrator-guard scenarios in `AccountAccessTests.cs` (`tests/Attribution.UnitTests`, `tests/Attribution.IntegrationTests`) as the behavioral oracle
- [ ] T164 [P] SQL-level tests for T163's three procedures in `tests/Attribution.SqlTests/Identity/` per research.md §18 (write before/alongside T163 per Constitution Principle V), including a check that the resulting native database account actually has (or, after deactivation, actually lacks) the expected role membership
- [ ] T165 [P] *(direct-access framing 2026-09-08, same day)* Grant `role_system_administrator` `EXECUTE` on T163's three procedures and `SELECT` on the users list view (`apiche-config.md`'s "Admin — Users" section: List, Create, Deactivate, Override role) — no Apiche endpoint registration; these are invoked directly by an authenticated `role_system_administrator` database session

**Checkpoint**: Schema exists in `db/schema/`; the three native database roles exist with correct grants; the DNI/Integration-Service Apiche credential store is configured; the pattern every later Admin/Reporting story follows (grant-based direct access) is established.

---

## Phase 12: US1 Migration — DNI allocation and tracking-number management (Priority: P1) 🎯 MVP

**Goal**: Every `/v1/dni/*` and pool/number/website-configuration endpoint in `apiche-config.md` is served by Apiche + stored procedures, byte-for-byte matching the behavior `AllocationServiceTests.cs`, `MultiPoolAllocationTests.cs` and `MultiPoolSessionTests.cs` already prove.

**Independent Test**: Re-run quickstart.md §2 and §2a against the Apiche-served endpoints instead of the .NET API, with no change to `client/dni-script` and no observable behavioral difference.

- [ ] T166 [P] [US1] Write `sp_dni_allocate` (including FR-050 multi-pool allocation, cross-website pool-id filtering, session-growth resume, and T209's website-identity binding check — the authenticated DNI credential's own website must match the `website_id` parameter or the request is rejected) in `db/procedures/` per `apiche-config.md`, using `AllocationServiceTests.cs` and `MultiPoolAllocationTests.cs`/`MultiPoolSessionTests.cs` as the oracle (depends on T209)
- [ ] T167 [P] [US1] Write `sp_dni_heartbeat` and `sp_dni_consent` (including T209's website-identity binding check, same as T166) in `db/procedures/` per `apiche-config.md`, using `SessionServiceTests.cs` and the consent-grant/withdrawal scenarios in `AllocationServiceTests.cs` as the oracle (depends on T209)
- [ ] T168 [P] [US1] Write `sp_dni_shadow_observe` (including T209's website-identity binding check) and `sp_set_website_shadow_mode`/`sp_set_website_multi_pool` in `db/procedures/` per `apiche-config.md`, using `ShadowModeTests.cs` as the oracle (depends on T209)
- [ ] T169 [P] [US1] SQL-level tests for T166–T168's five procedures in `tests/Attribution.SqlTests/Dni/` (depends on T166, T167, T168)
- [ ] T170 [US1] Register `/v1/dni/allocate`, `/v1/dni/heartbeat`, `/v1/dni/consent`, `/v1/dni/shadow-observe` in `apiche/endpoints/dni/`, with the per-website Basic Auth Client ID/secret plus origin-restriction and rate-limiting configured per `contracts/dni-api.md` (depends on T166, T167, T168, T161)
- [ ] T171 [US1] Point `client/dni-script`'s existing Playwright suite (`replacement.spec.ts`, `spa-replacement.spec.ts`, `multi-tab.spec.ts`, `consent.spec.ts`, `fallback.spec.ts`, `shadow-mode.spec.ts`, `multi-pool-matching.spec.js`, `multi-pool-session-growth.spec.js`) at the Apiche-served endpoints and confirm every test still passes unmodified except for the base URL/credential fixture (depends on T170)
- [ ] T172 [P] [US1] Write `sp_create_pool`, `sp_import_tracking_numbers`, `sp_import_tracking_numbers_from_folder`, `sp_change_tracking_number_status`, `sp_move_tracking_number` in `db/procedures/` per `apiche-config.md`, reshaping the browser-upload path to accept a JSON array of pre-parsed DID strings per this session's FR-002 clarification, and confirming the folder-import path's `LOAD DATA INFILE` deployment-topology requirement (the import folder must be readable by the MySQL server process, not just the app tier — `apiche-config.md` Coverage Notes) against the actual deployment target before relying on it
- [ ] T173 [P] [US1] SQL-level tests for T172's five procedures in `tests/Attribution.SqlTests/Pools/`, including the folder-reimport-is-safe and path-traversal-rejected scenarios `FolderImportTests.cs` already covers (depends on T172)
- [ ] T174 [US1] *(direct-access framing 2026-09-08, same day)* Grant `role_system_administrator` `EXECUTE`/`SELECT` on every procedure/view in the "Admin — Number Pools", "Admin — Numbers" and "Admin — Websites" sections of `apiche-config.md` — no Apiche endpoint registration; these are invoked directly by an authenticated `role_system_administrator` database session (depends on T172, T162)
- [ ] T175 *(simplified 2026-09-08, same day)* `GET /v1/admin/numbers/import-folder/files` lists filesystem entries, not database rows, and can no longer be an Apiche concern at all now that Admin has direct database access: since the admin tooling is itself a trusted application (not public JS), it can simply read the configured folder from its own filesystem/network-share access directly — no gateway or stored procedure needed. Confirm the admin tooling's deployment has read access to that folder and document this as the resolution in `research.md`; this is materially simpler than the original companion-endpoint options considered (depends on T174)

**Checkpoint**: Every US1 endpoint is served by Apiche; `client/dni-script` needs no code changes, only a base-URL/credential swap.

---

## Phase 13: US2 Migration — Deterministic call attribution (Priority: P2)

**Goal**: 8x8 CDR/Call Leg ingestion and attribution decisioning run as Apiche-invoked stored procedures instead of `IngestionWorker`/`AttributionService`.

**Independent Test**: Re-run quickstart.md §3's SC-001 seeded-call scenario set and the SC-002 triple-reingestion check against the migrated ingestion/attribution path.

- [ ] T176 [P] [US2] Write `sp_ingest_call_record` and `sp_ingest_call_leg` (idempotent upsert on source natural keys, orphaned-leg handling, FR-045 re-derivation trigger) in `db/procedures/` per `apiche-config.md`, using `IngestionTests.cs` and `ReDerivationTests.cs` as the oracle
- [ ] T177 [P] [US2] Write `sp_advance_ingestion_checkpoint` in `db/procedures/` per `apiche-config.md`
- [ ] T178 [US2] Write `sp_attribute_call` (exact DID + window matching, unattributed/ambiguous classification and evidence storage, Review Case creation on ambiguity) in `db/procedures/` per `apiche-config.md`, using `AttributionServiceTests.cs` as the oracle (depends on T176)
- [ ] T179 [P] [US2] SQL-level tests for T176–T178's four procedures in `tests/Attribution.SqlTests/Attribution/`, including SC-001's full seeded-call scenario set translated from `SeededCallAttributionTests.cs` and the triple-reingestion check from `IdempotentReingestionTests.cs` (depends on T176, T177, T178)
- [ ] T180 [US2] Configure the "8x8 CDR & Call Leg ingestion" Apiche scheduled job in `apiche/jobs/ingestion-8x8.job` per `apiche-config.md`'s Background Worker Jobs section and research.md §19's documented connector assumption, binding its external-call response fields to T176/T177's procedures (depends on T176, T177)
- [ ] T181 [US2] Implement the replay/backfill trigger (operator-specified period, safe alongside live ingestion, FR-042) as an Apiche endpoint or ad-hoc job invocation calling T176's procedure, per `BackfillService.cs`'s existing behavior

**Checkpoint**: Ingestion and attribution run under Apiche with identical outcomes to the retired `IngestionWorker`/`AttributionService`.

---

## Phase 14: US3 Migration — Qualification (Priority: P3)

**Goal**: Rule versioning and per-call qualification judging move to `sp_qualify_call` and the rule-management stored procedures.

**Independent Test**: Re-run quickstart.md §4 (45s/75s boundary, rule-version publication leaving history unchanged) against the migrated path.

- [ ] T182 [US3] Write `sp_qualify_call` (most-specific-scope resolution, direction/answered/duration/website-campaign/local-timezone-time-of-day conditions) in `db/procedures/` per `apiche-config.md`, using `QualificationServiceTests.cs` and `TimeOfDayConditionTests.cs` as the oracle (depends on T178)
- [ ] T183 [P] [US3] Write `sp_create_qualification_rule_version` and `sp_delete_future_qualification_rule_version` (effective-period contiguity validation, reject gap/overlap) in `db/procedures/` per `apiche-config.md`, using `RuleVersioningTests.cs` as the oracle
- [ ] T184 [P] [US3] SQL-level tests for T182–T183's three procedures in `tests/Attribution.SqlTests/Qualification/`, including SC-011's rule-change-leaves-history-unchanged scenario from `RuleChangeHistoryTests.cs` (depends on T182, T183)
- [ ] T185 [US3] *(direct-access framing 2026-09-08, same day)* Grant `role_system_administrator` and `role_marketing_administrator` `EXECUTE`/`SELECT` on every procedure/view in the "Admin — Qualification Rules" section of `apiche-config.md` (matching that section's existing Role assignments) — no Apiche endpoint registration (depends on T183, T162)
- [ ] T186 [US3] Wire `sp_qualify_call` into T176's ingestion procedure so a re-derived call is re-qualified in the same flow FR-045 requires (depends on T176, T182)

**Checkpoint**: Qualification runs under Apiche with rule-version history intact.

---

## Phase 15: US4 Migration — Reporting and export (Priority: P4)

**Goal**: *(direct-access framing 2026-09-08, same day — research.md §22)* Every report becomes a direct parameterized `SELECT` (no stored procedures needed — `apiche-config.md` confirms none of this section's operations require one) that the reporting portal runs directly against MySQL as the signed-in user's own native database account — not an Apiche endpoint.

**Independent Test**: Re-run quickstart.md's report-reconciliation checks (User Story 4), connected as each of the three native database roles in turn, against the migrated views/queries.

- [ ] T187 [P] [US4] Create the seven reporting views/queries (dashboard, campaigns, calls, missed, qualified, unattributed, coverage) in `db/schema/` or `db/procedures/` per `apiche-config.md`'s `## Reporting` section — no Apiche endpoint registration, and no separate CSV variant: the reporting portal runs each query once and renders or exports the identical result set (Coverage Notes: response-format difference only, never a SQL difference)
- [ ] T188 [US4] Grant `SELECT` on all seven views per FR-031's role matrix — `role_analyst`: all seven, read-only; `role_marketing_administrator`: all seven plus review/rules (already covered by T185/T195); `role_system_administrator`: everything — enforced by MySQL's own grant system, not an API-layer filter (depends on T187, T162)
- [ ] T189 [P] [US4] Direct-database-access tests (in the new `tests/Attribution.DirectAccessTests/Reporting/`, per plan.md's Target Project Structure) confirming report totals reconcile against underlying call records for each of the three roles, and that a rendered screen and a CSV export drawn from the same query result can never disagree — translated from `ReportReconciliationTests.cs` and `CsvExportTests.cs` — plus a negative test confirming `role_analyst` is refused on any Admin-only procedure/view (FR-031, User Story 4 Acceptance Scenario 3) (depends on T187, T188)
- [ ] T190 [US4] Left-join/union the two known `PublicationDestination` values into the publication-health query so a destination with zero rows still reports (rather than being silently omitted), per `apiche-config.md` Coverage Notes on the publication-health view (depends on T187)

**Checkpoint**: Every report is a direct-access view/query with no stored-procedure dependency, and role filtering is proven at the database-grant level.

---

## Phase 16: US5 Migration — Publication to Google Ads and GA4 (Priority: P5)

**Goal**: Outbox-based publication and FR-044 correction propagation move to Apiche's scheduled-job model; the outbound HTTP call itself is isolated into an explicit, minimal companion component since a MySQL stored procedure cannot make one.

**Independent Test**: Re-run quickstart.md §5 against the migrated path, accepting the documented change from synchronous-in-request correction to eventually-consistent (sub-minute) per `apiche-config.md` Coverage Notes.

- [ ] T191 [US5] Write `sp_correct_publications_if_needed` (mark rows for retraction/adjustment, idempotent repeated correction) in `db/procedures/` per `apiche-config.md`, using `CorrectionPropagationTests.cs` as the oracle
- [ ] T192 [P] [US5] SQL-level tests for T191 in `tests/Attribution.SqlTests/Publication/`, including the idempotent-repeated-correction scenario (depends on T191)
- [ ] T193 [US5] Configure the "Publication (Google Ads / GA4 outbox drain)" Apiche scheduled job in `apiche/jobs/publish-google-ads.job` and `publish-ga4.job` per `apiche-config.md`'s Background Worker Jobs section and research.md §19 — this is where the actual outbound HTTP call to Google Ads/GA4 lives, since it cannot execute inside MySQL; if Apiche's job model cannot make an outbound HTTP call itself, build the smallest possible companion dispatcher that polls `conversion_publications` for `pending`/`pending_correction` rows and performs the call, reusing the existing `GoogleAds`/`GA4` client code from `src/Attribution.Infrastructure/GoogleAds/`, `GA4/` as a starting point rather than rewriting it (depends on T191)
- [ ] T194 [US5] Confirm end-to-end idempotency across T193's job and `sp_publish_conversion`-equivalent write path: a crash between "sent" and "recorded" cannot double-publish, per `PublicationIdempotencyTests.cs` (SC-002) (depends on T193)

**Checkpoint**: Publication and correction propagation run under Apiche/companion-dispatcher, with the same zero-duplicate guarantee as before.

---

## Phase 17: US6 Migration — Administration, review, alerting and audit (Priority: P6)

**Goal**: Every remaining Admin procedure/view (review, alerts, audit, health, privacy) moves to direct database access (2026-09-08, same day); the Alerting/Retention worker jobs remain Apiche scheduled jobs, since those are background processing, not an Admin/Reporting operation a human calls directly.

**Independent Test**: Re-run quickstart.md §6 and §7, connected as the appropriate native database role, against the migrated path.

- [ ] T195 [P] [US6] Write `sp_resolve_review_case` (supersede Attribution, trigger T191's correction if already published, audit) in `db/procedures/` per `apiche-config.md`, using `ReviewResolutionTests.cs` as the oracle — invoked directly by `role_marketing_administrator`/`role_system_administrator`, not via Apiche (depends on T178, T191, T162)
- [ ] T196 [P] [US6] Write `sp_acknowledge_alert` (direct-access, invoked by an admin acknowledging an alert), `sp_evaluate_alerts`, `sp_evaluate_one_alert` (Apiche-invoked, part of the `alerting.job` background job — unaffected by this session's direct-access change) in `db/procedures/` per `apiche-config.md`, using `AlertingTests.cs` as the oracle
- [ ] T197 [P] [US6] Write `sp_erase_visitor` (direct-access, System-Administrator-only), `sp_deidentify_expired`, `sp_purge_expired` (Apiche-invoked, part of the `retention.job` background job — unaffected) in `db/procedures/` per `apiche-config.md`, accepting the SHA2-based approximation of the .NET HMAC-SHA256 surrogate as a documented, non-bit-identical deviation (Coverage Notes) — flag this explicitly in the PR if any external reconciliation tooling depends on recomputing the surrogate independently
- [ ] T198 [P] [US6] SQL-level tests for T195–T197's seven procedures in `tests/Attribution.SqlTests/Administration/`, translating `AuditLogTests.cs`, `AuditImmutabilityTests.cs`, `RetentionIntegrityTests.cs` and `ErasureSlaTests.cs` scenarios (depends on T195, T196, T197)
- [ ] T199 [US6] *(direct-access framing 2026-09-08, same day)* Grant the appropriate role(s) `EXECUTE`/`SELECT` on every direct-access procedure/view in the "Admin — Review", "Admin — Alerts", "Admin — Audit", "Admin — Health", "Admin — Privacy" sections of `apiche-config.md` per that section's existing Role assignments (Privacy: System Administrator only; the rest: System Administrator + Marketing Administrator, per this document's intro) — no Apiche endpoint registration for any of these (depends on T195, T196, T197, T162)
- [ ] T200 [US6] Configure the "Alerting evaluation" and "Retention (de-identification & purge)" Apiche scheduled jobs in `apiche/jobs/alerting.job`, `retention.job` per `apiche-config.md` — unaffected by the direct-access change, since these are Apiche background jobs, not human-invoked Admin operations; alert email/webhook delivery needs the same outbound-HTTP treatment as T193 — either a native Apiche capability or the smallest possible companion dispatcher, reusing `src/Attribution.Workers/AlertingWorker/`'s existing delivery code as a starting point (depends on T196)
- [ ] T201 [US6] Confirm the Integration Service role is refused every interactive operation (it is never granted a native database account at all, and the DNI client credential carries no role) across the full migrated surface, per FR-038 and this session's FR-037 clarification (depends on T170, T174, T199)

**Checkpoint**: All six user stories are served, correctly split between Apiche (DNI, background jobs) and direct database access (Admin, Reporting); the .NET runtime is no longer required for any request path.

---

## Phase 18: Migration Polish & Cutover

**Purpose**: Prove full parity, cut traffic over, and retire the retired .NET runtime.

- [ ] T202 Run every quickstart.md scenario end-to-end against the fully-migrated stack — Apiche for DNI and background jobs, direct database access for Admin and Reporting (superseding T104's original run) — and confirm no behavioral regression against the .NET baseline
- [ ] T203 [P] Run the full existing `Attribution.UnitTests`/`Attribution.IntegrationTests`/`Attribution.Contract` suite one final time against the retired .NET code purely as the parity oracle, and diff its pass/fail results against `Attribution.SqlTests`/`Attribution.ContractTests` (DNI) and `Attribution.DirectAccessTests` (Admin/Reporting) run against the migrated stack for the same scenarios
- [ ] T204 Follow up on the FR-046/SC-016, FR-037, FR-035 and FR-002 amendments already recorded in spec.md's 2026-09-08 Clarifications sessions (both the Basic Auth session and the same-day direct-database-access session), confirming no remaining reference to TOTP, JWT, refresh tokens, "cannot hold a secret", or stale Basic-Auth-for-Admin wording survives in any still-referenced doc
- [ ] T205 Cut real usage over to the migrated stack: DNI request traffic to Apiche, and every interactive human's Admin/Reporting access to their native database account (per deployment topology) — and monitor SC-004/SC-005 (DNI allocation latency/availability, per T210's pre-cutover load test) and SC-002 (zero duplicate attribution/conversion) for at least one full ingestion cycle before decommissioning the .NET runtime
- [ ] T206 Remove `src/Attribution.Api`, `src/Attribution.Application`, `src/Attribution.Domain`, `src/Attribution.Infrastructure`, `src/Attribution.Workers` and their `tests/Attribution.UnitTests`/`IntegrationTests`/`Contract` projects once T203's parity comparison and T205's monitoring window are both clean (research.md §17) — retain the removal commit's diff as the permanent behavioral-equivalence record, since the code itself will be gone
- [ ] T207 Update `README`/deployment docs and CI to reference the Apiche + direct-database-access + MySQL stack, removing the dotnet build/test pipeline stage added historically and the parity stage added at T158

---

## Migration Dependencies & Execution Order

- **Phase 10 (Setup)** → **Phase 11 (Foundational)**: BLOCKS every per-story migration phase, primarily because T162 (actor-identity injection) is a prerequisite for every admin-write procedure in Phases 12–17.
- **Phase 12 (US1)** can start immediately after Phase 11 and has no dependency on other migration phases, mirroring US1's independence in the original build.
- **Phase 13 (US2)** depends only on Phase 11.
- **Phase 14 (US3)** depends on Phase 13 (T178's `sp_attribute_call` must exist for `sp_qualify_call` to have something to judge).
- **Phase 15 (US4)** depends on Phases 13 and 14 (reports read Attribution and Qualification Result rows).
- **Phase 16 (US5)** depends on Phase 14 (publishes qualified calls).
- **Phase 17 (US6)** depends on Phase 13 (Review Case) and Phase 16 (correction propagation) for `sp_resolve_review_case`.
- **Phase 18 (Cutover)** depends on all of Phases 12–17 being complete and independently validated.

### Parallel Opportunities

- Phases 12 and 13 (US1, US2 migration) can be staffed in parallel once Phase 11 completes, mirroring the original build's US1/US2 independence.
- All stored-procedure-writing tasks marked [P] within a phase touch different files and can run in parallel.
- SQL-level test tasks marked [P] can run in parallel with each other but not with the procedure(s) they test.

---

## Migration Implementation Strategy

1. Phase 10 + 11 first — the actor-identity injection mechanism (T162) is the one unknown that could force a design change if Apiche can't do it cleanly; prove it on the smallest possible endpoint (`/v1/admin/users`) before committing further procedures to the pattern.
2. Migrate US1 next and cut `client/dni-script`'s tests over to it (T171) as an early, low-risk proof that Apiche can serve real traffic correctly — this is the platform's highest-volume, latency-sensitive path (SC-004), so proving it early surfaces performance problems while there's still time to reconsider.
3. Migrate US2 → US3 → US4 → US5 → US6 in dependency order, validating each against its quickstart.md scenarios before moving on, exactly as the original build did.
4. Only cut real traffic over (T205) and remove the .NET projects (T206) once every story has passed its migrated independent test — this is a full backend replacement of a system with real deployment history (see the repository's recent deploy/UAT-checklist commits), so treat the .NET runtime as the safety net until Apiche has demonstrably matched it, not as dead code to delete early.
