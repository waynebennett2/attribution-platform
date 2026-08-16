using Attribution.Application.Administration;

namespace Attribution.Infrastructure.Data;

// For hosts with no interactive HTTP request to derive an actor from (Attribution.Workers)
// — AuditLogger already falls back to its "system" sentinel whenever ActorUserId is null.
public sealed class SystemActorContext : IActorContext
{
    public string? ActorUserId => null;
}
