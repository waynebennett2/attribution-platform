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

// FR-046: local username/password + mandatory TOTP MFA is the platform's sole interactive
// sign-in path. SC-016 requires a deactivated user to lose access within one refresh
// interval without a separate action in the platform; since there is no external identity
// provider to revoke a session against, that guarantee is built entirely from the 5-minute
// access-token lifetime plus a refresh-token exchange that is refused once the account is
// deactivated — this exercises both halves directly.
public class AccountAccessTests : IAsyncLifetime
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
    public async Task ATokenPastItsLifetime_IsRejected_RegardlessOfWhyItWasNotRefreshed()
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestAuth.IssueExpiredToken(Role.Analyst));

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var response = await _client.GetAsync($"/v1/reports/dashboard?from={today:yyyy-MM-dd}&to={today:yyyy-MM-dd}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task SignIn_WithCorrectPasswordAndTotp_IssuesAnAccessAndRefreshTokenPair_AndIsAudited()
    {
        var (username, password, totpSecret) = await SeedLocalUserAsync(Role.SystemAdministrator);
        var code = new Totp(Base32Encoding.ToBytes(totpSecret)).ComputeTotp();

        var response = await _client.PostAsJsonAsync("/v1/auth/sign-in", new { username, password, totp_code = code });

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<SignInResponse>();
        Assert.False(string.IsNullOrWhiteSpace(body!.AccessToken));
        Assert.False(string.IsNullOrWhiteSpace(body.RefreshToken));
        Assert.True(body.ExpiresAt <= DateTimeOffset.UtcNow.Add(JwtPolicy.TokenLifetime).AddSeconds(5));

        Assert.True(await WasAuditedAsync("SignIn", username, succeeded: true));
    }

    [Fact]
    public async Task SignIn_WithWrongTotpCode_IsRefused_AndAuditedAsAFailure()
    {
        var (username, password, _) = await SeedLocalUserAsync(Role.Analyst);

        var response = await _client.PostAsJsonAsync("/v1/auth/sign-in", new { username, password, totp_code = "000000" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.True(await WasAuditedAsync("SignIn", username, succeeded: false));
    }

    [Fact]
    public async Task Refresh_WithValidRefreshToken_IssuesANewRotatedPair()
    {
        var (username, password, totpSecret) = await SeedLocalUserAsync(Role.Analyst);
        var signInResponse = await SignInAsync(username, password, totpSecret);

        var refreshResponse = await _client.PostAsJsonAsync("/v1/auth/refresh", new { refresh_token = signInResponse.RefreshToken });

        refreshResponse.EnsureSuccessStatusCode();
        var refreshed = await refreshResponse.Content.ReadFromJsonAsync<SignInResponse>();
        Assert.NotEqual(signInResponse.RefreshToken, refreshed!.RefreshToken);
        Assert.False(string.IsNullOrWhiteSpace(refreshed.AccessToken));

        // Rotation: the old refresh token no longer works once a new one has been issued.
        var reuseOldToken = await _client.PostAsJsonAsync("/v1/auth/refresh", new { refresh_token = signInResponse.RefreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, reuseOldToken.StatusCode);
    }

    [Fact]
    public async Task Refresh_AfterAccountIsDeactivated_IsRefused()
    {
        var (username, password, totpSecret) = await SeedLocalUserAsync(Role.Analyst);
        var signInResponse = await SignInAsync(username, password, totpSecret);

        var connectionFactory = new MySqlConnectionFactory(TestDatabase.ConnectionString);
        var userRepository = new UserRepository(connectionFactory);
        var user = await userRepository.GetByUsernameAsync(username);
        user!.Deactivate();
        await userRepository.UpdateAsync(user);

        var refreshResponse = await _client.PostAsJsonAsync("/v1/auth/refresh", new { refresh_token = signInResponse.RefreshToken });

        Assert.Equal(HttpStatusCode.Unauthorized, refreshResponse.StatusCode);
    }

    [Fact]
    public async Task DeactivateUser_WhenAnotherActiveSystemAdministratorExists_Succeeds()
    {
        var (_, _, _, firstUserId) = await SeedLocalUserWithIdAsync(Role.SystemAdministrator);
        var (_, _, _, secondUserId) = await SeedLocalUserWithIdAsync(Role.SystemAdministrator);

        var adminClient = _factory.CreateClient();
        adminClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestAuth.IssueToken(Role.SystemAdministrator));

        var response = await adminClient.PostAsync($"/v1/admin/users/{firstUserId}/deactivate", content: null);

        response.EnsureSuccessStatusCode();
        _ = secondUserId; // kept active so this test never risks the shared database's last System Administrator.
    }

    private async Task<SignInResponse> SignInAsync(string username, string password, string totpSecret)
    {
        var code = new Totp(Base32Encoding.ToBytes(totpSecret)).ComputeTotp();
        var response = await _client.PostAsJsonAsync("/v1/auth/sign-in", new { username, password, totp_code = code });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<SignInResponse>())!;
    }

    private static async Task<(string Username, string Password, string TotpSecret)> SeedLocalUserAsync(Role role)
    {
        var (username, password, totpSecret, _) = await SeedLocalUserWithIdAsync(role);
        return (username, password, totpSecret);
    }

    private static async Task<(string Username, string Password, string TotpSecret, Guid Id)> SeedLocalUserWithIdAsync(Role role)
    {
        var authenticator = new LocalAuthenticator();
        var username = $"local-{Guid.NewGuid():N}";
        const string password = "correct horse battery staple";
        var totpSecret = LocalAuthenticator.GenerateTotpSecret();
        var user = DomainUser.CreateLocal(username, role, authenticator.HashPassword(password), totpSecret);

        var connectionFactory = new MySqlConnectionFactory(TestDatabase.ConnectionString);
        await new UserRepository(connectionFactory).AddAsync(user);

        return (username, password, totpSecret, user.Id);
    }

    private static async Task<bool> WasAuditedAsync(string action, string username, bool succeeded)
    {
        await using var connection = new MySqlConnection(TestDatabase.ConnectionString);
        await connection.OpenAsync();
        var count = await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM audit_entries WHERE action = @Action AND after_value LIKE @Username AND after_value LIKE @Succeeded",
            new { Action = action, Username = $"%{username}%", Succeeded = $"%{(succeeded ? "true" : "false")}%" });
        return count > 0;
    }

    private sealed record SignInResponse(
        [property: System.Text.Json.Serialization.JsonPropertyName("access_token")] string AccessToken,
        [property: System.Text.Json.Serialization.JsonPropertyName("expires_at")] DateTimeOffset ExpiresAt,
        [property: System.Text.Json.Serialization.JsonPropertyName("refresh_token")] string RefreshToken);
}
