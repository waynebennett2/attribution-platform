using Attribution.Domain.Calls;
using DomainAttribution = Attribution.Domain.Calls.Attribution;

namespace Attribution.Application.Attribution;

// FR-045: a call 8x8 reports as changed — still in progress at the previous ingestion, or
// later corrected — is updated in place and its attribution re-derived, never overwritten
// silently: the superseded decision is retained as history alongside the reason for the
// change. Re-ingesting an identical record is a no-op (Call.ApplyRestatement's own
// change-detection), which is what makes re-derivation idempotent without IngestionService
// needing to know the difference between "genuinely restated" and "seen before".
//
// Qualification re-derivation (the rest of FR-045) is wired in once User Story 3 lands
// (tasks.md T064); this service currently covers attribution only.
public sealed class ReDerivationService
{
    private readonly ICallRepository _callRepository;
    private readonly IAttributionRepository _attributionRepository;
    private readonly AttributionService _attributionService;

    public ReDerivationService(
        ICallRepository callRepository,
        IAttributionRepository attributionRepository,
        AttributionService attributionService)
    {
        _callRepository = callRepository;
        _attributionRepository = attributionRepository;
        _attributionService = attributionService;
    }

    public async Task<DomainAttribution?> ReDeriveIfChangedAsync(Call call, Analytics8x8CallRecord record, DateTimeOffset now)
    {
        var changed = call.ApplyRestatement(
            record.AnsweredAt, record.EndedAt, record.ConnectedDurationSeconds, record.Disposition, record.IsFinal, now);
        if (!changed)
        {
            return null;
        }

        await _callRepository.UpdateAsync(call);

        var current = await _attributionRepository.GetCurrentByCallIdAsync(call.Id);
        if (current is not null)
        {
            current.Supersede("call_record_restated");
            await _attributionRepository.UpdateAsync(current);
        }

        return await _attributionService.AttributeAsync(call, now);
    }
}
