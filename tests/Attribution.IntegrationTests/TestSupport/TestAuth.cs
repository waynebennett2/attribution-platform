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

    // FR-038: distinct from IssueToken(Role) — this carries identity_type=IntegrationService,
    // which IntegrationServiceAccessMiddleware keys off, rather than just the mapped role.
    public static string IssueIntegrationServiceToken()
    {
        var issuer = new JwtTokenIssuer(SigningSecret, Issuer, Audience);
        var user = User.CreateIntegrationService($"client-{Guid.NewGuid():N}");
        return issuer.IssueToken(user, DateTimeOffset.UtcNow);
    }

    // FR-046: a token issued far enough in the past that JwtPolicy.TokenLifetime (5
    // minutes) has already elapsed — simulates "the client didn't silently re-authenticate
    // before expiry", the mechanism that actually enforces revocation within one refresh
    // interval when there is no live federation session behind it any more.
    public static string IssueExpiredToken(Role role)
    {
        var issuer = new JwtTokenIssuer(SigningSecret, Issuer, Audience);
        var user = User.CreateFederated(Guid.NewGuid().ToString(), role);
        return issuer.IssueToken(user, DateTimeOffset.UtcNow.Subtract(JwtPolicy.TokenLifetime).AddSeconds(-30));
    }
}
