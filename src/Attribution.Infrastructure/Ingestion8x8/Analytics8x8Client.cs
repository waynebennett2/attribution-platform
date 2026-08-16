using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Attribution.Domain.Calls;
using Microsoft.Extensions.Options;

namespace Attribution.Infrastructure.Ingestion8x8;

// research.md §8: authenticates against Analytics for 8x8 Work (OAuth2 client-credentials,
// 8x8's own credential model) and polls Call Detail Records and Call Legs.
//
// NOTE: the endpoint paths and response shapes below are a best-effort placeholder — this
// repository has no fixture or contract for 8x8's actual API, so BaseUrl/TokenUrl and the
// DTOs' field names must be verified (and adjusted, without needing to touch
// IngestionService/IngestionWorker, since both depend only on IAnalytics8x8Client) against
// the deploying account's real Analytics for 8x8 Work API reference before go-live.
public sealed class Analytics8x8Client : IAnalytics8x8Client
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly HttpClient _httpClient;
    private readonly Analytics8x8ClientOptions _options;

    public Analytics8x8Client(HttpClient httpClient, IOptions<Analytics8x8ClientOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
    }

    public async Task<Analytics8x8Page> PollAsync(string? checkpointPosition, CancellationToken cancellationToken)
    {
        await AuthenticateAsync(cancellationToken);

        var query = checkpointPosition is null ? string.Empty : $"?since={Uri.EscapeDataString(checkpointPosition)}";
        var calls = await GetCallsAsync(query, cancellationToken);
        var legs = await GetCallLegsAsync(query, cancellationToken);

        // Advances by the latest call start time seen — a poll returning nothing leaves
        // the checkpoint exactly where it was (FR-016: a restart neither skips nor
        // reprocesses records).
        var nextPosition = calls.Count == 0
            ? checkpointPosition
            : calls.Max(c => c.StartedAt).ToString("O");

        return new Analytics8x8Page(calls, legs, nextPosition);
    }

    public async Task<Analytics8x8Page> PollRangeAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        await AuthenticateAsync(cancellationToken);

        var query = $"?from={Uri.EscapeDataString(from.ToString("O"))}&to={Uri.EscapeDataString(to.ToString("O"))}";
        var calls = await GetCallsAsync(query, cancellationToken);
        var legs = await GetCallLegsAsync(query, cancellationToken);

        // FR-042: a backfill never advances the live checkpoint — callers (BackfillService)
        // rely on this being null to know not to.
        return new Analytics8x8Page(calls, legs, NextCheckpointPosition: null);
    }

    private async Task AuthenticateAsync(CancellationToken cancellationToken)
    {
        if (_httpClient.DefaultRequestHeaders.Authorization is not null)
        {
            return; // cached for this HttpClient instance's lifetime (one poll cycle)
        }

        var tokenResponse = await _httpClient.PostAsync(
            _options.TokenUrl,
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = _options.ClientId,
                ["client_secret"] = _options.ClientSecret,
            }),
            cancellationToken);
        tokenResponse.EnsureSuccessStatusCode();

        var token = await tokenResponse.Content.ReadFromJsonAsync<TokenResponse>(JsonOptions, cancellationToken);
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", token?.AccessToken ?? throw new InvalidOperationException("Analytics for 8x8 Work returned no access token."));
    }

    private async Task<IReadOnlyList<Analytics8x8CallRecord>> GetCallsAsync(string query, CancellationToken cancellationToken)
    {
        var dtos = await _httpClient.GetFromJsonAsync<List<CdrDto>>($"{_options.BaseUrl}/cdrs{query}", JsonOptions, cancellationToken)
            ?? new List<CdrDto>();
        return dtos.Select(d => d.ToRecord()).ToList();
    }

    private async Task<IReadOnlyList<Analytics8x8CallLegRecord>> GetCallLegsAsync(string query, CancellationToken cancellationToken)
    {
        var dtos = await _httpClient.GetFromJsonAsync<List<CallLegDto>>($"{_options.BaseUrl}/call-legs{query}", JsonOptions, cancellationToken)
            ?? new List<CallLegDto>();
        return dtos.Select(d => d.ToRecord()).ToList();
    }

    private sealed record TokenResponse([property: JsonPropertyName("access_token")] string AccessToken);

    private sealed record CdrDto(
        string Id, string Direction, string DialledNumber, string? CallerId, DateTimeOffset StartedAt,
        DateTimeOffset? AnsweredAt, DateTimeOffset? EndedAt, int? ConnectedDurationSeconds, string? Disposition, bool IsFinal)
    {
        public Analytics8x8CallRecord ToRecord() => new(
            Id, Enum.Parse<CallDirection>(Direction, ignoreCase: true), DialledNumber, CallerId, StartedAt,
            AnsweredAt, EndedAt, ConnectedDurationSeconds, Disposition, IsFinal);
    }

    private sealed record CallLegDto(
        string CallRecordId, string LegId, string? SequenceOrRole, DateTimeOffset? StartedAt, DateTimeOffset? EndedAt)
    {
        public Analytics8x8CallLegRecord ToRecord() => new(CallRecordId, LegId, SequenceOrRole, StartedAt, EndedAt);
    }
}
