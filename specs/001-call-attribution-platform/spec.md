# Feature Specification: Call Attribution Platform

**Feature Branch**: `001-call-attribution-platform`

**Created**: 2026-08-06

**Status**: Draft — all clarifications resolved

**Input**: User description: Build a call attribution platform that replaces Mediahawk as our marketing attribution tool, using 8x8 Work as the sole telephony provider. Website visitors are shown a phone number drawn from a pool of 8x8 numbers (Dynamic Number Insertion); each session gets a unique number for its duration so inbound calls can be deterministically matched back to the session that displayed it. The system ingests call records from Analytics for 8x8 Work, reconstructs the call journey, and attributes calls using only strict, certain matches — unmatched calls stay unattributed rather than guessed. Attributed calls are evaluated against configurable rules to decide whether they count as a marketing conversion; qualifying calls are published to Google Ads (offline conversions) and Google Analytics 4 (Measurement Protocol). Enterprise-grade administration, reporting, audit trails, security and high availability are required, as this is intended as a reusable commercial platform.

<details>
<summary>Full original description as provided</summary>

Core capabilities requested: tracking number management (pools of 8x8 DIDs; import/activate/suspend/retire; atomic allocation; separate pools per website, campaign or business unit); Dynamic Number Insertion (replace every configured number on the page; multi-page and single-page applications; sticky per session; fall back to a default number if allocation fails); visitor and session tracking (visitor ID, session ID, landing page, referrer, UTM parameters, GCLID/GBRAID/WBRAID, configurable session timeout and heartbeat); 8x8 call data ingestion (authenticate against Analytics for 8x8 Work; poll Call Detail Records and Call Legs; ingestion checkpoints so nothing is double-processed or missed after restart; replay/backfill of historical data); call attribution (strict DID + time-window matching only, no probabilistic or fuzzy matching; store the evidence behind every match; classify unmatchable or ambiguous calls into explicit states rather than guessing); conversion qualification (default rule: inbound, answered, connected 60+ seconds; overridable via configurable rules; rules versioned over time so we know which rule applied to a given call); marketing integrations (Google Ads offline conversions; GA4 Measurement Protocol); reporting (executive dashboard, campaign performance, call detail search, missed calls, qualified calls, unattributed calls, CSV export from any report); administration and audit (users and roles, number pools and numbers, attribution and qualification rules, integration health, manual review workflow for calls needing human judgement, audit log of all administrator actions).

User roles: System Administrator (full configuration and user management); Marketing Administrator (campaigns, reporting, attribution review); Analyst (read-only reporting); Integration Service (system-to-system access, no interactive UI access).

Success criteria: DNI consistently displays the correct allocated 8x8 number; at least 95% of eligible calls correctly attributed during a parallel run alongside Mediahawk; no call attributed more than once; qualified calls successfully uploaded to Google Ads; GA4 events generated correctly; every administrator action captured in the audit log; system passes functional, integration, performance and security testing.

Non-functional expectations: 99.9% availability; 95% of DNI number-allocation requests under 300ms; authenticated access, role-based permissions, full audit logging of sensitive actions; horizontal scalability; idempotent and safely retryable ingestion; structured logging, metrics and health checks; consent-aware data handling with configurable retention.

Out of scope for this version: AI-based call transcription and sentiment analysis; CRM connectors; revenue attribution; predictive marketing analytics; real-time dashboards; multi-tenant SaaS deployment.

</details>

## Clarifications

### Session 2026-08-10

- Q: What request-rate limits should the visitor-facing number allocation and heartbeat endpoints enforce per origin and per client? → A: Derive from the already-agreed peak scale — 600 requests/minute per origin, 10 requests/minute per client
- Q: How many local break-glass administrator accounts should the platform support by default? → A: 2 (one primary, one backup), configurable upward per deployment
- Q: Should a call sitting in the manual review queue have a maximum time it's allowed to remain unresolved before it's escalated or flagged as overdue? → A: Yes — alert if unresolved past 48 hours, using the same threshold-alerting mechanism as other operational conditions
- Q: What file format(s) must the bulk number-import feature accept for loading tracking numbers into a pool? → A: CSV only, matching the platform's CSV-first export story
- Q: How should the insertion script detect a website's consent decision? → A: Via a platform-defined JS event/callback contract that any site's consent mechanism is wired to fire, rather than a bespoke per-site adapter

### Session 2026-08-09

- Q: When a call's conversion status changes after it was already published, what should happen to the conversion already sent to Google Ads and GA4? → A: Correct where the destination allows it — retract or adjust the Google Ads conversion, record that GA4 cannot be retracted, and audit both
- Q: When 8x8 later sends a changed version of an already-processed call record, should the platform update the call and re-run attribution and qualification? → A: Yes — re-ingest, update the call, re-derive attribution and qualification, keep the prior result as history, and correct any published conversion
- Q: How should platform users sign in? → A: Federated single sign-on against the customer's identity provider, with roles mapped from provider groups and overridable in-platform, plus a small number of local break-glass administrator accounts with MFA
- Q: Should the platform actively notify someone when ingestion stalls, publication starts failing, or a number pool nears exhaustion? → A: Yes — configurable thresholds alerting to email recipients and an outbound webhook, repeated until the condition clears or is acknowledged
- Q: With no parallel run against Mediahawk, what evidence should prove the platform attributes calls correctly? → A: Two gates — a controlled test in which known callers ring known tracking numbers from known sessions, requiring 100% correct attribution with the evidence chain checked, followed by a live measurement of the share of real calls reaching an attributed state, with a recorded reason for every call that does not
- Q: Is the Mediahawk parallel run dropped entirely, or kept as a later option? → A: Kept as a selectable second phase — the platform is accepted on its own standalone evidence first, and the parallel run can then be switched on per website once that testing is satisfactory

**Note**: the parallel run agreed on 2026-08-06 is no longer the launch gate. Acceptance rests on SC-001 and SC-018, which the platform evidences alone. The comparison against Mediahawk remains available as an optional later phase under FR-049 and is validation the business may choose to run, not a condition of shipping. The two 2026-08-06 bullets below are annotated accordingly rather than removed.

### Session 2026-08-06

