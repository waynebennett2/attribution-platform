namespace Attribution.Domain.Calls;

// research.md §8: Analytics for 8x8 Work source data, already shaped to this platform's
// needs. The concrete client (Infrastructure) is responsible for translating 8x8's own
// wire format into these records so the ingestion pipeline (Application) never depends on
// 8x8's API shape directly.
public interface IAnalytics8x8Client
{
    // FR-016: polls forward from the given checkpoint position (null on first run),
    // returning whatever's new plus the position to checkpoint once it's been processed.
    Task<Analytics8x8Page> PollAsync(string? checkpointPosition, CancellationToken cancellationToken);

    // FR-042: operator-specified period backfill/replay. Independent of the live
    // checkpoint — callers must not advance it from this page's result.
    Task<Analytics8x8Page> PollRangeAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken);
}

public sealed record Analytics8x8Page(
    IReadOnlyList<Analytics8x8CallRecord> Calls,
    IReadOnlyList<Analytics8x8CallLegRecord> CallLegs,
    string? NextCheckpointPosition);

public sealed record Analytics8x8CallRecord(
    string SourceRecordId,
    CallDirection Direction,
    string DialledNumber,
    string? CallerId,
    DateTimeOffset StartedAt,
    DateTimeOffset? AnsweredAt,
    DateTimeOffset? EndedAt,
    int? ConnectedDurationSeconds,
    string? Disposition,
    bool IsFinal);

public sealed record Analytics8x8CallLegRecord(
    string SourceCallRecordId,
    string SourceLegId,
    string? SequenceOrRole,
    DateTimeOffset? StartedAt,
    DateTimeOffset? EndedAt);
