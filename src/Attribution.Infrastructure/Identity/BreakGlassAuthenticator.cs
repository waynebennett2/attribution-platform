using Microsoft.AspNetCore.Identity;
using OtpNet;

namespace Attribution.Infrastructure.Identity;

// FR-046: break-glass accounts (local credentials + TOTP MFA), used only when the
// identity provider is unreachable or misconfigured. Every sign-in via this path is
// audited by the caller as an exceptional event.
public sealed class BreakGlassAuthenticator
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
}