- Q: How long after a visitor's session ends should their tracking number stay reserved to them, so that a call to it can still be attributed to that session? → A: Session end + 30 minutes, with the cooldown before re-allocation set equal to the window
- Q: Does this build deliver the reporting web interface and the visitor-facing number-insertion script, or only the platform sitting behind them? → A: The platform plus the insertion script. Report data and exports are delivered for the existing reporting portal to render; the reporting interface itself is not built here
- Q: When a visitor has not given consent to being tracked, what should the platform do? → A: Show the default number and track nothing until consent is given; on consent, create the session, capture the arrival details still present in the URL, and allocate a number from that point on
- Q: What peak load should the platform be sized for, measured in concurrent tracked sessions across all websites? → A: Up to approximately 250 concurrent tracked sessions, implying roughly 3,000 tracking numbers under the FR-018 window and FR-006 cooldown
- Q: How often should the platform pull new call records from 8x8, and therefore how fresh must reporting be? → A: Hourly, configurable
- Q: After how long without activity should a visitor's session be treated as over? → A: 30 minutes of inactivity, matching GA4's default, with a 5-minute heartbeat
- Q: At peak, roughly how many new visitor sessions start per minute across all websites? → A: About 7 per minute, implying a tracking number estate of approximately 630 before headroom
- Q: When comparing against Mediahawk during the parallel run, what has to match for a call to count as correctly attributed? → A: The same marketing source, medium and campaign credited by both systems, with disagreements sampled and adjudicated by hand *(2026-08-09 — no longer the launch gate; correctness is proven against seeded calls whose true session is known. This definition still governs if the optional parallel run of FR-049 is later enabled)*
- Q: How long should the platform keep each kind of data before purging it? → A: Tiered with de-identification — visitor and session identifiers de-identified at 14 months, de-identified call and attribution records kept 25 months, audit log 7 years
- Q: How long should the parallel run against Mediahawk last, and how many calls must it cover, before the 95% figure is accepted? → A: At least four weeks and at least 500 eligible calls compared *(2026-08-09 — the four weeks and 500 calls carry over to the live coverage window in SC-018, and continue to apply if the optional parallel run of FR-049 is enabled)*

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Visitor sees a tracked number and the session is recorded (Priority: P1)

A visitor arrives on a marketing website from a Google Ads click. Before they see any phone number, the platform allocates them a tracking number from the pool configured for that website and swaps it into every place a phone number appears on the page. The visitor keeps that same number as they browse. Behind the scenes the platform records who they are (anonymous visitor and session identifiers), how they arrived (landing page, referrer, UTM parameters, Google click identifiers), and which number they were shown, along with the exact time window the number was theirs.

**Why this priority**: Nothing downstream can work without it. The allocation record and its time window are the sole evidence later used to attribute a call, so this story establishes the platform's foundation. It is also independently valuable: it can be run in shadow mode alongside Mediahawk to prove number delivery and session capture before any call data is touched.

**Independent Test**: Load a configured website with UTM parameters and a Google click identifier in the URL, confirm a pool number is displayed in place of every configured number, navigate several pages (including in-app route changes), confirm the number does not change, and confirm a single session record exists holding the arrival details and the allocation window.

**Acceptance Scenarios**:

1. **Given** a consenting visitor and a website with an active number pool containing available numbers, **When** the visitor loads a page containing three instances of the site's static phone number, **Then** all three instances display the same allocated tracking number and one allocation record links that number to the visitor's session with a start time.
2. **Given** a visitor who has already been allocated a number, **When** they navigate to further pages or trigger in-app route changes within their session, **Then** the same number continues to be displayed and no additional number is allocated.
3. **Given** a website whose number pool has no available numbers, **When** a new visitor loads a page, **Then** the site's configured default number is displayed, the page shows no blank or partial number, and the allocation failure is recorded for operational visibility.
4. **Given** a visitor arriving with UTM parameters and a Google click identifier present in the URL, **When** their session is created, **Then** landing page, referrer, all UTM parameters and the click identifier are captured against that session.
5. **Given** a visitor whose session has been inactive beyond the configured session timeout, **When** they load a new page, **Then** a new session is created and a number is allocated for the new session.
6. **Given** a visitor who has not yet answered the site's consent prompt, **When** they load a page carrying UTM parameters and a Google click identifier, **Then** the configured default number is displayed, no session or allocation record is created, and no identifier is stored on their device; **and When** they then give consent on that same page, **Then** a session is created capturing those arrival details, and a tracking number is allocated and displayed from that point.

---

### User Story 2 - An inbound call is deterministically attributed to the session that generated it (Priority: P2)

Call records are pulled from Analytics for 8x8 Work on a schedule. For each inbound call, the platform looks up which session held the dialled number at the moment the call started. If exactly one session held it, the call is attributed to that session and the supporting evidence is stored. If no session held it, the call is recorded as unattributed. If the evidence is genuinely conflicting, the call is recorded as ambiguous and raised for human review. The platform never guesses.

**Why this priority**: This is the core value proposition versus Mediahawk and the thing the 95% accuracy target measures. It depends on Story 1's allocation records but delivers standalone value: marketing can already see which campaigns produced calls, before any qualification or publication exists.

**Independent Test**: Seed known allocation records, feed a set of call records containing matched, unmatched and deliberately conflicting cases, then confirm each call lands in the expected state with retrievable evidence, and confirm that re-feeding the identical call records changes nothing.

**Acceptance Scenarios**:

1. **Given** a call to a tracking number that exactly one session held at the call start time, **When** the call is processed, **Then** the call is attributed to that session and the stored evidence names the number, the session, and the allocation window used.
2. **Given** a call to a number that no session held at the call start time, **When** the call is processed, **Then** the call is recorded as unattributed with the reason, and it appears on the unattributed calls report.
3. **Given** a call whose dialled number and start time match more than one session's allocation window, **When** the call is processed, **Then** the call is recorded as ambiguous, is not attributed to any session, and is raised into the manual review queue.
4. **Given** a batch of call records that has already been fully processed, **When** the identical batch is ingested again, **Then** no call is attributed a second time, no duplicate call records are created, and no counts in any report change.
5. **Given** ingestion stops partway through a batch, **When** ingestion restarts, **Then** processing resumes from the last checkpoint with no call skipped and no call processed twice.
6. **Given** an inbound call that was never answered, **When** the call is processed, **Then** it is attributed using the same strict rules and appears on the missed calls report.
7. **Given** a call ingested while still in progress and judged not qualified on its partial duration, **When** 8x8 later supplies the completed record showing a longer connected duration, **Then** the stored call is updated, its attribution and qualification are re-derived against the rule version in force at the time of the call, the superseded result is retained as history with the reason, and re-ingesting that same completed record again changes nothing further.

---

### User Story 3 - Attributed calls are qualified into marketing conversions (Priority: P3)

An attributed call is evaluated against the qualification rules in force at the time of the call to decide whether it counts as a marketing conversion. Out of the box, a call qualifies when it is inbound, answered, and connected for at least 60 seconds. A Marketing Administrator can define different rules. Because rules are versioned, every call permanently records which rule version judged it, and changing a rule never rewrites history.

**Why this priority**: Converts raw attributed calls into the business signal marketing actually optimises against, and is a prerequisite for publication. Independently valuable because qualified-call reporting alone replaces a large part of what Mediahawk provides.

**Independent Test**: With the default rule active, feed attributed calls just under and just over the 60-second threshold plus unanswered calls, confirm correct qualification; then publish a new rule version, confirm new calls use it and previously judged calls retain their original result and rule version.

**Acceptance Scenarios**:

