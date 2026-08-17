namespace Attribution.Domain.Identity;

// FR-046: "the system MUST refuse an action that would leave zero active System
// Administrator accounts" — a pure rule shared by deactivating a user and changing a
// user's role away from System Administrator, kept independent of the repository so it can
// be tested without depending on how many such accounts happen to exist in a shared database.
public static class SystemAdministratorGuard
{
    public static bool WouldRemoveLastActiveSystemAdministrator(Role currentEffectiveRole, int activeSystemAdministratorCount) =>
        currentEffectiveRole == Role.SystemAdministrator && activeSystemAdministratorCount <= 1;
}
