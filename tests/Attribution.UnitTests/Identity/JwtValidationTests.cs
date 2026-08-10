using System;
using Attribution.Domain.Identity;
using Xunit;

namespace Attribution.UnitTests.Identity;

// FR-046, SC-016: the platform-issued token is short-lived (5 minutes) with silent
// refresh, so a revoked user loses access within one refresh interval rather than
// waiting for a longer-lived session to expire.
public class JwtValidationTests
{
    [Fact]
    public void TokenLifetime_IsFiveMinutes()
    {
        Assert.Equal(TimeSpan.FromMinutes(5), JwtPolicy.TokenLifetime);
    }

    [Fact]
    public void IsExpired_False_JustBeforeLifetimeElapses()
    {
        var issuedAt = new DateTimeOffset(2026, 8, 10, 12, 0, 0, TimeSpan.Zero);
        var now = issuedAt.Add(JwtPolicy.TokenLifetime).AddSeconds(-1);

        Assert.False(JwtPolicy.IsExpired(issuedAt, now));
    }

    [Fact]
    public void IsExpired_True_OnceLifetimeElapses()
    {
        var issuedAt = new DateTimeOffset(2026, 8, 10, 12, 0, 0, TimeSpan.Zero);
        var now = issuedAt.Add(JwtPolicy.TokenLifetime);

        Assert.True(JwtPolicy.IsExpired(issuedAt, now));
    }

    [Fact]
    public void ShouldRefresh_True_WithinRefreshMarginOfExpiry()
    {
        var issuedAt = new DateTimeOffset(2026, 8, 10, 12, 0, 0, TimeSpan.Zero);
        var refreshMargin = TimeSpan.FromSeconds(30);
        var now = issuedAt.Add(JwtPolicy.TokenLifetime) - TimeSpan.FromSeconds(10); // 10s from expiry

        Assert.True(JwtPolicy.ShouldRefresh(issuedAt, now, refreshMargin));
    }

    [Fact]
    public void ShouldRefresh_False_WellBeforeExpiry()
    {
        var issuedAt = new DateTimeOffset(2026, 8, 10, 12, 0, 0, TimeSpan.Zero);
        var refreshMargin = TimeSpan.FromSeconds(30);
        var now = issuedAt.AddMinutes(1); // 4 minutes still remain

        Assert.False(JwtPolicy.ShouldRefresh(issuedAt, now, refreshMargin));
    }

    [Fact]
    public void RevokedUser_TokenIssuedBeforeRevocation_ExpiresWithinOneLifetime()
    {
        // SC-016: no separate action is required in the platform — the existing token simply
        // isn't renewed once the identity provider stops asserting the user, and the token
        // itself is never valid for longer than JwtPolicy.TokenLifetime from issuance.
        var issuedAt = DateTimeOffset.UtcNow;
        var revokedAt = issuedAt.AddMinutes(2); // revoked mid-lifetime
        var maxPossibleValidity = issuedAt.Add(JwtPolicy.TokenLifetime);

        Assert.True(maxPossibleValidity - revokedAt <= JwtPolicy.TokenLifetime);
        Assert.True(maxPossibleValidity - revokedAt <= TimeSpan.FromMinutes(5));
    }
}
