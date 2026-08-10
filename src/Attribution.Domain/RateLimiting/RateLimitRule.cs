namespace Attribution.Domain.RateLimiting;

// FR-037: a per-origin or per-client threshold over a fixed window.
public sealed record RateLimitRule(int MaxRequests, TimeSpan Window);
