using Attribution.Application.Administration;
using Attribution.Application.Allocation;
using Attribution.Domain.Audit;
using Attribution.Domain.Calls;
using Attribution.Domain.Pools;
using Attribution.Domain.Publication;
using Attribution.Domain.Qualification;
using Attribution.Domain.Sessions;
using Attribution.Domain.Websites;
using DomainAllocation = Attribution.Domain.Sessions.Allocation;
using DomainAttribution = Attribution.Domain.Calls.Attribution;

namespace Attribution.UnitTests.TestSupport;

// Minimal hand-rolled in-memory fakes for the repository interfaces AttributionService,
// ReDerivationService and IngestionService depend on — no mocking library is used
// elsewhere in this project, so these follow the same convention. Every fake stores the
// exact domain object references it was given, so a private-setter mutation made by the
// service under test (e.g. Call.ApplyRestatement) is already visible through the fake
// without a separate "Update" step actually persisting anything.

internal sealed class FakeCallRepository : ICallRepository
{
    public List<Call> Calls { get; } = new();

    public Task<Call?> GetBySourceRecordIdAsync(string sourceRecordId) =>
        Task.FromResult(Calls.FirstOrDefault(c => c.SourceRecordId == sourceRecordId));

    public Task<Call?> GetByIdAsync(Guid id) => Task.FromResult(Calls.FirstOrDefault(c => c.Id == id));

    public Task AddAsync(Call call)
    {
        Calls.Add(call);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(Call call) => Task.CompletedTask;
}

internal sealed class FakeCallLegRepository : ICallLegRepository
{
    public List<CallLeg> Legs { get; } = new();

    public Task<CallLeg?> GetBySourceIdsAsync(string sourceCallRecordId, string sourceLegId) =>
        Task.FromResult(Legs.FirstOrDefault(l => l.SourceCallRecordId == sourceCallRecordId && l.SourceLegId == sourceLegId));

    public Task<IReadOnlyList<CallLeg>> GetOrphanedBySourceCallRecordIdAsync(string sourceCallRecordId) =>
        Task.FromResult<IReadOnlyList<CallLeg>>(
            Legs.Where(l => l.SourceCallRecordId == sourceCallRecordId && l.CallId is null).ToList());

    public Task AddAsync(CallLeg leg)
    {
        Legs.Add(leg);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(CallLeg leg) => Task.CompletedTask;
}

internal sealed class FakeIngestionCheckpointRepository : IIngestionCheckpointRepository
{
    public List<IngestionCheckpoint> Checkpoints { get; } = new();

    public Task<IngestionCheckpoint?> GetByFeedAsync(string feed) =>
        Task.FromResult(Checkpoints.FirstOrDefault(c => c.Feed == feed));

    public Task AddAsync(IngestionCheckpoint checkpoint)
    {
        Checkpoints.Add(checkpoint);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(IngestionCheckpoint checkpoint) => Task.CompletedTask;
}

internal sealed class FakeAttributionRepository : IAttributionRepository
{
    public List<DomainAttribution> Attributions { get; } = new();

    public Task<DomainAttribution?> GetCurrentByCallIdAsync(Guid callId) =>
        Task.FromResult(Attributions.FirstOrDefault(a => a.CallId == callId && a.IsCurrent));

    public Task AddAsync(DomainAttribution attribution)
    {
        Attributions.Add(attribution);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(DomainAttribution attribution) => Task.CompletedTask;
}

internal sealed class FakeTrackingNumberRepository : ITrackingNumberRepository
{
    public List<TrackingNumber> Numbers { get; } = new();

    public Task<TrackingNumber?> GetByIdAsync(Guid id) => Task.FromResult(Numbers.FirstOrDefault(n => n.Id == id));

    public Task<TrackingNumber?> GetByDidAsync(string did) => Task.FromResult(Numbers.FirstOrDefault(n => n.Did == did));

    public Task<IReadOnlyList<TrackingNumber>> GetByPoolAsync(Guid poolId) =>
        Task.FromResult<IReadOnlyList<TrackingNumber>>(Numbers.Where(n => n.PoolId == poolId).ToList());

