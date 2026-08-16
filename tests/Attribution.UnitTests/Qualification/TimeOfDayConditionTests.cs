using System;
using Attribution.Domain.Calls;
using Attribution.Domain.Qualification;
using Xunit;

namespace Attribution.UnitTests.Qualification;

// FR-023: a time-of-day condition MUST be evaluated in the website's local timezone, not
// the platform's canonical UTC storage timezone — "business hours" is a local-clock
// concept, and evaluating it against an unrelated storage timezone would silently shift
// the hours actually enforced for any website outside that timezone.
public class TimeOfDayConditionTests
{
    private static Call AnsweredCallAt(DateTimeOffset startedAt) => Call.Create(
        Guid.NewGuid().ToString(), CallDirection.Inbound, "+441632960001", "+441632960999",
        startedAt, answeredAt: startedAt, endedAt: startedAt.AddSeconds(90), connectedDurationSeconds: 90,
        disposition: "answered", isFinal: true, DateTimeOffset.UtcNow);

    [Fact]
    public void CallFallingInsideTheWindow_InTheWebsitesLocalTime_Qualifies_EvenThoughOutsideItInUtc()
    {
        // 2024-07-15T02:00:00Z is 2024-07-14T19:00 in America/Los_Angeles (PDT, UTC-7) —
        // inside a 17:00-21:00 local evening window, but well outside it if the same
        // 17:00-21:00 bounds were (incorrectly) applied directly to the UTC instant.
        var call = AnsweredCallAt(new DateTimeOffset(2024, 7, 15, 2, 0, 0, TimeSpan.Zero));
        var conditions = new QualificationConditions(
            RequiredDirection: null, AnsweredRequired: true, MinConnectedDurationSeconds: null,
            TimeOfDay: new TimeOfDayWindow(new TimeOnly(17, 0), new TimeOnly(21, 0)));
        var losAngeles = TimeZoneInfo.FindSystemTimeZoneById("America/Los_Angeles");

        Assert.True(RuleEvaluator.Evaluate(conditions, call, losAngeles));
        // Evaluating the identical window against the canonical storage timezone (UTC) —
        // what FR-023 explicitly forbids — gives the wrong answer, proving the local
        // conversion above is actually doing something.
        Assert.False(RuleEvaluator.Evaluate(conditions, call, TimeZoneInfo.Utc));
    }

    [Fact]
    public void CallFallingOutsideTheWindow_InTheWebsitesLocalTime_DoesNotQualify()
    {
        // Same UTC instant, but a window that no longer covers 19:00 PDT.
        var call = AnsweredCallAt(new DateTimeOffset(2024, 7, 15, 2, 0, 0, TimeSpan.Zero));
        var conditions = new QualificationConditions(
            null, true, null, new TimeOfDayWindow(new TimeOnly(9, 0), new TimeOnly(17, 0)));
        var losAngeles = TimeZoneInfo.FindSystemTimeZoneById("America/Los_Angeles");

        Assert.False(RuleEvaluator.Evaluate(conditions, call, losAngeles));
    }

    [Fact]
    public void WindowCrossingMidnight_ContainsInstantsOnEitherSideOfMidnight()
    {
        // 22:00-06:00 local — an overnight window.
        var window = new TimeOfDayWindow(new TimeOnly(22, 0), new TimeOnly(6, 0));

        Assert.True(window.Contains(new TimeOnly(23, 30)));
        Assert.True(window.Contains(new TimeOnly(2, 0)));
        Assert.False(window.Contains(new TimeOnly(12, 0)));
        Assert.False(window.Contains(new TimeOnly(6, 0))); // end is exclusive
        Assert.True(window.Contains(new TimeOnly(22, 0))); // start is inclusive
    }

    [Fact]
    public void NoTimeOfDayCondition_NeverRestrictsQualification()
    {
        var call = AnsweredCallAt(new DateTimeOffset(2024, 1, 1, 3, 0, 0, TimeSpan.Zero)); // 3am UTC, any timezone
        var conditions = new QualificationConditions(null, true, null, TimeOfDay: null);

        Assert.True(RuleEvaluator.Evaluate(conditions, call, TimeZoneInfo.FindSystemTimeZoneById("America/Los_Angeles")));
    }
}
