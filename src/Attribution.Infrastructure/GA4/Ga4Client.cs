using System.Net.Http.Json;
using Attribution.Domain.Publication;
using Microsoft.Extensions.Options;

namespace Attribution.Infrastructure.GA4;

// research.md §7: server-side GA4 Measurement Protocol event — the one stable, documented
// piece of this integration (unlike Google Ads' auth flow, this endpoint and payload
// shape are Google's simplest, long-stable server-to-server surface).
public sealed class Ga4Client : IGa4Client
{
    private readonly HttpClient _httpClient;
    private readonly Ga4ClientOptions _options;

    public Ga4Client(HttpClient httpClient, IOptions<Ga4ClientOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
    }

    public async Task SendEventAsync(Ga4Event conversionEvent, CancellationToken cancellationToken)
    {
        var url = $"{_options.Endpoint}?measurement_id={Uri.EscapeDataString(_options.MeasurementId)}&api_secret={Uri.EscapeDataString(_options.ApiSecret)}";

        // Built as a dictionary rather than an anonymous type: the Measurement Protocol's
        // "params" field name is a reserved C# keyword, so an anonymous-type member can't
        // spell it directly.
        var body = new Dictionary<string, object?>
        {
            ["client_id"] = conversionEvent.ClientId,
            ["events"] = new[]
            {
                new Dictionary<string, object?>
                {
                    ["name"] = conversionEvent.EventName,
                    ["params"] = new Dictionary<string, object?> { ["event_time"] = conversionEvent.EventTime.ToUnixTimeMilliseconds() },
                },
            },
        };

        var response = await _httpClient.PostAsJsonAsync(url, body, cancellationToken);
        response.EnsureSuccessStatusCode();
    }
}
