# DNI Client & UX Flow Requirements Checklist: Call Attribution Platform

**Purpose**: Validate the quality (completeness, clarity, consistency, measurability, coverage) of the requirements governing the visitor-facing insertion script — number replacement, session stickiness, consent gating, and fallback behavior — the surface the constitution requires browser-level tests for because no server-side test can evidence it.
**Created**: 2026-08-10
**Feature**: [spec.md](../spec.md), [plan.md](../plan.md), [contracts/dni-api.md](../contracts/dni-api.md), [contracts/consent-contract.md](../contracts/consent-contract.md)
**Depth**: Standard — pre-implementation author review before `/speckit-tasks`.

**Note**: Every item below tests whether a requirement is well-written, not whether any implementation behaves correctly — there is no implementation yet.

## Requirement Completeness

- [ ] CHK001 Is the set of DOM locations counted as a "click-to-call target" for FR-008 enumerated — `tel:` links only, or also data-attributes or JS click handlers some sites use for dialing? [Gap, Spec §FR-008]
- [ ] CHK002 Is a replacement-latency bound defined for numbers rendered after initial load (FR-009), so a visitor cannot see or click an untracked number before replacement completes? [Gap, Spec §FR-009]
- [ ] CHK003 Is retry/backoff behavior specified for the DNI client when an allocation request is rejected for rate limiting (FR-037), so repeated page interactions during a rate-limited period don't compound the condition? [Gap, Spec §FR-011, FR-037]

## Requirement Clarity

- [ ] CHK004 Is "configured phone number occurrence" in FR-008 specified to include multiple textual formatting variants of the same number (spacing, punctuation, international prefix), or only an exact string match? [Ambiguity, Spec §FR-008]
- [x] CHK005 Is "malformed" defined for FR-011's "never display a blank, partial or malformed number" — e.g., a tracking number substituted into a site's expected display pattern in a way that breaks that pattern? [Ambiguity, Spec §FR-011] — Resolved 2026-08-10: digit sequence must match exactly; formatting-only differences (spacing, punctuation) don't count as malformed.
- [ ] CHK006 Is the boundary of "that page view" in FR-014/FR-015's retain-until-consent language precisely defined for a single-page application, where no full page reload marks an obvious end? [Ambiguity, Spec §FR-014, FR-015, FR-009]

## Requirement Consistency

- [x] CHK007 Where a website's raw markup does not natively display any number matching the configured default — i.e., replacement is normally what puts a number on the page at all — does FR-039's "the website's configured default number MUST remain displayed" require the client to actively write the default number in pre-consent, or perform no DOM change at all? [Conflict, Spec §FR-008, FR-011, FR-039] — Resolved via clarification 2026-08-10: script actively writes the default number in pre-consent, same replacement mechanism as post-consent.
- [ ] CHK008 Is a grace period or retry allowance specified for a heartbeat delivery failure (a network blip) that is distinct from genuine visitor inactivity, so a session doesn't end early purely from one missed heartbeat? [Gap, Spec §FR-012]

## Acceptance Criteria Quality

- [ ] CHK009 Can SC-003's "at least 99.9% of tracked page views display either an allocated tracking number or the configured default number" be measured client-side, server-side, or both — is the measurement point specified? [Measurability, Spec §SC-003]

## Scenario Coverage

- [ ] CHK010 Are requirements defined for a visitor whose allocation call succeeds but whose subsequent heartbeat calls are silently blocked (e.g., by a browser extension) rather than genuinely inactive? [Coverage, Gap]

## Edge Case Coverage

- [ ] CHK011 Is behavior specified for a phone number appearing inside an iframe or shadow DOM that the DNI client's replacement logic may not traverse by default? [Edge Case, Gap]
- [ ] CHK012 Is behavior specified for a visitor who opens a new tab to the same site mid-session via a link carrying different UTM parameters — does the existing session/number persist, and is the new arrival information captured anywhere? [Edge Case, Gap, Spec §FR-010, FR-014]

## Dependencies & Assumptions

- [ ] CHK013 Is the assumption that a website's phone-number markup is static and known at configuration time (rather than dynamically generated per visitor from third-party data) validated against realistic customer site patterns? [Assumption, Spec §FR-008]

## Notes

- Check items off as completed: `[x]`
- Add findings inline as you resolve each item (e.g., "Resolved via clarification 2026-08-11" or "Confirmed non-issue: ...")
- Items marked `[Gap]`, `[Ambiguity]`, or `[Conflict]` that remain unchecked are candidates for a further `/speckit-clarify` pass before `/speckit-tasks`.
