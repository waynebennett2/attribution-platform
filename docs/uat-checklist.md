# UAT checklist — Call Attribution Platform

One row per Acceptance Scenario in `specs/001-call-attribution-platform/spec.md`'s six
User Stories. Run this against a deployed environment that has already passed
`scripts/smoke-test.ps1` — this checklist verifies business functionality, not that the
process is up.

**Roles used below** map to the platform's actual RBAC roles: System Administrator (full
access), Marketing Administrator (everything except user management), Analyst
(reports only), Integration Service (machine-to-machine, no interactive sign-in).

Some scenarios need engineering to force a condition a business tester can't trigger by
hand (a destination outage, a mid-batch ingestion crash). Those rows are marked
**[Eng-assisted]** — walk through them together rather than expecting a tester to run them
solo.

Record a Pass/Fail and note in the last column as you go; a Fail blocks sign-off until
re-tested.

---

## User Story 1 — Visitor sees a tracked number and the session is recorded

| # | Step | Expected result | Result |
|---|------|------------------|--------|
| UAT-1.1 | Load a page for a website with an active pool, containing 3 instances of its static number (e.g. `mock/site/index.html`, or `care-homes.html`) with consent already granted. | All 3 instances show the *same* allocated tracking number; `?debug=1` panel shows one session with a start time. | |
| UAT-1.2 | From that same page, navigate to another page on the site (or trigger an in-app route change). | The same tracking number is still shown; the debug panel's session ID is unchanged and no new allocation happened. | |
| UAT-1.3 | Suspend or retire every tracking number in the pool (`POST /v1/admin/numbers/{id}/suspend` for each, or via the Admin UI's Pool Detail page), then load the page as a new visitor. | The website's configured default number is shown, not a blank/partial number; `GET /v1/admin/alerts` shows the allocation failure recorded. | |
| UAT-1.4 | Load the page with `?utm_source=google&utm_medium=cpc&utm_campaign=uat&gclid=test123` appended to the URL. | The debug panel (or `GET /v1/admin/pools/{id}` → session lookup) shows landing page, referrer, all UTM params and the click id captured against the new session. | |
| UAT-1.5 | Allocate a number, then wait past the website's configured session timeout (see `Websites.session_timeout_seconds`) before loading another page. | A new session is created and a (possibly different) number is allocated — the old session does not resume. | |
| UAT-1.6 | Load the page *before* granting consent (if the demo/site has a consent gate), confirm behaviour, then grant consent on the same page. | Before consent: default number shown, no session/allocation created, nothing stored client-side. After consent: a session is created retroactively with the arrival details, and a tracking number is allocated. | |
| UAT-1.7 | On a multi-pool website (`care-homes.html`, FR-050) load the page showing all three care homes' numbers. | Each of the three cards shows a *different* allocated number from its own pool; the debug panel shows three concurrent allocation records under one session; one heartbeat keeps all three alive. | |

## User Story 2 — An inbound call is deterministically attributed to the session that generated it **[Eng-assisted — needs seeded/simulated call data]**

| # | Step | Expected result | Result |
|---|------|------------------|--------|
| UAT-2.1 | With engineering, seed an allocation and a matching inbound call to that number within its window, let ingestion run (or trigger it). | `GET /v1/reports/calls` shows the call as `Attributed`, with the number/session/window recorded as evidence. | |
| UAT-2.2 | Seed a call to a number no session held at that time. | Call shows as `Unattributed` with a reason; appears on `GET /v1/reports/unattributed`. | |
| UAT-2.3 | Seed two overlapping allocations for the same number, then a call matching both. | Call shows as `Ambiguous`, is not attributed to either session, and appears in `GET /v1/admin/review-cases`. | |
| UAT-2.4 | Re-run ingestion over a batch already processed (re-trigger the same 8x8 feed window). | No duplicate calls appear anywhere in Reports; totals are unchanged before/after. | |
| UAT-2.5 | Engineering stops the Workers service mid-ingestion-batch and restarts it. | On restart, ingestion resumes from its checkpoint (`GET /v1/admin/health/ingestion`) — no call is skipped or double-processed. | |
| UAT-2.6 | Seed an unanswered inbound call. | It's attributed using the same rules and appears on `GET /v1/reports/missed`. | |
| UAT-2.7 | Seed an in-progress call judged not-qualified on partial duration, then supply the completed record with a longer duration. | The call's attribution/qualification are re-derived under the rule version in force *at call time*; the prior result is retained as superseded history; re-ingesting the completed record again changes nothing further. | |

## User Story 3 — Attributed calls are qualified into marketing conversions

| # | Step | Expected result | Result |
|---|------|------------------|--------|
| UAT-3.1 | With the default rule active, seed/observe an attributed inbound call connected 75s. | Call marked `Qualified`; the rule version applied is recorded (`GET /v1/reports/qualified`). | |
| UAT-3.2 | Same as above but connected 45s. | Call marked not qualified, with the rule version still recorded. | |
| UAT-3.3 | As System/Marketing Administrator, publish a new qualification rule version with a different threshold (`POST /v1/admin/qualification-rules`) after calls already exist under version 1. | Previously judged calls keep their original result and version 1 reference; only calls after the new version's `effective_start` are judged by it. | |
| UAT-3.4 | Observe an unattributed call. | It is not marked qualified and remains visible on `GET /v1/reports/unattributed`. | |

## User Story 4 — Marketing reports on call performance and exports the data

| # | Step | Expected result | Result |
|---|------|------------------|--------|
| UAT-4.1 | Sign in as Marketing Administrator; open the executive dashboard (`GET /v1/reports/dashboard`) for a period with attributed, unattributed, missed and qualified calls. | Totals shown reconcile exactly with the underlying call list for that period. | |
| UAT-4.2 | From any report, trigger the CSV export (`/export.csv` variant) with a filter/period applied. | The CSV's rows and values match what's on screen, including the applied filter and period. | |
| UAT-4.3 | Sign in as a user with only the Analyst role; attempt to reach number pool, rule, or user management (in the UI, or `POST /v1/admin/pools`, `/v1/admin/qualification-rules`, `/v1/admin/users` directly). | Access is refused (403); `GET /v1/admin/audit` shows the attempt recorded. | |
| UAT-4.4 | With calls attributed across several UTM campaigns, view campaign performance (`GET /v1/reports/campaigns`). | Each call is grouped under the campaign captured on its originating session. | |

## User Story 5 — Qualified calls are published to Google Ads and GA4 **[Eng-assisted for 5.2/5.3/5.5/5.6 — needs forced conditions]**

| # | Step | Expected result | Result |
|---|------|------------------|--------|
| UAT-5.1 | Qualify a call whose session carries a GCLID. | Exactly one offline conversion reaches Google Ads and one event reaches GA4; both outcomes recorded against the call (visible via `GET /v1/admin/health/publication` and the call's detail). | |
| UAT-5.2 | Re-run publication for a call already published successfully (engineering re-triggers the publication worker for it). | No second conversion is created at either destination. | |
| UAT-5.3 | With engineering forcing a destination outage/transient error. | The attempt retries with backoff; the failure shows on `GET /v1/admin/health/publication`; it eventually succeeds exactly once. | |
| UAT-5.4 | Qualify a call whose session has no GCLID. | Not reported to Google Ads, with the reason recorded; still shows as qualified in reporting. | |
| UAT-5.5 | With engineering forcing a permanent rejection from a destination. | Call is flagged with the rejection reason and surfaced to an administrator, not retried indefinitely. | |
| UAT-5.6 | Take a call already published to both destinations and resolve a review case (or restate its source) so it no longer qualifies. | Google Ads conversion is retracted (recorded); GA4 event is recorded as unretractable with a reason; both actions audited; repeating the correction changes nothing further. | |

## User Story 6 — Administrators configure the platform and everything they do is auditable

| # | Step | Expected result | Result |
|---|------|------------------|--------|
| UAT-6.1 | As System Administrator: change a user's role (`POST /v1/admin/users/{id}/role-override`), suspend a tracking number, publish a new qualification rule version. | All three actions appear in `GET /v1/admin/audit` with actor, timestamp, target and what changed. | |
| UAT-6.2 | Attempt to modify or delete any existing audit log entry (there is no such endpoint — confirm none exists / a direct attempt is refused). | The attempt fails and is itself recorded. | |
| UAT-6.3 | **[Eng-assisted]** Have engineering stop 8x8 ingestion (or simulate the feed going stale) past the expected interval. | `GET /v1/admin/health/ingestion` shows unhealthy with the last successful ingestion time; an alert fires to the configured email/webhook; it repeats at the configured interval without duplicating, and stops on acknowledgement (`POST /v1/admin/alerts/{id}/acknowledge`) or once data flows again. | |
| UAT-6.4 | **[Eng-assisted]** Drive a pool's utilisation over its configured warning threshold (allocate/suspend numbers until it crosses). | An alert is raised and notified before the pool would actually be exhausted. | |
| UAT-6.5 | As Marketing Administrator, resolve an ambiguous review case (`POST /v1/admin/review-cases/{id}/resolve`) to a specific session. | The call's attribution updates; the resolution and resolver are recorded as evidence; no duplicate conversion is produced; the action is audited. | |
| UAT-6.6 | Attempt interactive sign-in (`POST /v1/auth/sign-in`) using an Integration Service account's credentials. | Sign-in is refused, while that account's normal system-to-system access continues to work. | |
| UAT-6.7 | As System Administrator, deactivate a currently signed-in user (`POST /v1/admin/users/{id}/deactivate`), then have that user attempt `POST /v1/auth/refresh` with their existing refresh token. | The refresh is refused and access is lost — no need to wait for the access token's own short expiry. | |

---

## Sign-off

| Story | Scenarios passed | Blocking failures | Signed off by | Date |
|-------|-------------------|--------------------|----------------|------|
| US1 — DNI allocation | /7 | | | |
| US2 — Call attribution | /7 | | | |
| US3 — Qualification | /4 | | | |
| US4 — Reporting | /4 | | | |
| US5 — Publication | /6 | | | |
| US6 — Admin & audit | /7 | | | |
