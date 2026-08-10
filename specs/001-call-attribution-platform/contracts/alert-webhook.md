# Contract: Outbound Alert Webhook (FR-047)

The platform POSTs to a customer-configured webhook endpoint (per-condition configurable) whenever an alertable condition is raised, repeated, acknowledged or cleared. Delivered alongside an email notification; webhook delivery failure is itself surfaced on integration health (FR-047) rather than silently swallowed.

## POST {customer webhook endpoint}

**Payload**
```json
{
  "alert_id": "string",
  "condition_type": "ingestion_lag | publication_failure_rate | allocation_failure_rate | pool_utilisation | review_case_age",
  "scope": { "...": "e.g. { \"pool_id\": \"...\" } or { \"destination\": \"google_ads\" }" },
  "status": "raised | repeated | acknowledged | cleared",
  "threshold": "string",
  "current_value": "string",
  "raised_at": "2026-08-10T12:00:00Z",
  "occurred_at": "2026-08-10T12:15:00Z"
}
```

**Delivery semantics**
- `raised`: sent once when the threshold is first crossed, within 15 minutes (SC-017).
- `repeated`: sent at the configured interval while the same condition remains open — never as a distinct new `alert_id` (FR-047's "not sent as new alerts").
- `acknowledged`: sent once, immediately after an administrator acknowledges via `POST /v1/admin/alerts/{id}/acknowledge`.
- `cleared`: sent once when the underlying condition is next evaluated as healthy.

**Expected response**: any 2xx within a short timeout counts as delivered; non-2xx or timeout is retried with backoff and, if delivery keeps failing, is itself flagged on `/v1/admin/health/*` (FR-047 — "failure to deliver a notification MUST NOT suppress the underlying condition").