1. **Given** the default qualification rule is in force, **When** an attributed inbound call is answered and connected for 75 seconds, **Then** the call is marked qualified and records the rule version applied.
2. **Given** the default qualification rule is in force, **When** an attributed inbound call is answered and connected for 45 seconds, **Then** the call is marked not qualified and records the rule version applied.
3. **Given** a set of calls already qualified under rule version 1, **When** an administrator publishes rule version 2 with a different threshold, **Then** the previously judged calls retain their original qualification result and their reference to version 1, and only calls occurring after version 2 takes effect are judged by it.
4. **Given** a call that could not be attributed, **When** qualification runs, **Then** the call is not qualified as a marketing conversion and remains visible on the unattributed calls report.

---

### User Story 4 - Marketing reports on call performance and exports the data (Priority: P4)

A Marketing Administrator or Analyst signs in and sees an executive dashboard summarising call volume, attribution rates and qualified conversions. They can break performance down by campaign, search individual call detail, and review missed, qualified and unattributed calls. Any report can be exported to CSV for offline analysis or sharing.

**Why this priority**: This is how the business consumes the platform and how the parallel run against Mediahawk is actually evidenced. It depends on attribution and qualification data existing, but delivers value without any outbound Google integration.

**Independent Test**: With a known dataset loaded, sign in as each reporting role, confirm every report renders the expected figures that reconcile against the underlying call records, confirm an Analyst cannot reach administrative functions, and confirm each report exports a CSV whose contents match what was displayed.

**Acceptance Scenarios**:

1. **Given** a period containing attributed, unattributed, missed and qualified calls, **When** a Marketing Administrator opens the executive dashboard for that period, **Then** the totals shown reconcile exactly with the underlying call records for that period.
2. **Given** any report is open, **When** the user exports it to CSV, **Then** the export contains the same rows and values as displayed, including the applied filters and period.
3. **Given** a user holding only the Analyst role, **When** they attempt to reach number pool management, rule management or user management, **Then** access is refused and the attempt is recorded.
4. **Given** calls attributed to several campaigns, **When** the user views campaign performance, **Then** each call's contribution is grouped by the campaign captured on its originating session.

---

### User Story 5 - Qualified calls are published to Google Ads and GA4 (Priority: P5)

Once a call qualifies, the platform reports it outward: to Google Ads as an offline conversion tied to the original Google click, and to Google Analytics 4 as an event via the Measurement Protocol. Publication is retried safely on failure and can never create a duplicate conversion. The outcome of every publication attempt is recorded so an administrator can see exactly what was sent, what succeeded, and what needs attention.

**Why this priority**: Closes the loop so campaign bidding can optimise against real phone enquiries. Sequenced after reporting because the internal numbers must be trusted before they are pushed into ad platforms where they influence spend.

**Independent Test**: Qualify a set of calls where some originating sessions carry a Google click identifier and some do not, then confirm the former are published to both destinations exactly once, the latter are handled per the documented rule without error, and forced failures are retried without producing duplicates.

**Acceptance Scenarios**:

1. **Given** a qualified call whose originating session captured a Google click identifier, **When** publication runs, **Then** exactly one offline conversion is reported to Google Ads and exactly one event is reported to GA4, and both outcomes are recorded against the call.
2. **Given** a qualified call that has already been published successfully, **When** publication runs again for any reason, **Then** no second conversion is created at either destination.
3. **Given** a destination is unavailable or returns a transient failure, **When** publication is attempted, **Then** the attempt is retried with backoff, the failure is visible on the integration health view, and success eventually results in exactly one conversion.
4. **Given** a qualified call whose originating session carries no Google click identifier, **When** publication runs, **Then** the call is not reported to Google Ads, the reason is recorded against the call, and the call still appears as qualified in reporting.
5. **Given** a destination permanently rejects a conversion, **When** the rejection is received, **Then** the call is flagged with the rejection reason and surfaced to an administrator rather than retried indefinitely.
6. **Given** a call already published to both destinations, **When** a review resolution or a source restatement makes it no longer qualify, **Then** the Google Ads conversion is retracted and the outcome recorded, the GA4 event is recorded as unretractable with that reason, both actions are audited, and repeating the correction changes nothing further at either destination.

---

### User Story 6 - Administrators configure the platform and everything they do is auditable (Priority: P6)

A System Administrator manages users and their roles, number pools and individual numbers, and the attribution and qualification rules. Both administrator types can see integration health at a glance — whether call data is flowing from 8x8 and whether conversions are reaching Google — and can work a manual review queue for calls needing human judgement. Every administrative action is written to an audit log that cannot be altered.

**Why this priority**: Required for the platform to be operable and commercially credible, and mandated by the governing principles on auditability and security. Sequenced last because earlier stories can be configured directly during development, but no production launch is possible without it.

**Independent Test**: Perform one action of every administrative type, confirm each appears in the audit log with actor, action, target, before and after values and timestamp, confirm the log cannot be edited or deleted, and confirm resolving a manual review case updates the call and is itself audited.

**Acceptance Scenarios**:

1. **Given** a System Administrator changes a user's role, suspends a tracking number, and publishes a new qualification rule version, **When** the audit log is inspected, **Then** all three actions appear with actor, timestamp, target and what changed.
2. **Given** any audit log entry exists, **When** any user including a System Administrator attempts to modify or delete it, **Then** the attempt fails and is itself recorded.
3. **Given** call data has not arrived from 8x8 for longer than the expected interval, **When** an administrator views integration health, **Then** the 8x8 ingestion status is shown as unhealthy with the time of the last successful ingestion, an alert is raised and notified to the configured email recipients and webhook, the alert repeats at the configured interval while the condition persists without raising a second alert, and it stops on acknowledgement or when data flows again.
4. **Given** a number pool whose utilisation crosses its configured warning threshold, **When** the threshold is crossed, **Then** an alert is raised and notified before the pool is exhausted, so the degradation to the fallback number is pre-empted rather than discovered in reporting.
5. **Given** a call sitting in the manual review queue as ambiguous, **When** a Marketing Administrator resolves it to a specific session, **Then** the call's attribution is updated, the resolution and the resolver are recorded as evidence, no duplicate conversion is produced, and the action is audited.
6. **Given** the Integration Service role, **When** its credentials are used to attempt interactive sign-in to the administrative interface, **Then** access is refused while system-to-system data exchange continues to work.
7. **Given** a signed-in user whose access the customer's identity provider then withdraws, **When** they next make any request, **Then** access is refused without waiting for their existing session to expire; **and Given** the identity provider is unreachable, **When** a break-glass administrator account signs in with multi-factor authentication, **Then** access is granted and the sign-in is audited and surfaced as an exceptional event.

---

### Edge Cases

**Number allocation and display**

