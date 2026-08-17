using Attribution.Domain.Pools;
using Attribution.Domain.Sessions;
using Attribution.Domain.Websites;

namespace Attribution.Application.Allocation;

// FR-003, FR-006, FR-007, FR-010, FR-012, FR-013-FR-015, FR-018, FR-039, FR-050:
// orchestrates the DNI allocation lifecycle — allocate, heartbeat, and consent
// grant/withdrawal — for both a single-pool website and a multi-pool-enabled one.
public sealed class AllocationService
{
    private readonly IWebsiteRepository _websiteRepository;
    private readonly INumberPoolRepository _poolRepository;
    private readonly ITrackingNumberRepository _trackingNumberRepository;
    private readonly ISessionRepository _sessionRepository;
    private readonly IAllocationRepository _allocationRepository;
    private readonly IAtomicAllocator _atomicAllocator;

    public AllocationService(
        IWebsiteRepository websiteRepository,
        INumberPoolRepository poolRepository,
        ITrackingNumberRepository trackingNumberRepository,
        ISessionRepository sessionRepository,
        IAllocationRepository allocationRepository,
        IAtomicAllocator atomicAllocator)
    {
        _websiteRepository = websiteRepository;
        _poolRepository = poolRepository;
        _trackingNumberRepository = trackingNumberRepository;
        _sessionRepository = sessionRepository;
        _allocationRepository = allocationRepository;
        _atomicAllocator = atomicAllocator;
    }

    // FR-039: also the "grant consent after initial refusal" path — called with
    // consentGranted: true once the visitor answers the consent prompt. FR-014: provenance
    // defaults to Ordinary (consent already held at first page view); callers handling a
    // late grant pass Degraded when the original entry-page arrival details are no longer
    // recoverable (the visitor navigated away before consenting).
    //
    // FR-050: matchedPoolIds and existingSessionId are used only for a multi-pool-enabled
    // website (see research.md §15) — both are ignored for a single-pool website, which
    // behaves exactly as it always has.
    public async Task<AllocateResult> AllocateAsync(
        Guid websiteId,
        bool consentGranted,
        ArrivalDetails arrival,
        DateTimeOffset now,
        SessionProvenance provenance = SessionProvenance.Ordinary,
        IReadOnlyList<Guid>? matchedPoolIds = null,
        Guid? existingSessionId = null)
    {
        var website = await _websiteRepository.GetByIdAsync(websiteId)
            ?? throw new InvalidOperationException($"Unknown website {websiteId}.");

        if (website.MultiPoolEnabled)
        {
            return await AllocateMultiPoolAsync(website, consentGranted, arrival, now, provenance, matchedPoolIds, existingSessionId);
        }

        if (!consentGranted)
        {
            // FR-039: no session, no allocation, no identifier stored — default number only.
            return new AllocateResult(null, website.DefaultNumber, "no_consent", null);
        }

        // Simplification for this increment: allocates from the website-scoped pool only.
        // Campaign/business-unit-scoped pool selection (FR-004) during allocation is a
        // documented follow-up, not required by User Story 1's acceptance scenarios.
        var pool = (await _poolRepository.GetByScopeAsync("website", websiteId)).FirstOrDefault();
        if (pool is null)
        {
            return new AllocateResult(null, website.DefaultNumber, "no_pool_configured", null);
        }

        var visitor = Visitor.Create(websiteId);
        var session = Session.Create(
            visitor.Id, websiteId, arrival, provenance, now,
            TimeSpan.FromSeconds(website.SessionTimeoutSeconds));

        var attempt = await _atomicAllocator.TryAllocateAsync(
            visitor,
            session,
            pool.Id,
            TimeSpan.FromSeconds(website.CooldownSeconds),
            windowStart: now,
            TimeSpan.FromSeconds(website.AllocationWindowExtensionSeconds),
            now);

        if (!attempt.Succeeded)
        {
            // FR-011: pool exhausted; caller (DniController) is responsible for surfacing
            // this for operational visibility (metrics/logging) alongside returning the default.
            return new AllocateResult(null, website.DefaultNumber, "pool_exhausted", null);
        }

        var trackingNumber = await _trackingNumberRepository.GetByIdAsync(attempt.Allocation!.TrackingNumberId)
            ?? throw new InvalidOperationException("Allocated tracking number vanished mid-request.");

        return new AllocateResult(session.Id, trackingNumber.Did, null, session.ExpiresAt);
    }

