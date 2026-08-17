using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Attribution.Domain.Identity;
using Attribution.IntegrationTests.TestSupport;
using Dapper;
using Microsoft.AspNetCore.Mvc.Testing;
using MySqlConnector;
using Xunit;

namespace Attribution.IntegrationTests.Administration;

// The admin UI needs to browse pools, websites and a pool's numbers before knowing any
// specific id — GetPool/AdminNumbersController etc. only ever supported fetch-by-id.
// Exercises the three list endpoints added for that: GET /v1/admin/pools,
// GET /v1/admin/websites, GET /v1/admin/pools/{id}/numbers.
public class AdminListEndpointsTests : IAsyncLifetime
{
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _adminClient = null!;

    public Task InitializeAsync()
    {
        DefaultTypeMap.MatchNamesWithUnderscores = true;
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Jwt:SigningSecret", TestAuth.SigningSecret);
            builder.UseSetting("Jwt:Issuer", TestAuth.Issuer);
            builder.UseSetting("Jwt:Audience", TestAuth.Audience);
        });
        _adminClient = _factory.CreateClient();
        _adminClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestAuth.IssueToken(Role.SystemAdministrator));
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        _adminClient.Dispose();
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task ListPools_IncludesAPoolJustCreated()
    {
        var poolName = $"list-test-pool-{Guid.NewGuid():N}";
        var createResponse = await _adminClient.PostAsJsonAsync(
            "/v1/admin/pools", new { name = poolName, scope_type = "website", scope_ref = Guid.NewGuid().ToString() });
        createResponse.EnsureSuccessStatusCode();

        var listResponse = await _adminClient.GetAsync("/v1/admin/pools");

        listResponse.EnsureSuccessStatusCode();
        var pools = await listResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains(pools.EnumerateArray(), p => p.GetProperty("name").GetString() == poolName);
    }

    [Fact]
    public async Task ListPoolNumbers_ReturnsTheNumbersImportedIntoThatPool()
    {
        var createResponse = await _adminClient.PostAsJsonAsync(
            "/v1/admin/pools", new { name = $"pool-{Guid.NewGuid():N}", scope_type = "website", scope_ref = Guid.NewGuid().ToString() });
        createResponse.EnsureSuccessStatusCode();
        var poolId = (await createResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString();

        var did = $"+44163{Random.Shared.Next(1000000, 9999999)}";
        using var content = new MultipartFormDataContent();
        content.Add(new StringContent($"did\n{did}\n"), "file", "numbers.csv");
        var importResponse = await _adminClient.PostAsync($"/v1/admin/pools/{poolId}/numbers/import", content);
        importResponse.EnsureSuccessStatusCode();

        var listResponse = await _adminClient.GetAsync($"/v1/admin/pools/{poolId}/numbers");

        listResponse.EnsureSuccessStatusCode();
        var numbers = await listResponse.Content.ReadFromJsonAsync<JsonElement>();
        var number = Assert.Single(numbers.EnumerateArray());
        Assert.Equal(did, number.GetProperty("did").GetString());
        Assert.Equal("Active", number.GetProperty("status").GetString());
    }

    [Fact]
    public async Task ListWebsites_ReturnsMultiPoolAndShadowModeFlags()
    {
        var websiteId = Guid.NewGuid();
        await using (var connection = new MySqlConnection(TestDatabase.ConnectionString))
        {
            await connection.OpenAsync();
            var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO websites
                    (id, name, permitted_origins, default_number, session_timeout_seconds, heartbeat_interval_seconds,
                     allocation_window_extension_seconds, cooldown_seconds, consent_required, shadow_mode_enabled,
                     multi_pool_enabled, local_timezone, created_at, updated_at)
                VALUES
                    (@id, @name, 'https://example.com', '01632 960000', 1800, 300, 1800, 1800, 1, 0, 1, 'UTC', UTC_TIMESTAMP(), UTC_TIMESTAMP())
                """;
            command.Parameters.AddWithValue("@id", websiteId.ToString());
            command.Parameters.AddWithValue("@name", $"list-website-{websiteId:N}");
            await command.ExecuteNonQueryAsync();
        }

        var listResponse = await _adminClient.GetAsync("/v1/admin/websites");

        listResponse.EnsureSuccessStatusCode();
        var websites = await listResponse.Content.ReadFromJsonAsync<JsonElement>();
        var website = websites.EnumerateArray().Single(w => w.GetProperty("id").GetString() == websiteId.ToString());
        Assert.True(website.GetProperty("multiPoolEnabled").GetBoolean());
        Assert.False(website.GetProperty("shadowModeEnabled").GetBoolean());
    }
}
