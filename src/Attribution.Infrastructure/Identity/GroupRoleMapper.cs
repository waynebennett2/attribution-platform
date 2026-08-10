using Attribution.Domain.Identity;

namespace Attribution.Infrastructure.Identity;

// FR-046: maps the identity provider's asserted groups to a platform Role. Falls back to
// the least-privileged role (Analyst) when no configured group matches, rather than
// defaulting to elevated access.
public sealed class GroupRoleMapper
{
    private readonly IReadOnlyDictionary<string, Role> _groupToRole;

    public GroupRoleMapper(IReadOnlyDictionary<string, Role> groupToRole)
    {
        _groupToRole = groupToRole;
    }

    public Role MapGroups(IEnumerable<string> providerGroups)
    {
        foreach (var group in providerGroups)
        {
            if (_groupToRole.TryGetValue(group, out var role))
            {
                return role;
            }
        }

        return Role.Analyst;
    }
}