- Number pool exhausted at the moment of allocation, or the allocation service is unreachable — the site must still show the configured default number, never a blank or partially replaced number.
- The same visitor opens several concurrent tabs on the same site — all tabs must show the one number allocated to that session.
- A visitor's script is blocked or fails to execute — the page must be left showing its static default number.
- Phone numbers injected into the page after initial load, or revealed by in-app navigation, must still be replaced.
- A number is suspended or retired while a live session currently holds it, and while historical calls already reference it.
- The same physical number appears in more than one pool, or is moved between pools.

**Attribution timing and integrity**

- A call arrives after the visitor's session has already expired but while the number is still reserved to them.
- A visitor calls long after their 30-minute window has closed, by which time the number has been re-allocated to someone else. The call matches the new holder's window exactly and will be attributed to them. This is a bounded misattribution inherent to any pooled model rather than an ambiguous match, and it shrinks as the cooldown is lengthened beyond the FR-006 minimum. It must be understood as the accepted cost of the chosen window, and the residual rate should be estimated during the parallel run.
- Two allocation windows for the same number overlap despite the FR-006 cooldown, because of a release, clock or configuration defect. Because the cooldown is at least as long as the attribution window, this cannot occur in correct operation, so any ambiguous match arising this way must be treated as a defect signal and not as routine output. The exception is the optional shadow mode (FR-049), where the cooldown belongs to the system doing the inserting rather than to this platform: if Mediahawk re-shows a number sooner than the FR-018 window, observed windows can genuinely overlap and the resulting ambiguity is a property of the parallel run, not a defect.
- Timestamps from 8x8 and from session tracking disagree because of clock skew, differing timezones, or a daylight-saving transition; calls that start on one side of midnight and end on the other.
- A call is still in progress when ingestion runs, so its duration is not yet final. It is ingested and judged on what is known, then re-derived under FR-045 once the source reports the completed call — so a long call first seen mid-flight is not permanently stranded below the qualification threshold.
- Calls with withheld or anonymous caller identification.
- A call to a tracking number that the platform has no record of ever allocating.

**Ingestion resilience**

- The same call record is delivered twice by the source, or call legs arrive before the call detail record they belong to.
- Ingestion restarts mid-batch; a backfill covers a range that has already been ingested; a backfill runs concurrently with live ingestion.
- Source credentials expire, the source is rate-limited, or the source is unavailable for an extended period, creating a large catch-up backlog.
- The alerting path itself fails — the mail relay is down or the webhook endpoint is unreachable — so the condition is real but nobody is told. Delivery failure must be visible in its own right rather than presenting as an absence of alerts.
- An outage spans many hours, so a single condition must keep one alert alive rather than raising a new one on each evaluation; and several conditions fire together because they share a root cause, such as ingestion stalling and publication failing at once.
- Source data is later corrected or restated after it has already been attributed. The call is updated and re-derived under FR-045 against the rule version that originally judged it, and any published conversion is corrected under FR-044.
- A restatement changes the dialled number or the call start time such that the call now matches a different allocation window, or none at all, so a previously attributed call becomes unattributed or moves to a different session.

**Qualification and publication**

- A qualification rule is changed, deleted, or made retroactively narrower while calls judged under it already exist.
- Two rule versions have overlapping effective periods, or a call falls in a gap where no rule is in force.
- A qualified call's Google click identifier has aged beyond the destination's accepted conversion window, or is rejected as invalid.
- A destination accepts the request but silently discards the payload, so apparent success is not real success.
- A manual review resolution qualifies a call that was previously published as not qualified, or unqualifies one already published. The correction is propagated to Google Ads as an upload or a retraction under FR-044, while GA4 keeps the original event because the Measurement Protocol cannot retract one — so the platform records that destination as knowingly divergent rather than silently correct.

**Privacy, retention and access**

- A visitor gives consent only after having already seen, and possibly dialled, the default number — that call cannot be attributed, and the session legitimately begins at the moment of consent rather than at arrival.
- A visitor browses several pages before consenting, by which point the campaign parameters and click identifier are no longer in the URL and nothing was stored. The session must be recorded with degraded provenance rather than crediting the page they happened to be on when they consented.
- A visitor withdraws consent part-way through a session, so tracking stops, the number is released and the page reverts to the default number, while calls already placed during the consented period stay attributed.
- A visitor never consents at all, so they remain entirely invisible to attribution and their calls to the default number are not tracked.
- A data subject requests erasure for a visitor whose calls have already been published to external destinations.
- The retention period expires for data still referenced by an open manual review case or by a report covering that period.
- De-identification falls due while a call is still under dispute, awaiting republication, or subject to a late correction from the source, so the surrogate must keep the evidence chain readable without restoring the identifiers.
- A user's role is revoked while they hold an active session, or the customer's identity provider disables them centrally, so the platform must stop honouring an already-issued session rather than waiting for it to expire.
- The customer's identity provider is unreachable, or its group-to-role mapping is misconfigured so that nobody holds System Administrator — the case the break-glass accounts in FR-046 exist to cover.

## Requirements *(mandatory)*

### Functional Requirements

**Delivery boundary**: This build delivers the platform and the visitor-facing number-insertion script. It does not build the reporting web interface; instead it delivers the report data and exports that the existing reporting portal renders. Requirements and acceptance scenarios below are written in terms of user-visible behaviour regardless of which side of that boundary renders it — where a scenario refers to a screen, it is validated against the report data and exports this build serves, together with the portal as an integrated consumer.

**Tracking number management**

- **FR-001**: System MUST allow administrators to create and manage pools of 8x8 telephone numbers (DIDs).
- **FR-002**: System MUST allow numbers to be imported into a pool in bulk from a CSV file, rejecting duplicates and malformed entries with a per-entry reason.
- **FR-003**: System MUST allocate a number to a session atomically, such that no two concurrent sessions can ever hold the same number at the same time.
- **FR-004**: System MUST support separate number pools scoped per website, per campaign, and per business unit, configurable without code change.
- **FR-005**: System MUST support the number lifecycle states active, suspended and retired, and MUST exclude suspended and retired numbers from new allocations while preserving their historical attribution records.
- **FR-006**: System MUST release an allocated number back to its pool once its allocation window ends, and MUST NOT re-allocate it until a cooldown at least as long as the attribution window defined in FR-018 has elapsed. The cooldown MUST be configurable, and the system MUST reject any configuration in which the cooldown is shorter than the attribution window, since that would allow two allocation windows for the same number to overlap.
- **FR-007**: System MUST allow a default fallback number to be configured per website, to be used whenever allocation cannot be satisfied.

**Dynamic Number Insertion**

- **FR-008**: System MUST replace every configured phone number occurrence on a page with the visitor's allocated tracking number, including both displayed text and click-to-call targets.
- **FR-009**: System MUST perform replacement correctly on both traditional multi-page websites and single-page applications, including numbers rendered after initial page load.
- **FR-010**: System MUST keep the same allocated number displayed to a visitor for the whole of their active session.
- **FR-011**: System MUST never display a blank, partial or malformed number; where allocation is unavailable the configured default number MUST remain in place.

**Visitor and session tracking**

