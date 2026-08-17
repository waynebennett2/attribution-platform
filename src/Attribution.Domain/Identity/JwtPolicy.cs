namespace Attribution.Domain.Identity;

// FR-046, SC-016: the platform-issued token's lifetime and refresh policy. Kept as pure,
// dependency-free logic in Domain; actual token issuance/signing (Infrastructure.Identity)
// and validation middleware (Api.Middleware) both defer to these values so the 5-minute
// bound is defined in exactly one place.
public static class JwtPolicy
{
    public static readonly TimeSpan TokenLifetime = TimeSpan.FromMinutes(5);

    // FR-046: how long a refresh token remains usable since its last exchange. A sliding
    // window — each successful refresh rotates the token and resets this timer — so a user
    // active through the day never has to re-enter their password and TOTP code, while an
    // idle or deactivated account's ability to refresh lapses within this window regardless.
    public static readonly TimeSpan RefreshTokenLifetime = TimeSpan.FromHours(12);

    public static bool IsExpired(DateTimeOffset issuedAt, DateTimeOffset now) =>
        now >= issuedAt + TokenLifetime;

    // The client should proactively refresh before actual expiry so a request never lands
    // on an already-expired token.
    public static bool ShouldRefresh(DateTimeOffset issuedAt, DateTimeOffset now, TimeSpan refreshMargin) =>
        now >= issuedAt + TokenLifetime - refreshMargin;
}
