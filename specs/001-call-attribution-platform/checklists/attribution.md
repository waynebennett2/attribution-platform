# Attribution & Qualification Requirements Checklist: Call Attribution Platform

**Purpose**: Validate the quality (completeness, clarity, consistency, measurability, coverage) of the requirements governing tracking-number allocation, deterministic call attribution, and qualification-rule evaluation — the platform's core value proposition and its hardest-to-get-right domain logic.
**Created**: 2026-08-10
**Feature**: [spec.md](../spec.md), [plan.md](../plan.md), [data-model.md](../data-model.md)
**Depth**: Standard — pre-implementation author review before `/speckit-tasks`.

**Note**: Every item below tests whether a requirement is well-written, not whether any implementation behaves correctly — there is no implementation yet.

## Requirement Completeness

- [ ] CHK001 Are the validation rules that make a bulk-imported number entry "malformed" (FR-002) explicitly enumerated, rather than left to implementation judgment? [Gap, Spec §FR-002]
- [ ] CHK002 Are the evidence fields recorded for a shadow-mode allocation (FR-049) fully enumerated and distinguished from an ordinary allocation's evidence? [Completeness, Spec §FR-019, FR-049]
- [ ] CHK003 Is the exact structure of a qualification rule's time-of-day condition specified — start/end time, applicable days of week, and which timezone it evaluates against? [Gap, Spec §FR-023]

## Requirement Clarity

- [ ] CHK004 Is "concurrent sessions" in FR-003's atomic-allocation guarantee precise enough to cover two simultaneous allocation requests from the same visitor's two open tabs, not just two different visitors? [Clarity, Spec §FR-003]
- [x] CHK005 Is the timezone a qualification rule's time-of-day condition evaluates against specified — the platform's single canonical storage timezone (Assumptions §Attribution and data) or each website's local timezone? [Ambiguity, Spec §FR-023] — Resolved via clarification 2026-08-10: each website's local timezone.
- [x] CHK006 Is "short-lived" quantified for the platform-issued JWT, given SC-016 requires access to be refused on a revoked user's "next request" without waiting for session expiry? [Ambiguity, Spec §FR-046, SC-016] — Resolved via clarification 2026-08-10: ~5-minute JWT with silent refresh; SC-016 reworded to a 5-minute bound rather than a literal next-request guarantee.

## Requirement Consistency

- [x] CHK007 Does FR-039's "release the allocated number" on consent withdrawal align with FR-006/FR-018's rule that a number is only released once its allocation window (session end + the configurable extension) closes — or is withdrawal meant as a stated exception to that extension? [Conflict, Spec §FR-039, FR-006, FR-018] — Resolved via clarification 2026-08-10: withdrawal is an explicit exception, immediate release, FR-006 cooldown still applies.
- [ ] CHK008 Are the five qualification-rule condition dimensions named in FR-023 (direction, answered, duration, website/campaign, time-of-day) consistently reflected in the Qualification Rule key entity description? [Consistency, Spec §FR-023, Key Entities]

## Acceptance Criteria Quality

- [ ] CHK009 Can "the true session known for every call" (SC-001's rationale) be verified by an automated test harness, or does it depend on a tester's independent record-keeping that the platform can't itself validate? [Measurability, Spec §SC-001]
- [ ] CHK010 Is SC-018's "at least 95% reach an attributed state" paired with a defined method for computing the stated confidence interval, or only asserted as a target figure? [Measurability, Spec §SC-018]

## Scenario Coverage

- [ ] CHK011 Are requirements defined for a call ingested while its matching Attribution or Qualification Result is mid-supersession under FR-045 (i.e., a restatement is being processed at the same moment)? [Coverage, Gap]
- [ ] CHK012 Are requirements defined for a Review Case resolution (FR-036) arriving concurrently with an FR-045 re-derivation on the same call? [Coverage, Gap]

## Edge Case Coverage

- [ ] CHK013 Is the behavior specified when a qualification rule's website/campaign scope (e.g., the website itself) is deleted or reassigned while a version of that scope's rule is still effective? [Edge Case, Gap]
- [ ] CHK014 Are requirements defined for a call whose start time falls exactly on a qualification rule version's `effective_start` boundary? [Edge Case, Spec §FR-024]

## Dependencies & Assumptions

- [ ] CHK015 Is the assumption that 8x8 and platform timestamps share "a known and stable timezone" (Assumptions §Attribution and data) validated against 8x8's actual API behavior, or only asserted? [Assumption, Spec Assumptions §Attribution and data]

## Notes

- Check items off as completed: `[x]`
- Add findings inline as you resolve each item (e.g., "Resolved via clarification 2026-08-11" or "Confirmed non-issue: ...")
- Items marked `[Gap]`, `[Ambiguity]`, or `[Conflict]` that remain unchecked are candidates for a further `/speckit-clarify` pass before `/speckit-tasks`.
