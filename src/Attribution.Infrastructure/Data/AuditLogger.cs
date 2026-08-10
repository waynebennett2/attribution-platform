using System.Text.Json;
using Attribution.Application.Administration;
using Attribution.Domain.Audit;

namespace Attribution.Infrastructure.Data;

public sealed class AuditLogger : IAuditLogger
{
    private const string SystemActor = "system"; // e.g. worker-initiated actions, no interactive user

    private readonly IAuditRepository _auditRepository;
    private readonly IActorContext _actorContext;

    public AuditLogger(IAuditRepository auditRepository, IActorContext actorContext)
    {
        _auditRepository = auditRepository;
        _actorContext = actorContext;
    }

    public async Task RecordAsync(string action, string targetType, string targetId, object? before, object? after)
    {
        var entry = AuditEntry.Create(
            actorUserId: _actorContext.ActorUserId ?? SystemActor,
            action: action,
            targetType: targetType,
            targetId: targetId,
            beforeValue: before is null ? null : JsonSerializer.Serialize(before),
            afterValue: after is null ? "{}" : JsonSerializer.Serialize(after));

        await _auditRepository.AddAsync(entry);
    }
}
