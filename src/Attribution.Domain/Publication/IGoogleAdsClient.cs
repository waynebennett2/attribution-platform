namespace Attribution.Domain.Publication;

// research.md §6: Google Ads offline-conversion upload, keyed on whichever click
// identifier the originating session captured, plus the retraction/adjustment FR-044 needs.
public interface IGoogleAdsClient
{
    // Returns the destination's own conversion identifier (ConversionPublication.ExternalId).
    Task<string> UploadConversionAsync(GoogleAdsConversion conversion, CancellationToken cancellationToken);

    Task RetractAsync(string externalId, CancellationToken cancellationToken);

    Task AdjustAsync(string externalId, GoogleAdsConversion conversion, CancellationToken cancellationToken);
}

public sealed record GoogleAdsConversion(
    string? Gclid, string? Gbraid, string? Wbraid, DateTimeOffset ConversionTime, string ConversionActionId);