- **FR-012**: System MUST support a configurable session timeout and heartbeat interval per website, so that sessions expire predictably. The timeout MUST default to 30 minutes of inactivity, matching GA4's default session definition so that session boundaries agree across both systems, and the heartbeat MUST default to 5 minutes so that a genuinely active visitor's session is refreshed well inside the timeout.
- **FR-013**: System MUST capture a visitor identifier and a session identifier for every visit.
- **FR-014**: System MUST capture landing page, referrer and all UTM parameters as they were on the visitor's first page view, and MUST retain them for the life of that page view so they remain capturable if the session is created later at the moment of consent. Where consent is given only after the visitor has navigated away from their entry page, and the arrival details are therefore no longer recoverable, the system MUST record the session as having degraded attribution provenance rather than record a substitute landing page as if it were the original.
- **FR-015**: System MUST capture Google click identifiers (GCLID, GBRAID, WBRAID) where present, under the same retention and degraded-provenance rules as FR-014, and MUST capture the visitor's GA4 client identifier so qualified calls can later be joined to the correct GA4 session.

**Call data ingestion**

- **FR-016**: System MUST authenticate against Analytics for 8x8 Work and poll Call Detail Records on a configurable schedule, defaulting to hourly, maintaining an ingestion checkpoint such that a restart neither skips nor reprocesses records. Changing the cadence MUST NOT alter any attribution outcome, since matching replays stored allocation windows rather than depending on live state.
- **FR-017**: System MUST ingest Call Legs and reconstruct the call journey for each call, idempotently, so that repeated ingestion of the same source data produces no duplicate records and no duplicate attribution.
- **FR-045**: Where 8x8 supplies a changed version of a call record the platform has already processed — a call in progress at the previous ingestion whose duration was not yet final, or a record the source has since corrected — System MUST update the stored call in place, and MUST re-derive its attribution and its qualification from the updated facts. Re-derivation MUST use the qualification rule version in force at the time of the call, not the version current at the time of re-derivation, so that a restatement corrects the facts without rewriting the rule that judged them. The superseded attribution and qualification results MUST be retained as history alongside the reason for the change, and any conversion already published on the strength of the superseded result MUST be corrected under FR-044. Re-derivation MUST be idempotent: re-ingesting an unchanged record MUST leave the call, its attribution, its qualification and its publications untouched.

**Call attribution**

- **FR-018**: System MUST attribute an inbound call to a session using only an exact match between the dialled number and an allocation window covering the call start time. Probabilistic, fuzzy and heuristic matching MUST NOT be used. An allocation window MUST run from the moment the number is first displayed to the visitor until 30 minutes after their session ends, and this extension MUST be configurable per website.
- **FR-019**: System MUST store the evidence supporting every attribution decision — the matched number, the matched session, the allocation window, the call start time, and the rule applied — and MUST retain that evidence for audit for the configured retention period.
- **FR-020**: System MUST classify a call with no matching allocation window as unattributed, recording the reason, and MUST NOT assign it to any session.
- **FR-021**: System MUST classify a call matching more than one allocation window as ambiguous, MUST NOT assign it to any session, and MUST raise it for manual review.

**Conversion qualification**

- **FR-022**: System MUST provide a default qualification rule under which a call qualifies when it is inbound, answered, and connected for 60 seconds or longer.
- **FR-023**: System MUST allow the default rule to be overridden by configurable qualification rules, without code change.
- **FR-024**: System MUST version qualification rules, MUST record against every judged call which rule version was applied, and MUST NOT alter the qualification result of any call already judged when a rule changes.

**Marketing integrations**

- **FR-025**: System MUST publish qualified calls to Google Ads as offline conversions.
- **FR-026**: System MUST publish qualified calls to Google Analytics 4 as events via the Measurement Protocol.
- **FR-027**: System MUST guarantee that publication is idempotent and safely retryable, such that no qualified call ever produces more than one conversion at either destination regardless of retries, restarts or reprocessing.
- **FR-028**: System MUST record the outcome of every publication attempt per call and destination, including what was sent, the result, the failure reason where applicable, and whether the call remains eligible for retry.
- **FR-044**: Where a call's qualification result changes after it has already been published — because a manual review resolution changed its attribution, or because the source restated the call — System MUST propagate the correction to each destination as far as that destination permits. For Google Ads this means retracting a conversion that no longer qualifies and adjusting one whose value or timing changed. For GA4, whose Measurement Protocol offers no retraction, System MUST record the correction as unpropagatable against the call rather than reporting it as corrected. Every correction, and every case that could not be propagated, MUST be recorded against the call, surfaced on integration health, and audited. Corrections MUST be idempotent under FR-027, so that a repeated retraction produces no further change at the destination.

**Reporting and export**

- **FR-029**: System MUST serve the report data behind an executive dashboard, campaign performance reporting, call detail search, a missed calls report, a qualified calls report, and an unattributed calls report, each filterable by date period, in a form the reporting portal can render without applying business logic of its own.
- **FR-030**: System MUST produce a CSV export for every report, containing the same rows, values, filters and period as the report data it was generated from, so that an export and its report can never disagree.
- **FR-031**: System MUST restrict every report's data and export to what the requesting user's role permits.

**Administration and audit**

- **FR-032**: System MUST allow administrators to manage users and to assign the roles System Administrator, Marketing Administrator, Analyst and Integration Service.
- **FR-033**: System MUST allow administrators to manage attribution configuration and qualification rules, with changes taking effect from a stated point forward only.
- **FR-034**: System MUST present integration health, including the time of the last successful 8x8 ingestion, current ingestion lag, and the success and failure counts of Google Ads and GA4 publication, and MUST indicate an unhealthy state when data has not flowed within the expected interval. System MUST also present per-pool number utilisation and MUST warn before a pool is exhausted, since exhaustion silently degrades attribution to the fallback number rather than producing a visible error.
- **FR-035**: System MUST write every administrator action to an audit log recording actor, action, target, before and after values, and timestamp; audit entries MUST NOT be editable or deletable by any role, and attempts to do so MUST themselves be recorded.
- **FR-036**: System MUST provide a manual review workflow in which a reviewer can resolve ambiguous or disputed calls, where the resolution is stored as attribution evidence, is audited, and cannot produce a duplicate conversion. Where the resolution changes the qualification of a call that has already been published, the correction MUST be propagated under FR-044. A review case unresolved past a configurable age, defaulting to 48 hours, MUST be treated as an alertable condition under FR-047 so that an aging case surfaces rather than sitting invisibly in the queue.
- **FR-046**: System MUST authenticate interactive users by federating to the deploying customer's identity provider using standard single sign-on, and MUST NOT store passwords for federated users. System MUST map provider groups to the roles in FR-032, and MUST allow an administrator to override a mapped role for an individual user, with the override recorded and audited. Where the provider no longer asserts a user, that user MUST lose access without requiring a separate action in the platform. System MUST additionally support a small, configurable number of local break-glass administrator accounts protected by multi-factor authentication, for use when the provider is unreachable or misconfigured, defaulting to 2 accounts (one primary, one backup) so that a single account's unavailability does not itself cause lockout during a provider outage; every break-glass sign-in MUST be audited and surfaced to administrators as an exceptional event. The Integration Service role remains authenticated system-to-system rather than through the provider, and remains barred from interactive access under FR-038.
- **FR-047**: System MUST actively notify configured recipients when an operational condition crosses a configurable threshold, covering at minimum ingestion lag, Google Ads and GA4 publication failure rate, allocation failure rate, per-pool number utilisation, and manual review case age (FR-036). Notifications MUST be delivered by email and by an outbound webhook, both configurable per condition, and MUST repeat at a configurable interval until the condition clears or an administrator acknowledges it. Repeat notifications for a condition already firing MUST NOT be sent as new alerts, so that a sustained outage produces a continuing alert rather than a flood. Every alert raised, acknowledged and cleared MUST be recorded, and acknowledgement MUST be audited. Failure to deliver a notification MUST NOT suppress the underlying condition on the integration health view of FR-034.

