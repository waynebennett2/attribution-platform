using Attribution.Domain.Identity;
using Microsoft.AspNetCore.Authorization;

namespace Attribution.Api.Middleware;

// Usage: [RequireOperation(Operation.ManagePools)] on a controller action.
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public sealed class RequireOperationAttribute : AuthorizeAttribute
{
    public RequireOperationAttribute(Operation operation) : base(policy: operation.ToString()) { }
}
