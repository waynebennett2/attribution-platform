using Attribution.Domain.Identity;
using Xunit;

namespace Attribution.UnitTests.Identity;

// FR-046: "the system MUST refuse an action that would leave zero active System
// Administrator accounts."
public class SystemAdministratorGuardTests
{
    [Fact]
    public void RefusesRemoval_WhenTargetIsSystemAdministrator_AndOnlyOneActiveRemains()
    {
        Assert.True(SystemAdministratorGuard.WouldRemoveLastActiveSystemAdministrator(Role.SystemAdministrator, activeSystemAdministratorCount: 1));
    }

    [Fact]
    public void AllowsRemoval_WhenAnotherActiveSystemAdministratorStillExists()
    {
        Assert.False(SystemAdministratorGuard.WouldRemoveLastActiveSystemAdministrator(Role.SystemAdministrator, activeSystemAdministratorCount: 2));
    }

    [Theory]
    [InlineData(Role.MarketingAdministrator)]
    [InlineData(Role.Analyst)]
    [InlineData(Role.IntegrationService)]
    public void AllowsRemoval_WhenTargetIsNotASystemAdministrator_RegardlessOfCount(Role role)
    {
        Assert.False(SystemAdministratorGuard.WouldRemoveLastActiveSystemAdministrator(role, activeSystemAdministratorCount: 1));
    }
}
