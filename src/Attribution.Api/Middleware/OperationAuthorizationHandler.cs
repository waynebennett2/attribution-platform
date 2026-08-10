using Attribution.Domain.Identity;
using Attribution.Infrastructure.Identity;
using Microsoft.AspNetCore.Authorization;

namespace Attribution.Api.Middleware;

public sealed class OperationAuthorizationHandler : AuthorizationHandler<OperationRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context, OperationRequirement requirement)
    {
        var roleClaim = context.User.FindFirst(JwtTokenIssuer.RoleClaimType)?.Value;
        if (roleClaim is not null
            && Enum.TryParse<Role>(roleClaim, out var role)
            && RbacPolicy.IsAllowed(role, requirement.Operation))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
