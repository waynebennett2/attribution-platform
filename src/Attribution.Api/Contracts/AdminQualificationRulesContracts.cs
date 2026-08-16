using System.Text.Json.Serialization;

namespace Attribution.Api.Contracts;

public sealed class CreateQualificationRuleRequestDto
{
    [JsonPropertyName("scope_type")] public string ScopeType { get; set; } = string.Empty;
    [JsonPropertyName("scope_ref")] public string? ScopeRef { get; set; }
    [JsonPropertyName("conditions")] public QualificationConditionsDto Conditions { get; set; } = new();
    [JsonPropertyName("effective_start")] public DateTimeOffset EffectiveStart { get; set; }
}

public sealed class QualificationConditionsDto
{
    // "inbound" | "outbound" | null (unconstrained)
    [JsonPropertyName("direction")] public string? Direction { get; set; }
    [JsonPropertyName("answered_required")] public bool AnsweredRequired { get; set; }
    [JsonPropertyName("min_connected_duration_seconds")] public int? MinConnectedDurationSeconds { get; set; }
    [JsonPropertyName("time_of_day")] public TimeOfDayDto? TimeOfDay { get; set; }
}

// Local ("wall clock") times, e.g. "09:00", evaluated in the website's local timezone (FR-023).
public sealed class TimeOfDayDto
{
    [JsonPropertyName("start")] public string Start { get; set; } = string.Empty;
    [JsonPropertyName("end")] public string End { get; set; } = string.Empty;
}
