namespace Attribution.Application.Administration;

// FR-035: every administrator action is written to an audit log recording actor, action,
// target, before and after values, and timestamp. Application-layer use cases call this
// explicitly at the point of the change — only the use case knows the real semantic
// before/after state, which generic HTTP middleware cannot infer.
public interface IAuditLogger
{
    Task RecordAsync(string action, string targetType, string targetId, object? before, object? after);
}