**Attribution coverage and optional parallel run**

- **FR-048**: System MUST report, for any chosen period, the count and proportion of inbound calls to pool tracking numbers in each attribution state — attributed, unattributed, ambiguous — broken down by the recorded reason and by website. Because standalone acceptance has no second attribution system to be compared against, this breakdown is the sole evidence of how completely the platform is attributing live traffic, so it MUST reconcile exactly with the underlying call records and MUST be available through reporting and export under FR-029 and FR-030 rather than requiring a database query.
- **FR-049**: System MUST support an optional per-website shadow mode, disabled by default, in which the insertion script records the session together with the phone number another system displayed to that visitor, without replacing any number itself, so that the platform holds an allocation window for a number it did not itself allocate. This exists to allow a parallel run against Mediahawk to be switched on after standalone acceptance, so that both systems judge the same calls. Calls to those numbers MUST be attributed by the identical strict rules of FR-018, with no exception made for shadow mode. Shadow mode MUST be switchable per website through configuration without code change, MUST leave the page's displayed numbers untouched, and MUST be recorded against each session so that shadow-derived attributions are distinguishable from ordinary ones throughout reporting. Because the re-use interval of an observed number is controlled by the inserting system rather than by FR-006, System MUST tolerate overlapping observed windows by classifying the affected calls as ambiguous under FR-021, and MUST report that ambiguity separately from ambiguity arising in ordinary operation, where such overlap would instead signal a defect. Switching shadow mode off MUST return that website to ordinary allocation with no reprocessing of what shadow mode already attributed. Neither SC-001 nor SC-018 depends on this mode being exercised.

**Security, privacy and operations**

- **FR-037**: System MUST require authenticated access for all administrative, reporting and data-exchange operations. The visitor-facing allocation operation, which cannot hold a secret, MUST instead be restricted to configured website origins and rate-limited per origin and per client, defaulting to 600 requests per minute per origin and 10 requests per minute per client, each configurable per website. The per-origin default sits comfortably above the roughly 57 requests per minute (7 allocations plus 50 heartbeats) the whole platform is sized for at peak under SC-004; the per-client default comfortably covers the one allocation call and the periodic heartbeats (FR-012) a single genuine visitor issues within a session, while bounding a single client's ability to exhaust the pool or degrade the service for others.
- **FR-038**: System MUST enforce role-based authorisation on every operation, and MUST deny the Integration Service role any interactive administrative or reporting access while permitting system-to-system data exchange.
- **FR-039**: System MUST publish a standard JavaScript event/callback contract through which a website's consent mechanism reports the visitor's current consent state and any later change to it; the insertion script MUST read this on load and remain subscribed to it for the life of the page view, rather than each deployment integrating consent through bespoke per-site logic. System MUST NOT create a session, allocate a tracking number, or store any identifier on a visitor's device before that visitor has given consent through this contract; until then the website's configured default number MUST remain displayed. On consent being given, the system MUST create the session, capture the arrival details still available at that moment, and allocate a tracking number from that point forward. On consent being withdrawn, the system MUST stop tracking, end the session, release the allocated number and revert the page to the default number, while data already captured during the consented period remains subject to the normal retention rules in FR-040 unless erasure is requested. System MUST support erasure of an identified visitor's data on request.
- **FR-040**: System MUST support a configurable retention period per data category and MUST purge or de-identify data automatically once it expires, except where it is still referenced by an open manual review case. Defaults MUST be: visitor and session identifiers de-identified at 14 months; call, attribution and publication records retained for 25 months, in de-identified form once that first threshold has passed; audit log entries retained for 7 years. De-identification MUST preserve the integrity of the attribution evidence required by FR-019, by replacing identifiers with a stable non-reversible surrogate rather than severing the linkage, so that historical reports and audit trails remain internally consistent after the identifiers themselves are gone.
- **FR-041**: System MUST emit structured logs, operational metrics and health checks sufficient to trace a single call end to end, from number allocation through attribution and qualification to publication, and MUST expose those metrics and health checks in a form the customer's own monitoring can consume alongside the platform's own alerting in FR-047.
- **FR-042**: System MUST support replay and backfill of historical call data over an operator-specified period, without creating duplicate records, duplicate attributions or duplicate conversions, and without disrupting live ingestion.
- **FR-043**: System MUST scale horizontally as call and traffic volume grows, with no single point of failure in the visitor-facing allocation path.

### Key Entities

