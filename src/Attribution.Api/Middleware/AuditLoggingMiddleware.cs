using Attribution.Application.Administration;

namespace Attribution.Api.Middleware;

// FR-035: enriches every request's structured logs with the acting user (ties into
// T020's observability) and makes the actor available to Application-layer use cases via
// IActorContext, so they can call IAuditLogger.RecordAsync with the actor already
// resolved. This middleware does not itself decide what "changed" — see IAuditLogger's
// doc comment for why that has to be call-site knowledge.
public sealed class AuditLoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<AuditLoggingMiddleware> _logger;

    public AuditLoggingMiddleware(RequestDelegate next, ILogger<AuditLoggingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, IActorContext actorContext)
    {
        using (_logger.BeginScope(new Dictionary<string, object?>
        {
            ["ActorUserId"] = actorContext.ActorUserId,
            ["RequestPath"] = context.Request.Path.Value,
            ["RequestMethod"] = context.Request.Method,
        }))
        {
            await _next(context);
        }
    }
}
