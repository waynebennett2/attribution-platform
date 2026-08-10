using System.Net.Http.Json;
using System.Text.Json;
using Attribution.Infrastructure.Data.Migrations;
using Microsoft.AspNetCore.Mvc.Testing;
using MySqlConnector;
using Testcontainers.MySql;
using Xunit;

namespace Attribution.IntegrationTests.Dni;

// FR-003, FR-011: exercises POST /v1/dni/allocate end to end against a real MySQL 8.0+
// instance (Testcontainers), including the pool-exhausted fallback. Could not be
// executed in the sandboxed environment this was authored in (Docker daemon
// unreachable) — verified by inspection/compilation only; expected to run in CI/local dev.
public class AllocateEndpointTests : IAsyncLifetime
{
    private readonly MySqlContainer _mysql = new MySqlBuilder("mysql:8.0").Build();
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        await _mysql.StartAsync();
        MigrationRunner.ApplyMigrations(_mysql.GetConnectionString());

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:AttributionDb", _mysql.GetConnectionString());
            builder.UseSetting("Jwt:SigningSecret", "integration-test-signing-secret-at-least-32-characters");
            builder.UseSetting("Jwt:Issuer", "attribution-platform-tests");
            builder.UseSetting("Jwt:Audience", "attribution-platform-tests");
        });
        _client = _factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
        await _mysql.DisposeAsync();
    }

    [Fact]
    public async Task Allocate_WithConsentGranted_ReturnsANumber_AndCreatesASession()
    {
        var websiteId = await SeedWebsiteAsync(defaultNumber: "+15550000000");
        var poolId = await SeedPoolAsync(websiteId);
        await SeedTrackingNumberAsync(poolId, "+15551234567");

        var response = await _client.PostAsJsonAsync("/v1/dni/allocate", new
        {
            website_id = websiteId.ToString(),
            client_token = "client-1",
            consent_granted = true,
            landing_page = "https://example.com/",
            utm = new { source = "google", medium = "cpc", campaign = "spring" },
            gclid = "gclid-1",
        });

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.False(string.IsNullOrEmpty(body.GetProperty("session_id").GetString()));
        Assert.Equal("+15551234567", body.GetProperty("number").GetString());
    }

    [Fact]
    public async Task Allocate_WithConsentWithheld_ReturnsDefaultNumber_NoSession()
    {
        var websiteId = await SeedWebsiteAsync(defaultNumber: "+15550000001");

        var response = await _client.PostAsJsonAsync("/v1/dni/allocate", new
        {
            website_id = websiteId.ToString(),
            client_token = "client-2",
            consent_granted = false,
        });

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(JsonValueKind.Null, body.GetProperty("session_id").ValueKind);
        Assert.Equal("+15550000001", body.GetProperty("number").GetString());
        Assert.Equal("no_consent", body.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task Allocate_WhenPoolExhausted_ReturnsDefaultNumber_WithReasonRecorded()
    {
        // FR-011: a pool configured with zero numbers is the simplest way to force exhaustion.
        var websiteId = await SeedWebsiteAsync(defaultNumber: "+15550000002");
        await SeedPoolAsync(websiteId);

        var response = await _client.PostAsJsonAsync("/v1/dni/allocate", new
        {
            website_id = websiteId.ToString(),
            client_token = "client-3",
            consent_granted = true,
        });

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(JsonValueKind.Null, body.GetProperty("session_id").ValueKind);
        Assert.Equal("+15550000002", body.GetProperty("number").GetString());
        Assert.Equal("pool_exhausted", body.GetProperty("reason").GetString());
    }

    private async Task<Guid> SeedWebsiteAsync(string defaultNumber)
    {
        var id = Guid.NewGuid();
        await using var connection = new MySqlConnection(_mysql.GetConnectionString());
        await connection.OpenAsync();
        var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO websites
                (id, name, permitted_origins, default_number, session_timeout_seconds, heartbeat_interval_seconds,
                 allocation_window_extension_seconds, cooldown_seconds, consent_required, shadow_mode_enabled,
                 local_timezone, created_at, updated_at)
            VALUES
                (@id, 'Test Website', 'https://example.com', @defaultNumber, 1800, 300, 1800, 1800, 1, 0,
                 'UTC', UTC_TIMESTAMP(), UTC_TIMESTAMP())
            """;
        command.Parameters.AddWithValue("@id", id.ToString());
        command.Parameters.AddWithValue("@defaultNumber", defaultNumber);
        await command.ExecuteNonQueryAsync();
        return id;
    }

    private async Task<Guid> SeedPoolAsync(Guid websiteId)
    {
        var id = Guid.NewGuid();
        await using var connection = new MySqlConnection(_mysql.GetConnectionString());
        await connection.OpenAsync();
        var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO number_pools (id, name, scope_type, scope_ref, created_at, updated_at)
            VALUES (@id, 'Test Pool', 'website', @scopeRef, UTC_TIMESTAMP(), UTC_TIMESTAMP())
            """;
        command.Parameters.AddWithValue("@id", id.ToString());
        command.Parameters.AddWithValue("@scopeRef", websiteId.ToString());
        await command.ExecuteNonQueryAsync();
        return id;
    }

    private async Task SeedTrackingNumberAsync(Guid poolId, string did)
    {
        await using var connection = new MySqlConnection(_mysql.GetConnectionString());
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
    }
}
