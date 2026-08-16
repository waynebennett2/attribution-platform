using Attribution.Domain.Publication;

namespace Attribution.IntegrationTests.TestSupport;

// A stand-in for tests that need to construct CorrectionService but never actually expect
// a correction to reach Google Ads (e.g. idempotent-reingestion tests where nothing about
// the qualification decision changes) — throws if it's ever actually called, so a test
// relying on this incorrectly exercising a real correction fails loudly rather than
// silently succeeding against a fake conversion.
public sealed class NoOpGoogleAdsClient : IGoogleAdsClient
{
    public Task<string> UploadConversionAsync(GoogleAdsConversion conversion, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("NoOpGoogleAdsClient.UploadConversionAsync should never be called by this test.");

    public Task RetractAsync(string externalId, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("NoOpGoogleAdsClient.RetractAsync should never be called by this test.");

    public Task AdjustAsync(string externalId, GoogleAdsConversion conversion, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("NoOpGoogleAdsClient.AdjustAsync should never be called by this test.");
}
