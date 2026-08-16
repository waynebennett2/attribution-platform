using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Attribution.Domain.Identity;
using Attribution.IntegrationTests.TestSupport;
using Dapper;
using Microsoft.AspNetCore.Mvc.Testing;
using MySqlConnector;
using Xunit;

namespace Attribution.IntegrationTests.Reporting;

// spec.md User Story 4, Acceptance Scenario 3 / FR-031, FR-038: an Analyst can read
// reports but is refused on administrative functions — pool, rule and user management —
// and that refusal is itself recorded (AuthorizationFailureAuditMiddleware), not just
// silently 403'd.
public class RoleRestrictionTests : IAsyncLifetime
{
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _analystClient = null!;

    public Task InitializeAsync()
    {
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Jwt:SigningSecret", TestAuth.SigningSecret);
            builder.UseSetting("Jwt:Issuer", TestAuth.Issuer);
            builder.UseSetting("Jwt:Audience", TestAuth.Audience);
        });
        _analystClient = _factory.CreateClient();
        _analystClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestAuth.IssueToken(Role.Analyst));
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        _analystClient.Dispose();
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task Analyst_IsRefused_OnNumberPoolManagement_AndTheAttemptIsRecorded()
    {
        var response = await _analystClient.PostAsJsonAsync("/v1/admin/pools", new { name = "x", scope_type = "website", scope_ref = Guid.NewGuid().ToString() });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.True(await WasRecordedAsRefusedAsync("/v1/admin/pools"));
    }

    [Fact]
    public async Task Analyst_IsRefused_OnQualificationRuleManagement_AndTheAttemptIsRecorded()
    {
        var response = await _analystClient.PostAsJsonAsync(
            "/v1/admin/qualification-rules",
            new { scope_type = "default", conditions = new { answered_required = true }, effective_start = DateTimeOffset.UtcNow });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.True(await WasRecordedAsRefusedAsync("/v1/admin/qualification-rules"));
    }

    [Fact]
    public async Task Analyst_CanStillReadReports_TheRestrictionIsSpecificToAdministrativeFunctions()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var response = await _analystClient.GetAsync($"/v1/reports/dashboard?from={today:yyyy-MM-dd}&to={today:yyyy-MM-dd}");

        response.EnsureSuccessStatusCode();
    }

    private async Task<bool> WasRecordedAsRefusedAsync(string path)
    {
        await using var connection = new MySqlConnection(TestDatabase.ConnectionString);
        await connection.OpenAsync();
        var count = await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM audit_entries WHERE action = 'AccessRefused' AND target_id = @Path AND occurred_at >= @Since",
            new { Path = path, Since = DateTimeOffset.UtcNow.AddMinutes(-1) });
        return count > 0;
    }
}
