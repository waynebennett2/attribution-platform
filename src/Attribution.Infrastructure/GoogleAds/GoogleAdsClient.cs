using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Attribution.Domain.Publication;
using Microsoft.Extensions.Options;

namespace Attribution.Infrastructure.GoogleAds;

// research.md §6: Google Ads offline-conversion upload (ConversionUploadService) for
// FR-025, and the adjustment endpoint (retract/restate) FR-044 needs.
//
// NOTE: this follows Google Ads API v17's documented REST shape (OAuth2 refresh-token
// auth, developer-token header, uploadClickConversions /
// uploadConversionAdjustments) as a best-effort implementation — like
// Analytics8x8Client, this repository has no live sandbox to verify against, so the exact
// request/response field names must be confirmed against the deploying account's actual
// API version before go-live. ApiBaseUrl is configuration specifically so an API version
// bump never needs a code change.
public sealed class GoogleAdsClient : IGoogleAdsClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly HttpClient _httpClient;
    private readonly GoogleAdsClientOptions _options;

    public GoogleAdsClient(HttpClient httpClient, IOptions<GoogleAdsClientOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
    }

    public async Task<string> UploadConversionAsync(GoogleAdsConversion conversion, CancellationToken cancellationToken)
    {
        await AuthenticateAsync(cancellationToken);

        var request = new
        {
            conversions = new[]
            {
                new
                {
                    gclid = conversion.Gclid,
                    gbraid = conversion.Gbraid,
                    wbraid = conversion.Wbraid,
                    conversionAction = $"customers/{_options.CustomerId}/conversionActions/{conversion.ConversionActionId}",
                    conversionDateTime = FormatConversionDateTime(conversion.ConversionTime),
                },
            },
            partialFailure = true,
        };

        var response = await _httpClient.PostAsJsonAsync(
            $"{_options.ApiBaseUrl}/customers/{_options.CustomerId}:uploadClickConversions", request, JsonOptions, cancellationToken);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<UploadClickConversionsResponse>(JsonOptions, cancellationToken);
        var result = body?.Results?.FirstOrDefault()
            ?? throw new InvalidOperationException("Google Ads returned no conversion result.");
        return result.Gclid ?? conversion.Gclid ?? conversion.Gbraid ?? conversion.Wbraid ?? string.Empty;
    }

    public async Task RetractAsync(string externalId, CancellationToken cancellationToken) =>
        await UploadAdjustmentAsync(externalId, "RETRACT", null, cancellationToken);

    public async Task AdjustAsync(string externalId, GoogleAdsConversion conversion, CancellationToken cancellationToken) =>
        await UploadAdjustmentAsync(externalId, "RESTATEMENT", conversion, cancellationToken);

    private async Task UploadAdjustmentAsync(
        string externalId, string adjustmentType, GoogleAdsConversion? conversion, CancellationToken cancellationToken)
    {
        await AuthenticateAsync(cancellationToken);

        var request = new
        {
            conversionAdjustments = new[]
            {
                new
                {
                    gclid = externalId,
                    conversionAction = conversion is null
                        ? null
                        : $"customers/{_options.CustomerId}/conversionActions/{conversion.ConversionActionId}",
                    adjustmentType,
                    adjustmentDateTime = FormatConversionDateTime(DateTimeOffset.UtcNow),
                },
            },
            partialFailure = true,
        };

        var response = await _httpClient.PostAsJsonAsync(
            $"{_options.ApiBaseUrl}/customers/{_options.CustomerId}:uploadConversionAdjustments", request, JsonOptions, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    private async Task AuthenticateAsync(CancellationToken cancellationToken)
    {
        if (_httpClient.DefaultRequestHeaders.Authorization is not null)
        {
            return;
        }

        var tokenResponse = await _httpClient.PostAsync(
            _options.TokenUrl,
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["client_id"] = _options.ClientId,
                ["client_secret"] = _options.ClientSecret,
                ["refresh_token"] = _options.RefreshToken,
            }),
            cancellationToken);
        tokenResponse.EnsureSuccessStatusCode();

        var token = await tokenResponse.Content.ReadFromJsonAsync<TokenResponse>(JsonOptions, cancellationToken);
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", token?.AccessToken ?? throw new InvalidOperationException("Google returned no access token."));
        _httpClient.DefaultRequestHeaders.Remove("developer-token");
        _httpClient.DefaultRequestHeaders.Add("developer-token", _options.DeveloperToken);
        if (!string.IsNullOrWhiteSpace(_options.LoginCustomerId))
        {
            _httpClient.DefaultRequestHeaders.Remove("login-customer-id");
            _httpClient.DefaultRequestHeaders.Add("login-customer-id", _options.LoginCustomerId);
        }
    }

    // Google Ads expects "yyyy-MM-dd HH:mm:ss+HH:mm".
    private static string FormatConversionDateTime(DateTimeOffset value) => value.ToString("yyyy-MM-dd HH:mm:sszzz");

    private sealed record TokenResponse([property: JsonPropertyName("access_token")] string AccessToken);

    private sealed record UploadClickConversionsResponse(List<ConversionResult>? Results);

    private sealed record ConversionResult(string? Gclid);
}
