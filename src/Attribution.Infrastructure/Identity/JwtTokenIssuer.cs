using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Attribution.Domain.Identity;
using Microsoft.IdentityModel.Tokens;

namespace Attribution.Infrastructure.Identity;

// FR-046: issues the platform's own short-lived JWT once local sign-in succeeds, carrying
// the mapped/overridden role. 5-minute lifetime and refresh margin come from
// Domain.Identity.JwtPolicy — defined once, used everywhere.
public sealed class JwtTokenIssuer : ITokenIssuer
{
    public const string RoleClaimType = "role";
    public const string IdentityTypeClaimType = "identity_type";

    private readonly SymmetricSecurityKey _signingKey;
    private readonly string _issuer;
    private readonly string _audience;

    public JwtTokenIssuer(string signingSecret, string issuer, string audience)
    {
        if (string.IsNullOrWhiteSpace(signingSecret) || signingSecret.Length < 32)
        {
            throw new ArgumentException("JWT signing secret must be at least 32 characters.", nameof(signingSecret));
        }

        _signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingSecret));
        _issuer = issuer;
        _audience = audience;
    }

    public string IssueToken(User user, DateTimeOffset issuedAt)
    {
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(RoleClaimType, user.EffectiveRole.ToString()),
            new Claim(IdentityTypeClaimType, user.IdentityType.ToString()),
        };

        var credentials = new SigningCredentials(_signingKey, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: _issuer,
            audience: _audience,
            claims: claims,
            notBefore: issuedAt.UtcDateTime,
            expires: issuedAt.Add(JwtPolicy.TokenLifetime).UtcDateTime,
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public TokenValidationParameters BuildValidationParameters() => new()
    {
        ValidateIssuer = true,
        ValidIssuer = _issuer,
        ValidateAudience = true,
        ValidAudience = _audience,
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = _signingKey,
        ValidateLifetime = true,
        ClockSkew = TimeSpan.FromSeconds(5), // tokens are already only 5 minutes; keep skew tight
    };
}
