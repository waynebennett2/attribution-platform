using System.Net.Http.Headers;
using System.Net.Http.Json;
using Attribution.Domain.Audit;
using Attribution.Domain.Calls;
using Attribution.Domain.Identity;
using Attribution.Infrastructure.Data;
using Attribution.IntegrationTests.TestSupport;
using Dapper;
using Microsoft.AspNetCore.Mvc.Testing;
using MySqlConnector;
using Xunit;
using DomainAttribution = Attribution.Domain.Calls.Attribution;

namespace Attribution.IntegrationTests.Administration;

// FR-035: every administrator action is written to the audit log — this exercises a
// representative cross-section spanning both this user story's new admin surfaces (users,
// alerts, review cases) and an earlier one (qualification rules, number pools), asserting
// each produces a matching audit_entries row with the actor, action and target FR-035
// requires. AccessRefused (RoleRestrictionTests) and CorrectConversionPublication
// (CorrectionPropagationTests) are exercised by their own dedicated tests already.
public class AuditLogTests : IAsyncLifetime
{
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _adminClient = null!;
    private DateTimeOffset _startedAt;

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
        _startedAt = DateTimeOffset.UtcNow;
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        _adminClient.Dispose();
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task CreatePool_IsRecorded()
    {
        var response = await _adminClient.PostAsJsonAsync(
            "/v1/admin/pools", new { name = $"pool-{Guid.NewGuid():N}", scope_type = "website", scope_ref = Guid.NewGuid().ToString() });
        response.EnsureSuccessStatusCode();

        Assert.True(await ActionWasRecordedAsync("CreatePool", "NumberPool"));
    }

    [Fact]
    public async Task CreateQualificationRuleVersion_IsRecorded()
    {
        var response = await _adminClient.PostAsJsonAsync(
            "/v1/admin/qualification-rules",
            new { scope_type = "campaign", scope_ref = $"campaign-{Guid.NewGuid():N}", conditions = new { answered_required = true }, effective_start = DateTimeOffset.UtcNow });
        response.EnsureSuccessStatusCode();

        Assert.True(await ActionWasRecordedAsync("CreateQualificationRuleVersion", "QualificationRule"));
    }

    [Fact]
    public async Task CreateUser_AndOverrideRole_AreBothRecorded()
    {
        var createResponse = await _adminClient.PostAsJsonAsync(
            "/v1/admin/users", new { username = $"local-{Guid.NewGuid():N}", password = "correct horse battery staple", role = "Analyst" });
        createResponse.EnsureSuccessStatusCode();
        Assert.True(await ActionWasRecordedAsync("CreateUser", "User"));

        var created = await createResponse.Content.ReadFromJsonAsync<CreatedUserResponse>();
        var overrideResponse = await _adminClient.PostAsJsonAsync(
            $"/v1/admin/users/{created!.Id}/role-override", new { role = "MarketingAdministrator" });
        overrideResponse.EnsureSuccessStatusCode();

        Assert.True(await ActionWasRecordedAsync("OverrideUserRole", "User", created.Id.ToString()));
    }

    [Fact]
    public async Task AcknowledgeAlert_IsRecorded()
    {
        var connectionFactory = new MySqlConnectionFactory(TestDatabase.ConnectionString);
        var alertRepository = new AlertRepository(connectionFactory);
        var alert = Alert.Raise(AlertConditionType.PoolUtilisation, $"pool-{Guid.NewGuid():N}", "utilisation >= 90%", DateTimeOffset.UtcNow);
        await alertRepository.AddAsync(alert);

        var response = await _adminClient.PostAsync($"/v1/admin/alerts/{alert.Id}/acknowledge", content: null);
        response.EnsureSuccessStatusCode();

        Assert.True(await ActionWasRecordedAsync("AcknowledgeAlert", "Alert", alert.Id.ToString()));
    }

    [Fact]
    public async Task ResolveReviewCase_IsRecorded()
    {
        var (call, attribution) = await SeedAmbiguousCallAsync();
        var reviewCase = ReviewCase.Open(call.Id, attribution.Id, DateTimeOffset.UtcNow);
        var connectionFactory = new MySqlConnectionFactory(TestDatabase.ConnectionString);
        await new ReviewCaseRepository(connectionFactory).AddAsync(reviewCase);

        var response = await _adminClient.PostAsJsonAsync(
            $"/v1/admin/review-cases/{reviewCase.Id}/resolve", new { confirm_unattributed = true });
        response.EnsureSuccessStatusCode();

        Assert.True(await ActionWasRecordedAsync("ResolveReviewCase", "ReviewCase", reviewCase.Id.ToString()));
    }

    private async Task<bool> ActionWasRecordedAsync(string action, string targetType, string? targetId = null)
    {
        await using var connection = new MySqlConnection(TestDatabase.ConnectionString);
        await connection.OpenAsync();
        var sql = "SELECT COUNT(*) FROM audit_entries WHERE action = @Action AND target_type = @TargetType AND occurred_at >= @Since"
            + (targetId is null ? string.Empty : " AND target_id = @TargetId");
        var count = await connection.ExecuteScalarAsync<int>(sql, new { Action = action, TargetType = targetType, TargetId = targetId, Since = _startedAt });
        return count > 0;
    }

    private static async Task<(Call Call, DomainAttribution Attribution)> SeedAmbiguousCallAsync()
    {
        await using var connection = new MySqlConnection(TestDatabase.ConnectionString);
        await connection.OpenAsync();

        var call = Call.Create(
            $"audit-{Guid.NewGuid()}", CallDirection.Inbound, "+441632960001", "+441632960999",
            DateTimeOffset.UtcNow, null, null, null, "no_answer", false, DateTimeOffset.UtcNow);
        await connection.ExecuteAsync(
            """
            INSERT INTO calls
                (id, source_record_id, direction, dialled_number, caller_id, started_at, answered_at,
                 ended_at, connected_duration_seconds, disposition, is_final, ingested_at, updated_at)
            VALUES
                (@Id, @SourceRecordId, @Direction, @DialledNumber, @CallerId, @StartedAt, @AnsweredAt,
                 @EndedAt, @ConnectedDurationSeconds, @Disposition, @IsFinal, @IngestedAt, @UpdatedAt)
            """,
            new
            {
                Id = call.Id.ToString(), call.SourceRecordId, Direction = call.Direction.ToString(), call.DialledNumber,
                call.CallerId, call.StartedAt, call.AnsweredAt, call.EndedAt, call.ConnectedDurationSeconds,
                call.Disposition, call.IsFinal, call.IngestedAt, call.UpdatedAt,
            });

        var attribution = DomainAttribution.Ambiguous(call.Id, "multiple_allocation_windows_cover_call_start", call.StartedAt);
        await connection.ExecuteAsync(
            """
            INSERT INTO attributions (id, call_id, session_id, allocation_id, state, reason, is_shadow_derived, is_current, decided_at)
            VALUES (@Id, @CallId, NULL, NULL, @State, @Reason, 0, 1, @DecidedAt)
            """,
            new { Id = attribution.Id.ToString(), CallId = call.Id.ToString(), State = attribution.State.ToString(), attribution.Reason, attribution.DecidedAt });

        return (call, attribution);
    }

    private sealed record CreatedUserResponse([property: System.Text.Json.Serialization.JsonPropertyName("id")] Guid Id);
}