    public Task AddAsync(TrackingNumber number)
    {
        Numbers.Add(number);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(TrackingNumber number) => Task.CompletedTask;
}

internal sealed class FakeNumberPoolRepository : INumberPoolRepository
{
    public List<NumberPool> Pools { get; } = new();

    public Task<NumberPool?> GetByIdAsync(Guid id) => Task.FromResult(Pools.FirstOrDefault(p => p.Id == id));

    public Task<IReadOnlyList<NumberPool>> GetByScopeAsync(string scopeType, Guid scopeRef) =>
        Task.FromResult<IReadOnlyList<NumberPool>>(Pools.Where(p => p.ScopeType == scopeType && p.ScopeRef == scopeRef).ToList());

    public Task<IReadOnlyList<NumberPool>> GetAllAsync() => Task.FromResult<IReadOnlyList<NumberPool>>(Pools.ToList());

    public Task AddAsync(NumberPool pool)
    {
        Pools.Add(pool);
        return Task.CompletedTask;
    }
}

internal sealed class FakeAllocationRepository : IAllocationRepository
{
    public List<DomainAllocation> Allocations { get; } = new();

    public Task<DomainAllocation?> GetBySessionIdAsync(Guid sessionId) =>
        Task.FromResult(Allocations.FirstOrDefault(a => a.SessionId == sessionId));

    public Task<IReadOnlyList<DomainAllocation>> GetAllBySessionIdAsync(Guid sessionId) =>
        Task.FromResult<IReadOnlyList<DomainAllocation>>(Allocations.Where(a => a.SessionId == sessionId).ToList());

    public Task<IReadOnlyList<DomainAllocation>> GetCoveringInstantAsync(Guid trackingNumberId, DateTimeOffset instant) =>
        Task.FromResult<IReadOnlyList<DomainAllocation>>(
            Allocations.Where(a => a.TrackingNumberId == trackingNumberId && a.CoversInstant(instant)).ToList());

    public Task AddAsync(DomainAllocation allocation)
    {
        Allocations.Add(allocation);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(DomainAllocation allocation) => Task.CompletedTask;
}

// FR-050: a simplified stand-in for the real atomic SKIP LOCKED allocator — "available"
// here just means "not already recorded in Allocations", which is enough to exercise
// AllocationService's multi-pool orchestration (distinct-pool-per-session, session growth,
// per-pool exhaustion) without needing a real database. The atomic SQL semantics
// themselves are exercised separately, against a real MySQL instance.
internal sealed class FakeAtomicAllocator : IAtomicAllocator
{
    private readonly FakeSessionRepository _sessionRepository;
    private readonly FakeAllocationRepository _allocationRepository;

    // Writes into the same FakeSessionRepository/FakeAllocationRepository instances the
    // test injects into AllocationService, exactly as the real AtomicAllocator inserts
    // into the same `sessions`/`allocations` tables the real repositories read from —
    // otherwise a test asserting against the repository's own state would never see what
    // this fake "allocated".
    public FakeAtomicAllocator(FakeSessionRepository sessionRepository, FakeAllocationRepository allocationRepository)
    {
        _sessionRepository = sessionRepository;
        _allocationRepository = allocationRepository;
    }

    public List<TrackingNumber> AvailableNumbers { get; } = new();
    public List<Visitor> Visitors { get; } = new();

    public List<Session> Sessions => _sessionRepository.Sessions;
    public List<DomainAllocation> Allocations => _allocationRepository.Allocations;

    public Task<AllocationAttemptResult> TryAllocateAsync(
        Visitor visitor, Session session, Guid poolId, TimeSpan cooldown,
        DateTimeOffset windowStart, TimeSpan allocationWindowExtension, DateTimeOffset now)
    {
        var candidate = AvailableNumbers.FirstOrDefault(n => n.PoolId == poolId && Allocations.All(a => a.TrackingNumberId != n.Id));
        if (candidate is null)
        {
            return Task.FromResult(new AllocationAttemptResult(false, null));
        }

        Visitors.Add(visitor);
        _sessionRepository.Sessions.Add(session);
        var allocation = DomainAllocation.Create(candidate.Id, session.Id, poolId, windowStart, session.ExpiresAt, allocationWindowExtension);
        _allocationRepository.Allocations.Add(allocation);
        return Task.FromResult(new AllocationAttemptResult(true, allocation));
    }

