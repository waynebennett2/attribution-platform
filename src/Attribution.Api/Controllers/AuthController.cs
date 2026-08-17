using Attribution.Application.Administration;
using Attribution.Domain.Identity;
using Attribution.Infrastructure.Identity;
using Microsoft.AspNetCore.Mvc;

namespace Attribution.Api.Controllers;

// FR-046: local username/password + mandatory TOTP MFA — the platform's sole interactive
// sign-in path. Both endpoints are unauthenticated by design: a caller with no valid token
// yet is exactly who needs to reach them, and the credentials (or refresh token) themselves
// are the authentication.
[ApiController]
[Route("v1/auth")]
public sealed class AuthController : ControllerBase
{
    private readonly IUserRepository _userRepository;
    private readonly LocalAuthenticator _authenticator;
    private readonly ITokenIssuer _tokenIssuer;
    private readonly IAuditLogger _auditLogger;

    public AuthController(
        IUserRepository userRepository, LocalAuthenticator authenticator, ITokenIssuer tokenIssuer, IAuditLogger auditLogger)
    {
        _userRepository = userRepository;
        _authenticator = authenticator;
        _tokenIssuer = tokenIssuer;
        _auditLogger = auditLogger;
    }

    [HttpPost("sign-in")]
    public async Task<IActionResult> SignIn([FromBody] SignInRequest request)
    {
        var user = await _userRepository.GetByUsernameAsync(request.Username);
        // Same 401 whether the username doesn't exist, the account is deactivated, the
        // password is wrong, or the TOTP code is wrong — none of that is the caller's
        // business to distinguish from outside.
        var succeeded = user is not null && user.IdentityType == IdentityType.Local && user.IsActive
            && user.PasswordHash is not null && user.TotpSecret is not null
            && _authenticator.Verify(user.PasswordHash, request.Password, user.TotpSecret, request.TotpCode);

        // FR-046: every sign-in attempt, success or failure, is audited.
        await _auditLogger.RecordAsync(
            "SignIn", "User", user?.Id.ToString() ?? request.Username, before: null,
            after: new { username = request.Username, succeeded });

        if (!succeeded)
        {
            return Unauthorized();
        }

        var now = DateTimeOffset.UtcNow;
        user!.RecordActivity(now);
        var (accessToken, refreshToken, expiresAt) = IssueTokenPair(user, now);
        await _userRepository.UpdateAsync(user);

        return Ok(new SignInResponse(accessToken, expiresAt, refreshToken));
    }

    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh([FromBody] RefreshRequest request)
    {
        var hashedToken = LocalAuthenticator.HashRefreshToken(request.RefreshToken);
        var user = await _userRepository.GetByRefreshTokenHashAsync(hashedToken);

        var now = DateTimeOffset.UtcNow;
        // FR-046: this is the check that bounds a deactivated account's access loss to one
        // refresh interval — an expired or already-rotated-away token is refused just like
        // a deactivated account's is.
        if (user is null || !user.IsActive || user.RefreshTokenExpiresAt is null || now >= user.RefreshTokenExpiresAt)
        {
            return Unauthorized();
        }

        user.RecordActivity(now);
        var (accessToken, refreshToken, expiresAt) = IssueTokenPair(user, now);
        await _userRepository.UpdateAsync(user);

        return Ok(new SignInResponse(accessToken, expiresAt, refreshToken));
    }

    private (string AccessToken, string RefreshToken, DateTimeOffset ExpiresAt) IssueTokenPair(User user, DateTimeOffset now)
    {
        var accessToken = _tokenIssuer.IssueToken(user, now);
        var refreshToken = LocalAuthenticator.GenerateRefreshToken();
        user.IssueRefreshToken(LocalAuthenticator.HashRefreshToken(refreshToken), now.Add(JwtPolicy.RefreshTokenLifetime));
        return (accessToken, refreshToken, now.Add(JwtPolicy.TokenLifetime));
    }
}

public sealed record SignInRequest(
    string Username,
    string Password,
    [property: System.Text.Json.Serialization.JsonPropertyName("totp_code")] string TotpCode);

public sealed record RefreshRequest(
    [property: System.Text.Json.Serialization.JsonPropertyName("refresh_token")] string RefreshToken);

public sealed record SignInResponse(
    [property: System.Text.Json.Serialization.JsonPropertyName("access_token")] string AccessToken,
    [property: System.Text.Json.Serialization.JsonPropertyName("expires_at")] DateTimeOffset ExpiresAt,
    [property: System.Text.Json.Serialization.JsonPropertyName("refresh_token")] string RefreshToken);
