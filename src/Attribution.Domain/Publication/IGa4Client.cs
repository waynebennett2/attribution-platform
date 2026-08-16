namespace Attribution.Domain.Publication;

// research.md §7: server-side GA4 Measurement Protocol event, keyed on the GA4 client
// identifier captured on the originating session (FR-015, FR-026). No retraction endpoint
// exists — see FR-044 / ConversionPublication.MarkUnpropagatable.
public interface IGa4Client
{
    Task SendEventAsync(Ga4Event conversionEvent, CancellationToken cancellationToken);
}

public sealed record Ga4Event(string ClientId, string EventName, DateTimeOffset EventTime);
