using Attribution.Application.Qualification;
using Attribution.Domain.Calls;
using Attribution.Domain.Qualification;
using DomainAttribution = Attribution.Domain.Calls.Attribution;

namespace Attribution.Application.Attribution;

// FR-045: a call 8x8 reports as changed — still in progress at the previous ingestion, or
// later corrected — is updated in place, and both its attribution and (once it's
// attributed) its qualification are re-derived, never overwritten silently: each
// superseded decision is retained as history alongside the reason for the change.
// Re-ingesting an identical record is a no-op (Call.ApplyRestatement's own
// change-detection), which is what makes re-derivation idempotent without IngestionService
// needing to know the difference between "genuinely restated" and "seen before".
public sealed class ReDerivationService
{
    private readonly ICallRepository _callRepository;
    private readonly IAttributionRepository _attributionRepository;
    private readonly IQualificationResultRepository _qualificationResultRepository;
    private readonly AttributionService _attributionService;
    private readonly QualificationService _qualificationService;

    public ReDerivationService(
        ICallRepository callRepository,
        IAttributionRepository attributionRepository,
        IQualificationResultRepository qualificationResultRepository,
        AttributionService attributionService,
        QualificationService qualificationService)
    {
        _callRepository = callRepository;
        _attributionRepository = attributionRepository;
        _qualificationResultRepository = qualificationResultRepository;
        _attributionService = attributionService;
        _qualificationService = qualificationService;
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

        var currentAttribution = await _attributionRepository.GetCurrentByCallIdAsync(call.Id);
        if (currentAttribution is not null)
        {
            currentAttribution.Supersede("call_record_restated");
            await _attributionRepository.UpdateAsync(currentAttribution);
        }

        var attribution = await _attributionService.AttributeAsync(call, now);

        var currentQualification = await _qualificationResultRepository.GetCurrentByCallIdAsync(call.Id);
        if (currentQualification is not null)
        {
            currentQualification.Supersede("call_record_restated");
            await _qualificationResultRepository.UpdateAsync(currentQualification);
        }

        if (attribution.State == AttributionState.Attributed)
        {
            await _qualificationService.QualifyAsync(call, attribution, now);
        }

        return attribution;
    }
}