- **Number Pool**: A named collection of tracking numbers, scoped to a website, campaign or business unit, with its own default fallback number and allocation settings.
- **Tracking Number**: An 8x8 DID belonging to a pool, with a lifecycle state of active, suspended or retired.
- **Allocation**: The binding of one tracking number to one session for a bounded time window, whether the platform reserved that number itself or, in shadow mode, observed another system displaying it. The window, together with the number, is the sole evidence used for attribution. Windows for the same number must never overlap.
- **Website**: A tracked property and its configuration — pools in use, numbers to replace, default number, session timeout and heartbeat, consent settings, permitted origins, and whether the optional parallel-run shadow mode is currently enabled for it.
- **Visitor**: An anonymous returning individual, identified across sessions on one website.
- **Session**: One visit by a visitor, holding landing page, referrer, UTM parameters, Google click identifiers, GA4 client identifier, consent state, start and expiry.
- **Call**: An inbound or outbound call sourced from 8x8, holding direction, caller identification, dialled tracking number, start, answer and end times, connected duration and disposition.
- **Call Leg**: A constituent segment of a call, used to reconstruct the call journey.
- **Attribution**: The decision linking a call to a session, with state attributed, unattributed or ambiguous, the reason, and the stored supporting evidence.
- **Qualification Rule**: A versioned, configurable definition of what makes a call a marketing conversion, with an effective period.
- **Qualification Result**: The outcome of judging one call against one rule version. A rule change never alters it. It is superseded only when the source restates the underlying call, in which case the prior result is retained as history alongside the reason for the change.
- **Conversion Publication**: One attempt to report one qualified call to one destination, with status, attempt count, idempotency key, external identifier and failure reason. A publication may subsequently be retracted or adjusted, in which case the correction, its reason, and whether the destination accepted it are held against the same record.
- **Ingestion Checkpoint**: The position marking how far the platform has consumed each source feed.
- **User** and **Role**: An operator of the platform and the permissions granted to them. An interactive user is normally an identity asserted by the customer's identity provider, holding the provider's subject reference, the groups it asserts, the role derived from them and any in-platform override; break-glass accounts are the exception and are local to the platform.
- **Alert**: One operational condition that has crossed its configured threshold — ingestion lag, publication failure rate, allocation failure rate, pool utilisation, or a review case's age — with its threshold, when it was raised, its notification attempts, its acknowledgement and who made it, and when it cleared.
- **Audit Entry**: An immutable record of one administrative action.
- **Review Case**: A call requiring human judgement, its reviewer, resolution and outcome.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: In a controlled acceptance test, 100% of seeded calls are attributed to the session that actually generated them, with zero misattributions. A seeded call is one placed by a known caller to a known tracking number from a browser session the tester created, so the true answer is known independently of the platform. The set must exercise the full range of outcomes deliberately — a call inside the allocation window, one after the session expired but still inside the FR-018 extension, one after the window closed, one to a number never allocated, one to a suspended number, one during a daylight-saving transition, and one placed across midnight — and for each the platform's recorded state and stored evidence chain must match the tester's record of what actually happened. The bar is 100% rather than 95% because attribution is deterministic by Principle I: with the true session known for every call, any single incorrect result is a defect in the matching rule, not measurement noise.
- **SC-018**: Over at least four weeks of live traffic covering at least 500 inbound calls to pool tracking numbers, at least 95% reach an attributed state, and 100% of those that do not carry a recorded reason placing them in the unattributed or ambiguous state with its cause — verified from the FR-048 coverage breakdown, which must reconcile exactly with the underlying call records. The period is set at four weeks and 500 calls so that it spans a full monthly cycle and every day-of-week pattern, and so the figure carries a confidence interval tight enough to distinguish 95% from 90%. This measures how completely real traffic arrives matchable, which SC-001 cannot show; SC-001 measures whether the matches made are right, which this cannot show. Both are required.
- **SC-002**: No call is ever counted as attributed more than once and no qualified call produces more than one conversion at any destination, verified by reprocessing a full day of call data at least three times and observing zero change in all report totals.
- **SC-003**: Across a measured week of live traffic, at least 99.9% of tracked page views display either an allocated tracking number or the configured default number, and zero page views display a blank, partial or malformed number.
- **SC-004**: A visitor's tracking number is determined within 300 milliseconds for at least 95% of page loads, sustained at a peak of 250 concurrent tracked sessions. That peak comprises roughly 7 new allocation requests per minute from new sessions plus roughly 50 heartbeat refreshes per minute from live ones, so a load test must reproduce both rather than allocations alone.
- **SC-005**: The visitor-facing number allocation service is available at least 99.9% of the time, measured monthly, at that same peak load.
- **SC-006**: 100% of administrator actions are present in the audit log, verified by performing every administrative action type and reconciling against the log; zero audit entries can be altered or removed.
- **SC-007**: At least 99% of qualified calls that carry a Google click identifier are successfully reported to Google Ads and to GA4 within 24 hours of qualifying, with every remaining case carrying a recorded reason.
- **SC-008**: Call records ingested from 8x8 reconcile to 100% against the source for any audited period, with no gaps and no duplicates.
- **SC-009**: Every call that is unattributed or ambiguous appears in the corresponding report or review queue within 60 minutes of its record becoming available from 8x8, so that no call is silently dropped.
- **SC-010**: Every report and its CSV export can be produced for any chosen period through configuration and normal use alone, with no code change and no engineering assistance required on the platform side.
- **SC-011**: Changing a qualification rule leaves every previously judged call's result and applied rule version unchanged, verified by re-running the qualified calls report for a historical period before and after a rule change and observing identical output.
- **SC-012**: The platform passes functional, integration, performance and security testing, with no outstanding high or critical severity security finding at launch.
- **SC-013**: For visitors who have not consented, zero identifiers are stored on the device and zero session records exist, verified by loading tracked pages without consenting and inspecting both the browser and the platform's records.
- **SC-014**: Once a data category's retention threshold has elapsed, zero records in that category still carry visitor or session identifiers, and reports covering that historical period still reconcile to the same totals they produced beforehand — verified by running the purge against a seeded historical dataset and checking both properties.
- **SC-015**: Every call whose qualification changes after publication is either corrected at each destination or carries a recorded reason why that destination could not be corrected, with zero such calls left silently divergent — verified by resolving a review case both ways against published calls and reconciling the platform's record against Google Ads.
- **SC-016**: Withdrawing a user at the customer's identity provider removes their platform access on their next request with no separate action taken in the platform, and zero passwords exist for federated users — verified by disabling a signed-in test user centrally and inspecting the stored user records.
- **SC-017**: Every alertable condition raises a notification to its configured recipients within 15 minutes of the threshold being crossed, and a condition lasting several hours produces one continuing alert rather than one per evaluation — verified by inducing a stalled ingestion, a failing publication destination, a pool crossing its utilisation threshold, and a review case left unresolved past its 48-hour default threshold, and confirming each is notified, repeated and cleared as configured.

## Out of Scope

Explicitly excluded from this version, to be considered as future enhancements:

- AI-based call transcription and sentiment analysis.
- CRM connectors.
- Revenue attribution.
- Predictive marketing analytics.
- Real-time dashboards. Reporting is expected to reflect data as at the most recent completed ingestion cycle.
- Multi-tenant SaaS deployment. The platform is delivered as a single-tenant deployment per customer.
- Outbound call attribution. Outbound calls may be ingested but are not attributed or qualified.
- Telephony providers other than 8x8 Work.
- Marketing destinations other than Google Ads and Google Analytics 4.
- The reporting web interface. This build serves report data and CSV exports; rendering them is the existing reporting portal's responsibility. The visitor-facing insertion script, by contrast, **is** in scope.

## Assumptions

**Scope and boundaries**

