using System;
using Attribution.Domain.Sessions;
using Xunit;

namespace Attribution.UnitTests.Sessions;

// FR-012: configurable session timeout (default 30 min) and heartbeat (default 5 min).
public class SessionServiceTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(30);

    private static Session NewSession(DateTimeOffset startedAt) => Session.Create(
        Guid.NewGuid(), Guid.NewGuid(), ArrivalDetails.Empty, SessionProvenance.Ordinary, startedAt, Timeout);

    [Fact]
    public void NewSession_IsNotExpired_ImmediatelyAfterCreation()
    {
        var now = DateTimeOffset.UtcNow;
        var session = NewSession(now);

        Assert.False(session.IsExpired(now));
    }

    [Fact]
    public void Session_IsNotExpired_JustBeforeTimeoutElapses()
    {
        var now = DateTimeOffset.UtcNow;
        var session = NewSession(now);

        Assert.False(session.IsExpired(now.Add(Timeout).AddSeconds(-1)));
    }

    [Fact]
    public void Session_IsExpired_OnceTimeoutElapses()
    {
        var now = DateTimeOffset.UtcNow;
        var session = NewSession(now);

        Assert.True(session.IsExpired(now.Add(Timeout)));
    }

    [Fact]
    public void RefreshActivity_ExtendsExpiryFromTheHeartbeatMoment()
    {
        var now = DateTimeOffset.UtcNow;
        var session = NewSession(now);

        var heartbeatAt = now.AddMinutes(5); // default heartbeat interval, well inside the 30-min timeout
        session.RefreshActivity(heartbeatAt, Timeout);

        Assert.False(session.IsExpired(heartbeatAt.AddMinutes(29)));
        Assert.True(session.IsExpired(heartbeatAt.AddMinutes(30)));
    }

    [Fact]
    public void RefreshActivity_Throws_OnAnAlreadyExpiredSession()
    {
        var now = DateTimeOffset.UtcNow;
        var session = NewSession(now);

        Assert.Throws<InvalidOperationException>(() => session.RefreshActivity(now.Add(Timeout), Timeout));
    }

    [Fact]
    public void EndByTimeout_MakesSessionExpired_RegardlessOfExpiresAt()
    {
        var now = DateTimeOffset.UtcNow;
        var session = NewSession(now);

        session.EndByTimeout(now.AddMinutes(1));

        Assert.True(session.IsExpired(now.AddMinutes(1)));
    }

    // FR-039: withdrawal ends the session immediately and marks consent withdrawn,
    // distinct from an ordinary timeout-based end.
    [Fact]
    public void EndByConsentWithdrawal_EndsSessionImmediately_AndRecordsWithdrawnState()
    {
        var now = DateTimeOffset.UtcNow;
        var session = NewSession(now);

        session.EndByConsentWithdrawal(now.AddSeconds(30));

        Assert.Equal(ConsentState.Withdrawn, session.ConsentState);
        Assert.True(session.IsExpired(now.AddSeconds(30)));
    }
}
