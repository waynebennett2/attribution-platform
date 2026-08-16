using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Attribution.Domain.Identity;
using Attribution.IntegrationTests.TestSupport;
using Dapper;
using Microsoft.AspNetCore.Mvc.Testing;
using MySqlConnector;
using Xunit;

namespace Attribution.IntegrationTests.Reporting;

// FR-048, SC-018: the coverage breakdown (attributed/unattributed/ambiguous, by reason and
// website) is the sole evidence of how completely the platform attributes live traffic —
// it MUST reconcile exactly with the underlying call records. Verified against an
// independent raw-SQL count over the exact call ids this test seeded.
public class CoverageBreakdownTests : IAsyncLifetime
{
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;
    private DateOnly _day;
    private List<Guid> _callIds = null!;

    public async Task InitializeAsync()
    {
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Jwt:SigningSecret", TestAuth.SigningSecret);
            builder.UseSetting("Jwt:Issuer", TestAuth.Issuer);
            builder.UseSetting("Jwt:Audience", TestAuth.Audience);
        });
        _client = _factory.CreateClient();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestAuth.IssueToken(Role.MarketingAdministrator));

        _day = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-Random.Shared.Next(2000, 200000)));
        var dayStart = new DateTimeOffset(_day.ToDateTime(new TimeOnly(8, 0)), TimeSpan.Zero);

        var seed = await ReportingSeedHelper.CreateAsync(campaign: "coverage-test");
        var ids = new List<Guid>
        {
            await seed.SeedAttributedCallAsync(dayStart, answeredAt: dayStart, connectedDurationSeconds: 70, isQualified: true),
            await seed.SeedAttributedCallAsync(dayStart.AddHours(1), answeredAt: dayStart.AddHours(1), connectedDurationSeconds: 70, isQualified: true),
            await seed.SeedUnattributedCallAsync(dayStart.AddHours(2), "number_never_allocated"),
            await seed.SeedUnattributedCallAsync(dayStart.AddHours(3), "no_allocation_window_covers_call_start"),
            await seed.SeedUnattributedCallAsync(dayStart.AddHours(4), "no_allocation_window_covers_call_start"),
            await seed.SeedAmbiguousCallAsync(dayStart.AddHours(5)),
        };
        _callIds = ids;
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task CoverageTotals_ReconcileExactlyWithTheUnderlyingAttributionRecords()
    {
        await using var connection = new MySqlConnection(TestDatabase.ConnectionString);
        await connection.OpenAsync();
        var idList = _callIds.Select(id => id.ToString()).ToList();

        var expectedTotal = await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM attributions WHERE call_id IN @Ids AND is_current = 1", new { Ids = idList });
        var expectedAttributed = await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM attributions WHERE call_id IN @Ids AND is_current = 1 AND state = 'Attributed'", new { Ids = idList });
        var expectedUnattributed = await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM attributions WHERE call_id IN @Ids AND is_current = 1 AND state = 'Unattributed'", new { Ids = idList });
        var expectedAmbiguous = await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM attributions WHERE call_id IN @Ids AND is_current = 1 AND state = 'Ambiguous'", new { Ids = idList });

        var response = await _client.GetAsync($"/v1/reports/coverage?from={_day:yyyy-MM-dd}&to={_day:yyyy-MM-dd}");
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var totals = body.GetProperty("totals");

        // Independently-known exact numbers this test seeded.
        Assert.Equal(6, expectedTotal);
        Assert.Equal(2, expectedAttributed);
        Assert.Equal(3, expectedUnattributed);
        Assert.Equal(1, expectedAmbiguous);

        Assert.Equal(expectedTotal, totals.GetProperty("total").GetInt32());
        Assert.Equal(expectedAttributed, totals.GetProperty("attributed").GetInt32());
        Assert.Equal(expectedUnattributed, totals.GetProperty("unattributed").GetInt32());
        Assert.Equal(expectedAmbiguous, totals.GetProperty("ambiguous").GetInt32());
    }

    [Fact]
    public async Task CoverageRows_BreakDownUnattributedCallsByReason()
    {
        var response = await _client.GetAsync($"/v1/reports/coverage?from={_day:yyyy-MM-dd}&to={_day:yyyy-MM-dd}");
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var rows = body.GetProperty("rows").EnumerateArray().Where(r => r.GetProperty("state").GetString() == "Unattributed").ToList();

        var neverAllocated = rows.Single(r => r.GetProperty("reason").GetString() == "number_never_allocated");
        Assert.Equal(1, neverAllocated.GetProperty("count").GetInt32());

        var noWindow = rows.Single(r => r.GetProperty("reason").GetString() == "no_allocation_window_covers_call_start");
        Assert.Equal(2, noWindow.GetProperty("count").GetInt32());
    }
}
