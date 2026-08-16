using System;
using Attribution.Domain.Identity;
using Xunit;

namespace Attribution.UnitTests.Identity;

// FR-032, FR-046: user/role assignment and administrator role-override recording.
public class UserRoleTests
{
    [Fact]
    public void FederatedUser_EffectiveRole_IsMappedRole_WhenNoOverride()
    {
        var user = User.CreateFederated(subjectRef: "idp-subject-1", mappedRole: Role.Analyst);

        Assert.Equal(Role.Analyst, user.EffectiveRole);
        Assert.Equal(IdentityType.Federated, user.IdentityType);
        Assert.Null(user.RoleOverride);
    }

    [Fact]
    public void EffectiveRole_IsOverride_WhenAdministratorHasSetOne()
    {
        var user = User.CreateFederated(subjectRef: "idp-subject-2", mappedRole: Role.Analyst);

        user.ApplyRoleOverride(Role.MarketingAdministrator, overriddenBy: "sysadmin-1");

        Assert.Equal(Role.MarketingAdministrator, user.EffectiveRole);
        Assert.Equal(Role.MarketingAdministrator, user.RoleOverride);
        Assert.Equal("sysadmin-1", user.RoleOverriddenBy);
    }

    [Fact]
    public void BreakGlassUser_HasNoSubjectRef_AndRequiresMfa()
    {
        var user = User.CreateBreakGlass(
            username: "breakglass-primary", mappedRole: Role.SystemAdministrator,
            passwordHash: "hash", totpSecret: "secret");

        Assert.Equal(IdentityType.BreakGlass, user.IdentityType);
        Assert.Null(user.SubjectRef);
        Assert.True(user.MfaRequired);
    }

    [Fact]
    public void IntegrationServiceUser_IsNeverInteractive()
    {
        var user = User.CreateIntegrationService(clientId: "svc-8x8-ingest");

        Assert.Equal(IdentityType.IntegrationService, user.IdentityType);
        Assert.Equal(Role.IntegrationService, user.EffectiveRole);
        Assert.False(user.CanSignInInteractively());
    }

    [Fact]
    public void FederatedUser_CanSignInInteractively()
    {
        var user = User.CreateFederated(subjectRef: "idp-subject-3", mappedRole: Role.Analyst);

        Assert.True(user.CanSignInInteractively());
    }

    [Fact]
    public void ApplyRoleOverride_Throws_WhenOverriddenByIsEmpty()
    {
        var user = User.CreateFederated(subjectRef: "idp-subject-4", mappedRole: Role.Analyst);

        Assert.Throws<ArgumentException>(() => user.ApplyRoleOverride(Role.SystemAdministrator, overriddenBy: ""));
    }
}
