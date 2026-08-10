# Implementation Plan: Call Attribution Platform

**Branch**: `001-call-attribution-platform` | **Date**: 2026-08-10 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/001-call-attribution-platform/spec.md`

## Summary

Replace Mediahawk with a standalone call attribution platform built on 8x8 Work: a Dynamic Number Insertion (DNI) JavaScript client that allocates a tracking number per visitor session, a backend that deterministically attributes inbound 8x8 calls to the session that displayed the dialled number using only exact DID + time-window matching (never probabilistic), a versioned qualification rule engine that decides which attributed calls are marketing conversions, and outbound publication of qualified calls to Google Ads (offline conversions) and GA4 (Measurement Protocol). The platform runs entirely independently of Mediahawk — standalone acceptance evidence (SC-001, SC-018) is the launch gate; any comparison against Mediahawk is an optional, later, report-level exercise (FR-049), never a live integration. Technical approach: a layered C#/.NET 8 REST API plus decoupled background worker services for 8x8 polling and Google Ads/GA4 publication, backed by MySQL, with Dapper for the latency- and correctness-critical write paths (atomic number allocation, idempotent CDR/Call Leg upserts) and reporting queries, and FluentMigrator for versioned schema migrations.

## Technical Context

**Language/Version**: C#, .NET 8 (LTS) — mandated by the project constitution.

**Primary Dependencies**: ASP.NET Core Web API (REST, versioned, OpenAPI-documented per Principle III); Dapper as the sole data-access layer (see research.md — chosen over EF Core so the atomic allocation query (FR-003) and idempotent CDR/Call Leg upserts (FR-017) can be hand-written, transaction-scoped SQL rather than ORM-generated); FluentMigrator for versioned MySQL schema migrations; MySqlConnector as the ADO.NET provider; a background worker host (`Microsoft.Extensions.Hosting` `IHostedService`) for the 8x8 polling ingestion loop and the Google Ads/GA4 publication loop, each consuming an outbox table for at-least-once, idempotent delivery; an OpenID Connect client library for federated SSO (FR-046); a small DNI JavaScript client (vanilla JS or a minimal bundler-built module, no framework dependency, to keep footprint low on arbitrary customer sites) implementing allocation, heartbeat, DOM replacement, and the consent event/callback contract (FR-039).

**Storage**: MySQL 8.0+ — mandated by the project constitution. 8.0+ specifically because atomic number allocation (FR-003) uses `SELECT ... FOR UPDATE SKIP LOCKED` semantics, only available from MySQL 8.0.1.

**Testing**: xUnit for unit tests (Domain/Application layers, test-first per Principle V) and integration tests (MySQL via Testcontainers, 8x8/Google Ads/GA4 via recorded/mocked HTTP); Playwright for the DNI client's required browser-level tests (FR-008–FR-011, FR-039) covering multi-page and single-page-application replacement, session stickiness across tabs, post-load DOM mutation, consent grant/withdrawal, and fallback to the default number — no server-side test can evidence what a visitor's browser actually renders.

**Target Platform**: Linux containers (Docker), deployed behind a load balancer; API and worker services are stateless and scale horizontally per FR-043, with no single point of failure in the visitor-facing allocation path.

**Project Type**: Web service (REST API + background workers) plus a standalone client-side JavaScript library (the DNI insertion script). No first-party web frontend is built — the reporting portal is an existing, separately-owned consumer of this API (Delivery boundary, spec.md).

**Performance Goals**: DNI allocation determined within 300ms for ≥95% of requests at a sustained peak of ~57 requests/minute platform-wide (SC-004); hourly CDR/Call Leg ingestion cadence, configurable, with attribution outcomes unaffected by cadence changes (FR-016); alerts delivered within 15 minutes of a threshold crossing (SC-017); ≥99% of qualified calls with a Google click identifier published to Google Ads/GA4 within 24 hours (SC-007).

**Constraints**: 99.9% availability for the visitor-facing allocation service, measured monthly (SC-005); zero duplicate attribution and zero duplicate conversions under reprocessing (SC-002); strict deterministic DID + time-window matching only, no fuzzy/probabilistic logic (Principle I, FR-018); every ingestion and publication operation idempotent and safely retryable (Principle IV, FR-017, FR-027); rate limits of 600 req/min per origin and 10 req/min per client on the visitor-facing endpoints (FR-037); the DNI client MUST NOT make allocation, attribution or qualification decisions client-side (constitution, Insertion client boundary).

**Scale/Scope**: ~250 concurrent tracked sessions at peak across all websites, ~7 new sessions/minute, implying a tracking number estate of ~630 before headroom (spec Assumptions); retention tiers of 14 months (identifiers de-identified), 25 months (de-identified call/attribution records), 7 years (audit log); data-subject erasure completed within 30 days (SC-019); 6 user stories, 49 functional requirements, 19 success criteria, 17 key entities (User and Role counted separately). Monthly call volume and the number of deployed websites/pools were not stated in the spec and are treated as open operational sizing inputs — see research.md.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| # | Principle | Status | Rationale |
|---|-----------|--------|-----------|
| I | Deterministic Attribution Only (NON-NEGOTIABLE) | PASS | Attribution service performs exact DID + allocation-window matching only (FR-018); no scoring, ranking or fuzzy-match library is introduced anywhere in the design. |
| II | Layered Architecture | PASS | Presentation (ASP.NET Core controllers) → Application (use-case services) → Domain (attribution, qualification, allocation logic, no framework/infra references) → Infrastructure (Dapper repositories, 8x8/Google clients, outbox). Dapper SQL lives entirely in Infrastructure; Domain types are POCOs. |
| III | API-First | PASS | All capability — including what the reporting portal renders and what the DNI client calls — is exposed through versioned REST APIs (`/v1/...`), OpenAPI-documented. No shared DB access is granted to the portal or the client. |
| IV | Idempotent, Auditable Operations | PASS | CDR/Call Leg ingestion and publication use natural/source keys with idempotent upserts and an outbox pattern (FR-017, FR-027, FR-045); every attribution decision stores its evidence (FR-019); every admin action writes to an immutable audit log (FR-035). |
| V | Test-First for Business Logic (NON-NEGOTIABLE) | PASS (process gate, enforced at /speckit-tasks and code review) | Allocation, matching, qualification-rule evaluation land in the Domain/Application layers and are unit-testable in isolation from Dapper/HTTP; xUnit tests are required before/alongside implementation per Development Workflow §4. |
| VI | Security by Default | PASS | TLS everywhere; JWT for interactive users (issued after OIDC federation, FR-046) and API keys for the Integration Service role; RBAC enforced server-side on every operation (FR-038); the DNI client's allocation endpoint is untrusted-origin-restricted and rate-limited rather than authenticated, since it cannot hold a secret (FR-037). |
| VII | Observable by Design | PASS | Structured logs/metrics/health checks trace a call end-to-end from allocation through attribution, qualification and publication (FR-041); ingestion lag, publication failure rate, allocation failure rate, pool utilisation and review-case age are all alertable (FR-047). |
| VIII | Configuration Over Hardcoding | PASS | Number pool scoping, session timeout/heartbeat, qualification rules (including the new per-website/campaign scoping and time-of-day conditions), and retention periods are all administrator-configurable without code change (FR-004, FR-012, FR-023, FR-024, FR-040); rule changes never rewrite history (FR-024). |

No violations requiring justification. Complexity Tracking is left empty.

### Post-Phase 1 re-check

Re-evaluated against `data-model.md` and `contracts/` after Phase 1 design: no new violation surfaced.

- **Layering (II)** confirmed by data-model.md: every entity is a plain record with no ORM base class or query-builder dependency, so Domain stays framework-free; all Dapper SQL is confined to the Infrastructure layer described in Project Structure.
- **Idempotent/Auditable (IV)** confirmed by the Conversion Publication entity's `idempotency_key` and the Ingestion Checkpoint entity, and by the Audit Entry entity being explicitly append-only at the data-model level, not just in application code.
- **Configuration over Hardcoding (VIII)** confirmed by Qualification Rule's `conditions` and `scope_type`/`scope_ref` fields and the `admin-api.md` rule-management endpoints — rule content and scope are entirely data-driven, no code path branches on a specific customer or website.
- **Security by Default (VI)** confirmed by `dni-api.md`'s explicit no-secret, origin-restricted, rate-limited design for the one surface that cannot be authenticated, and `admin-api.md`/`reporting-api.md` requiring a platform-issued JWT plus RBAC on everything else.

Gate remains PASS; proceed to `/speckit-tasks`.

## Project Structure

### Documentation (this feature)

```text
specs/[###-feature]/
├── plan.md              # This file (/speckit-plan command output)
├── research.md          # Phase 0 output (/speckit-plan command)
├── data-model.md        # Phase 1 output (/speckit-plan command)
├── quickstart.md        # Phase 1 output (/speckit-plan command)
├── contracts/           # Phase 1 output (/speckit-plan command)
└── tasks.md             # Phase 2 output (/speckit-tasks command - NOT created by /speckit-plan)
```

### Source Code (repository root)

```text
src/
├── Attribution.Api/                 # ASP.NET Core Web API (Presentation) — DNI, admin, reporting, webhook-in endpoints
│   ├── Controllers/
│   ├── Middleware/                  # auth, RBAC, rate limiting, audit interceptor
│   └── Contracts/                   # request/response DTOs (versioned)
├── Attribution.Application/         # Application/Service layer — use-case orchestration, no framework refs
│   ├── Allocation/
│   ├── Attribution/
│   ├── Qualification/
│   ├── Publication/
│   └── Administration/
├── Attribution.Domain/              # Domain layer — entities, value objects, pure business rules, zero infra deps
│   ├── Websites/                    # Website
│   ├── Pools/                       # NumberPool, TrackingNumber
│   ├── Sessions/                    # Visitor, Session, Allocation
│   ├── Calls/                       # Call, CallLeg, Attribution, IngestionCheckpoint
│   ├── Qualification/               # QualificationRule, QualificationResult
│   ├── Publication/                 # ConversionPublication
│   ├── Identity/                    # User, Role
│   └── Audit/                       # AuditEntry, Alert, ReviewCase
├── Attribution.Infrastructure/      # Infrastructure — Dapper repositories, MySQL, outbox
│   ├── Data/                        # Dapper repositories, FluentMigrator migrations
│   ├── Ingestion8x8/                # Analytics for 8x8 Work client + CDR/Call Leg mapping
│   ├── GoogleAds/                   # Offline conversions client
│   ├── GA4/                         # Measurement Protocol client
│   └── Identity/                    # OIDC federation, break-glass accounts
└── Attribution.Workers/             # Background worker host (IHostedService)
    ├── IngestionWorker/             # polls 8x8 CDRs/Call Legs on FR-016 cadence
    ├── PublicationWorker/           # drains outbox to Google Ads/GA4
    ├── AlertingWorker/              # evaluates FR-047 thresholds
    └── RetentionWorker/             # FR-040 purge/de-identification, FR-039 erasure

client/
└── dni-script/                      # DNI JavaScript client (FR-008–FR-011, FR-039) — no server-side framework deps
    ├── src/
    │   ├── allocation.js            # allocation + heartbeat calls
    │   ├── replace.js                # DOM replacement, MutationObserver for post-load numbers
    │   └── consent.js                # platform-defined consent event/callback contract
    └── tests/                       # Playwright browser-level tests

tests/
├── Attribution.UnitTests/           # xUnit — Domain + Application, test-first per Principle V
├── Attribution.IntegrationTests/    # xUnit — MySQL via Testcontainers, 8x8/Google Ads/GA4 mocked HTTP
└── Attribution.Contract/            # API contract tests against contracts/
```

**Structure Decision**: Web-service option, adapted: a single ASP.NET Core solution split into the four constitution-mandated layers (`Attribution.Api` → `Attribution.Application` → `Attribution.Domain` → `Attribution.Infrastructure`) plus a separate `Attribution.Workers` host for the decoupled ingestion/publication/alerting/retention loops required by FR-016, FR-027 and FR-047, and a fully independent `client/dni-script` package for the visitor-facing insertion client — independent because it ships to customer websites, not to the API's own runtime, and is tested at the browser level rather than as a .NET project. No first-party reporting frontend exists in this repository (Delivery boundary, spec.md); the reporting portal is an external, separately-owned consumer of `Attribution.Api`.

## Complexity Tracking

No Constitution Check violations were identified; this section is intentionally empty.
