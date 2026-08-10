namespace Attribution.Domain.Identity;

// The set of RBAC-gated operations the platform exposes (FR-038).
public enum Operation
{
    ManageUsers,
    ManagePools,
    ManageNumbers,
    ManageRules,
    ViewReports,
    ExportReports,
    ManualReview,
    ViewIntegrationHealth,
    AcknowledgeAlerts,
    ViewAuditLog,
}
