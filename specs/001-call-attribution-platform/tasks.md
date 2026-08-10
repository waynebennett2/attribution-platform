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
- [X] T014 Implement OIDC federation and 5-minute JWT issuance with silent refresh in `src/Attribution.Infrastructure/Identity/` per research.md §5, FR-046 (depends on T013)
- [X] T015 [P] Implement break-glass account authentication (local credentials + TOTP MFA, default 2 accounts) in `src/Attribution.Infrastructure/Identity/` per FR-046 (depends on T013)
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

- [ ] T023 [P] [US1] Unit tests for atomic number allocation (`FOR UPDATE SKIP LOCKED` dequeue, no double-allocation) in `tests/Attribution.UnitTests/Allocation/AllocationServiceTests.cs`
- [ ] T024 [P] [US1] Unit tests for session timeout/heartbeat expiry logic in `tests/Attribution.UnitTests/Sessions/SessionServiceTests.cs`
- [ ] T025 [P] [US1] Integration test for `POST /v1/dni/allocate` against a MySQL Testcontainer, including pool-exhausted fallback (FR-011) in `tests/Attribution.IntegrationTests/Dni/AllocateEndpointTests.cs`
- [ ] T026 [P] [US1] Contract test validating request/response shapes against `contracts/dni-api.md` in `tests/Attribution.Contract/DniApiContractTests.cs`
- [ ] T027 [P] [US1] Playwright test: number replacement (displayed text + click-to-call targets) on a multi-page site in `client/dni-script/tests/replacement.spec.ts`
- [ ] T028 [P] [US1] Playwright test: SPA in-app route change and post-load DOM mutation replacement in `client/dni-script/tests/spa-replacement.spec.ts`
- [ ] T029 [P] [US1] Playwright test: session stickiness across concurrent tabs in `client/dni-script/tests/multi-tab.spec.ts`
- [ ] T030 [P] [US1] Playwright test: consent gating, grant-after-refusal, withdrawal, and the active pre-consent default-number DOM write in `client/dni-script/tests/consent.spec.ts`
- [ ] T031 [P] [US1] Playwright test: script-blocked fallback leaves the static default number in place in `client/dni-script/tests/fallback.spec.ts`

### Implementation for User Story 1

- [ ] T032 [P] [US1] Implement Number Pool and Tracking Number domain entities and repositories (exactly-one-pool-at-a-time move semantics, FR-004) in `src/Attribution.Domain/Pools/`, `src/Attribution.Infrastructure/Data/` (depends on T012)
- [ ] T033 [P] [US1] Implement Visitor and Session domain entities and repositories in `src/Attribution.Domain/Sessions/`, `src/Attribution.Infrastructure/Data/` (depends on T012)
- [ ] T034 [US1] Implement the Allocation domain entity and the atomic `FOR UPDATE SKIP LOCKED` allocation repository query in `src/Attribution.Domain/Sessions/Allocation.cs`, `src/Attribution.Infrastructure/Data/AllocationRepository.cs` per research.md §2, FR-003 (depends on T032, T033)
- [ ] T035 [US1] Implement `AllocationService` (allocate, heartbeat, release, suspended-number-in-progress exception FR-005, consent-withdrawal immediate release FR-018/FR-039) in `src/Attribution.Application/Allocation/AllocationService.cs` per FR-006, FR-007, FR-010, FR-012 (depends on T034)
- [ ] T036 [US1] Implement `POST /v1/dni/allocate`, `/heartbeat`, `/consent` in `src/Attribution.Api/Controllers/DniController.cs` per contracts/dni-api.md (depends on T035)
- [ ] T037 [P] [US1] Implement number pool CRUD and bulk CSV import endpoints (per-row accept/reject reasons, FR-002) in `src/Attribution.Api/Controllers/AdminPoolsController.cs` per contracts/admin-api.md, FR-001, FR-004, FR-005 (depends on T032)
- [ ] T038 [P] [US1] Implement the DNI client's allocation and heartbeat calls, with quick-backoff retry on a failed heartbeat before its next scheduled interval, in `client/dni-script/src/allocation.js` per contracts/dni-api.md, FR-012
- [ ] T039 [P] [US1] Implement the DNI client's DOM replacement — digit-normalized text matching, `tel:` links and configurable marker-attribute click-to-call targets, MutationObserver for post-load numbers, main-document scope only (no iframe/shadow-DOM traversal) — in `client/dni-script/src/replace.js` per FR-008, FR-009, FR-011
- [ ] T040 [P] [US1] Implement the DNI client's consent contract (read `window.__attributionConsent`, subscribe to `attribution:consent-change`, trigger active default-number write pre-consent) in `client/dni-script/src/consent.js` per contracts/consent-contract.md, FR-039
- [ ] T041 [US1] Wire landing page/referrer/UTM/GCLID/GBRAID/WBRAID/GA4-client-id capture, with degraded-provenance handling for late consent, into session creation in `src/Attribution.Application/Allocation/AllocationService.cs` per FR-013, FR-014, FR-015 (depends on T035)

