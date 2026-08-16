using System.Net;
using System.Net.Http.Headers;
using Attribution.IntegrationTests.TestSupport;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Attribution.IntegrationTests.Administration;

// FR-038: "deny the Integration Service role any interactive administrative or reporting
// access while permitting system-to-system data exchange." Both layers that enforce this
// are exercised: RbacPolicy's own empty grant set for the role (every [RequireOperation]
// endpoint) and IntegrationServiceAccessMiddleware's explicit backstop (any endpoint,
// including ones RBAC alone wouldn't gate).
public class IntegrationServiceAccessTests : IAsyncLifetime
{
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _integrationServiceClient = null!;

    public Task InitializeAsync()
    {
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Jwt:SigningSecret", TestAuth.SigningSecret);
            builder.UseSetting("Jwt:Issuer", TestAuth.Issuer);
            builder.UseSetting("Jwt:Audience", TestAuth.Audience);
        });
        _integrationServiceClient = _factory.CreateClient();
        _integrationServiceClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestAuth.IssueIntegrationServiceToken());
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        _integrationServiceClient.Dispose();
        await _factory.DisposeAsync();
    }

    [Theory]
    [InlineData("/v1/admin/pools/" + "00000000-0000-0000-0000-000000000000")]
    [InlineData("/v1/admin/health/pools")]
    [InlineData("/v1/admin/alerts")]
    [InlineData("/v1/admin/audit")]
    public async Task IntegrationServiceToken_IsRefused_OnEveryAdministrativeEndpoint(string path)
    {
        var response = await _integrationServiceClient.GetAsync(path);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task IntegrationServiceToken_IsRefused_OnReporting()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var response = await _integrationServiceClient.GetAsync($"/v1/reports/dashboard?from={today:yyyy-MM-dd}&to={today:yyyy-MM-dd}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
