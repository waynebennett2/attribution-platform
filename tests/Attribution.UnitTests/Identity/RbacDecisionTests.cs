using Attribution.Domain.Identity;
using Xunit;

namespace Attribution.UnitTests.Identity;

// FR-038: role-based authorization on every operation.
public class RbacDecisionTests
{
    [Theory]
    [InlineData(Operation.ManageUsers)]
    [InlineData(Operation.ManagePools)]
    [InlineData(Operation.ManageNumbers)]
    [InlineData(Operation.ManageRules)]
    [InlineData(Operation.ViewReports)]
    [InlineData(Operation.ExportReports)]
    [InlineData(Operation.ManualReview)]
    [InlineData(Operation.ViewIntegrationHealth)]
    [InlineData(Operation.AcknowledgeAlerts)]
    [InlineData(Operation.ViewAuditLog)]
    public void SystemAdministrator_IsAllowedEveryOperation(Operation operation)
    {
        Assert.True(RbacPolicy.IsAllowed(Role.SystemAdministrator, operation));
    }

    [Theory]
    [InlineData(Operation.ManageRules, true)]
    [InlineData(Operation.ViewReports, true)]
    [InlineData(Operation.ExportReports, true)]
    [InlineData(Operation.ManualReview, true)]
    [InlineData(Operation.ViewIntegrationHealth, true)]
    [InlineData(Operation.AcknowledgeAlerts, true)]
    [InlineData(Operation.ViewAuditLog, true)]
    [InlineData(Operation.ManageUsers, false)]
    [InlineData(Operation.ManagePools, false)]
    [InlineData(Operation.ManageNumbers, false)]
    public void MarketingAdministrator_MatchesPolicy(Operation operation, bool expected)
    {
        Assert.Equal(expected, RbacPolicy.IsAllowed(Role.MarketingAdministrator, operation));
    }

    [Theory]
    [InlineData(Operation.ViewReports, true)]
    [InlineData(Operation.ExportReports, true)]
    [InlineData(Operation.ManagePools, false)]
    [InlineData(Operation.ManageRules, false)]
    [InlineData(Operation.ManageUsers, false)]
    [InlineData(Operation.ViewAuditLog, false)]
    [InlineData(Operation.ManualReview, false)]
    public void Analyst_MatchesPolicy(Operation operation, bool expected)
    {
        Assert.Equal(expected, RbacPolicy.IsAllowed(Role.Analyst, operation));
    }

    // FR-038: Integration Service is denied interactive admin/reporting access regardless of operation.
    [Theory]
    [InlineData(Operation.ViewReports)]
    [InlineData(Operation.ManageUsers)]
    [InlineData(Operation.ManagePools)]
    [InlineData(Operation.ViewAuditLog)]
    public void IntegrationService_NeverAllowedInteractively(Operation operation)
    {
        Assert.False(RbacPolicy.IsAllowed(Role.IntegrationService, operation));
    }
}
