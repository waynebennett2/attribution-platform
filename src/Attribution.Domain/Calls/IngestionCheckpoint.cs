namespace Attribution.Domain.Calls;

// FR-016: the last successfully consumed position for one feed (e.g. "8x8-cdr"), so a
// restart resumes without gap or duplication regardless of polling cadence — cadence
// changes must never alter attribution outcomes, only how often this advances.
public class IngestionCheckpoint
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public string Feed { get; private set; } = string.Empty;
    public string Position { get; private set; } = string.Empty;
    public DateTimeOffset UpdatedAt { get; private set; } = DateTimeOffset.UtcNow;

    private IngestionCheckpoint() { }

    public static IngestionCheckpoint Create(string feed, string position, DateTimeOffset updatedAt) =>
        new() { Feed = feed, Position = position, UpdatedAt = updatedAt };

    public void Advance(string position, DateTimeOffset updatedAt)
    {
        Position = position;
        UpdatedAt = updatedAt;
    }

    internal static IngestionCheckpoint Rehydrate(Guid id, string feed, string position, DateTimeOffset updatedAt) =>
        new() { Id = id, Feed = feed, Position = position, UpdatedAt = updatedAt };
}