    public Task<AllocationAttemptResult> TryAllocateAdditionalAsync(
        Session session, Guid poolId, TimeSpan cooldown,
        DateTimeOffset windowStart, TimeSpan allocationWindowExtension, DateTimeOffset now)
    {
        var candidate = AvailableNumbers.FirstOrDefault(n => n.PoolId == poolId && Allocations.All(a => a.TrackingNumberId != n.Id));
        if (candidate is null)
        {
            return Task.FromResult(new AllocationAttemptResult(false, null));
        }

        var allocation = DomainAllocation.Create(candidate.Id, session.Id, poolId, windowStart, session.ExpiresAt, allocationWindowExtension);
        _allocationRepository.Allocations.Add(allocation);
        return Task.FromResult(new AllocationAttemptResult(true, allocation));
    }
}

internal sealed class FakeReviewCaseRepository : IReviewCaseRepository
{
    public List<ReviewCase> ReviewCases { get; } = new();

    public Task<ReviewCase?> GetByIdAsync(Guid id) =>
        Task.FromResult(ReviewCases.FirstOrDefault(r => r.Id == id));

    public Task<IReadOnlyList<ReviewCase>> GetOpenAsync() =>
        Task.FromResult<IReadOnlyList<ReviewCase>>(
            ReviewCases.Where(r => r.Status == ReviewCaseStatus.Open).ToList());

    public Task AddAsync(ReviewCase reviewCase)
    {
        ReviewCases.Add(reviewCase);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(ReviewCase reviewCase) => Task.CompletedTask;
}

internal sealed class FakeSessionRepository : ISessionRepository
{
    public List<Session> Sessions { get; } = new();

    public Task<Session?> GetByIdAsync(Guid id) => Task.FromResult(Sessions.FirstOrDefault(s => s.Id == id));

    public Task AddAsync(Session session)
    {
        Sessions.Add(session);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(Session session) => Task.CompletedTask;
}

internal sealed class FakeWebsiteRepository : IWebsiteRepository
{
    public List<Website> Websites { get; } = new();

    public Task<Website?> GetByIdAsync(Guid id) => Task.FromResult(Websites.FirstOrDefault(w => w.Id == id));

    public Task<Website?> GetByOriginAsync(string origin) =>
        Task.FromResult(Websites.FirstOrDefault(w => w.PermittedOrigins.Contains(origin, StringComparer.OrdinalIgnoreCase)));

    public Task<IReadOnlyList<Website>> GetAllAsync() => Task.FromResult<IReadOnlyList<Website>>(Websites.ToList());

    public Task AddAsync(Website website)
    {
        Websites.Add(website);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(Website website) => Task.CompletedTask;
}

internal sealed class FakeQualificationRuleRepository : IQualificationRuleRepository
{
    public List<QualificationRule> Rules { get; } = new();

    public Task<QualificationRule?> GetByIdAsync(Guid id) => Task.FromResult(Rules.FirstOrDefault(r => r.Id == id));

    public Task<QualificationRule?> GetInForceAsync(QualificationScopeType scopeType, string? scopeRef, DateTimeOffset instant) =>
        Task.FromResult(Rules.FirstOrDefault(
            r => r.ScopeType == scopeType && r.ScopeRef == scopeRef && r.IsInForceAt(instant)));

    public Task<QualificationRule?> GetLatestVersionAsync(QualificationScopeType scopeType, string? scopeRef) =>
        Task.FromResult(Rules.FirstOrDefault(
            r => r.ScopeType == scopeType && r.ScopeRef == scopeRef && r.EffectiveEnd is null));

    public Task<IReadOnlyList<QualificationRule>> GetByScopeAsync(QualificationScopeType scopeType, string? scopeRef) =>
        Task.FromResult<IReadOnlyList<QualificationRule>>(
            Rules.Where(r => r.ScopeType == scopeType && r.ScopeRef == scopeRef).ToList());

