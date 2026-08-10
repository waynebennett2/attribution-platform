namespace Attribution.Domain.RateLimiting;

// FR-037: rate limiting for the visitor-facing allocation/heartbeat endpoints, which
// cannot be authenticated because they can't hold a secret. Pure decision logic — the
// counter itself (in-process or shared) is an Infrastructure concern, see research.md §11.
public static class RateLimitPolicy
{
    public static readonly RateLimitRule DefaultPerOrigin = new(600, TimeSpan.FromMinutes(1));
    public static readonly RateLimitRule DefaultPerClient = new(10, TimeSpan.FromMinutes(1));

    public static bool IsAllowed(int requestsAlreadyInWindow, RateLimitRule rule) =>
        requestsAlreadyInWindow < rule.MaxRequests;

    // Fixed-window bucketing: returns the start of the window a given instant falls into,
    // so two instants within the same window always resolve to the same counter key.
    public static DateTimeOffset WindowStart(DateTimeOffset instant, TimeSpan window)
    {
        var ticks = instant.UtcTicks - (instant.UtcTicks % window.Ticks);
        return new DateTimeOffset(ticks, TimeSpan.Zero);
    }
}
