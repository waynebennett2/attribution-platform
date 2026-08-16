using Attribution.Domain.Audit;
using Attribution.Domain.Calls;
using Attribution.Domain.Pools;
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

internal sealed class FakeAllocationRepository : IAllocationRepository
{
    public List<DomainAllocation> Allocations { get; } = new();

    public Task<DomainAllocation?> GetBySessionIdAsync(Guid sessionId) =>
        Task.FromResult(Allocations.FirstOrDefault(a => a.SessionId == sessionId));

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

internal sealed class FakeReviewCaseRepository : IReviewCaseRepository
{
    public List<ReviewCase> ReviewCases { get; } = new();

    public Task AddAsync(ReviewCase reviewCase)
    {
        ReviewCases.Add(reviewCase);
        return Task.CompletedTask;
    }
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
