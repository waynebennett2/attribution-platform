using System;
using Attribution.Domain.Calls;
using Attribution.Domain.Qualification;
using Xunit;

namespace Attribution.UnitTests.Qualification;

// FR-022: the default rule — inbound, answered, connected 60 seconds or longer — tested
// at its boundary. 60 seconds exactly qualifies ("60 seconds or longer"); anything less
// does not.
public class QualificationServiceTests
{
    private static Call InboundAnsweredCall(int connectedDurationSeconds) => Call.Create(
        sourceRecordId: Guid.NewGuid().ToString(), CallDirection.Inbound, dialledNumber: "+441632960001",
        callerId: "+441632960999", startedAt: DateTimeOffset.UtcNow, answeredAt: DateTimeOffset.UtcNow,
        endedAt: DateTimeOffset.UtcNow.AddSeconds(connectedDurationSeconds), connectedDurationSeconds,
        disposition: "answered", isFinal: true, ingestedAt: DateTimeOffset.UtcNow);

    [Fact]
    public void CallConnectedFor45Seconds_DoesNotQualify_UnderTheDefaultRule()
    {
        var call = InboundAnsweredCall(45);

        Assert.False(RuleEvaluator.Evaluate(QualificationConditions.Default, call, TimeZoneInfo.Utc));
    }

    [Fact]
    public void CallConnectedFor59Seconds_DoesNotQualify_UnderTheDefaultRule()
    {
        var call = InboundAnsweredCall(59);

        Assert.False(RuleEvaluator.Evaluate(QualificationConditions.Default, call, TimeZoneInfo.Utc));
    }

    [Fact]
    public void CallConnectedForExactly60Seconds_Qualifies_UnderTheDefaultRule()
    {
        var call = InboundAnsweredCall(60);

        Assert.True(RuleEvaluator.Evaluate(QualificationConditions.Default, call, TimeZoneInfo.Utc));
    }

    [Fact]
    public void CallConnectedFor75Seconds_Qualifies_UnderTheDefaultRule()
    {
        var call = InboundAnsweredCall(75);

        Assert.True(RuleEvaluator.Evaluate(QualificationConditions.Default, call, TimeZoneInfo.Utc));
    }

    [Fact]
    public void UnansweredCall_NeverQualifies_UnderTheDefaultRule_RegardlessOfDuration()
    {
        var call = Call.Create(
            Guid.NewGuid().ToString(), CallDirection.Inbound, "+441632960001", "+441632960999",
            DateTimeOffset.UtcNow, answeredAt: null, endedAt: null, connectedDurationSeconds: null,
            disposition: null, isFinal: true, DateTimeOffset.UtcNow);

        Assert.False(RuleEvaluator.Evaluate(QualificationConditions.Default, call, TimeZoneInfo.Utc));
    }

    [Fact]
    public void OutboundCall_NeverQualifies_UnderTheDefaultRule_EvenIfAnsweredAndLongEnough()
    {
        var call = Call.Create(
            Guid.NewGuid().ToString(), CallDirection.Outbound, "+441632960001", "+441632960999",
            DateTimeOffset.UtcNow, answeredAt: DateTimeOffset.UtcNow, endedAt: DateTimeOffset.UtcNow.AddSeconds(90),
            connectedDurationSeconds: 90, disposition: "answered", isFinal: true, DateTimeOffset.UtcNow);

        Assert.False(RuleEvaluator.Evaluate(QualificationConditions.Default, call, TimeZoneInfo.Utc));
    }
}
