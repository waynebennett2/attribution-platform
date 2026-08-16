namespace Attribution.Infrastructure.GA4;

// Bound from the "Ga4" configuration section; real values (especially ApiSecret) belong
// in appsettings.{Environment}.local.json, never committed.
public sealed class Ga4ClientOptions
{
    public string Endpoint { get; set; } = "https://www.google-analytics.com/mp/collect";
    public string MeasurementId { get; set; } = string.Empty;
    public string ApiSecret { get; set; } = string.Empty;
}
