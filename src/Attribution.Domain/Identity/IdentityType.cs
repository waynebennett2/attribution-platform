namespace Attribution.Domain.Identity;

// FR-046: how a User authenticates. Local is the platform's sole interactive path
// (username/password + mandatory TOTP MFA); IntegrationService is the one exception,
// authenticated system-to-system via API key rather than interactively.
public enum IdentityType
{
    Local,
    IntegrationService,
}
