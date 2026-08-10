# Contract: Consent Signal (client-side JS)

Published by the DNI client per FR-039 (research.md §12) so any CMP or custom consent tool can be wired to it once, rather than each deployment needing bespoke integration code.

## Read on load

```js
window.__attributionConsent // { granted: boolean } | undefined
```
If `undefined` or `granted: false` at the time the DNI script evaluates, the page keeps showing the default number and no session/allocation call is made (FR-039).

## Subscribe for later changes

```js
window.addEventListener('attribution:consent-change', (event) => {
  // event.detail === { granted: boolean }
});
```
The deploying site's consent mechanism (CMP or custom) MUST dispatch this event whenever the visitor's consent decision changes during the page view — both a first grant and a later withdrawal. The DNI client remains subscribed for the life of the page view (FR-039) and reacts by calling `POST /v1/dni/consent` (see `dni-api.md`).

## Non-goals

This contract carries only a boolean consent state — it is not a consent-category system (marketing vs. analytics vs. necessary, etc.). The platform's data collection is all-or-nothing per FR-039; category-level consent, if a customer's CMP exposes it, is the deploying site's responsibility to collapse into this single boolean before dispatching.
