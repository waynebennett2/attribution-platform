using Attribution.Domain.Qualification;

namespace Attribution.Application.Qualification;

// FR-024: within one scope, consecutive rule versions' effective periods must be
// contiguous and non-overlapping — a new version's effective_start must equal the prior
// version's effective_end exactly. The API contract only takes an effective_start for the
// new version (contracts/admin-api.md), so this service *derives* the prior version's
// close from it rather than trusting a separately-supplied effective_end — a gap or
// overlap becomes structurally impossible rather than merely validated against, the same
// pattern FR-006 already applies to cooldown vs. attribution window.
public sealed class RuleVersioningService
{
    private readonly IQualificationRuleRepository _ruleRepository;

    public RuleVersioningService(IQualificationRuleRepository ruleRepository)
    {
        _ruleRepository = ruleRepository;
    }

    public async Task<QualificationRule> CreateVersionAsync(
        QualificationScopeType scopeType,
        string? scopeRef,
        QualificationConditions conditions,
        DateTimeOffset effectiveStart,
        string createdBy,
        DateTimeOffset now)
    {
        var latest = await _ruleRepository.GetLatestVersionAsync(scopeType, scopeRef);

        if (latest is null)
        {
            var first = QualificationRule.Create(scopeType, scopeRef, version: 1, conditions, effectiveStart, null, createdBy, now);
            await _ruleRepository.AddAsync(first);
            return first;
        }

        if (effectiveStart <= latest.EffectiveStart)
        {
            // Closing the current version at or before its own start would leave a gap —
            // nothing would cover the instants between the new period's start and the
            // current version's actual start.
            throw new QualificationRuleContiguityException(
                $"A new version's effective_start ({effectiveStart:O}) must be after the currently-open version's effective_start ({latest.EffectiveStart:O}).");
        }

        latest.SetEffectiveEnd(effectiveStart);
        await _ruleRepository.UpdateAsync(latest);

        var next = QualificationRule.Create(scopeType, scopeRef, latest.Version + 1, conditions, effectiveStart, null, createdBy, now);
        await _ruleRepository.AddAsync(next);
        return next;
    }

    // FR-024's "never alter... already judged": only a not-yet-effective future version
    // (effective_start still ahead of now) may be removed. Reopens whichever version it
    // had closed, so that scope reverts to exactly the state it was in before the deleted
    // version was created.
    public async Task DeleteFutureVersionAsync(Guid id, DateTimeOffset now)
    {
        var rule = await _ruleRepository.GetByIdAsync(id)
            ?? throw new InvalidOperationException($"Unknown qualification rule {id}.");

        if (rule.EffectiveStart <= now)
        {
            throw new InvalidOperationException(
                "Only a not-yet-effective future version may be deleted — a live or past version has already judged calls.");
        }

        var scopeVersions = await _ruleRepository.GetByScopeAsync(rule.ScopeType, rule.ScopeRef);
        var predecessor = scopeVersions.SingleOrDefault(v => v.EffectiveEnd == rule.EffectiveStart);
        if (predecessor is not null)
        {
            predecessor.SetEffectiveEnd(null);
            await _ruleRepository.UpdateAsync(predecessor);
        }

        await _ruleRepository.DeleteAsync(id);
    }
}

public sealed class QualificationRuleContiguityException : InvalidOperationException
{
    public QualificationRuleContiguityException(string message) : base(message) { }
}
