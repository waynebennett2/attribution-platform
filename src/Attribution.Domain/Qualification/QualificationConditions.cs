using Attribution.Domain.Calls;

namespace Attribution.Domain.Qualification;

// FR-022, FR-023: the condition set one qualification rule version evaluates a call
// against. Every field is optional except AnsweredRequired — an absent RequiredDirection,
// MinConnectedDurationSeconds or TimeOfDay imposes no constraint on that dimension.
public sealed record QualificationConditions(
    CallDirection? RequiredDirection,
    bool AnsweredRequired,
    int? MinConnectedDurationSeconds,
    TimeOfDayWindow? TimeOfDay)
{
    // FR-022: the platform default — inbound, answered, connected 60 seconds or longer.
    public static QualificationConditions Default { get; } = new(CallDirection.Inbound, true, 60, null);
}
