using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using OtpNet;

namespace Attribution.Infrastructure.Identity;

// FR-046: the platform's sole interactive authentication mechanism — local username +
// password plus mandatory TOTP MFA — together with the refresh-token hashing that backs
// POST /v1/auth/refresh.
public sealed class LocalAuthenticator
{
    private readonly PasswordHasher<object> _passwordHasher = new();

    public string HashPassword(string plainTextPassword) =>
        _passwordHasher.HashPassword(default!, plainTextPassword);

    public bool VerifyPassword(string storedHash, string providedPassword) =>
        _passwordHasher.VerifyHashedPassword(default!, storedHash, providedPassword) != PasswordVerificationResult.Failed;

    public static string GenerateTotpSecret()
    {
        var key = KeyGeneration.GenerateRandomKey(20); // 160-bit, RFC 4226/6238 recommended minimum
        return Base32Encoding.ToString(key);
    }

    public bool VerifyTotpCode(string totpSecretBase32, string providedCode)
    {
        var totp = new Totp(Base32Encoding.ToBytes(totpSecretBase32));
        // One-step tolerance each side absorbs normal clock drift between server and
        // authenticator app without meaningfully widening the acceptance window.
        return totp.VerifyTotp(providedCode, out _, new VerificationWindow(previous: 1, future: 1));
    }

    // Both factors must succeed independently — a valid password with a wrong/missing TOTP
    // code (or vice versa) is a failed sign-in, never a partial success.
    public bool Verify(string storedPasswordHash, string providedPassword, string totpSecretBase32, string providedTotpCode)
    {
        return VerifyPassword(storedPasswordHash, providedPassword) && VerifyTotpCode(totpSecretBase32, providedTotpCode);
    }

    // FR-046: an opaque, unguessable refresh token. Only its hash (below) is ever stored;
    // the raw value is returned to the client exactly once, at issuance.
    public static string GenerateRefreshToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    public static string HashRefreshToken(string refreshToken) =>
        Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(refreshToken)));
}
