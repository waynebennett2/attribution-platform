using Attribution.Infrastructure.Identity;

namespace Attribution.Api.Middleware;

// FR-038: "deny the Integration Service role any interactive administrative or reporting
// access while permitting system-to-system data exchange." RbacPolicy already grants that
// role zero Operations, which alone already blocks every [RequireOperation]-gated admin
// endpoint — this middleware is the explicit, defense-in-depth backstop for that same rule,
// so a future accidental grant addition to RbacPolicy could never silently reopen access.
// Registered after UseAuthentication (the identity_type claim must already be parsed) and
// before UseAuthorization, so this runs before RBAC gets a chance to evaluate anything.
//
// No system-to-system HTTP endpoint exists anywhere in this codebase today — outbound
// publication (Google Ads, GA4) is this platform calling out, not the Integration Service
// calling in — so SystemToSystemPathPrefixes is empty and this denies every authenticated
// request from that identity type. It exists as a named extension point rather than an
// inline empty check so a future inbound system-to-system surface has an obvious place to
// register itself without touching the denial logic.
public sealed class IntegrationServiceAccessMiddleware
{
    private static readonly string[] SystemToSystemPathPrefixes = [];

    private readonly RequestDelegate _next;

    public IntegrationServiceAccessMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var identityType = context.User.FindFirst(JwtTokenIssuer.IdentityTypeClaimType)?.Value;
        if (identityType == "IntegrationService"
            && !SystemToSystemPathPrefixes.Any(prefix => context.Request.Path.StartsWithSegments(prefix)))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        await _next(context);
    }
}
