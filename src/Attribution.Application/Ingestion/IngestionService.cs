using Attribution.Application.Attribution;
using Attribution.Domain.Calls;

namespace Attribution.Application.Ingestion;

// FR-016, FR-017, FR-042: idempotent CDR/Call Leg upsert plus checkpoint advancement — the
// core pipeline shared by both the live IngestionWorker loop and the operator-triggered
// BackfillService, so neither can drift from the other's idempotency guarantees.
public sealed class IngestionService
{
    private readonly ICallRepository _callRepository;
    private readonly ICallLegRepository _callLegRepository;
    private readonly IIngestionCheckpointRepository _checkpointRepository;
    private readonly AttributionService _attributionService;
    private readonly ReDerivationService _reDerivationService;

    public IngestionService(
        ICallRepository callRepository,
        ICallLegRepository callLegRepository,
        IIngestionCheckpointRepository checkpointRepository,
        AttributionService attributionService,
        ReDerivationService reDerivationService)
    {
        _callRepository = callRepository;
        _callLegRepository = callLegRepository;
        _checkpointRepository = checkpointRepository;
        _attributionService = attributionService;
        _reDerivationService = reDerivationService;
    }

    // Processes one polled page and, if the caller wants the named feed's checkpoint
    // advanced (the live worker does; BackfillService does not — see PollRangeAsync),
    // does so only once every record in the page has been durably upserted, so a crash
    // mid-page is always safe to resume from where the checkpoint last landed.
    public async Task ProcessPageAsync(string feed, Analytics8x8Page page, DateTimeOffset now)
    {
        foreach (var record in page.Calls)
        {
            await ProcessCallAsync(record, now);
        }

        foreach (var leg in page.CallLegs)
        {
            await ProcessCallLegAsync(leg);
        }

        if (page.NextCheckpointPosition is not null)
        {
            await AdvanceCheckpointAsync(feed, page.NextCheckpointPosition, now);
        }
    }

    private async Task ProcessCallAsync(Analytics8x8CallRecord record, DateTimeOffset now)
    {
        var existing = await _callRepository.GetBySourceRecordIdAsync(record.SourceRecordId);
        if (existing is null)
        {
            var call = Call.Create(
                record.SourceRecordId, record.Direction, record.DialledNumber, record.CallerId,
                record.StartedAt, record.AnsweredAt, record.EndedAt, record.ConnectedDurationSeconds,
                record.Disposition, record.IsFinal, now);
            await _callRepository.AddAsync(call);

            // spec.md "Ingestion resilience": a Call Leg may have arrived first and be
            // sitting orphaned, waiting for this CDR.
            var orphans = await _callLegRepository.GetOrphanedBySourceCallRecordIdAsync(record.SourceRecordId);
            foreach (var orphan in orphans)
            {
                orphan.LinkToCall(call.Id);
                await _callLegRepository.UpdateAsync(orphan);
            }

            await _attributionService.AttributeAsync(call, now);
            return;
        }

        // FR-045: re-derivation itself no-ops when nothing actually changed, so a
        // byte-identical re-ingestion of an already-processed record is a safe no-op here too.
        await _reDerivationService.ReDeriveIfChangedAsync(existing, record, now);
    }

    private async Task ProcessCallLegAsync(Analytics8x8CallLegRecord record)
    {
        var existing = await _callLegRepository.GetBySourceIdsAsync(record.SourceCallRecordId, record.SourceLegId);
        if (existing is not null)
        {
            // Call Legs are immutable once ingested (data-model.md) — repeated ingestion
            // of the identical natural key is a pure no-op, satisfying FR-017.
            return;
        }

        var parentCall = await _callRepository.GetBySourceRecordIdAsync(record.SourceCallRecordId);
        var leg = CallLeg.Create(
            record.SourceCallRecordId, record.SourceLegId, record.SequenceOrRole,
            record.StartedAt, record.EndedAt, parentCall?.Id);
        await _callLegRepository.AddAsync(leg);
    }

    private async Task AdvanceCheckpointAsync(string feed, string position, DateTimeOffset now)
    {
        var checkpoint = await _checkpointRepository.GetByFeedAsync(feed);
        if (checkpoint is null)
        {
            await _checkpointRepository.AddAsync(IngestionCheckpoint.Create(feed, position, now));
            return;
        }

        checkpoint.Advance(position, now);
        await _checkpointRepository.UpdateAsync(checkpoint);
    }
}
