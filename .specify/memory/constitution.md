# 8x8 Call Attribution Platform — Constitution

**Version:** 2.0.0
**Ratified:** 2026-08-05
**Last Amended:** 2026-09-08

## Preamble

This constitution governs the design and implementation of the 8x8 Call Attribution Platform backend — a Mediahawk-replacement call attribution system built on 8x8 Work, using Dynamic Number Insertion, deterministic call matching, and integrations with Google Ads and Google Analytics 4. It supersedes ad hoc technical decisions; every spec, plan, and implementation must comply with the principles below.

> **Note on database:** database is MySQL, consistent with the uploaded requirements documents.

## Core Principles

### I. Deterministic Attribution Only (NON-NEGOTIABLE)
Call attribution must be based strictly on DID allocation and time-window matching against a website session. The system must never use probabilistic, fuzzy, or heuristic matching to attribute a call. Any call that cannot be matched with certainty is classified as unattributed or ambiguous and surfaced for manual review — it is never guessed. *(Rationale: FR-018, FR-020, FR-021 — attribution integrity is the core value proposition versus Mediahawk.)*

### II. Layered Architecture (Visitor-Facing and Background Surface) *(narrowed 2026-09-08 — see Amendment History)*
The visitor-facing Dynamic Number Insertion surface, and all background/worker processing, are served exclusively by a 3rd-party API tool called Apiche. Apiche exposes API endpoints to the DNI client: it accepts data via GET/POST/PUT/DELETE — GET/DELETE parameters come from the URL query string, POST/PUT parameters come from a JSON object in the request body — and each endpoint is backed by exactly one parameterized SQL statement, with `<parameter_name>` placeholders bound 1:1 to a URL parameter or JSON field, substituted with the actual received value at execution time. Administrative and reporting operations are governed by Principle III's direct-database-access model instead — they are not Apiche endpoints. *(Rationale: explicit non-functional maintainability requirement for the one surface that must remain gateway-fronted because it is public and untrusted; also required for independent unit/SQL-level testing of business logic.)*

### III. API-First for Untrusted Surfaces; Direct Database Access for Trusted Internal Tooling *(amended 2026-09-08 — see Amendment History)*
The DNI JavaScript client is an API consumer only, calling Apiche's versioned REST endpoints (Principle II) — it is public, untrusted code and MUST NOT hold database credentials or embed business logic. Any communication with 8x8 happens via API in the same way. Any future customer-facing or mobile-app consumer is likewise an API consumer only, with no shared database access and no business logic duplicated client-side.

The PHP/JS/CSS reporting frontend, and the platform's administrative tooling, are trusted internal operator applications and MUST instead connect directly to the database — each interactive human user authenticates as their own native database account, mapped to one of the database's own roles (System Administrator, Marketing Administrator, Analyst), and authorization is enforced by the database's own role grants rather than by an API-layer check. This is not a weakening of Principle VI's security-by-default requirement — enforcement simply moves to the database engine, which remains server-side and non-bypassable by the calling application. *(Rationale: the reporting and administrative surfaces are used only by trusted, already-authenticated internal operators — not the public — so routing them through Apiche added a layer without adding safety; the visitor-facing surface has no such trust boundary and must stay gateway-fronted.)*

### IV. Idempotent, Auditable Operations
All ingestion (Call Detail Records, Call Legs) and all state-changing operations must be idempotent and safe to retry. Every attribution decision stores its supporting evidence (matched DID, session window, timestamps). Every administrator action is written to an immutable audit log. *(Rationale: FR-016, FR-017, FR-019, FR-035, NFR Reliability/Compliance, Acceptance Criteria "no duplicate attribution.")*

### V. Test-First for Business Logic (NON-NEGOTIABLE)
All business logic — number allocation, session matching, attribution, qualification rule evaluation — must have unit tests written before or alongside implementation, not after. No PR touching Domain or Application layer logic merges without passing tests. Integration tests cover 8x8 and Google API boundaries using recorded/mocked responses. *(Rationale: explicit user requirement; also the only way to trust "never guess attribution" over time as rules evolve.)*

### VI. Security by Default *(scoped 2026-09-08 — see Amendment History)*
All endpoints require TLS, and so does every direct database connection under Principle III. Apiche API access (the DNI surface) is authenticated via HTTP Basic authentication — a per-website client ID/client secret for the JavaScript client (no role required), or an API key for system-to-system access. Administrative and reporting access is not an Apiche/HTTP surface at all (Principle III): each interactive human authenticates as their own native database account, with roles (System Administrator, Marketing Administrator, Analyst) enforced by the database's own role grants. Secrets and credentials are never stored in source control or logs. *(Rationale: NFR Security; FR-032 user/role management.)*

