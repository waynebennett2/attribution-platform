namespace Attribution.Infrastructure.Alerting;

// Bound from the "Smtp" configuration section; the outbound mail path FR-047's dependency
// section calls for. Real host/credentials belong in appsettings.{Environment}.local.json.
public sealed class SmtpOptions
{
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 25;
    public bool EnableSsl { get; set; } = true;
    public string? Username { get; set; }
    public string? Password { get; set; }
    public string FromAddress { get; set; } = "alerts@attribution.local";
}
