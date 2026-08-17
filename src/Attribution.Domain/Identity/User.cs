namespace Attribution.Domain.Identity;

// FR-032, FR-046: an operator of the platform and the permissions granted to them.
public class User
{
    public Guid Id { get; private set; } = Guid.NewGuid();

    public string? Username { get; private set; }
    public string? ClientId { get; private set; }

    public IdentityType IdentityType { get; private set; }
    public Role MappedRole { get; private set; }
    public Role? RoleOverride { get; private set; }
    public string? RoleOverriddenBy { get; private set; }

    // Local accounts only (FR-046); null for IntegrationService, which authenticates via API key.
    public string? PasswordHash { get; private set; }
    public string? TotpSecret { get; private set; }
    public bool MfaRequired { get; private set; }

    // The current rotating refresh token's hash and expiry (FR-046); null when there is
    // no live session (never signed in, signed out, or deactivated). Never store the raw
    // token itself — only a hash, the same discipline as PasswordHash.
    public string? RefreshTokenHash { get; private set; }
    public DateTimeOffset? RefreshTokenExpiresAt { get; private set; }

    public bool IsActive { get; private set; } = true;
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastSeenAt { get; private set; }

    // FR-046: a System Administrator's later change takes precedence over the role assigned at creation.
    public Role EffectiveRole => RoleOverride ?? MappedRole;

    private User() { }

    public static User CreateLocal(string username, Role mappedRole, string passwordHash, string totpSecret)
    {
        if (string.IsNullOrWhiteSpace(username))
        {
            throw new ArgumentException("Local users must have a username.", nameof(username));
        }

        if (string.IsNullOrWhiteSpace(passwordHash) || string.IsNullOrWhiteSpace(totpSecret))
        {
            // FR-046: every local account is always MFA-protected — both factors must be
            // provisioned up front, never added later as an afterthought.
            throw new ArgumentException("Local users must be provisioned with a password hash and a TOTP secret.");
        }

        return new User
        {
            Username = username,
            IdentityType = IdentityType.Local,
            MappedRole = mappedRole,
            PasswordHash = passwordHash,
            TotpSecret = totpSecret,
            MfaRequired = true,
        };
    }

    public static User CreateIntegrationService(string clientId)
    {
        if (string.IsNullOrWhiteSpace(clientId))
        {
            throw new ArgumentException("Integration Service users must have a client id.", nameof(clientId));
        }

        return new User
        {
            ClientId = clientId,
            IdentityType = IdentityType.IntegrationService,
            MappedRole = Role.IntegrationService,
            MfaRequired = false, // Authenticated via API key, not an interactive credential.
        };
    }

    // FR-046: role changes are recorded (who, and what changed) so they can be audited by the caller.
    public void ApplyRoleOverride(Role newRole, string overriddenBy)
    {
        if (string.IsNullOrWhiteSpace(overriddenBy))
        {
            throw new ArgumentException("A role change must record who made it.", nameof(overriddenBy));
        }

        RoleOverride = newRole;
        RoleOverriddenBy = overriddenBy;
    }

    // FR-038: the Integration Service role is denied any interactive sign-in.
    public bool CanSignInInteractively() => IdentityType != IdentityType.IntegrationService;

    public void Deactivate()
    {
        IsActive = false;
        RefreshTokenHash = null;
        RefreshTokenExpiresAt = null;
    }

    // FR-046: issued on sign-in and on every refresh-token exchange; replaces whatever
    // refresh token (if any) preceded it, so a stolen-then-reused old token stops working.
    public void IssueRefreshToken(string refreshTokenHash, DateTimeOffset expiresAt)
    {
        RefreshTokenHash = refreshTokenHash;
        RefreshTokenExpiresAt = expiresAt;
    }

    public void RecordActivity(DateTimeOffset at) => LastSeenAt = at;

    // Infrastructure-only reconstruction from stored state (see AssemblyInfo.cs).
    internal static User Rehydrate(
        Guid id,
        string? username,
        string? clientId,
        IdentityType identityType,
        Role mappedRole,
        Role? roleOverride,
        string? roleOverriddenBy,
        string? passwordHash,
        string? totpSecret,
        bool mfaRequired,
        string? refreshTokenHash,
        DateTimeOffset? refreshTokenExpiresAt,
        bool isActive,
        DateTimeOffset createdAt,
        DateTimeOffset? lastSeenAt) => new()
        {
            Id = id,
            Username = username,
            ClientId = clientId,
            IdentityType = identityType,
            MappedRole = mappedRole,
            RoleOverride = roleOverride,
            RoleOverriddenBy = roleOverriddenBy,
            PasswordHash = passwordHash,
            TotpSecret = totpSecret,
            MfaRequired = mfaRequired,
            RefreshTokenHash = refreshTokenHash,
            RefreshTokenExpiresAt = refreshTokenExpiresAt,
            IsActive = isActive,
            CreatedAt = createdAt,
            LastSeenAt = lastSeenAt,
        };
}
