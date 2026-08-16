using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Attribution.Domain.Identity;
using Attribution.Infrastructure.Data;
using Attribution.Infrastructure.Identity;
using Attribution.IntegrationTests.TestSupport;
using Dapper;
using Microsoft.AspNetCore.Mvc.Testing;
using MySqlConnector;
using OtpNet;
using Xunit;
using DomainUser = Attribution.Domain.Identity.User;

namespace Attribution.IntegrationTests.Administration;

// FR-046: "The platform-issued token MUST be short-lived, expiring within 5 minutes...
// where the provider no longer asserts a user, that user MUST lose access within one
// refresh interval without requiring a separate action in the platform." There is no real
// identity provider in this environment to actually revoke a session against, so this
// exercises the mechanism the guarantee is built from directly: JwtPolicy.TokenLifetime is
// exactly 5 minutes, and a token past that lifetime is rejected outright — a provider that
// stops asserting a user simply stops the client's silent re-authentication, and the
// already-issued token then expires on its own within that same window. Also covers the
// other half of FR-046: break-glass MFA sign-in via AuthController, the local recovery path
// used when the provider itself is unreachable.
public class FederationRevocationTests : IAsyncLifetime
{
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;

    public Task InitializeAsync()
    {
        DefaultTypeMap.MatchNamesWithUnderscores = true;
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Jwt:SigningSecret", TestAuth.SigningSecret);
            builder.UseSetting("Jwt:Issuer", TestAuth.Issuer);
            builder.UseSetting("Jwt:Audience", TestAuth.Audience);
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
    public void TokenLifetime_IsExactlyFiveMinutes()
    {
        // FR-046's literal number — a regression here silently widens or narrows the
        // window every other guarantee in this file (and AuthController) depends on.
        Assert.Equal(TimeSpan.FromMinutes(5), JwtPolicy.TokenLifetime);
    }

    [Fact]
    public async Task ATokenPastItsLifetime_IsRejected_RegardlessOfWhyReAuthenticationDidNotHappen()
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestAuth.IssueExpiredToken(Role.Analyst));

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var response = await _client.GetAsync($"/v1/reports/dashboard?from={today:yyyy-MM-dd}&to={today:yyyy-MM-dd}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task BreakGlassSignIn_WithCorrectPasswordAndTotp_IssuesAToken_AndIsAuditedAsExceptional()
    {
        var (username, password, totpSecret) = await SeedBreakGlassUserAsync(Role.SystemAdministrator);
        var code = new Totp(Base32Encoding.ToBytes(totpSecret)).ComputeTotp();

        var response = await _client.PostAsJsonAsync("/v1/auth/break-glass/sign-in", new { username, password, totp_code = code });

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<SignInResponse>();
        Assert.False(string.IsNullOrWhiteSpace(body!.AccessToken));
        Assert.True(body.ExpiresAt <= DateTimeOffset.UtcNow.Add(JwtPolicy.TokenLifetime).AddSeconds(5));

        Assert.True(await WasAuditedAsync("BreakGlassSignIn", username));
    }

    [Fact]
    public async Task BreakGlassSignIn_WithWrongTotpCode_IsRefused()
    {
        var (username, password, _) = await SeedBreakGlassUserAsync(Role.Analyst);

        var response = await _client.PostAsJsonAsync("/v1/auth/break-glass/sign-in", new { username, password, totp_code = "000000" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static async Task<(string Username, string Password, string TotpSecret)> SeedBreakGlassUserAsync(Role role)
    {
        var authenticator = new BreakGlassAuthenticator();
        var username = $"breakglass-{Guid.NewGuid():N}";
        const string password = "correct horse battery staple";
        var totpSecret = BreakGlassAuthenticator.GenerateTotpSecret();
        var user = DomainUser.CreateBreakGlass(username, role, authenticator.HashPassword(password), totpSecret);

        var connectionFactory = new MySqlConnectionFactory(TestDatabase.ConnectionString);
        await new UserRepository(connectionFactory).AddAsync(user);

        return (username, password, totpSecret);
    }

    private static async Task<bool> WasAuditedAsync(string action, string username)
    {
        await using var connection = new MySqlConnection(TestDatabase.ConnectionString);
        await connection.OpenAsync();
        var count = await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM audit_entries WHERE action = @Action AND after_value LIKE @Username",
            new { Action = action, Username = $"%{username}%" });
        return count > 0;
    }

    private sealed record SignInResponse(
        [property: System.Text.Json.Serialization.JsonPropertyName("access_token")] string AccessToken,
        [property: System.Text.Json.Serialization.JsonPropertyName("expires_at")] DateTimeOffset ExpiresAt);
}
