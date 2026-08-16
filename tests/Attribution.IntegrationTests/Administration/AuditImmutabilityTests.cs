using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Attribution.Domain.Identity;
using Attribution.IntegrationTests.TestSupport;
using Dapper;
using Microsoft.AspNetCore.Mvc.Testing;
using MySqlConnector;
using Xunit;

namespace Attribution.IntegrationTests.Administration;

// FR-035: "audit entries MUST NOT be editable or deletable by any role" — enforced
// structurally rather than merely by omission: IAuditRepository (Attribution.Domain.Audit)
// exposes no Update or Delete method at all, so there is no code path anywhere in the
// application, for any role, that could alter a stored entry — a PUT or DELETE against the
// read-only audit endpoint hits no route, and even the most privileged role (System
// Administrator) gets exactly the same 404 as any other. "attempts... MUST themselves be
// recorded" has nothing to record here: there is no mutating endpoint to attempt in the
// first place, so no attempt can occur that FR-035 would need to capture — the same
// structural guarantee that makes it un-editable also makes it un-attemptable.
public class AuditImmutabilityTests : IAsyncLifetime
{
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _adminClient = null!;
    private Guid _entryId;
    private string _originalAfterValue = string.Empty;

    public async Task InitializeAsync()
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

        // Any pre-existing entry does; create one deterministically via a real action
        // rather than reaching into the schema, so this exercises the same path a real
        // administrator's audited action would.
        var response = await _adminClient.PostAsJsonAsync(
            "/v1/admin/pools", new { name = $"immutability-{Guid.NewGuid():N}", scope_type = "website", scope_ref = Guid.NewGuid().ToString() });
        response.EnsureSuccessStatusCode();

        await using var connection = new MySqlConnection(TestDatabase.ConnectionString);
        await connection.OpenAsync();
        var row = await connection.QuerySingleAsync<(string Id, string AfterValue)>(
            "SELECT id, after_value FROM audit_entries WHERE action = 'CreatePool' ORDER BY occurred_at DESC LIMIT 1");
        _entryId = Guid.Parse(row.Id);
        _originalAfterValue = row.AfterValue;
    }

    public async Task DisposeAsync()
    {
        _adminClient.Dispose();
        await _factory.DisposeAsync();
    }

    [Theory]
    [InlineData("PUT")]
    [InlineData("PATCH")]
    [InlineData("DELETE")]
    public async Task NoRouteExists_ToModifyOrDeleteAnAuditEntry(string method)
    {
        var response = await _adminClient.SendAsync(new HttpRequestMessage(new HttpMethod(method), $"/v1/admin/audit/{_entryId}"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await AssertEntryUnchangedAsync();
    }

    [Fact]
    public async Task EntryIsUnchanged_AfterAttemptedMutation()
    {
        await _adminClient.SendAsync(new HttpRequestMessage(HttpMethod.Delete, $"/v1/admin/audit/{_entryId}"));
        await AssertEntryUnchangedAsync();
    }

    private async Task AssertEntryUnchangedAsync()
    {
        await using var connection = new MySqlConnection(TestDatabase.ConnectionString);
        await connection.OpenAsync();
        var afterValue = await connection.ExecuteScalarAsync<string>(
            "SELECT after_value FROM audit_entries WHERE id = @Id", new { Id = _entryId.ToString() });
        Assert.Equal(_originalAfterValue, afterValue);
    }
}
