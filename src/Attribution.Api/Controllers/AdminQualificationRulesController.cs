using Attribution.Api.Contracts;
using Attribution.Api.Middleware;
using Attribution.Application.Administration;
using Attribution.Application.Qualification;
using Attribution.Domain.Calls;
using Attribution.Domain.Identity;
using Attribution.Domain.Qualification;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;

namespace Attribution.Api.Controllers;

// contracts/admin-api.md §Qualification rules. FR-022-FR-024, FR-033.
[ApiController]
[EnableCors("AdminUi")]
[Route("v1/admin/qualification-rules")]
[Authorize]
public sealed class AdminQualificationRulesController : ControllerBase
{
    private readonly IQualificationRuleRepository _ruleRepository;
    private readonly RuleVersioningService _versioningService;
    private readonly IActorContext _actorContext;
    private readonly IAuditLogger _auditLogger;

    public AdminQualificationRulesController(
        IQualificationRuleRepository ruleRepository,
        RuleVersioningService versioningService,
        IActorContext actorContext,
        IAuditLogger auditLogger)
    {
        _ruleRepository = ruleRepository;
        _versioningService = versioningService;
        _actorContext = actorContext;
        _auditLogger = auditLogger;
    }

    [HttpGet]
    [RequireOperation(Operation.ManageRules)]
    public async Task<IActionResult> ListVersions([FromQuery(Name = "scope_type")] string scopeType, [FromQuery(Name = "scope_ref")] string? scopeRef)
    {
        if (!Enum.TryParse<QualificationScopeType>(scopeType, ignoreCase: true, out var parsedScopeType))
        {
            return BadRequest("scope_type must be one of: default, website, campaign.");
        }

        var versions = await _ruleRepository.GetByScopeAsync(parsedScopeType, scopeRef);
        return Ok(versions.OrderBy(v => v.Version).Select(ToDto));
    }

    [HttpPost]
    [RequireOperation(Operation.ManageRules)]
    public async Task<IActionResult> CreateVersion([FromBody] CreateQualificationRuleRequestDto request)
    {
        if (!Enum.TryParse<QualificationScopeType>(request.ScopeType, ignoreCase: true, out var scopeType))
        {
            return BadRequest("scope_type must be one of: default, website, campaign.");
        }

        if (scopeType != QualificationScopeType.Default && string.IsNullOrWhiteSpace(request.ScopeRef))
        {
            return BadRequest("scope_ref is required for a website- or campaign-scoped rule.");
        }

        CallDirection? direction = null;
        if (!string.IsNullOrWhiteSpace(request.Conditions.Direction))
        {
            if (!Enum.TryParse<CallDirection>(request.Conditions.Direction, ignoreCase: true, out var parsedDirection))
            {
                return BadRequest("conditions.direction must be one of: inbound, outbound.");
            }

            direction = parsedDirection;
        }

        TimeOfDayWindow? timeOfDay = null;
        if (request.Conditions.TimeOfDay is not null)
        {
            if (!TimeOnly.TryParse(request.Conditions.TimeOfDay.Start, out var start)
                || !TimeOnly.TryParse(request.Conditions.TimeOfDay.End, out var end))
            {
                return BadRequest("conditions.time_of_day.start/end must be valid times (e.g. \"09:00\").");
            }

            timeOfDay = new TimeOfDayWindow(start, end);
        }

        var conditions = new QualificationConditions(
            direction, request.Conditions.AnsweredRequired, request.Conditions.MinConnectedDurationSeconds, timeOfDay);

        var actorUserId = _actorContext.ActorUserId ?? "unknown";
        try
        {
            var rule = await _versioningService.CreateVersionAsync(
                scopeType, request.ScopeRef, conditions, request.EffectiveStart, actorUserId, DateTimeOffset.UtcNow);

            await _auditLogger.RecordAsync("CreateQualificationRuleVersion", "QualificationRule", rule.Id.ToString(), before: null, after: ToDto(rule));

            return CreatedAtAction(nameof(ListVersions), new { scope_type = request.ScopeType, scope_ref = request.ScopeRef }, ToDto(rule));
        }
        catch (QualificationRuleContiguityException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    // FR-024's "never alter... already judged": only a not-yet-effective future version
    // may be deleted (a live/past version cannot be).
    [HttpDelete("{id:guid}")]
    [RequireOperation(Operation.ManageRules)]
    public async Task<IActionResult> DeleteFutureVersion(Guid id)
    {
        var rule = await _ruleRepository.GetByIdAsync(id);
        if (rule is null)
        {
            return NotFound();
        }

        try
        {
            await _versioningService.DeleteFutureVersionAsync(id, DateTimeOffset.UtcNow);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }

        await _auditLogger.RecordAsync("DeleteQualificationRuleVersion", "QualificationRule", id.ToString(), before: ToDto(rule), after: null);

        return NoContent();
    }

    private static object ToDto(QualificationRule rule) => new
    {
        id = rule.Id,
        scope_type = rule.ScopeType.ToString(),
        scope_ref = rule.ScopeRef,
        version = rule.Version,
        conditions = new
        {
            direction = rule.Conditions.RequiredDirection?.ToString(),
            answered_required = rule.Conditions.AnsweredRequired,
            min_connected_duration_seconds = rule.Conditions.MinConnectedDurationSeconds,
            time_of_day = rule.Conditions.TimeOfDay is { } window
                ? new { start = window.StartLocal.ToString("HH:mm"), end = window.EndLocal.ToString("HH:mm") }
                : null,
        },
        effective_start = rule.EffectiveStart,
        effective_end = rule.EffectiveEnd,
        created_by = rule.CreatedBy,
        created_at = rule.CreatedAt,
    };
}
