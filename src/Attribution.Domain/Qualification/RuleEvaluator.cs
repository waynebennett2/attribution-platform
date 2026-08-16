using Attribution.Domain.Calls;

namespace Attribution.Domain.Qualification;

// FR-022, FR-023: the qualification decision, isolated as pure logic so it's directly
// unit-testable against hand-built calls and conditions — no database. The caller
// resolves the website's TimeZoneInfo once (research.md's "one internal rule-evaluator
// function shared by every rule") rather than this function doing a timezone-database
// lookup itself.
public static class RuleEvaluator
{
    public static bool Evaluate(QualificationConditions conditions, Call call, TimeZoneInfo websiteTimeZone)
    {
        if (conditions.RequiredDirection is { } direction && call.Direction != direction)
        {
            return false;
        }

        if (conditions.AnsweredRequired && call.AnsweredAt is null)
        {
            return false;
        }

        if (conditions.MinConnectedDurationSeconds is { } minSeconds && (call.ConnectedDurationSeconds ?? 0) < minSeconds)
        {
            return false;
        }

        if (conditions.TimeOfDay is { } window)
        {
            // FR-023: evaluated in the website's local timezone, never the platform's
            // canonical UTC storage timezone — "business hours" is a local-clock concept.
            var local = TimeZoneInfo.ConvertTime(call.StartedAt, websiteTimeZone);
            if (!window.Contains(TimeOnly.FromDateTime(local.DateTime)))
            {
                return false;
            }
        }

        return true;
    }
}
