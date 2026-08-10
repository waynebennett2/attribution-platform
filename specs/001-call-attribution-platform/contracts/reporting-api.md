# Contract: Reporting Data API

Consumed by the existing (externally-owned) reporting portal, never by the visitor-facing client. Every endpoint is role-filtered (FR-031) and has a matching CSV export (FR-030) that reproduces exactly the same rows/values/filters/period as the JSON response.

## Reports (FR-029)

| Method | Path | Notes |
|---|---|---|
| GET | `/v1/reports/dashboard?from=&to=` | executive summary: volume, attribution rate, qualified conversions |
| GET | `/v1/reports/campaigns?from=&to=` | grouped by the campaign captured on the originating session (FR-014) |
| GET | `/v1/reports/calls?from=&to=&state=&q=` | call detail search |
| GET | `/v1/reports/missed?from=&to=` | unanswered inbound calls |
| GET | `/v1/reports/qualified?from=&to=` | qualified calls |
| GET | `/v1/reports/unattributed?from=&to=` | includes ambiguous, broken down by reason |
| GET | `/v1/reports/coverage?from=&to=` | FR-048 attributed/unattributed/ambiguous breakdown by reason and website — the sole evidence for SC-018 |

Each of the above also exists as `GET /v1/reports/{name}/export.csv` with identical query parameters (FR-030).

## Response shape (JSON reports)

```json
{
  "period": { "from": "2026-07-13", "to": "2026-08-10" },
  "filters": { "...": "echoed back verbatim" },
  "rows": [ { "...": "report-specific columns" } ],
  "totals": { "...": "reconciles exactly against underlying call records, FR-029" }
}
```

## Authorization (FR-031)

- Analyst: read-only on all `/v1/reports/*`; 403 on any `/v1/admin/*` path (User Story 4, Acceptance Scenario 3).
- Marketing Administrator: all reports plus manual review and rule management.
- System Administrator: all of the above plus users, pools and numbers.
- Integration Service: no interactive reporting access (FR-038) — this API surface is not exposed to that role at all.
