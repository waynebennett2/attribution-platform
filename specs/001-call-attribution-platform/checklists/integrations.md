# Integrations & Publication Requirements Checklist: Call Attribution Platform

**Purpose**: Validate the quality (completeness, clarity, consistency, measurability, coverage) of the requirements governing 8x8 call-data ingestion and Google Ads/GA4 publication — the platform's two external-system boundaries and the source of most idempotency/correction complexity.
**Created**: 2026-08-10
**Feature**: [spec.md](../spec.md), [plan.md](../plan.md), [research.md](../research.md)
**Depth**: Standard — pre-implementation author review before `/speckit-tasks`.

**Note**: Every item below tests whether a requirement is well-written, not whether any implementation behaves correctly — there is no implementation yet.

## Requirement Completeness

- [ ] CHK001 Is there a defined retention/timeout for Call Legs that arrive with no matching parent Call Detail Record, so orphaned legs don't accumulate indefinitely? [Gap, Spec Edge Cases §Ingestion resilience]
- [ ] CHK002 Is a first-attempt publication latency target defined — how soon after qualification a Google Ads/GA4 publish attempt begins — distinct from SC-007's 24-hour completion bar? [Gap, Spec §FR-025, FR-026, SC-007]
- [ ] CHK003 Does the plan/research record how the platform will detect a destination that accepts a request but silently discards the payload, per the spec's explicit deferral of that decision to technical planning? [Gap, Spec Edge Cases §Qualification and publication, cross-ref plan.md/research.md]
- [ ] CHK004 Is a prioritization or throttling rule defined for a large backfill catch-up (after extended source unavailability) so it doesn't starve the SC-007 24-hour publication bar for calls still arriving on the live feed? [Gap, Spec Edge Cases §Ingestion resilience, SC-007]

## Requirement Clarity

- [ ] CHK005 Is "what was sent" in FR-028's per-attempt record specified as the exact payload transmitted, or only a summary of it? [Clarity, Spec §FR-028]
- [ ] CHK006 Is the retention/de-identification treatment of a stored publication payload (FR-028) explicitly tied to the FR-040 tiers, given it may itself contain GCLID/GA4-client-id identifiers? [Gap, Spec §FR-028, FR-040]

## Requirement Consistency

- [ ] CHK007 Does the Edge Cases bullet on an aged or rejected Google click identifier cross-reference the general permanent-rejection handling already defined in User Story 5's acceptance scenarios, or does it read as a separate, unaddressed case? [Traceability, Spec Edge Cases §Qualification and publication, User Story 5 Scenario 5]
- [x] CHK008 Does FR-027's "no qualified call ever produces more than one conversion... regardless of retries" specify whether the idempotency key is scoped per lifetime-of-the-call or per publish-episode, given FR-044 allows a call to be retracted and potentially re-qualified later? [Ambiguity, Spec §FR-027, FR-044] — Resolved via clarification 2026-08-10: scoped per publish episode; a genuine retract-then-requalify gets a new key.

## Acceptance Criteria Quality

- [ ] CHK009 Is FR-016's claim that "changing the cadence MUST NOT alter any attribution outcome" backed by a corresponding, independently verifiable Success Criterion, or only asserted within the functional requirement itself? [Measurability, Spec §FR-016]

## Scenario Coverage

- [ ] CHK010 Are requirements defined for a Call Leg that never finds its parent Call Detail Record within any bounded time (source never sends it, or it was dropped)? [Coverage, Gap]
- [ ] CHK011 Are requirements defined for the ordering or precedence between a live-ingestion cycle and a concurrently-running backfill touching an overlapping time range? [Coverage, Spec Edge Cases §Ingestion resilience]

## Edge Case Coverage

- [ ] CHK012 Is behavior specified for a qualified call whose destination publication is still pending when a manual review resolution changes its qualification a second time before the first publish attempt completes? [Edge Case, Gap]

## Dependencies & Assumptions

- [ ] CHK013 Is the assumption that Google Ads' retraction/adjustment API and GA4's Measurement Protocol behave as described validated against current API documentation, or only asserted as a point-in-time capability? [Assumption, Spec Assumptions §Dependencies]

## Notes

- Check items off as completed: `[x]`
- Add findings inline as you resolve each item (e.g., "Resolved via clarification 2026-08-11" or "Confirmed non-issue: ...")
- Items marked `[Gap]`, `[Ambiguity]`, or `[Conflict]` that remain unchecked are candidates for a further `/speckit-clarify` pass before `/speckit-tasks`.
