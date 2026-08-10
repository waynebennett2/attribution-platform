using System;
using Attribution.Domain.RateLimiting;
using Xunit;

namespace Attribution.UnitTests.RateLimiting;

// FR-037: 600 req/min per origin, 10 req/min per client, both configurable per website.
public class RateLimitPolicyTests
{
    [Fact]
    public void DefaultPerOriginRule_Is600PerMinute()
    {
        Assert.Equal(600, RateLimitPolicy.DefaultPerOrigin.MaxRequests);
        Assert.Equal(TimeSpan.FromMinutes(1), RateLimitPolicy.DefaultPerOrigin.Window);
    }

    [Fact]
    public void DefaultPerClientRule_Is10PerMinute()
    {
        Assert.Equal(10, RateLimitPolicy.DefaultPerClient.MaxRequests);
        Assert.Equal(TimeSpan.FromMinutes(1), RateLimitPolicy.DefaultPerClient.Window);
    }

    [Theory]
    [InlineData(0, 10, true)]
    [InlineData(9, 10, true)]
    [InlineData(10, 10, false)]
    [InlineData(11, 10, false)]
    public void IsAllowed_ComparesCountAgainstThreshold(int alreadyInWindow, int max, bool expectedAllowed)
    {
        var rule = new RateLimitRule(max, TimeSpan.FromMinutes(1));

        Assert.Equal(expectedAllowed, RateLimitPolicy.IsAllowed(alreadyInWindow, rule));
    }

    [Fact]
    public void WindowStart_BucketsInstantsWithinSameWindowIdentically()
    {
        var window = TimeSpan.FromMinutes(1);
        var first = new DateTimeOffset(2026, 8, 10, 12, 30, 5, TimeSpan.Zero);
        var second = new DateTimeOffset(2026, 8, 10, 12, 30, 55, TimeSpan.Zero);

        Assert.Equal(RateLimitPolicy.WindowStart(first, window), RateLimitPolicy.WindowStart(second, window));
    }

    [Fact]
    public void WindowStart_BucketsInstantsAcrossWindowBoundaryDifferently()
    {
        var window = TimeSpan.FromMinutes(1);
        var beforeBoundary = new DateTimeOffset(2026, 8, 10, 12, 30, 59, TimeSpan.Zero);
        var afterBoundary = new DateTimeOffset(2026, 8, 10, 12, 31, 0, TimeSpan.Zero);

        Assert.NotEqual(RateLimitPolicy.WindowStart(beforeBoundary, window), RateLimitPolicy.WindowStart(afterBoundary, window));
    }
}
