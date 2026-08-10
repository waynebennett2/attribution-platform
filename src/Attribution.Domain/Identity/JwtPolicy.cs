namespace Attribution.Domain.Identity;

// FR-046, SC-016: the platform-issued token's lifetime and refresh policy. Kept as pure,
// dependency-free logic in Domain; actual token issuance/signing (Infrastructure.Identity)
// and validation middleware (Api.Middleware) both defer to these values so the 5-minute
// bound is defined in exactly one place.
public static class JwtPolicy
{
    public static readonly TimeSpan TokenLifetime = TimeSpan.FromMinutes(5);

    public static bool IsExpired(DateTimeOffset issuedAt, DateTimeOffset now) =>
        now >= issuedAt + TokenLifetime;

    // The client should proactively refresh before actual expiry so a request never lands
    // on an already-expired token (silent refresh against the still-active browser session).
    public static bool ShouldRefresh(DateTimeOffset issuedAt, DateTimeOffset now, TimeSpan refreshMargin) =>
        now >= issuedAt + TokenLifetime - refreshMargin;
}
