namespace Attribution.Domain.Identity;

// FR-032, FR-046: an operator of the platform and the permissions granted to them.
public class User
{
    public Guid Id { get; private set; } = Guid.NewGuid();

    // Null for break-glass and integration-service identities, which are local to the platform.
    public string? SubjectRef { get; private set; }

    public string? Username { get; private set; }
    public string? ClientId { get; private set; }

    public IdentityType IdentityType { get; private set; }
    public Role MappedRole { get; private set; }
    public Role? RoleOverride { get; private set; }
    public string? RoleOverriddenBy { get; private set; }
    public bool MfaRequired { get; private set; }
    public bool IsActive { get; private set; } = true;
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastSeenAt { get; private set; }

    // FR-046: an administrator override takes precedence over the provider-mapped role.
    public Role EffectiveRole => RoleOverride ?? MappedRole;

    private User() { }

    public static User CreateFederated(string subjectRef, Role mappedRole)
    {
        if (string.IsNullOrWhiteSpace(subjectRef))
        {
            throw new ArgumentException("Federated users must have a subject reference.", nameof(subjectRef));
        }

        return new User
        {
            SubjectRef = subjectRef,
            IdentityType = IdentityType.Federated,
            MappedRole = mappedRole,
            MfaRequired = false, // MFA, if any, is enforced by the identity provider for this path.
        };
    }

    public static User CreateBreakGlass(string username, Role mappedRole)
    {
        if (string.IsNullOrWhiteSpace(username))
        {
            throw new ArgumentException("Break-glass users must have a username.", nameof(username));
        }

        return new User
        {
            Username = username,
            IdentityType = IdentityType.BreakGlass,
            MappedRole = mappedRole,
            MfaRequired = true, // FR-046: break-glass accounts are always MFA-protected.
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

    // FR-046: role overrides are recorded (who, and what changed) so they can be audited by the caller.
    public void ApplyRoleOverride(Role newRole, string overriddenBy)
    {
        if (string.IsNullOrWhiteSpace(overriddenBy))
        {
            throw new ArgumentException("A role override must record who made it.", nameof(overriddenBy));
        }

        RoleOverride = newRole;
        RoleOverriddenBy = overriddenBy;
    }

    // FR-038: the Integration Service role is denied any interactive sign-in.
    public bool CanSignInInteractively() => IdentityType != IdentityType.IntegrationService;

    public void Deactivate() => IsActive = false;

    public void RecordActivity(DateTimeOffset at) => LastSeenAt = at;

    // Infrastructure-only reconstruction from stored state (see AssemblyInfo.cs).
    internal static User Rehydrate(
        Guid id,
        string? subjectRef,
        string? username,
        string? clientId,
        IdentityType identityType,
        Role mappedRole,
        Role? roleOverride,
        string? roleOverriddenBy,
        bool mfaRequired,
        bool isActive,
        DateTimeOffset createdAt,
        DateTimeOffset? lastSeenAt) => new()
        {
            Id = id,
            SubjectRef = subjectRef,
            Username = username,
            ClientId = clientId,
            IdentityType = identityType,
            MappedRole = mappedRole,
            RoleOverride = roleOverride,
            RoleOverriddenBy = roleOverriddenBy,
            MfaRequired = mfaRequired,
            IsActive = isActive,
            CreatedAt = createdAt,
            LastSeenAt = lastSeenAt,
        };
}
