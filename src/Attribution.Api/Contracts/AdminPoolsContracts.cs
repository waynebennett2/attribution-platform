using System.Text.Json.Serialization;

namespace Attribution.Api.Contracts;

public sealed class CreatePoolRequestDto
{
    [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
    [JsonPropertyName("scope_type")] public string ScopeType { get; set; } = string.Empty;
    [JsonPropertyName("scope_ref")] public string ScopeRef { get; set; } = string.Empty;
    [JsonPropertyName("default_number")] public string? DefaultNumber { get; set; }
}

public sealed class ImportRowResultDto
{
    [JsonPropertyName("row")] public int Row { get; set; }
    [JsonPropertyName("did")] public string Did { get; set; } = string.Empty;
    [JsonPropertyName("accepted")] public bool Accepted { get; set; }
    [JsonPropertyName("reason")] public string? Reason { get; set; }
}

public sealed class MoveNumberRequestDto
{
    [JsonPropertyName("target_pool_id")] public string TargetPoolId { get; set; } = string.Empty;
}
