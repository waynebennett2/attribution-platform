using System.Text.Json.Serialization;

namespace Attribution.Api.Contracts;

// Shapes mirror contracts/dni-api.md exactly (snake_case on the wire).

public sealed class UtmDto
{
    [JsonPropertyName("source")] public string? Source { get; set; }
    [JsonPropertyName("medium")] public string? Medium { get; set; }
    [JsonPropertyName("campaign")] public string? Campaign { get; set; }
    [JsonPropertyName("term")] public string? Term { get; set; }
    [JsonPropertyName("content")] public string? Content { get; set; }
}

public sealed class AllocateRequestDto
{
    [JsonPropertyName("website_id")] public string WebsiteId { get; set; } = string.Empty;
    [JsonPropertyName("client_token")] public string ClientToken { get; set; } = string.Empty;
    [JsonPropertyName("consent_granted")] public bool ConsentGranted { get; set; }
    [JsonPropertyName("landing_page")] public string? LandingPage { get; set; }
    [JsonPropertyName("referrer")] public string? Referrer { get; set; }
    [JsonPropertyName("utm")] public UtmDto? Utm { get; set; }
    [JsonPropertyName("gclid")] public string? Gclid { get; set; }
    [JsonPropertyName("gbraid")] public string? Gbraid { get; set; }
    [JsonPropertyName("wbraid")] public string? Wbraid { get; set; }
    [JsonPropertyName("ga4_client_id")] public string? Ga4ClientId { get; set; }
}

public sealed class AllocateResponseDto
{
    [JsonPropertyName("session_id")] public string? SessionId { get; set; }
    [JsonPropertyName("number")] public string Number { get; set; } = string.Empty;
    [JsonPropertyName("expires_at")] public DateTimeOffset? ExpiresAt { get; set; }
    [JsonPropertyName("reason")] public string? Reason { get; set; }
}

public sealed class HeartbeatRequestDto
{
    [JsonPropertyName("session_id")] public string SessionId { get; set; } = string.Empty;
}

public sealed class HeartbeatResponseDto
{
    [JsonPropertyName("still_valid")] public bool StillValid { get; set; }
    [JsonPropertyName("number")] public string? Number { get; set; }
}

public sealed class ShadowObserveRequestDto
{
    [JsonPropertyName("website_id")] public string WebsiteId { get; set; } = string.Empty;
    [JsonPropertyName("session_id")] public string? SessionId { get; set; }
    [JsonPropertyName("observed_number")] public string ObservedNumber { get; set; } = string.Empty;
    [JsonPropertyName("landing_page")] public string? LandingPage { get; set; }
    [JsonPropertyName("referrer")] public string? Referrer { get; set; }
    [JsonPropertyName("utm")] public UtmDto? Utm { get; set; }
    [JsonPropertyName("gclid")] public string? Gclid { get; set; }
    [JsonPropertyName("gbraid")] public string? Gbraid { get; set; }
    [JsonPropertyName("wbraid")] public string? Wbraid { get; set; }
    [JsonPropertyName("ga4_client_id")] public string? Ga4ClientId { get; set; }
}

public sealed class ConsentRequestDto
{
    [JsonPropertyName("session_id")] public string? SessionId { get; set; }
    [JsonPropertyName("client_token")] public string ClientToken { get; set; } = string.Empty;
    [JsonPropertyName("website_id")] public string WebsiteId { get; set; } = string.Empty;
    [JsonPropertyName("consent")] public string Consent { get; set; } = string.Empty; // "granted" | "withdrawn"
    [JsonPropertyName("arrival_details")] public AllocateRequestDto? ArrivalDetails { get; set; }
}
