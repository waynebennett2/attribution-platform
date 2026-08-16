using System.Net.Http.Headers;
using Attribution.Application.Administration;
using Attribution.Domain.Identity;
using Attribution.Infrastructure.Data;
using Attribution.IntegrationTests.TestSupport;
using Dapper;
using Microsoft.AspNetCore.Mvc.Testing;
using MySqlConnector;
using Xunit;

namespace Attribution.IntegrationTests.Retention;

// FR-039, SC-019: an erasure request for an identified visitor completes well within the
// 30-day bar — this performs it synchronously, so "within 30 days" is trivially satisfied;
// what actually needs proving is that requesting it removes the visitor's session-tracking
// identifiers immediately (not queued for the routine 14-month sweep) and that a call
// attributed to that visitor is de-identified the same way.
public class ErasureSlaTests : IAsyncLifetime
{
    private RetentionService _retentionService = null!;

    public Task InitializeAsync()
    {
        DefaultTypeMap.MatchNamesWithUnderscores = true;
        var connectionFactory = new MySqlConnectionFactory(TestDatabase.ConnectionString);
        var unitOfWork = new UnitOfWork(connectionFactory);
        var policy = new RetentionPolicy { HmacKey = "integration-test-retention-hmac-key-at-least-32-chars" };
        _retentionService = new RetentionService(new RetentionRepository(connectionFactory, unitOfWork), policy);
        return Task.CompletedTask;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task ErasureRequest_CompletesImmediately_RegardlessOfTheVisitorsAge()
    {
        var now = DateTimeOffset.UtcNow;
        // A brand-new visitor — nowhere near the routine 14-month sweep's cutoff — proving
        // erasure is a distinct, on-demand path rather than merely an early sweep trigger.
        var (_, visitorId, sessionId, callId, originalCallerId) =
            await RetentionIntegrityTests.SeedFullyQualifiedCallAsync(startedAt: now);

        var startedAt = DateTimeOffset.UtcNow;
        await _retentionService.EraseVisitorAsync(visitorId, now);
        var completedWithin = DateTimeOffset.UtcNow - startedAt;

        Assert.True(completedWithin < TimeSpan.FromDays(30));

        await using var connection = new MySqlConnection(TestDatabase.ConnectionString);
        await connection.OpenAsync();

        var visitorDeIdentifiedAt = await connection.ExecuteScalarAsync<DateTime?>(
            "SELECT de_identified_at FROM visitors WHERE id = @Id", new { Id = visitorId.ToString() });
        Assert.NotNull(visitorDeIdentifiedAt);

        var sessionGclid = await connection.ExecuteScalarAsync<string?>(
            "SELECT gclid FROM sessions WHERE id = @Id", new { Id = sessionId.ToString() });
        Assert.Null(sessionGclid);

        var callerId = await connection.ExecuteScalarAsync<string>(
            "SELECT caller_id FROM calls WHERE id = @Id", new { Id = callId.ToString() });
        Assert.NotEqual(originalCallerId, callerId);
        Assert.Equal(64, callerId!.Length);
    }

    [Fact]
    public async Task ErasureRequest_LeavesACallUnderOpenReview_ForTheReviewerToStillSee()
    {
        var now = DateTimeOffset.UtcNow;
        var (_, visitorId, _, callId, originalCallerId) =
            await RetentionIntegrityTests.SeedFullyQualifiedCallAsync(startedAt: now);

        await using (var connection = new MySqlConnection(TestDatabase.ConnectionString))
        {
            await connection.OpenAsync();
            var attributionId = await connection.ExecuteScalarAsync<string>(
                "SELECT id FROM attributions WHERE call_id = @CallId", new { CallId = callId.ToString() });
            await connection.ExecuteAsync(
                """
                INSERT INTO review_cases (id, call_id, attribution_id, status, opened_at)
                VALUES (@Id, @CallId, @AttributionId, 'Open', UTC_TIMESTAMP())
                """,
                new { Id = Guid.NewGuid().ToString(), CallId = callId.ToString(), AttributionId = attributionId });
        }

        await _retentionService.EraseVisitorAsync(visitorId, now);

        await using var verifyConnection = new MySqlConnection(TestDatabase.ConnectionString);
        await verifyConnection.OpenAsync();
        var callerId = await verifyConnection.ExecuteScalarAsync<string>(
            "SELECT caller_id FROM calls WHERE id = @Id", new { Id = callId.ToString() });
        Assert.Equal(originalCallerId, callerId); // left untouched — the sweep picks it up once resolved
    }

    [Fact]
    public async Task ErasureEndpoint_ErasesTheVisitor_AndAuditsTheRequest()
    {
        var now = DateTimeOffset.UtcNow;
        var (_, visitorId, _, _, _) = await RetentionIntegrityTests.SeedFullyQualifiedCallAsync(startedAt: now);

        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Jwt:SigningSecret", TestAuth.SigningSecret);
            builder.UseSetting("Jwt:Issuer", TestAuth.Issuer);
            builder.UseSetting("Jwt:Audience", TestAuth.Audience);
        });
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestAuth.IssueToken(Role.SystemAdministrator));

        var response = await client.PostAsync($"/v1/admin/privacy/visitors/{visitorId}/erase", content: null);

        response.EnsureSuccessStatusCode();

        await using var connection = new MySqlConnection(TestDatabase.ConnectionString);
        await connection.OpenAsync();
        var visitorDeIdentifiedAt = await connection.ExecuteScalarAsync<DateTime?>(
            "SELECT de_identified_at FROM visitors WHERE id = @Id", new { Id = visitorId.ToString() });
        Assert.NotNull(visitorDeIdentifiedAt);

        var auditCount = await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM audit_entries WHERE action = 'EraseVisitor' AND target_id = @VisitorId",
            new { VisitorId = visitorId.ToString() });
        Assert.True(auditCount > 0);
    }
}
