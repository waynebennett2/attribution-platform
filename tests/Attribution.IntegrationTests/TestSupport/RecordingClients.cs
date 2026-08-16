using Attribution.Domain.Publication;

namespace Attribution.IntegrationTests.TestSupport;

// Stand-ins for the real Google Ads / GA4 clients — these tests verify the outbox/
// correction pipeline's own behavior (idempotency, retry, correction propagation), not
// that a real Google Ads or GA4 endpoint was called correctly.
public sealed class RecordingGoogleAdsClient : IGoogleAdsClient
{
    public List<GoogleAdsConversion> Uploaded { get; } = new();
    public List<string> Retracted { get; } = new();
    public List<(string ExternalId, GoogleAdsConversion Conversion)> Adjusted { get; } = new();
    public string NextExternalId { get; set; } = $"gclid-{Guid.NewGuid():N}";

    public Task<string> UploadConversionAsync(GoogleAdsConversion conversion, CancellationToken cancellationToken)
    {
        Uploaded.Add(conversion);
        return Task.FromResult(NextExternalId);
    }

    public Task RetractAsync(string externalId, CancellationToken cancellationToken)
    {
        Retracted.Add(externalId);
        return Task.CompletedTask;
    }

    public Task AdjustAsync(string externalId, GoogleAdsConversion conversion, CancellationToken cancellationToken)
    {
        Adjusted.Add((externalId, conversion));
        return Task.CompletedTask;
    }
}

public sealed class RecordingGa4Client : IGa4Client
{
    public List<Ga4Event> SentEvents { get; } = new();

    public Task SendEventAsync(Ga4Event conversionEvent, CancellationToken cancellationToken)
    {
        SentEvents.Add(conversionEvent);
        return Task.CompletedTask;
    }
}
