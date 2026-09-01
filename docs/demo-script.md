# Demo script — Call Attribution Platform

A ~15-minute walkthrough for stakeholders, using the Meridian & Manor mock site
(`mock/site`) pointed at your deployed API. Run this only after `scripts/smoke-test.ps1`
passes and the UAT checklist (`docs/uat-checklist.md`) is signed off.

## Before you present

- [ ] Run `scripts/smoke-test.ps1 -ApiBaseUrl <your-api> -WebsiteId <demo-website-id> ...` once, right before the room fills up.
- [ ] Confirm `scripts/seed-dev-data.sql` (or your demo-specific equivalent) has been applied to the target DB — you need the Meridian & Manor website, its three care-home pools, and their tracking numbers in place.
- [ ] Open `mock/site/index.html?api=https://<your-api-host>` once beforehand so the origin is saved to that browser's `localStorage` — don't fumble the query string live.
- [ ] Have a second browser tab signed in to the Admin UI already, as a Marketing Administrator.
- [ ] **Fallback plan**: if live allocation misbehaves on the day, have a screenshot/recording of a previous successful run, and a pre-seeded set of calls already sitting in Reports so you can still show that half of the story.

## 1. The visitor side (~4 min)

1. Open `mock/site/index.html` (no `?debug=1` yet — keep it clean for the first pass). Narrate: *"This is a real customer website — nothing here is faked; it's loading our actual production tracking script."*
2. Click through to `care-homes.html`. Point out the three care homes, each with its own phone number.
3. Reload the page. Ask the room to note the numbers before and after — they're unchanged (sticky session), even though the page reloaded.
4. Reload again with `?debug=1` appended. Point at the status panel: API base, website ID, session ID, three concurrent allocations — *"one per care home, from three independent pools."*
5. Talking point: *"Each of those numbers was allocated the instant the page loaded, from a pool of real 8x8 numbers, and it's now uniquely tied to this visit for the next [session timeout] minutes."*

## 2. Where that data lands (~4 min)

1. Switch to the Admin UI tab. Go to **Pools** → open one of the three care-home pools. Show the tracking number that was just allocated now marked held/active.
2. Go to **Websites** (or wherever session/allocation detail surfaces) — show the session just created, its landing page, referrer, and any UTM parameters if the demo URL carried them.
3. Talking point: *"Every arrival detail Mediahawk gave you — UTM, GCLID, referrer — is captured the same way, against a session id that's the join key for everything that happens next."*

## 3. A call comes in (~4 min)

Pick one:
- **If you can place a real call** to one of the demo numbers ahead of time: place it live, let it ring/answer, then move to Reports.
- **If not**: have engineering seed one attributed call beforehand and simply narrate the pipeline as you show it (be upfront that it's pre-seeded — credibility matters more than theatre).

1. Open **Reports → Calls**. Find the call; point out its state (`Attributed`), the session it's linked to, and the evidence (number + window).
2. Open **Reports → Qualified** (or the dashboard). Show it counted as a qualified conversion once past the duration threshold, with the qualification rule version recorded.
3. Talking point: *"No guessing — if the evidence had been ambiguous, this would have landed in the manual review queue instead of being silently attributed."* (Optionally show **Admin → Review Cases** to prove the queue exists, even if empty.)

## 4. Closing the loop (~2 min)

1. If Google Ads/GA4 are configured for the demo: show **Admin → Integration Health** with the publication outcome for that call (sent/adjusted).
2. If not configured: describe it — *"once qualified, this same call would report an offline conversion back to Google Ads against the original click, and an event to GA4 — automatically, exactly once, with retry-safe delivery."*

## 5. Wrap-up (~1 min)

- Show **Admin → Audit Log** briefly — *"every administrative action, and every sign-in attempt, is written here and can't be edited or deleted."*
- Tie back to the business case: *"This replaces Mediahawk end-to-end — DNI, attribution, qualification, reporting, and publication — running on infrastructure you control, deployed and rolled back with a single script."*

## Q&A landmines to be ready for

- **"What happens if two people share the same number at once?"** — pools reuse numbers only after a cooldown once released; concurrent allocation is handled atomically in the database (`FOR UPDATE SKIP LOCKED`), not by hope.
- **"What if the call can't be matched to a session?"** — shown live in the Unattributed report; it's never silently dropped.
- **"Can marketing break something by changing a qualification rule?"** — no: rules are versioned, and every past call keeps the rule version that actually judged it.
- **"Is this GDPR-safe?"** — visitor de-identification/erasure exists (`POST /v1/admin/privacy/visitors/{id}/erase`); happy to demo it separately if asked.
