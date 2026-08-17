using System.Net.Http.Json;
using System.Text.Json;
using Attribution.IntegrationTests.TestSupport;
using Microsoft.AspNetCore.Mvc.Testing;
using MySqlConnector;
using Xunit;

namespace Attribution.IntegrationTests.Dni;

// FR-012, FR-050: POST /v1/dni/heartbeat keeps every one of a multi-pool session's active
// allocations alive together via a single call, reporting validity and the current number
// per allocation rather than a single flat number.
public class MultiPoolHeartbeatEndpointTests : IAsyncLifetime
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
    public async Task Heartbeat_OneCall_KeepsEveryAllocationAlive_ReportedPerPool()
    {
        var websiteId = await SeedWebsiteAsync();
        var poolAId = await SeedPoolAsync(websiteId, "01632 960201");
        var poolBId = await SeedPoolAsync(websiteId, "01632 960202");
        var poolCId = await SeedPoolAsync(websiteId, "01632 960203");
        var didA = await SeedTrackingNumberAsync(poolAId);
        var didB = await SeedTrackingNumberAsync(poolBId);
        var didC = await SeedTrackingNumberAsync(poolCId);

        var allocateResponse = await _client.PostAsJsonAsync("/v1/dni/allocate", new
        {
            website_id = websiteId.ToString(),
            client_token = $"client-{Guid.NewGuid()}",
            consent_granted = true,
            matched_pool_ids = new[] { poolAId.ToString(), poolBId.ToString(), poolCId.ToString() },
        });
        allocateResponse.EnsureSuccessStatusCode();
        var sessionId = (await allocateResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("session_id").GetString();

        var heartbeatResponse = await _client.PostAsJsonAsync("/v1/dni/heartbeat", new { session_id = sessionId });

        heartbeatResponse.EnsureSuccessStatusCode();
        var body = await heartbeatResponse.Content.ReadFromJsonAsync<JsonElement>();

        Assert.True(body.GetProperty("still_valid").GetBoolean());
        Assert.False(body.TryGetProperty("number", out _)); // multi-pool: numbers live inside allocations, not a flat top-level one
        var allocations = body.GetProperty("allocations").EnumerateArray().ToList();
        Assert.Equal(3, allocations.Count);
        Assert.Contains(allocations, a => a.GetProperty("pool_id").GetString() == poolAId.ToString() && a.GetProperty("still_valid").GetBoolean() && a.GetProperty("number").GetString() == didA);
        Assert.Contains(allocations, a => a.GetProperty("pool_id").GetString() == poolBId.ToString() && a.GetProperty("still_valid").GetBoolean() && a.GetProperty("number").GetString() == didB);
        Assert.Contains(allocations, a => a.GetProperty("pool_id").GetString() == poolCId.ToString() && a.GetProperty("still_valid").GetBoolean() && a.GetProperty("number").GetString() == didC);
    }

    [Fact]
    public async Task Heartbeat_ExtendsEveryAllocationsWindowEnd_TogetherWithTheSession()
    {
        var websiteId = await SeedWebsiteAsync();
        var poolAId = await SeedPoolAsync(websiteId, "01632 960211");
        var poolBId = await SeedPoolAsync(websiteId, "01632 960212");
        await SeedTrackingNumberAsync(poolAId);
        await SeedTrackingNumberAsync(poolBId);

        var allocateResponse = await _client.PostAsJsonAsync("/v1/dni/allocate", new
        {
            website_id = websiteId.ToString(),
            client_token = $"client-{Guid.NewGuid()}",
            consent_granted = true,
            matched_pool_ids = new[] { poolAId.ToString(), poolBId.ToString() },
        });
        allocateResponse.EnsureSuccessStatusCode();
        var sessionId = (await allocateResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("session_id").GetString();

        await using var connection = new MySqlConnection(TestDatabase.ConnectionString);
        await connection.OpenAsync();
        var beforeCommand = connection.CreateCommand();
        beforeCommand.CommandText = "SELECT MIN(window_end) FROM allocations WHERE session_id = @sessionId";
        beforeCommand.Parameters.AddWithValue("@sessionId", sessionId);
        var beforeWindowEnd = Convert.ToDateTime(await beforeCommand.ExecuteScalarAsync());

        await Task.Delay(1100); // ensure a measurable clock difference before the heartbeat extends the window

        var heartbeatResponse = await _client.PostAsJsonAsync("/v1/dni/heartbeat", new { session_id = sessionId });
        heartbeatResponse.EnsureSuccessStatusCode();

        var afterCommand = connection.CreateCommand();
        afterCommand.CommandText = "SELECT MIN(window_end) FROM allocations WHERE session_id = @sessionId";
        afterCommand.Parameters.AddWithValue("@sessionId", sessionId);
        var afterWindowEnd = Convert.ToDateTime(await afterCommand.ExecuteScalarAsync());

        Assert.True(afterWindowEnd > beforeWindowEnd);
    }

    private async Task<Guid> SeedWebsiteAsync()
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
                (@id, 'Multi-Pool Heartbeat Test Website', 'https://example.com', '01632 960200', 1800, 300, 1800, 1800, 1, 0,
                 1, 'UTC', UTC_TIMESTAMP(), UTC_TIMESTAMP())
            """;
        command.Parameters.AddWithValue("@id", id.ToString());
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
