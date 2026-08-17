# Security, Privacy & Compliance Requirements Checklist: Call Attribution Platform

**Purpose**: Validate the quality (completeness, clarity, consistency, measurability, coverage) of the requirements governing authentication/authorization, consent, retention/de-identification, erasure, audit immutability, and alerting — the area the constitution treats as non-negotiable alongside deterministic attribution.
**Created**: 2026-08-10
**Feature**: [spec.md](../spec.md), [plan.md](../plan.md)
**Depth**: Standard — pre-implementation author review before `/speckit-tasks`.

**Note**: Every item below tests whether a requirement is well-written, not whether any implementation behaves correctly — there is no implementation yet.

## Requirement Completeness

- [x] CHK001 Is the accepted MFA method (or methods) for sign-in specified — TOTP, hardware key, SMS — or left fully open? [Gap, Spec §FR-046] — Resolved via clarification 2026-08-17: TOTP, mandatory for every local account (no longer break-glass-only).
- [ ] CHK002 Is the retention/de-identification job's run cadence specified (continuous, daily, etc.), given FR-040 requires purge or de-identification "automatically once it expires"? [Gap, Spec §FR-040]
- [ ] CHK003 Are the webhook payload's authentication or signing requirements specified, so a customer's endpoint can verify a request genuinely originated from the platform rather than a spoofed sender? [Gap, Spec §FR-047]
- [ ] CHK004 Is the acknowledgement authority for an alert restricted to specific roles, or can any authenticated user acknowledge any alert? [Gap, Spec §FR-047]

## Requirement Clarity

- [x] CHK005 Is the JWT lifetime/expiry duration quantified anywhere, given it underpins both FR-046's sign-in model and SC-016's immediate-revocation bar? [Gap, Ambiguity, Spec §FR-046, SC-016] — Resolved via clarification 2026-08-10: 5-minute access-token lifetime; 2026-08-17 clarified the refresh mechanism as a rotating refresh token rather than federated-session silent refresh.
- [ ] CHK006 Are the specific fields that must be redacted or surrogate-replaced during de-identification enumerated, or is "identifiers" left to implementation judgment? [Clarity, Spec §FR-040]
- [x] CHK007 Is any account count still capped now that local sign-in is the platform's sole interactive method rather than a break-glass fallback? [Clarity, Spec §FR-046] — Resolved via clarification 2026-08-17: the former break-glass cap of 2 no longer applies; local accounts are unlimited, with the sole constraint that at least one active System Administrator account must always exist.

## Requirement Consistency

- [ ] CHK008 Are role-based authorization requirements consistent between FR-031 (report/export access) and FR-038 (every operation), or does FR-031 restate a subset of FR-038 in a way that could drift as either is edited independently? [Consistency, Spec §FR-031, FR-038]
- [ ] CHK009 Does the 30-day erasure SLA (SC-019) account for data still referenced by an open manual review case, the same exception FR-040 already grants ordinary retention purging? [Consistency, Spec §FR-039, FR-040, SC-019]

## Acceptance Criteria Quality

- [ ] CHK010 Can "zero identifiers are stored on the device" (SC-013) be objectively verified given a browser exposes multiple storage surfaces (cookies, localStorage, IndexedDB, cache, service-worker storage) — does the requirement enumerate which surfaces are in scope for verification? [Measurability, Spec §SC-013]
- [x] CHK011 Is the mechanism by which SC-016's "next request" revocation is actually achieved specified (per-request identity-provider check vs. a very short token TTL vs. token introspection), or only the outcome asserted? [Gap, Spec §FR-046, SC-016] — Resolved via clarification 2026-08-10: short-lived JWT (~5 min) + silent refresh.

## Scenario Coverage

- [ ] CHK012 Is the audit log's "before and after values" requirement specified for creation-type actions (e.g., a bulk number import) where no prior "before" state exists? [Gap, Edge Case, Spec §FR-035]
- [ ] CHK013 Are requirements defined for what happens to an open Review Case resolution or an in-flight Conversion Publication correction if the audit log itself is temporarily unavailable? [Coverage, Gap]

## Edge Case Coverage

- [ ] CHK014 Is "an identified visitor" in FR-039's erasure requirement defined precisely enough to determine whether a record already de-identified under FR-040 is in or out of scope for a subsequent erasure request? [Ambiguity, Spec §FR-039, FR-040]

## Dependencies & Assumptions

- [ ] CHK015 Is the consent event/callback contract (FR-039) versioned, so a future breaking change to its shape doesn't silently break every already-integrated customer site's consent tooling? [Gap, Spec §FR-039]

## Notes

- Check items off as completed: `[x]`
- Add findings inline as you resolve each item (e.g., "Resolved via clarification 2026-08-11" or "Confirmed non-issue: ...")
- Items marked `[Gap]`, `[Ambiguity]`, or `[Conflict]` that remain unchecked are candidates for a further `/speckit-clarify` pass before `/speckit-tasks`.
