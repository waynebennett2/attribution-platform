namespace Attribution.Domain.Sessions;

// FR-003, FR-018, FR-019: the binding of one tracking number to one session for a
// bounded time window — the sole evidence used for attribution.
public class Allocation
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid TrackingNumberId { get; private set; }
    public Guid SessionId { get; private set; }
    public Guid? PoolIdAtAllocation { get; private set; }
    public DateTimeOffset WindowStart { get; private set; }
    public DateTimeOffset WindowEnd { get; private set; }
    public bool IsShadow { get; private set; } // FR-049
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;

    private Allocation() { }

    // FR-018: the window runs from the moment the number is first displayed until 30
    // minutes (configurable) after the session ends — computed once the session's actual
    // end is known, so this factory takes the session's expiry rather than assuming it.
    public static Allocation Create(
        Guid trackingNumberId,
        Guid sessionId,
        Guid? poolIdAtAllocation,
        DateTimeOffset windowStart,
        DateTimeOffset sessionExpiresAt,
        TimeSpan allocationWindowExtension,
        bool isShadow = false)
    {
        return new Allocation
        {
            TrackingNumberId = trackingNumberId,
            SessionId = sessionId,
            PoolIdAtAllocation = poolIdAtAllocation,
            WindowStart = windowStart,
            WindowEnd = sessionExpiresAt.Add(allocationWindowExtension),
            IsShadow = isShadow,
        };
    }

    // FR-018, FR-039: consent withdrawal ends the allocation window at the moment of
    // withdrawal — no extension — distinct from CloseAtSessionEnd's ordinary path.
    public void CloseImmediately(DateTimeOffset closedAt) => WindowEnd = closedAt;

    // FR-018: an ordinary session end (timeout) still gets the full extension from
    // whichever expiry the session actually ended at.
    public void CloseAtSessionEnd(DateTimeOffset sessionEndedAt, TimeSpan allocationWindowExtension) =>
        WindowEnd = sessionEndedAt.Add(allocationWindowExtension);

    public bool CoversInstant(DateTimeOffset instant) => instant >= WindowStart && instant < WindowEnd;

    // Two allocations for the same number must never have overlapping windows in
    // ordinary operation (FR-006); shadow-mode windows are the documented exception (FR-049).
    public bool OverlapsWith(Allocation other) =>
        TrackingNumberId == other.TrackingNumberId
        && WindowStart < other.WindowEnd
        && other.WindowStart < WindowEnd;

    internal static Allocation Rehydrate(
        Guid id, Guid trackingNumberId, Guid sessionId, Guid? poolIdAtAllocation,
        DateTimeOffset windowStart, DateTimeOffset windowEnd, bool isShadow, DateTimeOffset createdAt) => new()
        {
            Id = id,
            TrackingNumberId = trackingNumberId,
            SessionId = sessionId,
            PoolIdAtAllocation = poolIdAtAllocation,
            WindowStart = windowStart,
            WindowEnd = windowEnd,
            IsShadow = isShadow,
            CreatedAt = createdAt,
        };
}
