using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using MySqlConnector;
using Xunit;

namespace Attribution.IntegrationTests.Performance;

// SC-005: terminating one Api replica mid-load must produce zero failed allocation
// requests — nginx's passive health check plus its retry-against-the-other-upstream
// (nginx.conf's proxy_next_upstream, docs/deployment.md) is what's actually under test
// here, not the application code, which is identical to every other test in this suite.
//
// Same prerequisite as HorizontalScaleTest: `docker compose up -d --build api1 api2 nginx`
// from the repository root first; this test also shells out to `docker stop`/`docker start`
// on api1 specifically, so it additionally requires the Docker CLI to be on PATH and this
// process to have permission to control the compose project's containers. Restores api1
// to running in a finally block regardless of outcome, so a single run never leaves the
// topology down for whatever runs next.
public class FailoverTest
{
    private const string BaseUrl = "http://localhost:8080";
    private const string Api1ContainerName = "attribution-project-api1-1";
    private const int RequestCount = 30;

    [SkippableFact]
    public async Task StoppingOneInstanceMidLoad_ProducesZeroFailedAllocationRequests()
    {
        using var client = new HttpClient { BaseAddress = new Uri(BaseUrl) };
        await SkipUnlessTopologyIsUpAsync(client);

        var (websiteId, poolId) = await SeedWebsiteAndPoolAsync();
        for (var i = 0; i < RequestCount + 5; i++)
        {
            await SeedTrackingNumberAsync(poolId);
        }

        var results = new List<Task<HttpResponseMessage>>();
        for (var i = 0; i < RequestCount; i++)
        {
            results.Add(client.PostAsJsonAsync("/v1/dni/allocate", new
            {
                website_id = websiteId.ToString(),
                client_token = $"failover-test-{Guid.NewGuid()}",
                consent_granted = true,
                landing_page = "https://example.com/",
            }));

            // Stop api1 partway through the burst, so some in-flight/queued requests land
            // on it right as it goes down — exactly the SC-005 scenario, not a clean
            // before/after split.
            if (i == RequestCount / 2)
            {
                await StopContainerAsync(Api1ContainerName);
            }
        }

        try
        {
            var responses = await Task.WhenAll(results);
            var failed = responses.Where(r => !r.IsSuccessStatusCode).ToList();
            Assert.True(failed.Count == 0, $"{failed.Count}/{RequestCount} allocation requests failed during the mid-load instance stop.");

            foreach (var response in responses)
            {
                var body = await response.Content.ReadFromJsonAsync<JsonElement>();
                Assert.False(string.IsNullOrEmpty(body.GetProperty("session_id").GetString()));
            }
        }
        finally
        {
            await StartContainerAsync(Api1ContainerName);
        }
    }

    private static async Task StopContainerAsync(string name) => await RunDockerAsync($"stop {name}");

    private static async Task StartContainerAsync(string name) => await RunDockerAsync($"start {name}");

    private static async Task RunDockerAsync(string arguments)
    {
        var startInfo = new ProcessStartInfo("docker", arguments) { RedirectStandardOutput = true, RedirectStandardError = true };
        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start docker process.");
        await process.WaitForExitAsync();
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
                (@Id, 'Failover Test Website', 'https://example.com', '+441632960000', 1800, 300, 1800, 1800, 1, 0,
                 'UTC', UTC_TIMESTAMP(), UTC_TIMESTAMP())
            """,
            new { Id = websiteId.ToString() });
        await connection.ExecuteAsync(
            "INSERT INTO number_pools (id, name, scope_type, scope_ref, created_at, updated_at) VALUES (@Id, 'Failover Test Pool', 'website', @ScopeRef, UTC_TIMESTAMP(), UTC_TIMESTAMP())",
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

    private const string LocalComposeConnectionString =
        "Server=localhost;Port=3306;Database=attribution;User=attribution;Password=attribution_dev;";
}
