using Attribution.Domain.Identity;
using Attribution.Infrastructure.Identity;

namespace Attribution.IntegrationTests.TestSupport;

// Mints a bearer token with the same signing configuration AllocateEndpointTests already
// establishes via WithWebHostBuilder — reused by every test that needs an authenticated
// request. RBAC is claims-only (OperationAuthorizationHandler reads the "role" claim
// straight off the token, no database lookup), so no User row needs to exist for this to work.
public static class TestAuth
{
    public const string SigningSecret = "integration-test-signing-secret-at-least-32-characters";
    public const string Issuer = "attribution-platform-tests";
    public const string Audience = "attribution-platform-tests";

    public static string IssueToken(Role role)
    {
        var issuer = new JwtTokenIssuer(SigningSecret, Issuer, Audience);
        var user = User.CreateFederated(Guid.NewGuid().ToString(), role);
        return issuer.IssueToken(user, DateTimeOffset.UtcNow);
    }
}
