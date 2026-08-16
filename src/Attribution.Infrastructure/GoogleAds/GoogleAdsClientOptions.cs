namespace Attribution.Infrastructure.GoogleAds;

// Bound from the "GoogleAds" configuration section; real values belong in
// appsettings.{Environment}.local.json, never committed. Google Ads API auth is OAuth2
// refresh-token (not client-credentials): a one-time refresh token, obtained via Google's
// OAuth consent flow outside this application, is exchanged here for short-lived access
// tokens.
public sealed class GoogleAdsClientOptions
{
    public string ApiBaseUrl { get; set; } = "https://googleads.googleapis.com/v17";
    public string TokenUrl { get; set; } = "https://oauth2.googleapis.com/token";
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string RefreshToken { get; set; } = string.Empty;
    public string DeveloperToken { get; set; } = string.Empty;
    public string CustomerId { get; set; } = string.Empty;
    public string? LoginCustomerId { get; set; }
}
