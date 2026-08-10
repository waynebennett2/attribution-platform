using Attribution.Domain.Pools;
using Attribution.Domain.Sessions;
using Attribution.Domain.Websites;
using DomainAllocation = Attribution.Domain.Sessions.Allocation;

namespace Attribution.Application.Allocation;

// FR-049: records the session together with the phone number another system displayed,
// without replacing anything or allocating from the platform's own pool. Overlapping
// observed windows are tolerated by design (the cooldown belongs to the inserting
// system, not FR-006) — no atomic reservation is needed here, unlike ordinary allocation,
// since there is no scarce resource being contended for.
public sealed class ShadowAllocationService
{
    private readonly IWebsiteRepository _websiteRepository;
    private readonly INumberPoolRepository _poolRepository;
    private readonly ITrackingNumberRepository _trackingNumberRepository;
    private readonly IVisitorRepository _visitorRepository;
    private readonly ISessionRepository _sessionRepository;
    private readonly IAllocationRepository _allocationRepository;

    public ShadowAllocationService(
        IWebsiteRepository websiteRepository,
        INumberPoolRepository poolRepository,
        ITrackingNumberRepository trackingNumberRepository,
        IVisitorRepository visitorRepository,
        ISessionRepository sessionRepository,
        IAllocationRepository allocationRepository)
    {
        _websiteRepository = websiteRepository;
        _poolRepository = poolRepository;
        _trackingNumberRepository = trackingNumberRepository;
        _visitorRepository = visitorRepository;
        _sessionRepository = sessionRepository;
        _allocationRepository = allocationRepository;
    }

    public async Task<AllocateResult> RecordObservationAsync(
        Guid websiteId, string observedNumber, ArrivalDetails arrival, DateTimeOffset now)
    {
        var website = await _websiteRepository.GetByIdAsync(websiteId)
            ?? throw new InvalidOperationException($"Unknown website {websiteId}.");

        if (!website.ShadowModeEnabled)
        {
            throw new InvalidOperationException("Shadow mode is not enabled for this website (FR-049).");
        }

        var pool = (await _poolRepository.GetByScopeAsync("website", websiteId)).FirstOrDefault()
            ?? throw new InvalidOperationException("No pool configured for website.");

        // The observed number needs a TrackingNumber row to satisfy Allocation's FK, even
        // though the platform never allocates it — find-or-create by DID.
        var trackingNumber = await _trackingNumberRepository.GetByDidAsync(observedNumber);
        if (trackingNumber is null)
        {
            trackingNumber = TrackingNumber.Create(pool.Id, observedNumber);
            await _trackingNumberRepository.AddAsync(trackingNumber);
        }

        var visitor = Visitor.Create(websiteId);
        await _visitorRepository.AddAsync(visitor);

        var session = Session.Create(
            visitor.Id, websiteId, arrival, SessionProvenance.Ordinary, now,
            TimeSpan.FromSeconds(website.SessionTimeoutSeconds));
        await _sessionRepository.AddAsync(session);

        var allocation = DomainAllocation.Create(
            trackingNumber.Id,
            session.Id,
            pool.Id,
            windowStart: now,
            session.ExpiresAt,
            TimeSpan.FromSeconds(website.AllocationWindowExtensionSeconds),
            isShadow: true);
        await _allocationRepository.AddAsync(allocation);

        return new AllocateResult(session.Id, observedNumber, null, session.ExpiresAt);
    }
}
