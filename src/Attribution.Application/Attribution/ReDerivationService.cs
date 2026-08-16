using Attribution.Application.Publication;
using Attribution.Application.Qualification;
using Attribution.Domain.Calls;
using Attribution.Domain.Qualification;
using Microsoft.Extensions.Logging;
using DomainAttribution = Attribution.Domain.Calls.Attribution;

namespace Attribution.Application.Attribution;

// FR-045: a call 8x8 reports as changed — still in progress at the previous ingestion, or
// later corrected — is updated in place, and both its attribution and (once it's
// attributed) its qualification are re-derived, never overwritten silently: each
// superseded decision is retained as history alongside the reason for the change.
// Re-ingesting an identical record is a no-op (Call.ApplyRestatement's own
// change-detection), which is what makes re-derivation idempotent without IngestionService
// needing to know the difference between "genuinely restated" and "seen before". FR-044:
// also where a restatement's correction to an already-published call gets propagated —
// CorrectionService, not QualificationService.QualifyAsync's own (fresh-publish-only)
// enqueue path, since there is something to retract/adjust rather than enqueue.
public sealed class ReDerivationService
{
    private readonly ICallRepository _callRepository;
    private readonly IAttributionRepository _attributionRepository;
    private readonly IQualificationResultRepository _qualificationResultRepository;
    private readonly AttributionService _attributionService;
    private readonly QualificationService _qualificationService;
    private readonly CorrectionService _correctionService;
    private readonly ILogger<ReDerivationService> _logger;

    public ReDerivationService(
        ICallRepository callRepository,
        IAttributionRepository attributionRepository,
        IQualificationResultRepository qualificationResultRepository,
        AttributionService attributionService,
        QualificationService qualificationService,
        CorrectionService correctionService,
        ILogger<ReDerivationService> logger)
    {
        _callRepository = callRepository;
        _attributionRepository = attributionRepository;
        _qualificationResultRepository = qualificationResultRepository;
        _attributionService = attributionService;
        _qualificationService = qualificationService;
        _correctionService = correctionService;
        _logger = logger;
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

        var previousQualification = await _qualificationResultRepository.GetCurrentByCallIdAsync(call.Id);
        if (previousQualification is not null)
        {
            previousQualification.Supersede("call_record_restated");
            await _qualificationResultRepository.UpdateAsync(previousQualification);
        }

        QualificationResult? newQualification = null;
        if (attribution.State == AttributionState.Attributed)
        {
            // QualifyAsync's own enqueue path only fires for a genuinely fresh
            // qualification (PublicationService no-ops when an active publication already
            // exists) — an already-published call's correction is CorrectionService's job,
            // called separately below with the old and new results to compare.
            newQualification = await _qualificationService.QualifyAsync(call, attribution, now);
        }

        await _correctionService.CorrectIfNeededAsync(call, attribution, previousQualification, newQualification, now);

        // FR-041: the re-derivation path's own trace line, correlated by the same CallId
        // an earlier ingestion-time log line (IngestionService) would have used.
        _logger.LogInformation(
            "Re-derived call {CallId}: attribution={AttributionState} allocationId={AllocationId} qualified={Qualified}",
            call.Id, attribution.State, attribution.AllocationId, newQualification?.IsQualified);

        return attribution;
    }
}