### VII. Observable by Design
Every service emits structured logs, health checks, and metrics (ingestion lag, allocation failures, attribution match rate, API latency). Structured logging must allow tracing a single call from DNI allocation through attribution to Google Ads/GA4 publication. *(Rationale: NFR Monitoring; supports the SC-001/SC-018 attribution-accuracy acceptance criteria and operational troubleshooting.)*

### VIII. Configuration Over Hardcoding
Qualification rules, number pool assignment (by website/campaign/business unit), session timeout/heartbeat, and retention periods are configurable and versioned — not hardcoded. Rule changes do not retroactively alter historical attribution decisions. *(Rationale: FR-004, FR-012, FR-023, FR-024.)*

# Technology Constraints

**Worker services:** ApiChe for attribution processing.
- **Database:** MySQL.
- **Architecture style:** Layered (N-tier) monolith-first, structured to allow future extraction of the ingestion/worker services if scale requires it.
- **Data access:** DNI data access is handled by Apiche; administrative and reporting data access connects directly to the database under native database role-based access control (Principle III, 2026-09-08 amendment) — not through Apiche.
- **Background processing:** Scheduled/worker services for 8x8 polling (CDRs, Call Legs), and Google Ads/GA4 publication, are done by Apiche. 
- **API style:** REST, versioned, OpenAPI-documented.
- **Testing:** xUnit (or NUnit) for unit tests against Domain/Application layers; integration test project for infrastructure boundaries (MySQL, 8x8 API, Google Ads/GA4 API) using test containers or mocked HTTP. The DNI insertion client additionally requires its own automated browser-level tests covering number replacement, single-page-application navigation, session stickiness, consent grant and withdrawal, and fallback to the default number — no server-side test can evidence what a visitor actually sees.
- **CI/CD:** Automated build, test, and static analysis gate on every PR; no merge to main with failing or skipped tests on business-logic code.
- **Reporting portal boundary:** The PHP/JS/CSS reporting portal is out of scope for this programme. 
- **Insertion client boundary:** The DNI JavaScript client **is** in scope and is delivered by this programme. Being ours does not make it trusted — it executes in an attacker-controlled environment, so every request it makes is validated, authorized and rate-limited server-side exactly as the portal's are. The client may hold presentation state and session-continuity state (the allocated number, the entry URL for the life of a page view), but it MUST NOT make allocation, attribution or qualification decisions; those remain server-side without exception.
## Non-Functional Targets

| Requirement | Target |
|---|---|
| Availability | 99.9% |
| DNI allocation performance | 95% of requests under 300ms |
| Attribution accuracy | 100% on seeded/controlled calls (SC-001); ≥95% of live calls reach an attributed state over a 4-week/500-call window (SC-018) |
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

## Amendment History

### 2.0.0 — 2026-09-08 (MAJOR)

**Change.** Principle II is narrowed to govern only the visitor-facing DNI surface and background/worker processing — Apiche is no longer described as "the entire backend" with "no other backend entry point." Principle III is amended to extend the reporting frontend's existing direct-database-access allowance to also cover the platform's administrative operations, with authorization enforced via native database roles (System Administrator, Marketing Administrator, Analyst) instead of an Apiche/HTTP-layer RBAC check. Principle VI is updated to match: Basic Auth now governs only the Apiche/DNI surface, while administrative and reporting access authenticates as native database accounts under Principle III. (While editing Principle VI's line, also corrected its heading level to match every other principle and a typo ("Javascfript"), both pre-existing and unrelated to this amendment's substance.)

