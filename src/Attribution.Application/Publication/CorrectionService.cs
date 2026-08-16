using Attribution.Application.Administration;
using Attribution.Domain.Calls;
using Attribution.Domain.Publication;
using Attribution.Domain.Qualification;
using Attribution.Domain.Sessions;
using DomainAttribution = Attribution.Domain.Calls.Attribution;

namespace Attribution.Application.Publication;

// FR-044: propagates a qualification change to whatever was already published on the
// strength of the superseded result — Google Ads retraction/adjustment; GA4 recorded as
// unpropagatable, since the Measurement Protocol offers no retraction. Every correction
// (and every case that couldn't be propagated) is recorded against the call, audited, and
// idempotent under FR-027: repeating a correction against an already-corrected row is a
// no-op, guarded by each destination branch checking the row's current status first.
public sealed class CorrectionService
{
    private readonly IConversionPublicationRepository _publicationRepository;
    private readonly ISessionRepository _sessionRepository;
    private readonly IGoogleAdsClient _googleAdsClient;
    private readonly IAuditLogger _auditLogger;

    public CorrectionService(
        IConversionPublicationRepository publicationRepository,
        ISessionRepository sessionRepository,
        IGoogleAdsClient googleAdsClient,
        IAuditLogger auditLogger)
    {
        _publicationRepository = publicationRepository;
        _sessionRepository = sessionRepository;
        _googleAdsClient = googleAdsClient;
        _auditLogger = auditLogger;
    }

    // oldResult: the qualification result being superseded (null or never-qualified means
    // nothing was ever published, so there is nothing to correct — PublicationService's own
    // enqueue path already covers a call qualifying for the first time). newResult: null
    // when re-derivation left the call no longer attributed at all — treated the same as
    // "no longer qualified" for correction purposes, since either way nothing justifies the
    // conversion anymore.
    public async Task CorrectIfNeededAsync(
        Call call, DomainAttribution attribution, QualificationResult? oldResult, QualificationResult? newResult, DateTimeOffset now)
    {
        if (oldResult is null || !oldResult.IsQualified)
        {
            return;
        }

        var stillQualified = newResult?.IsQualified ?? false;
        await CorrectGa4Async(call, stillQualified, now);
        await CorrectGoogleAdsAsync(call, attribution, stillQualified, now);
    }

    private async Task CorrectGa4Async(Call call, bool stillQualified, DateTimeOffset now)
    {
        var publication = await _publicationRepository.GetActiveForCallAsync(call.Id, PublicationDestination.Ga4);
        if (publication is null || publication.Status != PublicationStatus.Sent || publication.Correction is not null)
        {
            return; // nothing sent to correct, or already recorded as divergent.
        }

        var reason = stillQualified ? "qualification_details_changed" : "no_longer_qualified";
        publication.MarkUnpropagatable(reason, now);
        await _publicationRepository.UpdateAsync(publication);
        await _auditLogger.RecordAsync(
            "CorrectConversionPublication", "ConversionPublication", publication.Id.ToString(),
            before: new { publication.Status }, after: new { status = "unpropagatable", destination = "Ga4", reason });
    }

    private async Task CorrectGoogleAdsAsync(Call call, DomainAttribution attribution, bool stillQualified, DateTimeOffset now)
    {
        var publication = await _publicationRepository.GetActiveForCallAsync(call.Id, PublicationDestination.GoogleAds);
        if (publication is null || publication.Status is not (PublicationStatus.Sent or PublicationStatus.Adjusted))
        {
            return; // nothing sent to correct.
        }

        if (!stillQualified)
        {
            if (publication.Status == PublicationStatus.Retracted)
            {
                return;
            }

            await _googleAdsClient.RetractAsync(publication.ExternalId!, CancellationToken.None);
            publication.MarkRetracted("no_longer_qualified", now);
            await _publicationRepository.UpdateAsync(publication);
            await _auditLogger.RecordAsync(
                "CorrectConversionPublication", "ConversionPublication", publication.Id.ToString(),
                before: new { publication.Status }, after: new { status = "retracted", destination = "GoogleAds", reason = "no_longer_qualified" });
            return;
        }

        // Still qualified, but the facts behind it changed (e.g. a restated duration) —
        // an adjustment, not a retraction.
        var session = attribution.SessionId is { } sessionId ? await _sessionRepository.GetByIdAsync(sessionId) : null;
        var conversion = new GoogleAdsConversion(
            session?.Arrival.Gclid, session?.Arrival.Gbraid, session?.Arrival.Wbraid, call.StartedAt, ConversionActionId: "call_qualified");
        await _googleAdsClient.AdjustAsync(publication.ExternalId!, conversion, CancellationToken.None);
        publication.MarkAdjusted("qualification_details_changed", now);
        await _publicationRepository.UpdateAsync(publication);
        await _auditLogger.RecordAsync(
            "CorrectConversionPublication", "ConversionPublication", publication.Id.ToString(),
            before: new { publication.Status }, after: new { status = "adjusted", destination = "GoogleAds", reason = "qualification_details_changed" });
    }
}