- Although the platform is intended to be commercially reusable, it is deployed as a single tenant per customer, since multi-tenant SaaS is explicitly out of scope. "Reusable" is taken to mean repeatably deployable and configurable, not shared-tenancy.
- Only inbound calls are attributed and qualified. The default qualification rule's "inbound" condition is read as excluding outbound calls from conversion entirely.
- Acceptance is staged. The platform is first proven standalone against SC-001 and SC-018, which need nothing from Mediahawk. Only once that testing satisfies the business is the optional parallel run of FR-049 considered. Nothing in the launch path therefore depends on Mediahawk remaining live, on its contract being extended, or on its output being available — the risk that previously sat on SC-001 is removed rather than mitigated.
- If the optional parallel run is later enabled, it assumes Mediahawk is still live and still inserting its own numbers, that those numbers terminate on 8x8 and so appear in the Call Detail Records the platform ingests, that the platform's script can read the number Mediahawk inserted, and that both systems are paid for concurrently. Where a Mediahawk number does not route through 8x8 no record exists and that call falls outside the comparison. These conditions should be confirmed before the run is scheduled; they now qualify an optional exercise rather than a launch gate, so failing to meet them delays nothing.
- Shadow mode suppresses the platform's own number insertion for the websites it is enabled on. SC-003 and SC-004, which measure what a visitor is shown and how quickly, are therefore measured on ordinary operation and not during a shadow-mode period.
- Bringing the insertion script into this build was resolved by constitution amendment 1.1.0 on 2026-08-06, which splits the former Frontend boundary clause so that the reporting portal stays out of scope while the insertion client is in scope as a deliverable. The amendment also confirms that the client remains untrusted at the API boundary despite being ours, and forbids it from making allocation, attribution or qualification decisions.
- The reporting portal is owned by an existing team and treated as an untrusted consumer: it receives only data its caller's role permits, and it is not relied upon to enforce any access rule of its own.

**Requirement numbering**

- Functional requirement numbers are aligned to the identifiers already cited in the project constitution, so that its existing traceability continues to resolve — specifically FR-003 atomic allocation, FR-004 pool scoping, FR-012 session timeout and heartbeat, FR-016 and FR-017 idempotent ingestion, FR-018 strict matching, FR-019 stored evidence, FR-020 unattributed, FR-021 ambiguous, FR-023 and FR-024 configurable and versioned rules, FR-032 user and role management, FR-035 audit log.
- Requirements added by later clarification sessions take the next free number rather than being inserted in sequence, so an existing identifier never shifts meaning. FR-044 to FR-048 were added on 2026-08-09 and therefore appear at the end of their topical subsection rather than in numerical order.

**Attribution and data**

- The GA4 client identifier must be captured at session time, even though it was not requested explicitly, because a call conversion cannot otherwise be joined to the originating GA4 session.
- The 30-minute session timeout deliberately matches GA4's default rather than being chosen on its own merits. Because FR-015 captures the GA4 client identifier so that a qualified call can be joined back to the visit that produced it (FR-026), a disagreement over where a session ends would let a call be reported against a different GA4 session than the one that generated it — a silent corruption of the very join the identifier exists to enable. Changing the timeout away from 30 minutes therefore has a reporting-integrity cost, not just a pool-sizing one.
- Google Ads offline conversion reporting is keyed on the Google click identifiers captured on the session. Qualified calls from sessions without such an identifier — organic, direct or referral traffic — cannot be reported to Google Ads. They are still qualified, still reported internally, and the omission is recorded rather than treated as a failure.
- Analytics for 8x8 Work exposes Call Detail Records and Call Legs by polling, with timestamps in a known and stable timezone, and retains them long enough to support backfill.
- All timestamps are stored in a single canonical timezone, and comparisons between call times and allocation windows are performed in that timezone to avoid daylight-saving errors.
- A tracking number is unavailable for 90 minutes after its visitor's last activity: 30 minutes until the session times out (FR-012), 30 more of attribution window (FR-018), then 30 of cooldown (FR-006). A pool must therefore hold at least as many numbers as the peak count of sessions simultaneously in any of those three states — live, expired but still inside the window, or cooling down — plus headroom. Sizing each pool is an operational responsibility of the deploying customer; the platform reports exhaustion rather than preventing it.
- A call's connected duration as reported by the source is authoritative for qualification, including when the source revises it. The platform therefore holds no independent view of a call's facts and never overrides the source; it re-derives from whatever 8x8 most recently states (FR-045).

**Operations and expected scale**

- The platform is sized for a peak of approximately 250 concurrent tracked sessions across all websites, arising from roughly 7 new sessions starting per minute. The tracking number estate follows from that arrival rate rather than from the concurrency figure: 7 per minute multiplied by the 90-minute hold time gives approximately 630 numbers before headroom. Because exhaustion degrades silently to the fallback number rather than raising an error, pools must be provisioned with meaningful headroom above that figure and monitored against the utilisation warning required by FR-034. Monthly call volume and the number of websites and pools were not stated and should be confirmed during technical planning, as they affect ingestion sizing and report query cost but not pool sizing.
- Retention is tiered because identifiers and attribution facts have different useful lifetimes. The 14-month identifier threshold mirrors GA4's own maximum retention, so platform data does not outlive the system it feeds; the 25-month record threshold preserves like-for-like comparison against the same month a year earlier, which is the main reason the data is kept at all. Both are comfortably beyond Google Ads' 90-day offline conversion window, so no publication path is foreclosed by them.
- Data subject erasure requests can be honoured within the platform, but conversions already reported to external destinations are governed by those destinations' own retention.

**Dependencies**

- An outbound mail path is available to the deployment for the notifications required by FR-047, and the customer nominates the recipients and, where they want one, a webhook endpoint. The platform raises and delivers alerts; it does not provide on-call paging, escalation or rostering, which remain the customer's own operational concern.
- Google Ads accepts retraction and adjustment of an already-uploaded offline conversion, which is what makes FR-044 possible on that destination; GA4's Measurement Protocol offers no equivalent, which is why the divergence there is recorded rather than fixed.
- Valid credentials and sufficient API quota exist for Analytics for 8x8 Work, Google Ads and Google Analytics 4, and the 8x8 numbers to be used as tracking numbers are already provisioned and routable.
- The deploying customer operates an identity provider supporting standard single sign-on, can expose group membership for role mapping, and will withdraw a leaver's access there. Without it only the break-glass accounts of FR-046 can sign in, which is a recovery path rather than a way to run the platform. Federation is compatible with the constitution's JWT constraint: the provider establishes who the user is, and the platform issues its own short-lived token carrying the mapped role.
- The platform defines and publishes the consent event/callback contract (FR-039); each tracked website's existing consent mechanism (whichever CMP or custom tool it uses) must be wired, by the deploying customer, to fire it on load and on later changes, so that consent granted or withdrawn during a page view is acted on immediately. Without that wiring the platform cannot tell a visitor who has refused from one who has not yet been asked, and FR-039 cannot be satisfied.
- Because attribution now begins at consent rather than at arrival, measured attributed call volume will be lower than Mediahawk's if Mediahawk tracks pre-consent. This affects volume comparisons during the parallel run but not SC-001, which measures campaign-level agreement across the calls both systems saw rather than total counts. Total-volume differences between the two systems are therefore expected and are not a defect.
- Tracked websites can install and load the visitor-facing insertion script, and their operators can specify which numbers on which pages are to be replaced.