    // FR-050: pools always carries the website's full pool->number map (static metadata,
    // safe pre-consent per FR-039). session_id/allocations are populated only once consent
    // is granted and at least one requested pool actually allocates.
    private async Task<AllocateResult> AllocateMultiPoolAsync(
        Website website,
        bool consentGranted,
        ArrivalDetails arrival,
        DateTimeOffset now,
        SessionProvenance provenance,
        IReadOnlyList<Guid>? matchedPoolIds,
        Guid? existingSessionId)
    {
        var allPools = await _poolRepository.GetByScopeAsync("website", website.Id);
        var poolsMap = allPools
            .Select(p => new PoolNumber(p.Id, p.DefaultNumber ?? website.DefaultNumber))
            .ToList();

        if (!consentGranted)
        {
            return new AllocateResult(null, null, "no_consent", null, poolsMap, null);
        }

        // FR-050: a client-supplied pool id is untrusted input on this unauthenticated,
        // origin-restricted endpoint (FR-037) — drop anything not actually scoped to this
        // website rather than allocating from it.
        var requestedPoolIds = (matchedPoolIds ?? Array.Empty<Guid>())
            .Where(id => allPools.Any(p => p.Id == id))
            .Distinct()
            .ToList();

        if (requestedPoolIds.Count == 0)
        {
            return new AllocateResult(null, null, "pending_match", null, poolsMap, null);
        }

        // research.md §15: resume an existing, still-active session and allocate only the
        // pools it doesn't already hold, rather than starting a second session.
        if (existingSessionId is Guid sessionId)
        {
            var existingSession = await _sessionRepository.GetByIdAsync(sessionId);
            if (existingSession is not null && !existingSession.IsExpired(now))
            {
                var alreadyHeld = (await _allocationRepository.GetAllBySessionIdAsync(sessionId))
                    .Where(a => a.PoolIdAtAllocation.HasValue)
                    .Select(a => a.PoolIdAtAllocation!.Value)
                    .ToHashSet();
                var newPoolIds = requestedPoolIds.Where(id => !alreadyHeld.Contains(id)).ToList();

                var grown = await AllocateAdditionalPoolsAsync(website, existingSession, newPoolIds, now);
                return new AllocateResult(existingSession.Id, null, null, existingSession.ExpiresAt, poolsMap, grown);
            }
            // Session unknown or expired — fall through and start a fresh one below.
        }

        // First allocation for a new session: the first pool to succeed creates the
        // Visitor and Session; every subsequent successful pool is added under it, so one
        // exhausted pool never blocks the others from allocating (FR-050).
        var visitor = Visitor.Create(website.Id);
        var newSession = Session.Create(
            visitor.Id, website.Id, arrival, provenance, now, TimeSpan.FromSeconds(website.SessionTimeoutSeconds));

        Session? persistedSession = null;
        var allocations = new List<PoolAllocation>();
        foreach (var poolId in requestedPoolIds)
        {
            var attempt = persistedSession is null
                ? await _atomicAllocator.TryAllocateAsync(
                    visitor, newSession, poolId, TimeSpan.FromSeconds(website.CooldownSeconds), now,
                    TimeSpan.FromSeconds(website.AllocationWindowExtensionSeconds), now)
                : await _atomicAllocator.TryAllocateAdditionalAsync(
                    persistedSession, poolId, TimeSpan.FromSeconds(website.CooldownSeconds), now,
                    TimeSpan.FromSeconds(website.AllocationWindowExtensionSeconds), now);

            if (!attempt.Succeeded)
            {
                continue; // FR-050: this pool's occurrences fall back to its own default number.
            }

            persistedSession ??= newSession;
            var trackingNumber = await _trackingNumberRepository.GetByIdAsync(attempt.Allocation!.TrackingNumberId)
                ?? throw new InvalidOperationException("Allocated tracking number vanished mid-request.");
            allocations.Add(new PoolAllocation(poolId, trackingNumber.Did, newSession.ExpiresAt));
        }

        if (persistedSession is null)
        {
            // FR-011: every requested pool was exhausted — no session left orphaned,
            // mirroring the single-pool pool_exhausted behavior.
            return new AllocateResult(null, null, "pool_exhausted", null, poolsMap, null);
        }

        return new AllocateResult(persistedSession.Id, null, null, persistedSession.ExpiresAt, poolsMap, allocations);
    }

