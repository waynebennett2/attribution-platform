# 8x8 Call Attribution Platform — Constitution

**Version:** 1.0.2
**Ratified:** 2026-08-05
**Last Amended:** 2026-08-06

## Preamble

This constitution governs the design and implementation of the 8x8 Call Attribution Platform backend — a Mediahawk-replacement call attribution system built on 8x8 Work, using Dynamic Number Insertion, deterministic call matching, and integrations with Google Ads and Google Analytics 4. It supersedes ad hoc technical decisions; every spec, plan, and implementation must comply with the principles below.

> **Note on database:** database is MySQL, consistent with the uploaded requirements documents.

## Core Principles

### I. Deterministic Attribution Only (NON-NEGOTIABLE)
Call attribution must be based strictly on DID allocation and time-window matching against a website session. The system must never use probabilistic, fuzzy, or heuristic matching to attribute a call. Any call that cannot be matched with certainty is classified as unattributed or ambiguous and surfaced for manual review — it is never guessed. *(Rationale: FR-018, FR-020, FR-021 — attribution integrity is the core value proposition versus Mediahawk.)*

### II. Layered Architecture
The backend is structured in strict layers: Presentation/API → Application/Service → Domain → Infrastructure/Data Access. Layers depend only inward; the Domain layer has no dependency on Infrastructure or frameworks. Cross-cutting concerns (logging, auth, validation) are implemented via middleware/decorators, not scattered through business logic. *(Rationale: explicit non-functional maintainability requirement; also required for independent unit testing of business logic.)*

### III. API-First
All backend capability is exposed through versioned REST APIs. The PHP/JS/CSS reporting frontend, the DNI JavaScript client, and any future customer portals or mobile apps are API consumers only — no shared database access, no business logic duplicated client-side. *(Rationale: explicit architectural requirement to support multiple frontends/future channels.)*

### IV. Idempotent, Auditable Operations
All ingestion (Call Detail Records, Call Legs) and all state-changing operations must be idempotent and safe to retry. Every attribution decision stores its supporting evidence (matched DID, session window, timestamps). Every administrator action is written to an immutable audit log. *(Rationale: FR-016, FR-017, FR-019, FR-035, NFR Reliability/Compliance, Acceptance Criteria "no duplicate attribution.")*

### V. Test-First for Business Logic (NON-NEGOTIABLE)
All business logic — number allocation, session matching, attribution, qualification rule evaluation — must have unit tests written before or alongside implementation, not after. No PR touching Domain or Application layer logic merges without passing tests. Integration tests cover 8x8 and Google API boundaries using recorded/mocked responses. *(Rationale: explicit user requirement; also the only way to trust "never guess attribution" over time as rules evolve.)*

### VI. Security by Default
All endpoints require TLS. All API access is authenticated via JWT (user-facing) or API keys (system-to-system, e.g. Integration Service role). Authorization is enforced via RBAC mapped to the defined roles (System Administrator, Marketing Administrator, Analyst, Integration Service). Secrets and credentials are never stored in source control or logs. *(Rationale: NFR Security; FR-032 user/role management.)*

### VII. Observable by Design
Every service emits structured logs, health checks, and metrics (ingestion lag, allocation failures, attribution match rate, API latency). Structured logging must allow tracing a single call from DNI allocation through attribution to Google Ads/GA4 publication. *(Rationale: NFR Monitoring; supports the 95%+ attribution accuracy acceptance criterion and operational troubleshooting.)*

### VIII. Configuration Over Hardcoding
Qualification rules, number pool assignment (by website/campaign/business unit), session timeout/heartbeat, and retention periods are configurable and versioned — not hardcoded. Rule changes do not retroactively alter historical attribution decisions. *(Rationale: FR-004, FR-012, FR-023, FR-024.)*

## Technology Constraints

- **Language/Runtime:** C#, .NET 8 (LTS).
- **Database:** MySQL.
- **Architecture style:** Layered (N-tier) monolith-first, structured to allow future extraction of the ingestion/worker services if scale requires it.
- **Data access:** ORM (e.g. EF Core) or micro-ORM (e.g. Dapper) — decision deferred to `/speckit.plan`, but must support atomic operations for number allocation (FR-003) and idempotent upserts for CDR/Call Leg ingestion.
- **Background processing:** Scheduled/worker services for 8x8 polling (CDRs, Call Legs) and Google Ads/GA4 publication, decoupled from the request/response API via a queue or outbox pattern to guarantee at-least-once delivery with idempotent handling.
- **API style:** REST, versioned, OpenAPI-documented.
- **Testing:** xUnit (or NUnit) for unit tests against Domain/Application layers; integration test project for infrastructure boundaries (MySQL, 8x8 API, Google Ads/GA4 API) using test containers or mocked HTTP.
- **CI/CD:** Automated build, test, and static analysis gate on every PR; no merge to main with failing or skipped tests on business-logic code.
- **Frontend boundary:** The PHP/JS/CSS reporting portal and DNI client are out of scope for this backend constitution but must be treated as untrusted API consumers (input validation, rate limiting apply at the API boundary).

## Non-Functional Targets

| Requirement | Target |
|---|---|
| Availability | 99.9% |
| DNI allocation performance | 95% of requests under 300ms |
| Attribution accuracy | ≥95% of eligible calls correctly attributed during parallel run |
| Duplicate attribution | Zero tolerance |
| Scalability | API and worker services scale horizontally (stateless where possible) |
| Compliance | Consent-aware data capture; configurable data retention |

## Development Workflow

1. No feature proceeds from `/speckit.specify` to `/speckit.plan` without resolving ambiguity via `/speckit.clarify` if the feature touches attribution, qualification, or financial/reporting data.
2. `/speckit.plan` must explicitly name the data access approach and confirm MySQL schema impact for any feature touching Tracking Numbers, Sessions, or Call records.
3. `/speckit.tasks` must separate Domain/business-logic tasks (test-first, per Principle V) from Infrastructure/integration tasks.
4. Code review must verify: layering respected (Principle II), audit logging present for admin actions (Principle IV), and unit test coverage for new business logic (Principle V) before merge.
5. Any change to qualification or attribution rules requires a versioned rule record — never an in-place mutation of historical logic.

## Governance

- This constitution supersedes informal conventions and prior undocumented decisions. Where a spec or plan conflicts with this document, the constitution wins unless formally amended.
- **Amendments** require: a documented rationale, explicit approval, and a migration note for any in-flight specs/plans affected. Amendments increment the version per semantic versioning:
  - **MAJOR** — removal or backward-incompatible change to a Core Principle (e.g. relaxing deterministic attribution).
  - **MINOR** — new principle, new section, or materially expanded guidance.
  - **PATCH** — clarifications, wording, typo fixes.
- All `/speckit.plan` and `/speckit.tasks` outputs must be checked against this constitution during `/speckit.analyze`.
- Reviewers are expected to flag any deviation from Principles I, IV, V, or VI explicitly — these four are treated as non-negotiable given the accuracy, audit, and security requirements of a commercial attribution platform.
