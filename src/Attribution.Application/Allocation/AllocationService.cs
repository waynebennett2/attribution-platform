using Attribution.Domain.Pools;
using Attribution.Domain.Sessions;
using Attribution.Domain.Websites;

namespace Attribution.Application.Allocation;

// FR-003, FR-006, FR-007, FR-010, FR-012, FR-013-FR-015, FR-018, FR-039: orchestrates
// the DNI allocation lifecycle — allocate, heartbeat, and consent grant/withdrawal.
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
    // consentGranted: true once the visitor answers the consent prompt.
    public async Task<AllocateResult> AllocateAsync(
        Guid websiteId, bool consentGranted, ArrivalDetails arrival, DateTimeOffset now)
    {
        var website = await _websiteRepository.GetByIdAsync(websiteId)
            ?? throw new InvalidOperationException($"Unknown website {websiteId}.");

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
            visitor.Id, websiteId, arrival, SessionProvenance.Ordinary, now,
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

    // FR-012: refreshes the session's expiry and extends the allocation's provisional
    // window end to match, so the number is understood to still be theirs.
    public async Task<(bool StillValid, string? Number)> HeartbeatAsync(Guid sessionId, DateTimeOffset now)
    {
        var session = await _sessionRepository.GetByIdAsync(sessionId);
        if (session is null || session.IsExpired(now))
        {
            return (false, null);
        }

        var website = await _websiteRepository.GetByIdAsync(session.WebsiteId)
            ?? throw new InvalidOperationException($"Unknown website {session.WebsiteId}.");

        session.RefreshActivity(now, TimeSpan.FromSeconds(website.SessionTimeoutSeconds));
        await _sessionRepository.UpdateAsync(session);

        var allocation = await _allocationRepository.GetBySessionIdAsync(sessionId);
        if (allocation is null)
        {
            return (true, null);
        }

        allocation.CloseAtSessionEnd(session.ExpiresAt, TimeSpan.FromSeconds(website.AllocationWindowExtensionSeconds));
        await _allocationRepository.UpdateAsync(allocation);

        var trackingNumber = await _trackingNumberRepository.GetByIdAsync(allocation.TrackingNumberId);
        return (true, trackingNumber?.Did);
    }

    // FR-018, FR-039: withdrawal ends the session and closes the allocation window
    // immediately — no FR-018 extension — distinct from an ordinary timeout end.
    public async Task<AllocateResult> WithdrawConsentAsync(Guid sessionId, DateTimeOffset now)
    {
        var session = await _sessionRepository.GetByIdAsync(sessionId)
            ?? throw new InvalidOperationException($"Unknown session {sessionId}.");
        var website = await _websiteRepository.GetByIdAsync(session.WebsiteId)
            ?? throw new InvalidOperationException($"Unknown website {session.WebsiteId}.");

        session.EndByConsentWithdrawal(now);
        await _sessionRepository.UpdateAsync(session);

        var allocation = await _allocationRepository.GetBySessionIdAsync(sessionId);
        if (allocation is not null)
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
