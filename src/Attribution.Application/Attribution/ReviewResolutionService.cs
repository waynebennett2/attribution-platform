using Attribution.Application.Publication;
using Attribution.Application.Qualification;
using Attribution.Domain.Audit;
using Attribution.Domain.Calls;
using Attribution.Domain.Qualification;
using Attribution.Domain.Sessions;
using DomainAttribution = Attribution.Domain.Calls.Attribution;

namespace Attribution.Application.Attribution;

// FR-036: a reviewer resolves an ambiguous or disputed call by either crediting it to a
// specific session or confirming it stays unattributed. The resolution is stored as
// ordinary attribution evidence — a new superseding Attribution row, exactly like FR-045's
// re-derivation — rather than as a special "manual override" field, so reporting and audit
// need no separate code path to understand what happened. Where the superseded attribution
// had already been qualified and published (a call that was ambiguous a second time, after
// an earlier resolution and a later restatement), the same CorrectionService FR-045 uses
// propagates the change under FR-044.
public sealed class ReviewResolutionService
{
    private readonly IReviewCaseRepository _reviewCaseRepository;
    private readonly ICallRepository _callRepository;
    private readonly IAttributionRepository _attributionRepository;
    private readonly IQualificationResultRepository _qualificationResultRepository;
    private readonly IAllocationRepository _allocationRepository;
    private readonly IAlertRepository _alertRepository;
    private readonly QualificationService _qualificationService;
    private readonly CorrectionService _correctionService;

    public ReviewResolutionService(
        IReviewCaseRepository reviewCaseRepository,
        ICallRepository callRepository,
        IAttributionRepository attributionRepository,
        IQualificationResultRepository qualificationResultRepository,
        IAllocationRepository allocationRepository,
        IAlertRepository alertRepository,
        QualificationService qualificationService,
        CorrectionService correctionService)
    {
        _reviewCaseRepository = reviewCaseRepository;
        _callRepository = callRepository;
        _attributionRepository = attributionRepository;
        _qualificationResultRepository = qualificationResultRepository;
        _allocationRepository = allocationRepository;
        _alertRepository = alertRepository;
        _qualificationService = qualificationService;
        _correctionService = correctionService;
    }

    public async Task<DomainAttribution> ResolveAsync(Guid reviewCaseId, Guid? chosenSessionId, string resolvedBy, DateTimeOffset now)
    {
        var reviewCase = await _reviewCaseRepository.GetByIdAsync(reviewCaseId)
            ?? throw new InvalidOperationException($"Unknown review case {reviewCaseId}.");

        if (reviewCase.Status == ReviewCaseStatus.Resolved)
        {
            throw new InvalidOperationException($"Review case {reviewCaseId} is already resolved.");
        }

        var call = await _callRepository.GetByIdAsync(reviewCase.CallId)
            ?? throw new InvalidOperationException($"Review case {reviewCaseId} references unknown call {reviewCase.CallId}.");

        var previousAttribution = await _attributionRepository.GetCurrentByCallIdAsync(call.Id);
        if (previousAttribution is not null)
        {
            previousAttribution.Supersede("manual_review_resolved");
            await _attributionRepository.UpdateAsync(previousAttribution);
        }

        DomainAttribution newAttribution;
        string resolution;
        if (chosenSessionId is { } sessionId)
        {
            var allocation = await _allocationRepository.GetBySessionIdAsync(sessionId)
                ?? throw new InvalidOperationException($"Session {sessionId} has no allocation to attribute this call against.");
            newAttribution = DomainAttribution.Attributed(call.Id, sessionId, allocation.Id, now);
            resolution = $"attributed_to_session_{sessionId}";
        }
        else
        {
            newAttribution = DomainAttribution.Unattributed(call.Id, "manual_review_confirmed_unattributed", now);
            resolution = "confirmed_unattributed";
        }

        await _attributionRepository.AddAsync(newAttribution);

        var previousQualification = await _qualificationResultRepository.GetCurrentByCallIdAsync(call.Id);
        if (previousQualification is not null)
        {
            previousQualification.Supersede("manual_review_resolved");
            await _qualificationResultRepository.UpdateAsync(previousQualification);
        }

        QualificationResult? newQualification = null;
        if (newAttribution.State == AttributionState.Attributed)
        {
            newQualification = await _qualificationService.QualifyAsync(call, newAttribution, now);
        }

        await _correctionService.CorrectIfNeededAsync(call, newAttribution, previousQualification, newQualification, now);

        reviewCase.Resolve(resolvedBy, resolution, now);
        await _reviewCaseRepository.UpdateAsync(reviewCase);

        // AlertingService's own review-case-age sweep only ever iterates open cases, so it
        // can never observe this one turning healthy again once it stops being open — the
        // resolution itself is what proves the alertable condition ("unresolved past
        // threshold") no longer holds, so this is the only place positioned to clear it.
        var openAgeAlert = await _alertRepository.GetOpenAsync(AlertConditionType.ReviewCaseAge, reviewCase.Id.ToString());
        if (openAgeAlert is not null)
        {
            openAgeAlert.Clear(now);
            await _alertRepository.UpdateAsync(openAgeAlert);
        }

        return newAttribution;
    }
}
