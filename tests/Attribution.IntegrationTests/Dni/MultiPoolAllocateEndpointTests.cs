using System.Net.Http.Json;
using System.Text.Json;
using Attribution.IntegrationTests.TestSupport;
using Microsoft.AspNetCore.Mvc.Testing;
using MySqlConnector;
using Xunit;

namespace Attribution.IntegrationTests.Dni;

// FR-050: POST /v1/dni/allocate's multi-pool response shapes — the pre-match pools map,
// matched_pool_ids -> allocations, per-pool exhaustion falling back to that pool's own
// default_number while other matched pools allocate normally, and cross-website pool-id
// scoping — against the project's shared MySQL database (TestSupport.TestDatabase).
public class MultiPoolAllocateEndpointTests : IAsyncLifetime
{
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;

    public Task InitializeAsync()
    {
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Jwt:SigningSecret", "integration-test-signing-secret-at-least-32-characters");
            builder.UseSetting("Jwt:Issuer", "attribution-platform-tests");
            builder.UseSetting("Jwt:Audience", "attribution-platform-tests");
        });
        _client = _factory.CreateClient();
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task Allocate_PreMatch_ReturnsThePoolsMap_AndNoSession()
    {
        var websiteId = await SeedWebsiteAsync(multiPoolEnabled: true);
        var poolAId = await SeedPoolAsync(websiteId, "01632 960101");
        var poolBId = await SeedPoolAsync(websiteId, "01632 960102");

        var response = await Allocate(websiteId, consentGranted: true, matchedPoolIds: null);

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(JsonValueKind.Null, body.GetProperty("session_id").ValueKind);
        Assert.Equal("pending_match", body.GetProperty("reason").GetString());
        var pools = body.GetProperty("pools").EnumerateArray().ToList();
        Assert.Equal(2, pools.Count);
        Assert.Contains(pools, p => p.GetProperty("pool_id").GetString() == poolAId.ToString() && p.GetProperty("default_number").GetString() == "01632 960101");
        Assert.Contains(pools, p => p.GetProperty("pool_id").GetString() == poolBId.ToString() && p.GetProperty("default_number").GetString() == "01632 960102");
    }

    [Fact]
    public async Task Allocate_WithMatchedPoolIds_ReturnsOneSessionId_AndOneAllocationPerPool()
    {
        var websiteId = await SeedWebsiteAsync(multiPoolEnabled: true);
        var poolAId = await SeedPoolAsync(websiteId, "01632 960111");
        var poolBId = await SeedPoolAsync(websiteId, "01632 960112");
        var poolCId = await SeedPoolAsync(websiteId, "01632 960113");
        var didA = await SeedTrackingNumberAsync(poolAId);
        var didB = await SeedTrackingNumberAsync(poolBId);
        var didC = await SeedTrackingNumberAsync(poolCId);

        var response = await Allocate(websiteId, consentGranted: true, matchedPoolIds: new[] { poolAId, poolBId, poolCId });

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.False(string.IsNullOrEmpty(body.GetProperty("session_id").GetString()));
        var allocations = body.GetProperty("allocations").EnumerateArray().ToList();
        Assert.Equal(3, allocations.Count);
        Assert.Contains(allocations, a => a.GetProperty("pool_id").GetString() == poolAId.ToString() && a.GetProperty("number").GetString() == didA);
        Assert.Contains(allocations, a => a.GetProperty("pool_id").GetString() == poolBId.ToString() && a.GetProperty("number").GetString() == didB);
        Assert.Contains(allocations, a => a.GetProperty("pool_id").GetString() == poolCId.ToString() && a.GetProperty("number").GetString() == didC);
    }

    [Fact]
    public async Task Allocate_OnePoolExhausted_OmitsItFromAllocations_WhileTheOthersStillAllocate()
    {
        var websiteId = await SeedWebsiteAsync(multiPoolEnabled: true);
        var exhaustedPoolId = await SeedPoolAsync(websiteId, "01632 960121"); // no tracking number seeded
        var healthyPoolId = await SeedPoolAsync(websiteId, "01632 960122");
        var did = await SeedTrackingNumberAsync(healthyPoolId);

        var response = await Allocate(websiteId, consentGranted: true, matchedPoolIds: new[] { exhaustedPoolId, healthyPoolId });

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        var allocations = body.GetProperty("allocations").EnumerateArray().ToList();
        var allocation = Assert.Single(allocations);
        Assert.Equal(healthyPoolId.ToString(), allocation.GetProperty("pool_id").GetString());
        Assert.Equal(did, allocation.GetProperty("number").GetString());
    }

    [Fact]
    public async Task Allocate_ASecondPageView_WithTheExistingSessionId_GrowsTheSameSession()
    {
        var websiteId = await SeedWebsiteAsync(multiPoolEnabled: true);
        var poolAId = await SeedPoolAsync(websiteId, "01632 960131");
        var poolBId = await SeedPoolAsync(websiteId, "01632 960132");
        await SeedTrackingNumberAsync(poolAId);
        var didB = await SeedTrackingNumberAsync(poolBId);

        var first = await Allocate(websiteId, consentGranted: true, matchedPoolIds: new[] { poolAId });
        first.EnsureSuccessStatusCode();
        var firstBody = await first.Content.ReadFromJsonAsync<JsonElement>();
        var sessionId = firstBody.GetProperty("session_id").GetString();

        var second = await Allocate(websiteId, consentGranted: true, matchedPoolIds: new[] { poolBId }, sessionId: sessionId);
        second.EnsureSuccessStatusCode();
        var secondBody = await second.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(sessionId, secondBody.GetProperty("session_id").GetString());
        var grown = Assert.Single(secondBody.GetProperty("allocations").EnumerateArray());
        Assert.Equal(poolBId.ToString(), grown.GetProperty("pool_id").GetString());
        Assert.Equal(didB, grown.GetProperty("number").GetString());

        await using var connection = new MySqlConnection(TestDatabase.ConnectionString);
        await connection.OpenAsync();
        var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM allocations WHERE session_id = @sessionId";
        command.Parameters.AddWithValue("@sessionId", sessionId);
        var count = Convert.ToInt32(await command.ExecuteScalarAsync());
        Assert.Equal(2, count); // one session, two pools' worth of allocations — not two sessions
    }

    [Fact]
    public async Task Allocate_AMultiPoolIdNotScopedToTheWebsite_IsSilentlyDropped()
    {
        var websiteId = await SeedWebsiteAsync(multiPoolEnabled: true);
        var poolAId = await SeedPoolAsync(websiteId, "01632 960141");
        await SeedTrackingNumberAsync(poolAId);
        var foreignPoolId = Guid.NewGuid(); // not scoped to this website at all

        var response = await Allocate(websiteId, consentGranted: true, matchedPoolIds: new[] { poolAId, foreignPoolId });

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var allocation = Assert.Single(body.GetProperty("allocations").EnumerateArray());
        Assert.Equal(poolAId.ToString(), allocation.GetProperty("pool_id").GetString());
    }

    [Fact]
    public async Task Allocate_SingleModeWebsite_CarriesNoPoolsOrAllocationsField()
    {
        var websiteId = await SeedWebsiteAsync(multiPoolEnabled: false);

        var response = await Allocate(websiteId, consentGranted: false, matchedPoolIds: null);

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(body.TryGetProperty("pools", out _));
        Assert.False(body.TryGetProperty("allocations", out _));
    }

    private Task<HttpResponseMessage> Allocate(Guid websiteId, bool consentGranted, Guid[]? matchedPoolIds, string? sessionId = null) =>
        _client.PostAsJsonAsync("/v1/dni/allocate", new
        {
            website_id = websiteId.ToString(),
            client_token = $"client-{Guid.NewGuid()}",
            consent_granted = consentGranted,
            matched_pool_ids = matchedPoolIds?.Select(id => id.ToString()).ToArray(),
            session_id = sessionId,
        });

    private async Task<Guid> SeedWebsiteAsync(bool multiPoolEnabled)
    {
        var id = Guid.NewGuid();
        await using var connection = new MySqlConnection(TestDatabase.ConnectionString);
        await connection.OpenAsync();
        var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO websites
                (id, name, permitted_origins, default_number, session_timeout_seconds, heartbeat_interval_seconds,
                 allocation_window_extension_seconds, cooldown_seconds, consent_required, shadow_mode_enabled,
                 multi_pool_enabled, local_timezone, created_at, updated_at)
            VALUES
                (@id, 'Multi-Pool Test Website', 'https://example.com', '01632 960100', 1800, 300, 1800, 1800, 1, 0,
                 @multiPoolEnabled, 'UTC', UTC_TIMESTAMP(), UTC_TIMESTAMP())
            """;
        command.Parameters.AddWithValue("@id", id.ToString());
        command.Parameters.AddWithValue("@multiPoolEnabled", multiPoolEnabled);
        await command.ExecuteNonQueryAsync();
        return id;
    }

    private async Task<Guid> SeedPoolAsync(Guid websiteId, string defaultNumber)
    {
        var id = Guid.NewGuid();
        await using var connection = new MySqlConnection(TestDatabase.ConnectionString);
        await connection.OpenAsync();
        var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO number_pools (id, name, scope_type, scope_ref, default_number, created_at, updated_at)
            VALUES (@id, 'Test Pool', 'website', @scopeRef, @defaultNumber, UTC_TIMESTAMP(), UTC_TIMESTAMP())
            """;
        command.Parameters.AddWithValue("@id", id.ToString());
        command.Parameters.AddWithValue("@scopeRef", websiteId.ToString());
        command.Parameters.AddWithValue("@defaultNumber", defaultNumber);
        await command.ExecuteNonQueryAsync();
        return id;
    }

    // Returns the DID it generated — a shared, persistent database means a hard-coded DID
    // across test runs would collide with a prior run's still-present row.
    private async Task<string> SeedTrackingNumberAsync(Guid poolId)
    {
        var did = $"+44163{Random.Shared.Next(1000000, 9999999)}";
        await using var connection = new MySqlConnection(TestDatabase.ConnectionString);
        await connection.OpenAsync();
        var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO tracking_numbers (id, pool_id, did, status, status_changed_at)
            VALUES (@id, @poolId, @did, 'Active', UTC_TIMESTAMP())
            """;
        command.Parameters.AddWithValue("@id", Guid.NewGuid().ToString());
        command.Parameters.AddWithValue("@poolId", poolId.ToString());
        command.Parameters.AddWithValue("@did", did);
        await command.ExecuteNonQueryAsync();
        return did;
    }
}
