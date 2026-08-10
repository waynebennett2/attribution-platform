namespace Attribution.Domain.Identity;

// FR-038: role-based authorization enforced on every operation. Pure decision logic —
// no I/O, no framework dependency — so it is directly unit-testable per Constitution
// Principle V and reusable from both the RBAC middleware and any future admin surface.
public static class RbacPolicy
{
    private static readonly IReadOnlyDictionary<Role, IReadOnlySet<Operation>> Grants =
        new Dictionary<Role, IReadOnlySet<Operation>>
        {
            [Role.SystemAdministrator] = new HashSet<Operation>(Enum.GetValues<Operation>()),
            [Role.MarketingAdministrator] = new HashSet<Operation>
            {
                Operation.ManageRules,
                Operation.ViewReports,
                Operation.ExportReports,
                Operation.ManualReview,
                Operation.ViewIntegrationHealth,
                Operation.AcknowledgeAlerts,
                Operation.ViewAuditLog,
            },
            [Role.Analyst] = new HashSet<Operation> { Operation.ViewReports, Operation.ExportReports },
            // FR-038: Integration Service is barred from interactive access entirely, regardless
            // of operation — it authenticates system-to-system and never reaches this policy in
            // practice, but the grant set here is intentionally empty as a defensive default.
            [Role.IntegrationService] = new HashSet<Operation>(),
        };

    public static bool IsAllowed(Role role, Operation operation) =>
        Grants.TryGetValue(role, out var allowed) && allowed.Contains(operation);
}