### Optional Shadow Mode for User Story 1 (FR-049)

> Numbered T113–T118, continuing the sequence. FR-049's shadow mode had no task coverage until this remediation; it supports the optional, later, report-level comparison against Mediahawk described in spec.md's Assumptions — never a live integration.

- [ ] T113 [P] [US1] Unit tests for shadow-mode allocation recording and overlapping-observed-window ambiguity tolerance, distinguished from ordinary-operation ambiguity, in `tests/Attribution.UnitTests/Allocation/ShadowModeTests.cs` per FR-049
- [ ] T114 [P] [US1] Playwright test: shadow mode leaves the page's displayed numbers untouched while still recording the observed number in `client/dni-script/tests/shadow-mode.spec.ts` per FR-049
- [ ] T115 [US1] Implement shadow-mode allocation recording (observe the displayed number, hold an allocation window without allocating, tolerate overlapping windows as ambiguous) in `src/Attribution.Application/Allocation/ShadowAllocationService.cs` per FR-049 (depends on T034, T051)
- [ ] T116 [US1] Implement the per-website shadow-mode toggle endpoint (configuration only, no code change) in `src/Attribution.Api/Controllers/AdminWebsitesController.cs` per FR-049 (depends on T012)
- [ ] T117 [P] [US1] Implement the DNI client's observe-only shadow mode (reads the displayed number, does not replace it, reports it via `POST /v1/dni/shadow-observe`) in `client/dni-script/src/shadow.js` per FR-049, contracts/dni-api.md (depends on T038)

**Checkpoint**: User Story 1 is fully functional and independently testable.

---

## Phase 4: User Story 2 - An inbound call is deterministically attributed to the session that generated it (Priority: P2)

**Goal**: Poll 8x8 Call Detail Records and Call Legs on schedule, and attribute each inbound call to the session that held the dialled number using only exact DID + time-window matching — never guessing.

**Independent Test**: Seed known allocation records, feed a set of call records containing matched, unmatched and deliberately conflicting cases, then confirm each call lands in the expected state with retrievable evidence, and confirm that re-feeding the identical call records changes nothing.

### Tests for User Story 2

- [ ] T042 [P] [US2] Unit tests for strict DID + window attribution matching (attributed/unattributed/ambiguous) in `tests/Attribution.UnitTests/Attribution/AttributionServiceTests.cs`
- [ ] T043 [P] [US2] Unit tests for idempotent CDR/Call Leg upsert and checkpoint advancement (FR-017) in `tests/Attribution.UnitTests/Ingestion/IngestionTests.cs`
- [ ] T044 [P] [US2] Unit tests for FR-045 re-derivation on restated or in-progress call records (rule-version-at-call-time preserved, superseded history retained) in `tests/Attribution.UnitTests/Attribution/ReDerivationTests.cs`
- [ ] T045 [P] [US2] Integration test covering SC-001's full seeded-call scenario set (in-window, expired-but-in-extension, closed-window, never-allocated, suspended-number, DST transition, midnight-crossing) against a MySQL Testcontainer in `tests/Attribution.IntegrationTests/Attribution/SeededCallAttributionTests.cs`
- [ ] T046 [P] [US2] Integration test: re-ingesting an identical batch three times produces zero change in any report total (SC-002) in `tests/Attribution.IntegrationTests/Ingestion/IdempotentReingestionTests.cs`

### Implementation for User Story 2