    public Task AddAsync(QualificationRule rule)
    {
        Rules.Add(rule);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(QualificationRule rule) => Task.CompletedTask;

    public Task DeleteAsync(Guid id)
    {
        Rules.RemoveAll(r => r.Id == id);
        return Task.CompletedTask;
    }
}

internal sealed class FakeQualificationResultRepository : IQualificationResultRepository
{
    public List<QualificationResult> Results { get; } = new();

    public Task<QualificationResult?> GetCurrentByCallIdAsync(Guid callId) =>
        Task.FromResult(Results.FirstOrDefault(r => r.CallId == callId && r.IsCurrent));

    public Task AddAsync(QualificationResult result)
    {
        Results.Add(result);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(QualificationResult result) => Task.CompletedTask;
}

internal sealed class FakeConversionPublicationRepository : IConversionPublicationRepository
{
    public List<ConversionPublication> Publications { get; } = new();

    // The real repository resolves a publication's call via a
    // conversion_publications -> qualification_results.call_id join; this fake has no
    // such join to work with, so tests populate this mapping directly (mirroring that
    // column) before exercising code that calls GetActiveForCallAsync.
    public Dictionary<Guid, Guid> CallIdByQualificationResultId { get; } = new();

    // No PublicationWorker unit tests exercise this fake yet.
    public Task<IReadOnlyList<PublicationWorkItem>> GetRetryableAsync(int maxAttempts, int limit) =>
        Task.FromResult<IReadOnlyList<PublicationWorkItem>>(Array.Empty<PublicationWorkItem>());

    public Task<ConversionPublication?> GetActiveForCallAsync(Guid callId, PublicationDestination destination) =>
        Task.FromResult(Publications.FirstOrDefault(p =>
            CallIdByQualificationResultId.TryGetValue(p.QualificationResultId, out var c) && c == callId
            && p.Destination == destination && p.Status != PublicationStatus.Retracted));

    public Task AddAsync(ConversionPublication publication)
    {
        Publications.Add(publication);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(ConversionPublication publication) => Task.CompletedTask;
}

internal sealed class FakeGoogleAdsClient : IGoogleAdsClient
{
    public List<GoogleAdsConversion> Uploaded { get; } = new();
    public List<string> Retracted { get; } = new();
    public List<(string ExternalId, GoogleAdsConversion Conversion)> Adjusted { get; } = new();
    public string NextExternalId { get; set; } = "gclid-conversion-1";

    public Task<string> UploadConversionAsync(GoogleAdsConversion conversion, CancellationToken cancellationToken)
    {
        Uploaded.Add(conversion);
        return Task.FromResult(NextExternalId);
    }

    public Task RetractAsync(string externalId, CancellationToken cancellationToken)
    {
        Retracted.Add(externalId);
        return Task.CompletedTask;
    }

    public Task AdjustAsync(string externalId, GoogleAdsConversion conversion, CancellationToken cancellationToken)
    {
        Adjusted.Add((externalId, conversion));
        return Task.CompletedTask;
    }
}

internal sealed class FakeGa4Client : IGa4Client
{
    public List<Ga4Event> SentEvents { get; } = new();

    public Task SendEventAsync(Ga4Event conversionEvent, CancellationToken cancellationToken)
    {
        SentEvents.Add(conversionEvent);
        return Task.CompletedTask;
    }
}

internal sealed record RecordedAuditEntry(string Action, string TargetType, string TargetId, object? Before, object? After);

internal sealed class FakeAuditLogger : IAuditLogger
{
    public List<RecordedAuditEntry> Entries { get; } = new();

