using System.Collections.Concurrent;
using Attribution.Domain.RateLimiting;

namespace Attribution.Api.Middleware;

// FR-037: rate limits the visitor-facing DNI endpoints (which cannot be authenticated,
// since they can't hold a secret) per origin and per client. In-process fixed-window
// counting (research.md §11) — adequate at the platform's stated scale; a shared-store
// upgrade is the documented first step if horizontal scale-out later causes meaningful
// skew between instances.
public sealed class RateLimitingMiddleware
{
    // Clients send this alongside the JSON body's client_token so the middleware can
    // rate-limit without buffering/parsing the request body on every call.
    public const string ClientTokenHeader = "X-Attribution-Client-Token";

    private readonly RequestDelegate _next;
    private readonly ConcurrentDictionary<string, (DateTimeOffset WindowStart, int Count)> _counters = new();

    public RateLimitingMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!context.Request.Path.StartsWithSegments("/v1/dni"))
        {
            await _next(context);
            return;
        }

        var origin = context.Request.Headers.Origin.ToString();
        var clientToken = context.Request.Headers[ClientTokenHeader].ToString();

        var originAllowed = string.IsNullOrEmpty(origin) || TryConsume($"origin:{origin}", RateLimitPolicy.DefaultPerOrigin);
        var clientAllowed = string.IsNullOrEmpty(clientToken) || TryConsume($"client:{clientToken}", RateLimitPolicy.DefaultPerClient);

        if (!originAllowed || !clientAllowed)
        {
            context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            return;
        }

        await _next(context);
    }

    private bool TryConsume(string key, RateLimitRule rule)
    {
        var now = DateTimeOffset.UtcNow;
        var windowStart = RateLimitPolicy.WindowStart(now, rule.Window);

        var updated = _counters.AddOrUpdate(
            key,
            addValueFactory: _ => (windowStart, 1),
            updateValueFactory: (_, existing) => existing.WindowStart == windowStart
                ? (existing.WindowStart, existing.Count + 1)
                : (windowStart, 1));

        // updated.Count already includes this request, so compare the count *before* it.
        return RateLimitPolicy.IsAllowed(updated.Count - 1, rule);
    }
}
