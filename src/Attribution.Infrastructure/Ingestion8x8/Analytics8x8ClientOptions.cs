namespace Attribution.Infrastructure.Ingestion8x8;

// Bound from the "Analytics8x8" configuration section. BaseUrl/TokenUrl are configuration
// rather than hard-coded specifically so an endpoint change never needs a code change
// (research.md §8) — real values (including ClientSecret) belong in
// appsettings.{Environment}.local.json, never committed.
public sealed class Analytics8x8ClientOptions
{
    public string BaseUrl { get; set; } = string.Empty;
    public string TokenUrl { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
}