- [ ] T047 [P] [US2] Implement Call and Call Leg domain entities and repositories (idempotent upsert on source natural keys, orphaned-leg handling) in `src/Attribution.Domain/Calls/`, `src/Attribution.Infrastructure/Data/` per FR-017 (depends on T011)
- [ ] T048 [P] [US2] Implement the Attribution domain entity and repository (attributed/unattributed/ambiguous state machine, superseded-history rows) in `src/Attribution.Domain/Calls/Attribution.cs`, `src/Attribution.Infrastructure/Data/AttributionRepository.cs` (depends on T011)
- [ ] T049 [P] [US2] Implement the Ingestion Checkpoint domain entity and repository in `src/Attribution.Domain/Calls/IngestionCheckpoint.cs`, `src/Attribution.Infrastructure/Data/` (depends on T011)
- [ ] T050 [US2] Implement the Analytics for 8x8 Work client (authentication, CDR/Call Leg polling) in `src/Attribution.Infrastructure/Ingestion8x8/` per research.md §8 (depends on T049)
- [ ] T051 [US2] Implement `AttributionService` (exact DID + window matching, unattributed/ambiguous classification, evidence storage per FR-019) in `src/Attribution.Application/Attribution/AttributionService.cs` per FR-018–FR-021 (depends on T048)
- [ ] T052 [US2] Implement Review Case creation on ambiguous attribution in `src/Attribution.Application/Attribution/AttributionService.cs`, `src/Attribution.Domain/Audit/ReviewCase.cs` per FR-021, FR-036 (depends on T051)
- [ ] T053 [US2] Implement re-derivation on restated/in-progress calls (FR-045: update in place, re-derive against rule version at call time, retain superseded history, idempotent on unchanged records) in `src/Attribution.Application/Attribution/ReDerivationService.cs` (depends on T051)
- [ ] T054 [US2] Implement the `IngestionWorker` loop (poll on FR-016's configurable cadence, idempotent upsert per FR-017, checkpoint advance, orphaned-Call-Leg handling) in `src/Attribution.Workers/IngestionWorker/` per research.md §3, §8 (depends on T050, T051, T053)
- [ ] T055 [P] [US2] Implement the replay/backfill command (operator-specified period, no duplicate records/attributions, safe alongside live ingestion) in `src/Attribution.Application/Attribution/BackfillService.cs` per FR-042 (depends on T054)

**Checkpoint**: User Stories 1 and 2 both work independently.

---

## Phase 5: User Story 3 - Attributed calls are qualified into marketing conversions (Priority: P3)

**Goal**: Evaluate every attributed call against the qualification rule in force (default, or the most specific website/campaign override) to decide whether it's a marketing conversion, with every rule version and its scope permanently recorded against the calls it judged.

**Independent Test**: With the default rule active, feed attributed calls just under and just over the 60-second threshold plus unanswered calls, confirm correct qualification; then publish a new rule version, confirm new calls use it and previously judged calls retain their original result and rule version.

### Tests for User Story 3

- [ ] T056 [P] [US3] Unit tests for default-rule evaluation at the 60-second boundary (45s not qualified, 75s qualified) in `tests/Attribution.UnitTests/Qualification/QualificationServiceTests.cs`
- [ ] T057 [P] [US3] Unit tests for rule versioning, most-specific-scope resolution, and effective-period contiguity validation (reject gap/overlap, FR-024) in `tests/Attribution.UnitTests/Qualification/RuleVersioningTests.cs`
- [ ] T058 [P] [US3] Unit tests for the time-of-day condition evaluated in the website's local timezone, not the canonical storage timezone, in `tests/Attribution.UnitTests/Qualification/TimeOfDayConditionTests.cs`
- [ ] T059 [P] [US3] Integration test: publishing a new rule version leaves previously-judged calls' results and rule-version references unchanged (SC-011) in `tests/Attribution.IntegrationTests/Qualification/RuleChangeHistoryTests.cs`

### Implementation for User Story 3

- [ ] T060 [P] [US3] Implement Qualification Rule and Qualification Result domain entities and repositories in `src/Attribution.Domain/Qualification/`, `src/Attribution.Infrastructure/Data/` (depends on T011)
- [ ] T061 [US3] Implement the rule condition evaluator (direction, answered, duration, website/campaign scope, website-local-timezone time-of-day) in `src/Attribution.Domain/Qualification/RuleEvaluator.cs` per FR-022, FR-023 (depends on T060)
- [ ] T062 [US3] Implement rule-version effective-period contiguity validation (reject any configuration creating a gap or overlap) in `src/Attribution.Application/Qualification/RuleVersioningService.cs` per FR-024 (depends on T060)
- [ ] T063 [US3] Implement `QualificationService` (most-specific-scope resolution, judge attributed calls, record rule version and scope applied) in `src/Attribution.Application/Qualification/QualificationService.cs` per FR-022–FR-024 (depends on T061, T062)
- [ ] T064 [US3] Wire qualification into the ingestion re-derivation pipeline in `src/Attribution.Application/Attribution/ReDerivationService.cs` (depends on T053, T063)
- [ ] T065 [US3] Implement qualification-rule management endpoints (create/list/delete-future-version-only) in `src/Attribution.Api/Controllers/AdminQualificationRulesController.cs` per contracts/admin-api.md, FR-033 (depends on T062)

**Checkpoint**: User Stories 1, 2 and 3 all work independently.

---

## Phase 6: User Story 4 - Marketing reports on call performance and exports the data (Priority: P4)

**Goal**: Serve executive-dashboard, campaign-performance, call-detail-search, missed/qualified/unattributed report data (and matching CSV exports) that reconcile exactly against underlying call records, filtered to what the requesting role permits.

**Independent Test**: With a known dataset loaded, sign in as each reporting role, confirm every report renders the expected figures that reconcile against the underlying call records, confirm an Analyst cannot reach administrative functions, and confirm each report exports a CSV whose contents match what was displayed.

### Tests for User Story 4

- [ ] T066 [P] [US4] Integration test: dashboard/campaign/call-detail/missed/qualified/unattributed report totals reconcile against underlying call records in `tests/Attribution.IntegrationTests/Reporting/ReportReconciliationTests.cs`
- [ ] T067 [P] [US4] Integration test: CSV export contains the same rows, values, filters and period as the report it was generated from (FR-030) in `tests/Attribution.IntegrationTests/Reporting/CsvExportTests.cs`
- [ ] T068 [P] [US4] Integration test: an Analyst-only user is refused on number pool/rule/user management, and the attempt is recorded (FR-031, FR-038) in `tests/Attribution.IntegrationTests/Reporting/RoleRestrictionTests.cs`
- [ ] T069 [P] [US4] Integration test: the FR-048 coverage breakdown reconciles exactly with underlying call records (SC-018 evidence) in `tests/Attribution.IntegrationTests/Reporting/CoverageBreakdownTests.cs`

### Implementation for User Story 4

- [ ] T070 [US4] Implement `ReportingService` (dashboard, campaign performance, call detail search, missed, qualified, unattributed, FR-048 coverage) query layer in `src/Attribution.Application/Administration/ReportingService.cs` per FR-029, FR-048 (depends on T048, T063)
- [ ] T071 [US4] Implement `GET /v1/reports/*` endpoints in `src/Attribution.Api/Controllers/ReportsController.cs` per contracts/reporting-api.md (depends on T070)
- [ ] T072 [US4] Implement `GET /v1/reports/*/export.csv` endpoints reusing the report query results (FR-030) in `src/Attribution.Api/Controllers/ReportsController.cs` (depends on T071)
- [ ] T073 [US4] Implement role-based report/export filtering (FR-031) in `src/Attribution.Api/Middleware/` or `ReportingService` (depends on T070, T016)
- [ ] T118 [US4] Flag shadow-derived attributions and report shadow-mode ambiguity separately from ordinary ambiguity per FR-049 in `src/Attribution.Application/Administration/ReportingService.cs` (depends on T070, T115)

**Checkpoint**: User Stories 1–4 all work independently.

---

## Phase 7: User Story 5 - Qualified calls are published to Google Ads and GA4 (Priority: P5)

**Goal**: Publish every qualified call to Google Ads (offline conversion) and GA4 (Measurement Protocol) exactly once, skip with a recorded reason where the needed identifier is missing, and propagate later corrections as far as each destination permits.

**Independent Test**: Qualify a set of calls where some originating sessions carry a Google click identifier and some do not, then confirm the former are published to both destinations exactly once, the latter are handled per the documented rule without error, and forced failures are retried without producing duplicates.

### Tests for User Story 5

- [ ] T074 [P] [US5] Unit tests for idempotency-key generation, scoped per publish episode (a genuine retract-then-requalify gets a new key, FR-027) in `tests/Attribution.UnitTests/Publication/IdempotencyKeyTests.cs`
- [ ] T075 [P] [US5] Unit tests for GCLID-missing (Google Ads) and GA4-client-id-missing (GA4) skip-with-reason handling in `tests/Attribution.UnitTests/Publication/SkipReasonTests.cs`
- [ ] T076 [P] [US5] Integration test: a qualified call is published exactly once to both destinations, and retries/reprocessing produce no duplicate (SC-002, FR-027) in `tests/Attribution.IntegrationTests/Publication/PublicationIdempotencyTests.cs`
- [ ] T077 [P] [US5] Integration test: FR-044 correction propagation — Google Ads retract/adjust succeeds, GA4 is recorded as unpropagatable — both audited in `tests/Attribution.IntegrationTests/Publication/CorrectionPropagationTests.cs`

### Implementation for User Story 5

- [ ] T078 [P] [US5] Implement the Conversion Publication domain entity and repository in `src/Attribution.Domain/Publication/ConversionPublication.cs`, `src/Attribution.Infrastructure/Data/` (depends on T011)
- [ ] T079 [P] [US5] Implement the Google Ads offline-conversions client (upload, retract, adjust) in `src/Attribution.Infrastructure/GoogleAds/` per research.md §6, FR-025
- [ ] T080 [P] [US5] Implement the GA4 Measurement Protocol client in `src/Attribution.Infrastructure/GA4/` per research.md §7
- [ ] T081 [US5] Implement `PublicationService` (write the outbox row in the same transaction as the qualification decision, per-episode idempotency key) in `src/Attribution.Application/Publication/PublicationService.cs` per FR-025–FR-028, research.md §3 (depends on T078)
- [ ] T082 [US5] Implement the `PublicationWorker` loop (drain the outbox, retry with backoff, record every attempt's outcome) in `src/Attribution.Workers/PublicationWorker/` per FR-027, FR-028 (depends on T079, T080, T081)
- [ ] T083 [US5] Implement correction propagation (FR-044: Google Ads retraction/adjustment, GA4 unpropagatable recording, idempotent repeated correction) in `src/Attribution.Application/Publication/CorrectionService.cs` (depends on T081)
- [ ] T084 [US5] Wire qualification-change triggers (manual review resolution, FR-045 re-derivation) to `CorrectionService` in `src/Attribution.Application/Qualification/QualificationService.cs`, `ReDerivationService.cs` (depends on T063, T083)

**Checkpoint**: User Stories 1–5 all work independently.

---

## Phase 8: User Story 6 - Administrators configure the platform and everything they do is auditable (Priority: P6)

**Goal**: Give administrators user/pool/rule management, integration health visibility, a manual review workflow, and threshold-based alerting — with every administrative action captured in an immutable audit log.

**Independent Test**: Perform one action of every administrative type, confirm each appears in the audit log with actor, action, target, before and after values and timestamp, confirm the log cannot be edited or deleted, and confirm resolving a manual review case updates the call and is itself audited.

### Tests for User Story 6

- [ ] T085 [P] [US6] Integration test: every administrative action type appears in the audit log with actor/target/before/after (SC-006) in `tests/Attribution.IntegrationTests/Administration/AuditLogTests.cs`
- [ ] T086 [P] [US6] Integration test: audit entry modification/deletion attempts are refused and the attempt is itself recorded (FR-035) in `tests/Attribution.IntegrationTests/Administration/AuditImmutabilityTests.cs`
- [ ] T087 [P] [US6] Integration test: an unhealthy ingestion/publication/pool condition raises an alert that is delivered, repeats without duplicating, and clears (FR-034, FR-047, SC-017), including a review case aged past 48 hours in `tests/Attribution.IntegrationTests/Administration/AlertingTests.cs`
- [ ] T088 [P] [US6] Integration test: resolving a manual review case updates attribution evidence, produces no duplicate conversion, and is audited (FR-036) in `tests/Attribution.IntegrationTests/Administration/ReviewResolutionTests.cs`
- [ ] T089 [P] [US6] Integration test: the Integration Service role is refused interactive sign-in while system-to-system access still works (FR-038) in `tests/Attribution.IntegrationTests/Administration/IntegrationServiceAccessTests.cs`
- [ ] T090 [P] [US6] Integration test: a revoked federated user loses access within the 5-minute refresh interval (SC-016), and a break-glass account can sign in with MFA when the identity provider is unreachable (FR-046) in `tests/Attribution.IntegrationTests/Administration/FederationRevocationTests.cs`

### Implementation for User Story 6

- [ ] T091 [P] [US6] Implement the Alert domain entity and repository (one open alert per condition invariant, FR-047) in `src/Attribution.Domain/Audit/Alert.cs`, `src/Attribution.Infrastructure/Data/` (depends on T011)
- [ ] T092 [US6] Implement `AlertingService` (threshold evaluation: ingestion lag, publication failure rate, allocation failure rate, pool utilisation, review-case age) in `src/Attribution.Application/Administration/AlertingService.cs` per FR-047 (depends on T091)
- [ ] T093 [US6] Implement the `AlertingWorker` loop plus email/webhook delivery and delivery-failure surfacing in `src/Attribution.Workers/AlertingWorker/` per contracts/alert-webhook.md, FR-047 (depends on T092)
- [ ] T094 [US6] Implement user/role management and role-override endpoints in `src/Attribution.Api/Controllers/AdminUsersController.cs` per contracts/admin-api.md, FR-032, FR-046 (depends on T013)
- [ ] T095 [US6] Implement integration-health endpoints (ingestion, publication, pool utilisation) in `src/Attribution.Api/Controllers/AdminHealthController.cs` per contracts/admin-api.md, FR-034
- [ ] T096 [US6] Implement the manual-review-case resolution endpoint (update attribution, propagate any already-published correction, audit) in `src/Attribution.Api/Controllers/AdminReviewController.cs` per contracts/admin-api.md, FR-036 (depends on T052, T083)
- [ ] T097 [US6] Implement the alert-acknowledgement endpoint in `src/Attribution.Api/Controllers/AdminAlertsController.cs` per contracts/admin-api.md, FR-047 (depends on T091)
- [ ] T098 [US6] Implement the read-only audit-log query endpoint in `src/Attribution.Api/Controllers/AdminAuditController.cs` per contracts/admin-api.md, FR-035 (depends on T017)
- [ ] T099 [US6] Enforce Integration Service system-to-system-only access (deny interactive sign-in) in `src/Attribution.Api/Middleware/` per FR-038 (depends on T016)

**Checkpoint**: All six user stories are independently functional.

---

## Phase 9: Polish & Cross-Cutting Concerns

**Purpose**: Retention/erasure, documentation, and platform-wide hardening that spans every user story.

- [ ] T122 [P] Integration test: run the retention purge against a seeded historical dataset spanning the 14/25-month thresholds, confirming identifiers are gone and report totals for that period reconcile identically before/after (SC-014) in `tests/Attribution.IntegrationTests/Retention/RetentionIntegrityTests.cs`
- [ ] T100 [P] Implement the `RetentionWorker` loop (14/25-month tiered de-identification via a stable HMAC surrogate, 7-year audit retention, open-review-case exception) in `src/Attribution.Workers/RetentionWorker/` per FR-040, research.md §10 (depends on T122)
- [ ] T123 [P] Integration test: submit a seeded erasure request and confirm completion, with the visitor's data gone, within a simulated 30-day window (SC-019) in `tests/Attribution.IntegrationTests/Retention/ErasureSlaTests.cs`
- [ ] T101 [P] Implement the data-subject erasure request endpoint, completing within the 30-day SC-019 bar in `src/Attribution.Api/Controllers/AdminPrivacyController.cs` per FR-039, SC-019 (depends on T123)
- [ ] T102 [P] Generate and publish OpenAPI documentation for every versioned endpoint per Constitution Principle III
- [ ] T103 [P] Propagate a correlation ID through structured logs end-to-end (allocation → attribution → qualification → publication) per FR-041
- [ ] T104 Run every quickstart.md validation scenario end-to-end against a seeded environment
- [ ] T105 [P] Security hardening pass: TLS enforcement, secret-scanning, dependency audit per Constitution Principle VI
- [ ] T106 [P] Performance/load test: DNI allocation at SC-004's peak (≈7 allocations/min + ≈50 heartbeats/min, 300ms p95) in `tests/Attribution.IntegrationTests/Performance/AllocationLoadTests.cs`
- [ ] T119 [P] Document and provision a multi-instance deployment topology (N API + N worker instances behind a load balancer, health-check-based instance removal) in `docker-compose.yml` / deployment docs per FR-043
- [ ] T120 Integration test: rerun SC-004's load test against 2+ concurrently running API instances with no shared in-process state, confirming identical latency/correctness in `tests/Attribution.IntegrationTests/Performance/HorizontalScaleTest.cs` per FR-043, SC-005 (depends on T119, T106)
- [ ] T121 Failover test: terminate one API instance mid-load and confirm zero failed allocation requests per SC-005 (depends on T119)
- [ ] T107 Reconcile plan.md/data-model.md/contracts/ against any drift discovered during implementation

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
