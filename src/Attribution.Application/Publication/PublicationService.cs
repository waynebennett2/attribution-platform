using Attribution.Domain.Calls;
using Attribution.Domain.Publication;
using Attribution.Domain.Qualification;
using Attribution.Domain.Sessions;
using DomainAttribution = Attribution.Domain.Calls.Attribution;

namespace Attribution.Application.Publication;

// FR-025-FR-028: enqueues a qualified call's outbox rows — the PublicationWorker (not this
// service) is what actually calls Google Ads/GA4. Idempotent by construction: only ever
// writes a new row when no active (non-retracted) one already exists for the
// call+destination, so calling this repeatedly for the same qualification decision (e.g.
// QualificationService.QualifyAsync running again on an unrelated re-derivation) can never
// produce a duplicate — the guarantee FR-027 asks for.
public sealed class PublicationService
{
    private readonly IConversionPublicationRepository _publicationRepository;
    private readonly ISessionRepository _sessionRepository;

    public PublicationService(IConversionPublicationRepository publicationRepository, ISessionRepository sessionRepository)
    {
        _publicationRepository = publicationRepository;
        _sessionRepository = sessionRepository;
    }

    public async Task EnqueueAsync(Call call, DomainAttribution attribution, QualificationResult result, DateTimeOffset now)
    {
        if (!result.IsQualified)
        {
            // FR-025: only qualified calls are published. An unqualified result simply has
            // nothing to enqueue — this is not a skip (no destination was ever considered).
            return;
        }

        var session = attribution.SessionId is { } sessionId ? await _sessionRepository.GetByIdAsync(sessionId) : null;

        await EnqueueDestinationAsync(call, result, session, PublicationDestination.GoogleAds, now);
        await EnqueueDestinationAsync(call, result, session, PublicationDestination.Ga4, now);
    }

    private async Task EnqueueDestinationAsync(
        Call call, QualificationResult result, Session? session, PublicationDestination destination, DateTimeOffset now)
    {
        var existing = await _publicationRepository.GetActiveForCallAsync(call.Id, destination);
        if (existing is not null)
        {
            return; // already enqueued/sent/skipped for this episode.
        }

        var idempotencyKey = $"{call.Id}:{destination}:{Guid.NewGuid():N}";

        // FR-026: a Google Ads conversion needs a click identifier (GCLID, or GBRAID/WBRAID
        // for app/enhanced-click flows); a GA4 event needs the client id captured at the
        // same moment (FR-015). Neither ever appears later for a session that never
        // captured one, so this is recorded once rather than retried.
        var skipReason = destination switch
        {
            PublicationDestination.GoogleAds when session is null
                || (session.Arrival.Gclid is null && session.Arrival.Gbraid is null && session.Arrival.Wbraid is null)
                => "no_google_click_identifier",
            PublicationDestination.Ga4 when session?.Arrival.Ga4ClientId is null => "no_ga4_client_id",
            _ => null,
        };

        var publication = skipReason is not null
            ? ConversionPublication.CreateSkipped(result.Id, destination, idempotencyKey, skipReason)
            : ConversionPublication.CreatePending(result.Id, destination, idempotencyKey);

        await _publicationRepository.AddAsync(publication);
    }
}
