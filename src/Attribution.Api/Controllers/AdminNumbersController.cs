using Attribution.Api.Contracts;
using Attribution.Api.Middleware;
using Attribution.Application.Administration;
using Attribution.Domain.Identity;
using Attribution.Domain.Pools;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Attribution.Api.Controllers;

// contracts/admin-api.md §Number pools & numbers. FR-004, FR-005: lifecycle transitions
// never touch a session's in-progress allocation — only future eligibility.
[ApiController]
[Route("v1/admin/numbers")]
[Authorize]
public sealed class AdminNumbersController : ControllerBase
{
    private readonly ITrackingNumberRepository _trackingNumberRepository;
    private readonly IAuditLogger _auditLogger;

    public AdminNumbersController(ITrackingNumberRepository trackingNumberRepository, IAuditLogger auditLogger)
    {
        _trackingNumberRepository = trackingNumberRepository;
        _auditLogger = auditLogger;
    }

    [HttpPost("{id:guid}/suspend")]
    [RequireOperation(Operation.ManageNumbers)]
    public Task<IActionResult> Suspend(Guid id) => ChangeStatus(id, "Suspend", n => n.Suspend());

    [HttpPost("{id:guid}/retire")]
    [RequireOperation(Operation.ManageNumbers)]
    public Task<IActionResult> Retire(Guid id) => ChangeStatus(id, "Retire", n => n.Retire());

    [HttpPost("{id:guid}/reactivate")]
    [RequireOperation(Operation.ManageNumbers)]
    public Task<IActionResult> Reactivate(Guid id) => ChangeStatus(id, "Reactivate", n => n.Reactivate());

    // FR-004: a number belongs to exactly one pool at a time; historical allocations keep
    // referencing the pool that held it at the time (Allocation.PoolIdAtAllocation), which
    // this move does not touch.
    [HttpPost("{id:guid}/move")]
    [RequireOperation(Operation.ManageNumbers)]
    public async Task<IActionResult> Move(Guid id, [FromBody] MoveNumberRequestDto request)
    {
        if (!Guid.TryParse(request.TargetPoolId, out var targetPoolId))
        {
            return BadRequest("target_pool_id must be a valid id.");
        }

        var number = await _trackingNumberRepository.GetByIdAsync(id);
        if (number is null)
        {
            return NotFound();
        }

        var before = new { number.PoolId };
        number.MoveToPool(targetPoolId);
        await _trackingNumberRepository.UpdateAsync(number);
        await _auditLogger.RecordAsync("MoveTrackingNumber", "TrackingNumber", id.ToString(), before, new { PoolId = targetPoolId });

        return NoContent();
    }

    private async Task<IActionResult> ChangeStatus(Guid id, string action, Action<TrackingNumber> apply)
    {
        var number = await _trackingNumberRepository.GetByIdAsync(id);
        if (number is null)
        {
            return NotFound();
        }

        var before = new { number.Status };
        apply(number);
        await _trackingNumberRepository.UpdateAsync(number);
        await _auditLogger.RecordAsync(action + "TrackingNumber", "TrackingNumber", id.ToString(), before, new { number.Status });

        return NoContent();
    }
}
