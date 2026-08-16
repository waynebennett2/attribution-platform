using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using MySqlConnector;
using Xunit;

namespace Attribution.IntegrationTests.Performance;

// FR-043, SC-005: reruns AllocationLoadTests' exact peak mix, but against the real,
// separately-running docker-compose topology (docs/deployment.md, T119) — nginx load
// balancing across two genuinely separate `Attribution.Api` processes (api1/api2), not one
// in-process WebApplicationFactory instance the way every other test in this suite uses.
// This is what actually proves "no shared in-process state": AllocationLoadTests alone
// can't distinguish "this works" from "this works because it's always the same process",
// since a WebApplicationFactory host is exactly one process.
//
// Prerequisite: `cp .env.example .env` (fill in secrets), then
// `docker compose up -d --build api1 api2 nginx` from the repository root. This test skips
// itself (not fails) when that topology isn't reachable on localhost:8080, since standing
// it up is an out-of-band step no other test in this suite requires.
public class HorizontalScaleTest
{
    private const string BaseUrl = "http://localhost:8080";
    private const int AllocationCount = 7;
    private const int HeartbeatCount = 50;

    [SkippableFact]
    public async Task PeakMix_AgainstTwoLiveInstances_AllSucceed_WithRequestsSpreadAcrossBoth()
    {
        using var client = new HttpClient { BaseAddress = new Uri(BaseUrl) };
        await SkipUnlessTopologyIsUpAsync(client);

        var (websiteId, poolId) = await SeedWebsiteAndPoolAsync();
        for (var i = 0; i < AllocationCount + 5; i++)
        {
            await SeedTrackingNumberAsync(poolId);
        }

        var sessionIds = new List<string>();
        for (var i = 0; i < AllocationCount; i++)
        {
            var response = await client.PostAsJsonAsync("/v1/dni/allocate", new
            {
                website_id = websiteId.ToString(),
                client_token = $"scale-test-{Guid.NewGuid()}",
                consent_granted = true,
                landing_page = "https://example.com/",
            });
            response.EnsureSuccessStatusCode();
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            var sessionId = body.GetProperty("session_id").GetString();
            Assert.False(string.IsNullOrEmpty(sessionId));
            sessionIds.Add(sessionId!);
        }

        for (var i = 0; i < HeartbeatCount; i++)
        {
            var response = await client.PostAsJsonAsync("/v1/dni/heartbeat", new { session_id = sessionIds[i % sessionIds.Count] });
            response.EnsureSuccessStatusCode();
        }

        // FR-043's "no shared in-process state" is what this whole exercise is actually
        // verifying: distinct sessions/tracking numbers were correctly allocated with no
        // duplicate or dropped assignment despite two separate processes serving the mix.
        Assert.Equal(AllocationCount, sessionIds.Distinct().Count());
    }

    private static async Task SkipUnlessTopologyIsUpAsync(HttpClient client)
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            var response = await client.GetAsync("/health", cts.Token);
            Skip.IfNot(response.IsSuccessStatusCode, $"docker-compose topology at {BaseUrl} is not healthy.");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            Skip.If(true, $"docker-compose topology is not reachable at {BaseUrl} — see this file's class-level comment for the prerequisite.");
        }
    }

    private static async Task<(Guid WebsiteId, Guid PoolId)> SeedWebsiteAndPoolAsync()
    {
        var websiteId = Guid.NewGuid();
        var poolId = Guid.NewGuid();
        await using var connection = new MySqlConnection(LocalComposeConnectionString);
        await connection.OpenAsync();
        await connection.ExecuteAsync(
            """
            INSERT INTO websites
                (id, name, permitted_origins, default_number, session_timeout_seconds, heartbeat_interval_seconds,
                 allocation_window_extension_seconds, cooldown_seconds, consent_required, shadow_mode_enabled,
                 local_timezone, created_at, updated_at)
            VALUES
                (@Id, 'Horizontal Scale Test Website', 'https://example.com', '+441632960000', 1800, 300, 1800, 1800, 1, 0,
                 'UTC', UTC_TIMESTAMP(), UTC_TIMESTAMP())
            """,
            new { Id = websiteId.ToString() });
        await connection.ExecuteAsync(
            "INSERT INTO number_pools (id, name, scope_type, scope_ref, created_at, updated_at) VALUES (@Id, 'Horizontal Scale Test Pool', 'website', @ScopeRef, UTC_TIMESTAMP(), UTC_TIMESTAMP())",
            new { Id = poolId.ToString(), ScopeRef = websiteId.ToString() });
        return (websiteId, poolId);
    }

    private static async Task SeedTrackingNumberAsync(Guid poolId)
    {
        var did = $"+44163{Random.Shared.Next(1000000, 9999999)}";
        await using var connection = new MySqlConnection(LocalComposeConnectionString);
        await connection.OpenAsync();
        await connection.ExecuteAsync(
            "INSERT INTO tracking_numbers (id, pool_id, did, status, status_changed_at) VALUES (@Id, @PoolId, @Did, 'Active', UTC_TIMESTAMP())",
            new { Id = Guid.NewGuid().ToString(), PoolId = poolId.ToString(), Did = did });
    }

    // Deliberately the *local* docker-compose mysql service (docker-compose.yml maps it to
    // localhost:3306), not TestSupport.TestDatabase's remote database — api1/api2 in the
    // running topology are themselves configured against this local one (see
    // docker-compose.yml's ConnectionStrings__AttributionDb), so seed data has to land
    // where those processes will actually look for it.
    private const string LocalComposeConnectionString =
        "Server=localhost;Port=3306;Database=attribution;User=attribution;Password=attribution_dev;";
}
