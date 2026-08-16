using Attribution.Application.Administration;

namespace Attribution.Api.Middleware;

// spec.md User Story 4, Acceptance Scenario 3: "access is refused and the attempt is
// recorded" — a refused request needs the same durable audit trail a successful one gets,
// not just an ambient log line. Registered before authentication/authorization in
// Program.cs's pipeline so control returns here (with the response status already set by
// AuthorizationMiddleware) even though that middleware short-circuited everything after
// it — an ASP.NET Core middleware's code after `await next()` still runs regardless of
// what happened deeper in the pipeline. Scoped only to 403: a 401 (no/invalid token) has
// no authenticated actor to attribute the attempt to, and RBAC refusal is what FR-038 and
// this scenario are actually about.
public sealed class AuthorizationFailureAuditMiddleware
{
    private readonly RequestDelegate _next;

    public AuthorizationFailureAuditMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, IAuditLogger auditLogger, IActorContext actorContext)
    {
        await _next(context);

        if (context.Response.StatusCode == StatusCodes.Status403Forbidden && actorContext.ActorUserId is not null)
        {
            await auditLogger.RecordAsync(
                "AccessRefused",
                "Endpoint",
                context.Request.Path.Value ?? string.Empty,
                before: null,
                after: new { context.Request.Method });
        }
    }
}
