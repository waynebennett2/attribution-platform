# Meridian & Manor Healthcare — mock demo site

A static two-page mock site for demoing the Call Attribution Platform end-to-end, per
`mock/Mock website.md`. Purely static HTML/CSS/JS, no build step, no framework — it embeds
the real DNI client (`client/dni-script/src/index.js`) directly by relative path, so the
demo exercises the actual production script rather than a mock of it.

- `index.html` — landing page: logo, company description, postcode search.
- `care-homes.html` — always shows the same three mock care homes, regardless of postcode.
- `assets/attribution.js` — wires `window.__attributionConfig`, the cookie-consent banner,
  and a small settings panel for pointing the demo at your API once it's deployed.
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
creates the Website row (`00000000-0000-0000-0000-000000000001`), its number pool, and the
default qualification rule the demo relies on.

## Known limitation: one shared tracking number, not three

`mock/Mock website.md` describes each of the three care homes showing its own distinct
phone number that gets swapped independently. The platform can't do that today: the seed
data creates exactly **one** Website with **one** number pool, and the DNI client
(`sessionStore.js`) keys its session in `localStorage` purely by `websiteId` — running three
independent `initAttribution()` instances against the same `websiteId` would collide (last
write wins, heartbeats stomping each other), and the client's `apply()` always rewrites
every matched number on the page to the *one* number it was just given.

So this mock instead shows the same default number (`01632 960000`, matching the seeded
Website's `default_number`) on all three cards, and after consent is granted, all three
cards swap **together** to the same allocated tracking number. That's an accurate demo of
what the platform currently does (a page-wide dynamic number swap), just not independent
per-listing attribution. Giving each care home its own tracking number would need three
separate Website/number-pool rows in the DB (i.e. modeling each care home as its own
"website" in the platform's data model) plus either three scoped script instances or an
extension to the DNI client to support multiple concurrent numbers on one page — both are
backend/script changes outside the scope of this static mock.

## Consent banner

The bottom banner is a stand-in cookie/consent prompt. Accept/Decline call the same
`window.__attributionConsent` + `attribution:consent-change` event contract the real script
expects (see `client/dni-script/src/consent.js`), matching how
`client/dni-script/tests/fixtures/demo.html` drives it for manual testing. The choice is
remembered in `localStorage` (`mm-demo:consent`) and can be reopened via "Manage cookie
preferences" in the footer.

## Debug status panel

Add `?debug=1` to either page's URL to show a small status box (API base, website ID,
consent state, current session) — useful for verifying allocation is actually happening
once the API is live.
