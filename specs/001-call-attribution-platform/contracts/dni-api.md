# Contract: DNI Visitor-Facing API

Unauthenticated (cannot hold a secret, FR-037); instead origin-restricted (`Origin` header checked against the Website's `permitted_origins`) and rate-limited (600 req/min/origin, 10 req/min/client — FR-037). Every response completes within 300ms for ≥95% of requests at peak (SC-004).

Every request MUST also carry the `X-Attribution-Client-Token` header, set to the same value as the request body's `client_token`, so the rate-limiting middleware can enforce the per-client threshold without buffering and parsing the JSON body on every call.

## POST /v1/dni/allocate

Requests a tracking number for a new page view.

**Request**
```json
{
  "website_id": "string",
  "client_token": "string",        // opaque, client-generated, used only for rate limiting — not a visitor identifier
  "consent_granted": true,
  "landing_page": "string",
  "referrer": "string",
  "utm": { "source": "string", "medium": "string", "campaign": "string", "term": "string", "content": "string" },
  "gclid": "string?", "gbraid": "string?", "wbraid": "string?",
  "ga4_client_id": "string?"
}
```

**Response 200** (consent_granted = true and allocation succeeded)
```json
{ "session_id": "string", "number": "string", "expires_at": "2026-08-10T12:30:00Z" }
```

**Response 200** (consent_granted = false, or pool exhausted / allocation service degraded — FR-011, FR-039)
```json
{ "session_id": null, "number": "<website default_number>", "reason": "no_consent | pool_exhausted | unavailable" }
```
No session or allocation record is created for this response shape (FR-039, FR-020's allocation-failure counterpart).

**Errors**: 403 origin not permitted; 429 rate limit exceeded (both fail closed to the default number on the client side, per FR-011 — the client never blocks rendering on this call).

## POST /v1/dni/heartbeat

Keeps an active session's allocation alive; called every `heartbeat_interval_seconds` (default 300s, FR-012).

**Request**
```json
{ "session_id": "string" }
```

**Response 200**
```json
{ "still_valid": true, "number": "string" }
```
```json
{ "still_valid": false, "number": "<default_number>" }
```
`still_valid: false` means the session expired since the last heartbeat (FR-012); the client MUST NOT call `/allocate` automatically in response — a fresh page view does that (FR-010's "for the whole of their active session" boundary).

## POST /v1/dni/consent

Reports a consent state change occurring after `/allocate` (grant after initial refusal, or withdrawal) — the server-side counterpart the client's consent contract (see `consent-contract.md`) triggers.

**Request**
```json
{ "session_id": "string?", "client_token": "string", "website_id": "string", "consent": "granted | withdrawn", "arrival_details": { "...": "same shape as /allocate, present only on late grant" } }
```

**Response 200**
- On `granted` with no prior session: same shape as `/allocate`'s success response — creates the session and allocates from this point (FR-039).
- On `withdrawn`: `{ "number": "<default_number>" }` — ends the session, releases the allocation (FR-039); data already captured stays subject to FR-040 retention.

## POST /v1/dni/shadow-observe

Optional, per-website (FR-049). Called instead of `/allocate` when shadow mode is enabled for the website: the script observes whatever number another system (e.g. Mediahawk) already displayed and reports it, without the platform replacing anything on the page.

**Request**
```json
{
  "website_id": "string",
  "session_id": "string",
  "observed_number": "string",
  "landing_page": "string", "referrer": "string",
  "utm": { "source": "string", "medium": "string", "campaign": "string", "term": "string", "content": "string" },
  "gclid": "string?", "gbraid": "string?", "wbraid": "string?", "ga4_client_id": "string?"
}
```

**Response 200**
```json
{ "recorded": true }
```
Creates a shadow-flagged Allocation (data-model.md's `Allocation.is_shadow`) for the observed number and window, without allocating a number from the platform's own pool. Overlapping observed windows are tolerated and later classified as ambiguous under FR-021, reported separately from ordinary-operation ambiguity (FR-049). No number is ever returned in the response — the page's own markup, or whatever the other system displayed, is left untouched.