**Rationale.** After reviewing `apiche-config.md` (produced by feature 001's Apiche-migration plan), the project owner determined that routing purely internal, already-trusted administrative and reporting operations through Apiche added an intermediary layer without adding real protection, since the visitor-facing surface Apiche actually protects — public, untrusted JavaScript that can never safely hold a database credential — is categorically different from an internal operator signed into an admin tool or reporting portal. Direct database access for these two surfaces, enforced by the database's own role-based access control, keeps enforcement server-side and non-bypassable — consistent with Principle VI — while removing Apiche as an unnecessary intermediary for trusted internal tooling.

**Approval.** Requested directly by the project owner, 2026-09-08.

**Migration note.** This is a MAJOR amendment because it removes Principle II's previous absolute claim that Apiche is the entire backend with no other entry point — a backward-incompatible change to a Core Principle, per this constitution's own semantic-versioning rule. Feature 001's `plan.md`, `research.md`, `data-model.md`, `contracts/`, `apiche-config.md` and `tasks.md` all require corresponding updates, tracked as part of the same session that produced this amendment. DNI and background-worker processing are unaffected and remain Apiche-fronted exactly as before.

### 1.1.2 — 2026-09-08 (PATCH)

**Change.** Removed the `Language/Runtime: .NET 8 (LTS)` line from Technology Constraints.

**Rationale.** That line was left over from before Principle II and Principle VI were amended to mandate Apiche as the entire backend; it contradicted the already-ratified "the backend will consist only of a 3rd party API tool called apiche" text by naming a language/runtime for a backend that this constitution says has none of this programme's own code in it. `/speckit.plan` on feature 001 surfaced the same drift this PATCH resolves for the plan artifacts (see `specs/001-call-attribution-platform/plan.md`'s "Architecture Migration" section and `research.md` §17), and the project owner asked directly for this constraint's removal once that drift was pointed out. No Core Principle changed — Principle II already said this — so this is a PATCH, correcting a stale, contradicted line rather than deciding anything new.

**Approval.** Requested directly by the project owner, 2026-09-08.

**Migration note.** No other Technology Constraints line changes. Feature 001's plan/research/data-model/contracts already reflect an Apiche-only backend as of this same date and need no further change on account of this PATCH.

### 1.1.1 — 2026-08-10 (PATCH)

**Change.** Corrected the Non-Functional Targets table and Principle VII's rationale, which still cited "≥95% during parallel run" as the attribution-accuracy acceptance bar. That bar was superseded by feature 001's 2026-08-09 clarification session: the parallel run against Mediahawk is no longer the launch gate. Acceptance now rests on SC-001 (100%, controlled test) and SC-018 (≥95% live coverage), with the parallel run available only as an optional later exercise under FR-049.

**Rationale.** `/speckit-analyze` on feature 001 flagged the constitution as internally out of sync with its own governed spec — a documentation drift, not a principle change, since Principle I (deterministic attribution) is untouched.

**Approval.** Pending project owner sign-off.

**Migration note.** No spec, plan, or task changes required; spec.md already reflects the corrected criteria.

### 1.1.0 — 2026-08-06 (MINOR)

**Change.** Split the former single `Frontend boundary` constraint in two. The PHP/JS/CSS reporting portal remains out of scope. The DNI JavaScript client is brought **into** scope as a deliverable of this programme, with an explicit statement that being in scope does not make it trusted, and an explicit prohibition on client-side allocation, attribution or qualification decisions. Extended the `Testing` constraint to require browser-level automated tests for the insertion client.

**Rationale.** Feature `001-call-attribution-platform` established that the insertion client cannot be correctly built by a party that does not own the requirements it implements. Number stickiness, replacement of numbers rendered after page load in single-page applications, the session heartbeat, consent-gated allocation and silent fallback to a default number are all platform requirements (FR-008 to FR-011, FR-039). Two of that feature's success criteria commit to measured browser behaviour — at least 99.9% of tracked page views displaying a valid number, and zero blank, partial or malformed numbers (SC-003) — and such commitments are unenforceable for code this programme does not own. The prior clause placed the client out of scope, which directly contradicted them.

**Approval.** Granted by the project owner, 2026-08-06.

**Migration note.** Affects in-flight feature `001-call-attribution-platform`, which recorded this conflict as an assumption pending amendment; that assumption now resolves against this version and may be reduced to a reference to this entry when planning begins. No other spec or plan exists, and no implementation work is affected. Principle III (API-First) is deliberately unchanged and remains satisfied: the insertion client is still an API consumer only, with no shared database access and no server-side business logic duplicated into it. No Core Principle was altered, removed or weakened, which is why this is a MINOR rather than MAJOR increment.

### 1.0.2 — 2026-08-06 (PATCH)

Restored text corrupted by a Windows-1252 save and converted the file to UTF-8. Recovered 14 em dashes, the Principle II layer arrows (which had been flattened to `?`, erasing the inward-dependency direction), and the Attribution accuracy target (which had been flattened from a floor to an equality, contradicting the "95%+" criterion cited under Principle VII). No wording changed.

### 1.0.1 and earlier

Predate this history section. 1.0.1 was the constitution substantially as first authored and ratified on 2026-08-05; the 1.0.0 to 1.0.1 change is undocumented.