    private async Task<List<PoolAllocation>> AllocateAdditionalPoolsAsync(
        Website website, Session session, IReadOnlyList<Guid> poolIds, DateTimeOffset now)
    {
        var grown = new List<PoolAllocation>();
        foreach (var poolId in poolIds)
        {
            var attempt = await _atomicAllocator.TryAllocateAdditionalAsync(
                session, poolId, TimeSpan.FromSeconds(website.CooldownSeconds), now,
                TimeSpan.FromSeconds(website.AllocationWindowExtensionSeconds), now);
            if (!attempt.Succeeded)
            {
                continue;
            }

            var trackingNumber = await _trackingNumberRepository.GetByIdAsync(attempt.Allocation!.TrackingNumberId)
                ?? throw new InvalidOperationException("Allocated tracking number vanished mid-request.");
            grown.Add(new PoolAllocation(poolId, trackingNumber.Did, session.ExpiresAt));
        }

        return grown;
    }

    // FR-012: refreshes the session's expiry and extends every one of its allocations'
    // provisional window ends to match, so each number is understood to still be theirs —
    // one heartbeat call for the whole session regardless of how many pools it holds (FR-050).
    public async Task<HeartbeatResult> HeartbeatAsync(Guid sessionId, DateTimeOffset now)
    {
        var session = await _sessionRepository.GetByIdAsync(sessionId);
        if (session is null || session.IsExpired(now))
        {
            return new HeartbeatResult(false, null);
        }

        var website = await _websiteRepository.GetByIdAsync(session.WebsiteId)
            ?? throw new InvalidOperationException($"Unknown website {session.WebsiteId}.");

        session.RefreshActivity(now, TimeSpan.FromSeconds(website.SessionTimeoutSeconds));
        await _sessionRepository.UpdateAsync(session);

        var allocations = await _allocationRepository.GetAllBySessionIdAsync(sessionId);
        if (allocations.Count == 0)
        {
            return new HeartbeatResult(true, null);
        }

        var extension = TimeSpan.FromSeconds(website.AllocationWindowExtensionSeconds);
        var perPool = new List<PoolHeartbeat>();
        foreach (var allocation in allocations)
        {
            allocation.CloseAtSessionEnd(session.ExpiresAt, extension);
            await _allocationRepository.UpdateAsync(allocation);

            if (website.MultiPoolEnabled && allocation.PoolIdAtAllocation.HasValue)
            {
                var did = (await _trackingNumberRepository.GetByIdAsync(allocation.TrackingNumberId))?.Did;
                perPool.Add(new PoolHeartbeat(allocation.PoolIdAtAllocation.Value, true, did));
            }
        }

        if (website.MultiPoolEnabled)
        {
            return new HeartbeatResult(true, null, perPool);
        }

        // Single-pool website: unchanged flat shape, using the one allocation this session
        // can ever hold.
        var trackingNumber = await _trackingNumberRepository.GetByIdAsync(allocations[0].TrackingNumberId);
        return new HeartbeatResult(true, trackingNumber?.Did);
    }

    // FR-018, FR-039: withdrawal ends the session and closes every one of its allocation
    // windows immediately — no FR-018 extension — distinct from an ordinary timeout end.
    public async Task<AllocateResult> WithdrawConsentAsync(Guid sessionId, DateTimeOffset now)
    {
        var session = await _sessionRepository.GetByIdAsync(sessionId)
            ?? throw new InvalidOperationException($"Unknown session {sessionId}.");
        var website = await _websiteRepository.GetByIdAsync(session.WebsiteId)
            ?? throw new InvalidOperationException($"Unknown website {session.WebsiteId}.");

        session.EndByConsentWithdrawal(now);
        await _sessionRepository.UpdateAsync(session);

        var allocations = await _allocationRepository.GetAllBySessionIdAsync(sessionId);
        foreach (var allocation in allocations)
        {
            allocation.CloseImmediately(now);
            await _allocationRepository.UpdateAsync(allocation);

            // FR-006's cooldown still applies from this earlier release point — the
            // denormalized ordering hint is kept in sync, not the authority for it (the
            // atomic allocator's query already checks the allocations table directly).
            var trackingNumber = await _trackingNumberRepository.GetByIdAsync(allocation.TrackingNumberId);
            if (trackingNumber is not null)
            {
                trackingNumber.Release(now);
                await _trackingNumberRepository.UpdateAsync(trackingNumber);
            }
        }

        return new AllocateResult(null, website.DefaultNumber, null, null);
    }
}
