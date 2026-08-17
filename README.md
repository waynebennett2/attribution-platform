# Call Attribution Platform

A standalone call attribution platform replacing Mediahawk: Dynamic Number Insertion (DNI) on 8x8 Work, deterministic call-to-session attribution, configurable qualification rules, and publication to Google Ads and GA4. Full spec, plan, and task breakdown live in [`specs/001-call-attribution-platform/`](specs/001-call-attribution-platform/).

**Branch**: `001-call-attribution-platform` (not `main` — check it out explicitly).

## Status

Setup, Foundational infrastructure, and **User Story 1 (DNI allocation + session capture)** are implemented and tested. See [`specs/001-call-attribution-platform/tasks.md`](specs/001-call-attribution-platform/tasks.md) for the full task list and what's checked off.

### What User Story 1 covers

A visitor arrives on a marketing website; the platform allocates them a tracking number from a pool and swaps it into every occurrence on the page (text in any formatting, `tel:` links, numbers rendered after load), keeps that number sticky across pages/tabs for the session, gates everything on consent, and captures arrival details (UTM params, GCLID/GBRAID/WBRAID, landing page, referrer). An optional shadow mode can observe a number another system displayed without touching the page.

**Not yet covered**: matching an inbound call back to a session (User Story 2), qualification, reporting, or publishing to Google Ads/GA4 — see `tasks.md` Phases 4–9.

## Prerequisites

- .NET 8 SDK
- Node.js 20+
- Docker Desktop (MySQL 8.0+, and Playwright's browser binaries)

## Running the automated tests

```bash
dotnet test tests/Attribution.UnitTests          # 87 tests — Domain business rules
dotnet test tests/Attribution.Contract            # 6 tests — API DTO shapes vs. contracts/dni-api.md
dotnet test tests/Attribution.IntegrationTests    # real MySQL via Testcontainers — NOT yet run anywhere; try this first
```
```bash
cd client/dni-script
npm install
npx playwright install chromium
npx playwright test                               # 11 tests — real browser exercising the DNI script
```

## Manual / exploratory testing

```bash
docker compose up -d mysql
dotnet run --project src/Attribution.Api          # migrations auto-apply in Development, then the API starts
```

In another terminal, seed a test website/pool/number (the admin API needs a JWT — sign in with `POST /v1/auth/sign-in` once a local account exists, or seed the SQL directly for a one-shot manual test):

```bash
mysql -h 127.0.0.1 -P 3306 -u attribution -pattribution_dev attribution < scripts/seed-dev-data.sql
```

Then call the DNI endpoints directly — they're deliberately unauthenticated (origin-restricted and rate-limited instead, per FR-037):

```bash
curl -X POST http://localhost:5xxx/v1/dni/allocate \
  -H "Content-Type: application/json" \
  -H "X-Attribution-Client-Token: test-client-1" \
  -d '{"website_id":"00000000-0000-0000-0000-000000000001","client_token":"test-client-1","consent_granted":true,"landing_page":"https://example.com/?utm_source=google"}'
```

(check the actual port in `src/Attribution.Api/Properties/launchSettings.json`). Expect a `session_id` and the tracking number `+15551234567` back. `POST /v1/dni/heartbeat` with that `session_id` extends it; `POST /v1/dni/consent` with `"consent":"withdrawn"` releases it immediately. Full contract: [`specs/001-call-attribution-platform/contracts/dni-api.md`](specs/001-call-attribution-platform/contracts/dni-api.md).

## Project layout

```
src/Attribution.Api/            REST API (controllers, auth/RBAC/rate-limit middleware)
src/Attribution.Application/    Use-case orchestration (no framework deps)
src/Attribution.Domain/         Entities and business rules (zero infra deps)
src/Attribution.Infrastructure/ Dapper repositories, MySQL migrations, external clients
src/Attribution.Workers/        Background loops (ingestion, publication, alerting, retention)
client/dni-script/              Visitor-facing DNI JavaScript client
tests/                          xUnit unit/integration/contract tests
scripts/seed-dev-data.sql       Manual-testing seed data
```

## Known gaps

- `Attribution.IntegrationTests` (Testcontainers MySQL) and the FluentMigrator schema itself haven't been verified against a live database anywhere yet — Docker's daemon wasn't reachable in the sandbox this was built in.
- Admin endpoints require a JWT, issued by `POST /v1/auth/sign-in` (local username/password + TOTP MFA, FR-046) — there's no self-registration, so the first account has to be seeded directly or created by an existing System Administrator.
