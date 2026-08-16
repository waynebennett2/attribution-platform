using Attribution.Api.Middleware;
using Attribution.Application.Administration;
using Attribution.Application.Attribution;
using Attribution.Domain.Audit;
using Attribution.Domain.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Attribution.Api.Controllers;

// contracts/admin-api.md §Manual review. FR-036.
[ApiController]
[Route("v1/admin/review-cases")]
[Authorize]
public sealed class AdminReviewController : ControllerBase
{
    private readonly IReviewCaseRepository _reviewCaseRepository;
    private readonly ReviewResolutionService _resolutionService;
    private readonly IActorContext _actorContext;
    private readonly IAuditLogger _auditLogger;
    private readonly AlertingThresholds _thresholds;

    public AdminReviewController(
        IReviewCaseRepository reviewCaseRepository,
        ReviewResolutionService resolutionService,
        IActorContext actorContext,
        IAuditLogger auditLogger,
        AlertingThresholds thresholds)
    {
        _reviewCaseRepository = reviewCaseRepository;
        _resolutionService = resolutionService;
        _actorContext = actorContext;
        _auditLogger = auditLogger;
        _thresholds = thresholds;
    }

    [HttpGet]
    [RequireOperation(Operation.ManualReview)]
    public async Task<IActionResult> ListOpen()
    {
        var cases = await _reviewCaseRepository.GetOpenAsync();
        var now = DateTimeOffset.UtcNow;
        return Ok(cases.Select(c => ToDto(c, now)));
    }

    // "{ session_id }" (attribute to that session) or "{ confirm_unattributed: true }"
    // (confirm the call stays unattributed) — exactly one of the two, per admin-api.md.
    [HttpPost("{id:guid}/resolve")]
    [RequireOperation(Operation.ManualReview)]
    public async Task<IActionResult> Resolve(Guid id, [FromBody] ResolveReviewCaseRequest request)
    {
        Guid? sessionId = null;
        if (!string.IsNullOrWhiteSpace(request.SessionId))
        {
            if (!Guid.TryParse(request.SessionId, out var parsed))
            {
                return BadRequest("session_id must be a valid GUID.");
            }

            sessionId = parsed;
        }
        else if (request.ConfirmUnattributed != true)
        {
            return BadRequest("Provide either session_id or confirm_unattributed: true.");
        }

        var actorUserId = _actorContext.ActorUserId ?? "unknown";
        try
        {
            var attribution = await _resolutionService.ResolveAsync(id, sessionId, actorUserId, DateTimeOffset.UtcNow);

            await _auditLogger.RecordAsync(
                "ResolveReviewCase", "ReviewCase", id.ToString(),
                before: new { status = "Open" },
                after: new { status = "Resolved", resolvedBy = actorUserId, attributionId = attribution.Id, sessionId });

            return Ok(new { attribution_id = attribution.Id, state = attribution.State.ToString() });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    private object ToDto(ReviewCase reviewCase, DateTimeOffset now)
    {
        var age = now - reviewCase.OpenedAt;
        return new
        {
            id = reviewCase.Id,
            call_id = reviewCase.CallId,
            attribution_id = reviewCase.AttributionId,
            status = reviewCase.Status.ToString(),
            opened_at = reviewCase.OpenedAt,
            age_seconds = age.TotalSeconds,
            past_age_threshold = age >= _thresholds.ReviewCaseAge,
        };
    }
}

public sealed record ResolveReviewCaseRequest(
    [property: System.Text.Json.Serialization.JsonPropertyName("session_id")] string? SessionId,
    [property: System.Text.Json.Serialization.JsonPropertyName("confirm_unattributed")] bool? ConfirmUnattributed);
