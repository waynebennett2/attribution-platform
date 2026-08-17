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

    // FR-050, multi-pool websites only — always ignored for multi_pool_enabled = false.
    [JsonPropertyName("matched_pool_ids")] public List<string>? MatchedPoolIds { get; set; }
    [JsonPropertyName("session_id")] public string? SessionId { get; set; }
}

public sealed class PoolNumberDto
{
    [JsonPropertyName("pool_id")] public string PoolId { get; set; } = string.Empty;
    [JsonPropertyName("default_number")] public string DefaultNumber { get; set; } = string.Empty;
}

public sealed class PoolAllocationDto
{
    [JsonPropertyName("pool_id")] public string PoolId { get; set; } = string.Empty;
    [JsonPropertyName("number")] public string Number { get; set; } = string.Empty;
    [JsonPropertyName("expires_at")] public DateTimeOffset ExpiresAt { get; set; }
}

public sealed class AllocateResponseDto
{
    [JsonPropertyName("session_id")] public string? SessionId { get; set; }
    [JsonPropertyName("number")] [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Number { get; set; }
    [JsonPropertyName("expires_at")] public DateTimeOffset? ExpiresAt { get; set; }
    [JsonPropertyName("reason")] public string? Reason { get; set; }

    // FR-050: present only for a multi-pool-enabled website's response — omitted entirely
    // (not even as null) for a single-pool website, so its shape never changes.
    [JsonPropertyName("pools")] [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<PoolNumberDto>? Pools { get; set; }
    [JsonPropertyName("allocations")] [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<PoolAllocationDto>? Allocations { get; set; }
}

public sealed class HeartbeatRequestDto
{
    [JsonPropertyName("session_id")] public string SessionId { get; set; } = string.Empty;
}

public sealed class PoolHeartbeatDto
{
    [JsonPropertyName("pool_id")] public string PoolId { get; set; } = string.Empty;
    [JsonPropertyName("still_valid")] public bool StillValid { get; set; }
    [JsonPropertyName("number")] public string? Number { get; set; }
}

public sealed class HeartbeatResponseDto
{
    [JsonPropertyName("still_valid")] public bool StillValid { get; set; }
    [JsonPropertyName("number")] [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Number { get; set; }

    // FR-050: present only for a multi-pool-enabled website's session.
    [JsonPropertyName("allocations")] [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<PoolHeartbeatDto>? Allocations { get; set; }
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