    public Task RecordAsync(string action, string targetType, string targetId, object? before, object? after)
    {
        Entries.Add(new RecordedAuditEntry(action, targetType, targetId, before, after));
        return Task.CompletedTask;
    }
}

internal sealed class FakeVisitor
{
    public required Guid Id { get; init; }
    public required DateTimeOffset FirstSeenAt { get; init; }
    public DateTimeOffset? DeIdentifiedAt { get; set; }
}

internal sealed class FakeRetentionCall
{
    public required Guid Id { get; init; }
    public required DateTimeOffset StartedAt { get; init; }
    public string? CallerId { get; set; }
    public DateTimeOffset? DeIdentifiedAt { get; set; }
    public bool Purged { get; set; }
    public Guid VisitorId { get; init; }
}

internal sealed class FakePublication
{
    public required Guid Id { get; init; }
    public required DateTimeOffset CallStartedAt { get; init; }
    public string ExternalId { get; set; } = string.Empty;
    public DateTimeOffset? DeIdentifiedAt { get; set; }
}

// FakeRetentionRepository intentionally keeps everything in one process-local list rather
// than the real repository's system-wide SQL sweep — RetentionServiceTests uses this
// specifically so its assertions about which rows a sweep did/didn't touch can never be
// confused by unrelated data the way a shared real database inevitably accumulates.
internal sealed class FakeRetentionRepository : IRetentionRepository
{
    public List<FakeVisitor> Visitors { get; } = new();
    public List<FakeRetentionCall> Calls { get; } = new();
    public List<FakePublication> Publications { get; } = new();
    public HashSet<Guid> OpenReviewCallIds { get; } = new();
    public List<string> PurgedAuditEntriesCutoffLog { get; } = new();

    public Task<IReadOnlyList<Guid>> GetVisitorIdsEligibleForDeIdentificationAsync(DateTimeOffset cutoff) =>
        Task.FromResult<IReadOnlyList<Guid>>(
            Visitors.Where(v => v.FirstSeenAt < cutoff && v.DeIdentifiedAt is null).Select(v => v.Id).ToList());

    public Task DeIdentifyVisitorAsync(Guid visitorId, DateTimeOffset now)
    {
        var visitor = Visitors.First(v => v.Id == visitorId);
        visitor.DeIdentifiedAt = now;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<(Guid Id, string? CallerId)>> GetCallsEligibleForDeIdentificationAsync(DateTimeOffset cutoff) =>
        Task.FromResult<IReadOnlyList<(Guid, string?)>>(
            Calls.Where(c => c.StartedAt < cutoff && c.DeIdentifiedAt is null).Select(c => (c.Id, c.CallerId)).ToList());

    public Task<bool> HasOpenReviewCaseAsync(Guid callId) => Task.FromResult(OpenReviewCallIds.Contains(callId));

    public Task DeIdentifyCallAsync(Guid callId, string? surrogateCallerId, DateTimeOffset now)
    {
        var call = Calls.First(c => c.Id == callId);
        if (surrogateCallerId is not null)
        {
            call.CallerId = surrogateCallerId;
        }

        call.DeIdentifiedAt = now;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<(Guid Id, string ExternalId)>> GetPublicationsEligibleForDeIdentificationAsync(DateTimeOffset cutoff) =>
        Task.FromResult<IReadOnlyList<(Guid, string)>>(
            Publications.Where(p => p.CallStartedAt < cutoff && p.DeIdentifiedAt is null).Select(p => (p.Id, p.ExternalId)).ToList());

    public Task DeIdentifyPublicationAsync(Guid publicationId, string surrogateExternalId, DateTimeOffset now)
    {
        var publication = Publications.First(p => p.Id == publicationId);
        publication.ExternalId = surrogateExternalId;
        publication.DeIdentifiedAt = now;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<Guid>> GetCallsEligibleForPurgeAsync(DateTimeOffset cutoff) =>
        Task.FromResult<IReadOnlyList<Guid>>(Calls.Where(c => c.StartedAt < cutoff && !c.Purged).Select(c => c.Id).ToList());

    public Task PurgeCallAsync(Guid callId)
    {
        Calls.First(c => c.Id == callId).Purged = true;
        return Task.CompletedTask;
    }

    public Task PurgeAuditLogOlderThanAsync(DateTimeOffset cutoff)
    {
        PurgedAuditEntriesCutoffLog.Add(cutoff.ToString("O"));
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<(Guid Id, string? CallerId)>> GetCallsForVisitorAsync(Guid visitorId) =>
        Task.FromResult<IReadOnlyList<(Guid, string?)>>(
            Calls.Where(c => c.VisitorId == visitorId).Select(c => (c.Id, c.CallerId)).ToList());
}
