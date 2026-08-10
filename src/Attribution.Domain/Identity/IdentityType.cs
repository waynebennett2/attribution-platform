namespace Attribution.Domain.Identity;

// FR-046: how a User authenticates. Federated is the normal path (customer's identity
// provider); BreakGlass and IntegrationService are the two documented exceptions.
public enum IdentityType
{
    Federated,
    BreakGlass,
    IntegrationService,
}
