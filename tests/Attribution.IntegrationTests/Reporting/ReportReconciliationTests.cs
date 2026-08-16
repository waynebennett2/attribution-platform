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

// FR-029: dashboard/campaign/call-detail/missed/qualified/unattributed report totals must
// reconcile exactly against the underlying call records — verified here by an independent
// raw-SQL count over the exact call ids this test seeded, not just against the test's own
// expected-value arithmetic (which could share a bug with the report code).
public class ReportReconciliationTests : IAsyncLifetime
{
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;
    private DateOnly _day;
    private List<Guid> _callIds = null!;
    private Guid _attributedQualifiedCallId;
    private Guid _attributedUnqualifiedCallId;
    private Guid _missedCallId;
    private Guid _unattributedCallId;
    private Guid _ambiguousCallId;
    private string _did = string.Empty;

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

        // Far enough in the past, at a random offset, that this run's period cannot
        // overlap another concurrently-running test's own random period on this shared database.
        _day = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-Random.Shared.Next(2000, 200000)));
        var dayStart = new DateTimeOffset(_day.ToDateTime(new TimeOnly(10, 0)), TimeSpan.Zero);

        var seed = await ReportingSeedHelper.CreateAsync(campaign: "reconciliation-test");
        _did = seed.Did;
        _attributedQualifiedCallId = await seed.SeedAttributedCallAsync(dayStart, answeredAt: dayStart, connectedDurationSeconds: 90, isQualified: true);
        _attributedUnqualifiedCallId = await seed.SeedAttributedCallAsync(dayStart.AddHours(1), answeredAt: dayStart.AddHours(1), connectedDurationSeconds: 30, isQualified: false);
        _missedCallId = await seed.SeedAttributedCallAsync(dayStart.AddHours(2), answeredAt: null, isQualified: false);
        _unattributedCallId = await seed.SeedUnattributedCallAsync(dayStart.AddHours(3), "number_never_allocated");
        _ambiguousCallId = await seed.SeedAmbiguousCallAsync(dayStart.AddHours(4));

        _callIds = new List<Guid> { _attributedQualifiedCallId, _attributedUnqualifiedCallId, _missedCallId, _unattributedCallId, _ambiguousCallId };
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task DashboardTotals_ReconcileAgainstTheUnderlyingCallAndAttributionRecords()
    {
        var (independentTotal, independentAttributed, independentQualified) = await IndependentCountsAsync();

        var body = await GetReportAsync("dashboard");
        var totals = body.GetProperty("totals");

        Assert.Equal(independentTotal, totals.GetProperty("total_calls").GetInt32());
        Assert.Equal(independentAttributed, totals.GetProperty("attributed_calls").GetInt32());
        Assert.Equal(independentQualified, totals.GetProperty("qualified_calls").GetInt32());

        // Sanity on the exact numbers this test seeded, not just self-consistency.
        Assert.Equal(5, independentTotal);
        Assert.Equal(3, independentAttributed); // qualified + unqualified + missed are all Attributed
        Assert.Equal(1, independentQualified);
    }

    [Fact]
    public async Task CampaignReport_GroupsAttributedCallsByTheOriginatingSessionsCampaign()
    {
        var body = await GetReportAsync("campaigns");
        var row = body.GetProperty("rows").EnumerateArray().Single(r => r.GetProperty("campaign").GetString() == "reconciliation-test");

        Assert.Equal(3, row.GetProperty("total_calls").GetInt32());
        Assert.Equal(1, row.GetProperty("qualified_calls").GetInt32());
    }

    [Fact]
    public async Task MissedReport_ContainsExactlyTheUnansweredInboundCall()
    {
        var body = await GetReportAsync("missed");
        var rows = body.GetProperty("rows").EnumerateArray().ToList();

        Assert.Contains(rows, r => r.GetProperty("call_id").GetGuid() == _missedCallId);
        Assert.DoesNotContain(rows, r => r.GetProperty("call_id").GetGuid() == _attributedQualifiedCallId);
    }

    [Fact]
    public async Task QualifiedReport_ContainsExactlyTheQualifiedCall()
    {
        var body = await GetReportAsync("qualified");
        var rows = body.GetProperty("rows").EnumerateArray().ToList();

        Assert.Single(rows, r => r.GetProperty("call_id").GetGuid() == _attributedQualifiedCallId);
        Assert.DoesNotContain(rows, r => r.GetProperty("call_id").GetGuid() == _attributedUnqualifiedCallId);
    }

    [Fact]
    public async Task UnattributedReport_IncludesBothTheUnattributedAndTheAmbiguousCall()
    {
        var body = await GetReportAsync("unattributed");
        var rows = body.GetProperty("rows").EnumerateArray().ToList();

        Assert.Contains(rows, r => r.GetProperty("call_id").GetGuid() == _unattributedCallId);
        Assert.Contains(rows, r => r.GetProperty("call_id").GetGuid() == _ambiguousCallId);
        Assert.Equal(2, body.GetProperty("totals").GetProperty("count").GetInt32());
    }

    [Fact]
    public async Task CallDetailSearch_FindsACallByItsDialledNumber_AndRespectsTheStateFilter()
    {
        var body = await GetReportAsync("calls", $"&q={Uri.EscapeDataString(_did)}&state=attributed");
        var rows = body.GetProperty("rows").EnumerateArray().ToList();

        Assert.All(rows, r => Assert.Equal("Attributed", r.GetProperty("attribution_state").GetString()));
        Assert.Contains(rows, r => r.GetProperty("call_id").GetGuid() == _attributedQualifiedCallId);
        Assert.DoesNotContain(rows, r => r.GetProperty("call_id").GetGuid() == _unattributedCallId);
    }

    private async Task<JsonElement> GetReportAsync(string reportName, string extraQuery = "")
    {
        var response = await _client.GetAsync($"/v1/reports/{reportName}?from={_day:yyyy-MM-dd}&to={_day:yyyy-MM-dd}{extraQuery}");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private async Task<(int Total, int Attributed, int Qualified)> IndependentCountsAsync()
    {
        await using var connection = new MySqlConnection(TestDatabase.ConnectionString);
        await connection.OpenAsync();
        var idList = _callIds.Select(id => id.ToString()).ToList();

        var total = await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM calls WHERE id IN @Ids", new { Ids = idList });
        var attributed = await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM attributions WHERE call_id IN @Ids AND is_current = 1 AND state = 'Attributed'", new { Ids = idList });
        var qualified = await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM qualification_results WHERE call_id IN @Ids AND is_current = 1 AND is_qualified = 1", new { Ids = idList });

        return (total, attributed, qualified);
    }
}
