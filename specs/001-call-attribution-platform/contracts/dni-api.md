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
  "ga4_client_id": "string?",
  "matched_pool_ids": ["string"],  // FR-050, multi-pool websites only — pool ids whose default_number the client found on the page; omitted/empty otherwise, and always ignored for a website with multi_pool_enabled = false
  "session_id": "string?"  // FR-050, multi-pool websites only — present when the client already holds a session from an earlier page view of this visit (research.md §15's session-growth case) and is requesting allocation for newly-matched pools only; omitted on the very first page view of a session, and always ignored for a website with multi_pool_enabled = false (single-pool websites never resume a session this way — FR-010's existing "no second allocation" behavior is unchanged)
}
```

**Response 200** (consent_granted = true and allocation succeeded, `multi_pool_enabled = false` — unchanged from today)
```json
{ "session_id": "string", "number": "string", "expires_at": "2026-08-10T12:30:00Z" }
```

**Response 200** (consent_granted = false, or pool exhausted / allocation service degraded — FR-011, FR-039; `multi_pool_enabled = false`)
```json
{ "session_id": null, "number": "<website default_number>", "reason": "no_consent | pool_exhausted | unavailable" }
```
No session or allocation record is created for this response shape (FR-039, FR-020's allocation-failure counterpart).

**Response 200** (`multi_pool_enabled = true` — FR-050): every response additionally carries the website's pool→number map, regardless of consent state or whether `matched_pool_ids` was supplied — this is static pool metadata, not an allocation, so returning it pre-consent creates no session, allocates no number and stores no identifier (FR-039 governs those three actions only):
```json
{ "pools": [ { "pool_id": "string", "default_number": "string" } ], "...": "plus whichever shape below applies" }
```
- `consent_granted = false`, or `matched_pool_ids` omitted/empty (matching not yet done): `{ "session_id": null, "reason": "no_consent | pending_match" }` alongside `pools`.
- `consent_granted = true` and `matched_pool_ids` non-empty: the server MUST first drop any requested pool id not actually scoped to `website_id` (FR-050) — this endpoint is unauthenticated and origin-restricted rather than authenticated (FR-037), so a client-supplied id is untrusted input; silently dropping an out-of-scope id, rather than erroring, matches how a malformed/out-of-scope request is already handled elsewhere on this endpoint. If the request carries `session_id` and that session is still active, its existing allocations MUST be left untouched and only the pool ids among `matched_pool_ids` it does not already hold are allocated (research.md §15's session growth); if `session_id` is absent, unknown, or expired, a new Session (and Visitor) is created and every remaining requested pool is attempted. One Tracking Number is allocated per pool attempted that has one available, and `allocations` in the response below lists only what was newly allocated by this call — a resumed session's already-held allocations are not repeated, since the client already holds them from its earlier response:
  ```json
  { "session_id": "string", "allocations": [ { "pool_id": "string", "number": "string", "expires_at": "2026-08-10T12:30:00Z" } ] }
  ```
  A requested pool with no number available is omitted from `allocations` and instead reported the same way a `pool_exhausted` single-pool allocation is, scoped to that pool: the client falls back to that pool's own `default_number` (already in hand from `pools`) for that pool's occurrences only (FR-050), while every other requested pool's allocation proceeds normally.

**Errors**: 403 origin not permitted; 429 rate limit exceeded (both fail closed to the default number on the client side, per FR-011 — the client never blocks rendering on this call).

## POST /v1/dni/heartbeat

Keeps an active session's allocation(s) alive; called every `heartbeat_interval_seconds` (default 300s, FR-012). One call keeps every one of the session's active allocations alive together, regardless of how many pools it holds (FR-050) — the request shape never changes with pool count.

**Request**
```json
{ "session_id": "string" }
```

**Response 200** (`multi_pool_enabled = false` — unchanged from today)
```json
{ "still_valid": true, "number": "string" }
```
```json
{ "still_valid": false, "number": "<default_number>" }
```

**Response 200** (`multi_pool_enabled = true`, FR-050): validity and number are reported per allocation rather than once for the whole session, since one pool's Tracking Number could in principle be released independently of another's:
```json
{ "still_valid": true, "allocations": [ { "pool_id": "string", "still_valid": true, "number": "string" } ] }
```
`still_valid: false` at the top level means the whole session expired since the last heartbeat; a per-allocation `still_valid: false` means only that one pool's allocation lapsed while the rest of the session remains active — the client falls back to that pool's own `default_number` for its occurrences only, exactly as the equivalent case on `/allocate` does.

`still_valid: false` MUST NOT cause the client to call `/allocate` automatically — a fresh page view does that (FR-010's "for the whole of their active session" boundary).

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
