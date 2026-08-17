using Attribution.Api.Middleware;
using Attribution.Application.Administration;
using Attribution.Domain.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;

namespace Attribution.Api.Controllers;

// FR-039: "System MUST support erasure of an identified visitor's data on request,
// completing the erasure within 30 days of the request being received." No wire contract
// exists for this anywhere in contracts/ (admin-api.md predates this requirement's
// implementation) — this performs the erasure synchronously and returns once complete,
// which trivially satisfies the 30-day bar rather than requiring a queued request/status
// workflow the spec never actually asks for.
[ApiController]
[EnableCors("AdminUi")]
[Route("v1/admin/privacy")]
[Authorize]
public sealed class AdminPrivacyController : ControllerBase
{
    private readonly RetentionService _retentionService;
    private readonly IActorContext _actorContext;
    private readonly IAuditLogger _auditLogger;

    public AdminPrivacyController(RetentionService retentionService, IActorContext actorContext, IAuditLogger auditLogger)
    {
        _retentionService = retentionService;
        _actorContext = actorContext;
        _auditLogger = auditLogger;
    }

    [HttpPost("visitors/{id:guid}/erase")]
    [RequireOperation(Operation.ManagePrivacy)]
    public async Task<IActionResult> EraseVisitor(Guid id)
    {
        var now = DateTimeOffset.UtcNow;
        await _retentionService.EraseVisitorAsync(id, now);

        var actorUserId = _actorContext.ActorUserId ?? "unknown";
        await _auditLogger.RecordAsync(
            "EraseVisitor", "Visitor", id.ToString(), before: null, after: new { erasedBy = actorUserId, completedAt = now });

        return Ok(new { visitor_id = id, completed_at = now });
    }
}
