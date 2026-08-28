# Meridian & Manor Healthcare — mock demo site

A static two-page mock site for demoing the Call Attribution Platform end-to-end, per
`mock/Mock website.md`. Purely static HTML/CSS/JS, no build step, no framework — it embeds
the real DNI client (`client/dni-script/src/index.js`) directly by relative path, so the
demo exercises the actual production script rather than a mock of it.

- `index.html` — landing page: logo, company description, postcode search.
- `care-homes.html` — always shows the same three mock care homes, regardless of postcode.
  Each has its own number pool (FR-050 multi-pool DNI), so each gets its own independently
  allocated tracking number.
- `assets/attribution.js` — wires `window.__attributionConfig` and a small settings panel for
  pointing the demo at your API once it's deployed.
- `assets/search.js` — page 1's postcode form (light format validation only; no real lookup).

## Running it

Serve the **repo root** (not just `mock/site`) with any static file server, since the pages
import the DNI client from `../../client/dni-script/src/`:

```
cd attribution-project
python -m http.server 4173
```

Then open `http://localhost:4173/mock/site/index.html`.

Port **4173** (or 3000) matters: `scripts/seed-dev-data.sql` seeds the demo Website's
`permitted_origins` as `http://localhost:4173` / `http://localhost:3000`, and the API
rejects cross-origin DNI calls from origins not on that list. Serving from another port
will need that column updated (or the origin check will reject the allocate/heartbeat
calls).

## Pointing at the real API

Once the API is deployed, either:

- Open the page with `?api=https://your-api-host` once — it's saved to `localStorage` and
  reused on every later visit, or
- Use the "Demo settings" panel in the footer of either page.

It defaults to `http://localhost:8080` (the nginx port in the repo's `docker-compose.yml`).

Before any of this works, run `scripts/seed-dev-data.sql` against the target database — it
creates the Website row (`00000000-0000-0000-0000-000000000001`) with `multi_pool_enabled=1`,
its three number pools (one per care home), and the default qualification rule the demo
relies on.

## Independent per-care-home numbers

`mock/Mock website.md` describes each of the three care homes showing its own distinct phone
number that gets swapped independently. This works via FR-050 multi-pool DNI: the seeded
Website has three number pools, each with its own `default_number` (`01632 960010/11/12`,
one per card on `care-homes.html`). The DNI client (`client/dni-script/src/index.js`) matches
each pool's `default_number` against the page locally and requests an allocation per pool it
finds, so each care home's number is replaced independently rather than the whole page
swapping to one shared number.

Each card's phone number is plain text/`tel:` link only — no `data-attribution-number`
marker attribute. That marker is a page-wide "replace this element regardless of its current
number" mechanism (see `replace.js`), which isn't scoped per pool; using it here would let
whichever pool's replacer runs last overwrite all three cards.

## Consent

This mock has no cookie/consent banner. `assets/attribution.js` sets
`window.__attributionConsent = { granted: true }` before the DNI client runs, so tracking
numbers are injected onto the page immediately on load, with no visitor action required.

## Debug status panel

Add `?debug=1` to either page's URL to show a small status box (API base, website ID,
consent state, current session) — useful for verifying allocation is actually happening
once the API is live.
