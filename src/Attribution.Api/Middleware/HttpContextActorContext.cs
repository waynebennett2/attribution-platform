using System.IdentityModel.Tokens.Jwt;
using Attribution.Application.Administration;

namespace Attribution.Api.Middleware;

public sealed class HttpContextActorContext : IActorContext
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public HttpContextActorContext(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public string? ActorUserId =>
        _httpContextAccessor.HttpContext?.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
}
