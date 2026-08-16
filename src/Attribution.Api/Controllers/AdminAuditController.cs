using Attribution.Api.Middleware;
using Attribution.Domain.Audit;
using Attribution.Domain.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Attribution.Api.Controllers;

// contracts/admin-api.md §Audit log. FR-035. "read-only; no PUT/PATCH/DELETE exists on
// this resource by design" — matching IAuditRepository, which likewise exposes no
// update/delete method (see AuditImmutabilityTests).
[ApiController]
[Route("v1/admin/audit")]
[Authorize]
public sealed class AdminAuditController : ControllerBase
{
    private readonly IAuditRepository _auditRepository;

    public AdminAuditController(IAuditRepository auditRepository)
    {
        _auditRepository = auditRepository;
    }

    [HttpGet]
    [RequireOperation(Operation.ViewAuditLog)]
    public async Task<IActionResult> Query(
        [FromQuery(Name = "target_type")] string? targetType,
        [FromQuery(Name = "target_id")] string? targetId,
        [FromQuery] string? from,
        [FromQuery] string? to)
    {
        DateTimeOffset? parsedFrom = null;
        if (from is not null)
        {
            if (!DateTimeOffset.TryParse(from, out var f))
            {
                return BadRequest("from must be a valid timestamp.");
            }

            parsedFrom = f;
        }

        DateTimeOffset? parsedTo = null;
        if (to is not null)
        {
            if (!DateTimeOffset.TryParse(to, out var t))
            {
                return BadRequest("to must be a valid timestamp.");
            }

            parsedTo = t;
        }

        var entries = await _auditRepository.QueryAsync(targetType, targetId, parsedFrom, parsedTo);
        return Ok(entries.Select(ToDto));
    }

    private static object ToDto(AuditEntry entry) => new
    {
        id = entry.Id,
        actor_user_id = entry.ActorUserId,
        action = entry.Action,
        target_type = entry.TargetType,
        target_id = entry.TargetId,
        before_value = entry.BeforeValue,
        after_value = entry.AfterValue,
        occurred_at = entry.OccurredAt,
    };
}
