using System.Text.Json.Serialization;

namespace Attribution.Infrastructure.Alerting;

// Wire shape fixed by contracts/alert-webhook.md — property names are the contract's exact
// JSON keys, not just a naming-policy convention, so they're spelled out explicitly rather
// than relying on a serializer-wide snake_case policy that could silently drift if the
// policy ever changes.
public sealed record AlertWebhookPayload(
    [property: JsonPropertyName("alert_id")] string AlertId,
    [property: JsonPropertyName("condition_type")] string ConditionType,
    [property: JsonPropertyName("scope")] IReadOnlyDictionary<string, string?> Scope,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("threshold")] string Threshold,
    [property: JsonPropertyName("current_value")] string CurrentValue,
    [property: JsonPropertyName("raised_at")] DateTimeOffset RaisedAt,
    [property: JsonPropertyName("occurred_at")] DateTimeOffset OccurredAt);
