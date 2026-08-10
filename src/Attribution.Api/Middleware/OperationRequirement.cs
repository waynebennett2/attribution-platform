using Attribution.Domain.Identity;
using Microsoft.AspNetCore.Authorization;

namespace Attribution.Api.Middleware;

// FR-038: role-based authorization enforced on every operation. Each Operation gets its
// own ASP.NET Core authorization policy (registered in Program.cs), evaluated here
// against Attribution.Domain.Identity.RbacPolicy — the single source of truth for who
// can do what.
public sealed class OperationRequirement : IAuthorizationRequirement
{
    public Operation Operation { get; }

    public OperationRequirement(Operation operation)
    {
        Operation = operation;
    }
}
