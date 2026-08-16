using Attribution.Application.Administration;
using Attribution.Domain.Identity;
using Attribution.Infrastructure.Identity;
using Microsoft.AspNetCore.Mvc;

namespace Attribution.Api.Controllers;

// FR-046: break-glass sign-in — the local, MFA-protected recovery path used when the
// identity provider is unreachable or misconfigured. There is no equivalent endpoint for
// ordinary federated sign-in: that path is the identity provider's own SSO flow redirecting
// back with an already-established session, which this repository has no live provider to
// exercise, so this controller covers exactly the one interactive sign-in path the platform
// itself is responsible for. Unauthenticated by design — a caller with no token yet is
// exactly who needs to reach this — the credentials themselves are the authentication.
[ApiController]
[Route("v1/auth")]
public sealed class AuthController : ControllerBase
{
    private readonly IUserRepository _userRepository;
    private readonly BreakGlassAuthenticator _authenticator;
    private readonly ITokenIssuer _tokenIssuer;
    private readonly IAuditLogger _auditLogger;

    public AuthController(
        IUserRepository userRepository, BreakGlassAuthenticator authenticator, ITokenIssuer tokenIssuer, IAuditLogger auditLogger)
    {
        _userRepository = userRepository;
        _authenticator = authenticator;
        _tokenIssuer = tokenIssuer;
        _auditLogger = auditLogger;
    }

    [HttpPost("break-glass/sign-in")]
    public async Task<IActionResult> SignIn([FromBody] BreakGlassSignInRequest request)
    {
        var user = await _userRepository.GetByUsernameAsync(request.Username);
        // Same 401 whether the username doesn't exist, the account is deactivated, the
        // password is wrong, or the TOTP code is wrong — none of that is the caller's
        // business to distinguish from outside.
        if (user is null || user.IdentityType != IdentityType.BreakGlass || !user.IsActive
            || user.PasswordHash is null || user.TotpSecret is null
            || !_authenticator.Verify(user.PasswordHash, request.Password, user.TotpSecret, request.TotpCode))
        {
            return Unauthorized();
        }

        var now = DateTimeOffset.UtcNow;
        user.RecordActivity(now);
        await _userRepository.UpdateAsync(user);

        // FR-046: "every break-glass sign-in MUST be audited and surfaced to
        // administrators as an exceptional event" — a plain successful action, not a
        // failure, is still audited here precisely because break-glass use itself is the
        // exceptional thing.
        await _auditLogger.RecordAsync(
            "BreakGlassSignIn", "User", user.Id.ToString(), before: null, after: new { user.Username, role = user.EffectiveRole.ToString() });

        var token = _tokenIssuer.IssueToken(user, now);
        return Ok(new BreakGlassSignInResponse(token, now.Add(JwtPolicy.TokenLifetime)));
    }
}

public sealed record BreakGlassSignInRequest(
    string Username,
    string Password,
    [property: System.Text.Json.Serialization.JsonPropertyName("totp_code")] string TotpCode);

public sealed record BreakGlassSignInResponse(
    [property: System.Text.Json.Serialization.JsonPropertyName("access_token")] string AccessToken,
    [property: System.Text.Json.Serialization.JsonPropertyName("expires_at")] DateTimeOffset ExpiresAt);
