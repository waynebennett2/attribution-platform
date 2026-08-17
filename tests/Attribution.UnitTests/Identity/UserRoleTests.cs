using System;
using Attribution.Domain.Identity;
using Xunit;

namespace Attribution.UnitTests.Identity;

// FR-032, FR-046: local username/password + TOTP account provisioning, role assignment
// and role-change recording.
public class UserRoleTests
{
    [Fact]
    public void LocalUser_EffectiveRole_IsMappedRole_WhenNoOverride()
    {
        var user = User.CreateLocal("analyst-1", Role.Analyst, passwordHash: "hash", totpSecret: "secret");

        Assert.Equal(Role.Analyst, user.EffectiveRole);
        Assert.Equal(IdentityType.Local, user.IdentityType);
        Assert.Null(user.RoleOverride);
    }

    [Fact]
    public void EffectiveRole_IsOverride_WhenAdministratorHasSetOne()
    {
        var user = User.CreateLocal("analyst-2", Role.Analyst, passwordHash: "hash", totpSecret: "secret");

        user.ApplyRoleOverride(Role.MarketingAdministrator, overriddenBy: "sysadmin-1");

        Assert.Equal(Role.MarketingAdministrator, user.EffectiveRole);
        Assert.Equal(Role.MarketingAdministrator, user.RoleOverride);
        Assert.Equal("sysadmin-1", user.RoleOverriddenBy);
    }

    [Fact]
    public void LocalUser_RequiresMfa()
    {
        var user = User.CreateLocal(
            username: "local-primary", mappedRole: Role.SystemAdministrator,
            passwordHash: "hash", totpSecret: "secret");

        Assert.Equal(IdentityType.Local, user.IdentityType);
        Assert.True(user.MfaRequired);
    }

    [Fact]
    public void CreateLocal_Throws_WhenPasswordHashOrTotpSecretMissing()
    {
        Assert.Throws<ArgumentException>(() => User.CreateLocal("no-mfa", Role.Analyst, passwordHash: "", totpSecret: "secret"));
        Assert.Throws<ArgumentException>(() => User.CreateLocal("no-mfa", Role.Analyst, passwordHash: "hash", totpSecret: ""));
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
    public void LocalUser_CanSignInInteractively()
    {
        var user = User.CreateLocal("analyst-3", Role.Analyst, passwordHash: "hash", totpSecret: "secret");

        Assert.True(user.CanSignInInteractively());
    }

    [Fact]
    public void ApplyRoleOverride_Throws_WhenOverriddenByIsEmpty()
    {
        var user = User.CreateLocal("analyst-4", Role.Analyst, passwordHash: "hash", totpSecret: "secret");

        Assert.Throws<ArgumentException>(() => user.ApplyRoleOverride(Role.SystemAdministrator, overriddenBy: ""));
    }

    [Fact]
    public void Deactivate_ClearsRefreshToken()
    {
        var user = User.CreateLocal("analyst-5", Role.Analyst, passwordHash: "hash", totpSecret: "secret");
        user.IssueRefreshToken("hashed-token", DateTimeOffset.UtcNow.AddHours(12));

        user.Deactivate();

        Assert.False(user.IsActive);
        Assert.Null(user.RefreshTokenHash);
        Assert.Null(user.RefreshTokenExpiresAt);
    }
}
