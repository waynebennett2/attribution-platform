# Contract: Reporting Data API

> **Updated 2026-09-08, twice.** First to HTTP Basic Auth, now superseded same day: after reviewing `apiche-config.md`, the project owner moved every report below off Apiche entirely, onto **direct database access** (research.md §22, constitution amendment 2.0.0). **These are no longer HTTP endpoints.** Consumed by the existing (externally-owned) reporting portal, which now connects directly to MySQL as each signed-in user's own native database account (never by the visitor-facing client, which never had access to this surface). "Method"/"Path" below is retained as a stable operation identifier for cross-reference against `apiche-config.md`'s `## Reporting` section (which gives the exact `SELECT` each report runs directly), not a literal HTTP route. Role-filtering (FR-031) is enforced by the database's own role grants (`role_system_administrator` / `role_marketing_administrator` / `role_analyst`) rather than an API-layer check; there is no separate CSV endpoint (FR-030) — the portal runs the same query once and renders it as a screen or exports it as CSV from the identical result set, which is what actually guarantees an export can never disagree with what was displayed.

## Reports (FR-029)

| Operation | Reference Path | Notes |
|---|---|---|
| Query | `/v1/reports/dashboard?from=&to=` | executive summary: volume, attribution rate, qualified conversions |
| Query | `/v1/reports/campaigns?from=&to=` | grouped by the campaign captured on the originating session (FR-014) |
| Query | `/v1/reports/calls?from=&to=&state=&q=` | call detail search |
| Query | `/v1/reports/missed?from=&to=` | unanswered inbound calls |
| Query | `/v1/reports/qualified?from=&to=` | qualified calls |
| Query | `/v1/reports/unattributed?from=&to=` | includes ambiguous, broken down by reason |
| Query | `/v1/reports/coverage?from=&to=` | FR-048 attributed/unattributed/ambiguous breakdown by reason and website — the sole evidence for SC-018 |

CSV export (FR-030) is the reporting portal's own rendering choice against the same query result — see the banner note above; there is no separate query or reference path for it.

## Response shape (each report's query result)

```json
{
  "period": { "from": "2026-07-13", "to": "2026-08-10" },
  "filters": { "...": "echoed back verbatim" },
  "rows": [ { "...": "report-specific columns" } ],
  "totals": { "...": "reconciles exactly against underlying call records, FR-029" }
}
```
This shape describes the logical result set each query in `apiche-config.md`'s `## Reporting` section returns (rows plus a totals summary), which the reporting portal is free to shape into its own screen or CSV — it is no longer a literal HTTP JSON response body.

## Authorization (FR-031, enforced by native database role grants — research.md §22)

- Analyst: read-only on all `/v1/reports/*`; 403 on any `/v1/admin/*` path (User Story 4, Acceptance Scenario 3).
- Marketing Administrator: all reports plus manual review and rule management.
- System Administrator: all of the above plus users, pools and numbers.
- Integration Service: no interactive reporting access (FR-038) — this API surface is not exposed to that role at all.
