using Attribution.Api.Contracts;
using Attribution.Application.Allocation;
using Attribution.Domain.Sessions;
using Attribution.Domain.Websites;
using Microsoft.AspNetCore.Mvc;

namespace Attribution.Api.Controllers;

// contracts/dni-api.md. Unauthenticated by design (FR-037) — origin-restricted and
// rate-limited instead (RateLimitingMiddleware).
[ApiController]
[Route("v1/dni")]
public sealed class DniController : ControllerBase
{
    private readonly AllocationService _allocationService;
    private readonly ShadowAllocationService _shadowAllocationService;
    private readonly IWebsiteRepository _websiteRepository;
    private readonly ILogger<DniController> _logger;

    public DniController(
        AllocationService allocationService,
        ShadowAllocationService shadowAllocationService,
        IWebsiteRepository websiteRepository,
        ILogger<DniController> logger)
    {
        _allocationService = allocationService;
        _shadowAllocationService = shadowAllocationService;
        _websiteRepository = websiteRepository;
        _logger = logger;
    }

    [HttpPost("allocate")]
    public async Task<ActionResult<AllocateResponseDto>> Allocate([FromBody] AllocateRequestDto request)
    {
        if (!Guid.TryParse(request.WebsiteId, out var websiteId))
        {
            return NotFound();
        }

        // FR-037: origins are restricted to those configured for the website.
        var origin = Request.Headers.Origin.ToString();
        if (!await IsOriginPermittedAsync(websiteId, origin))
        {
            return Forbid();
        }

        var arrival = ToArrivalDetails(request);
        var matchedPoolIds = ParsePoolIds(request.MatchedPoolIds);
        Guid.TryParse(request.SessionId, out var existingSessionId);
        var result = await _allocationService.AllocateAsync(
            websiteId, request.ConsentGranted, arrival, DateTimeOffset.UtcNow,
            matchedPoolIds: matchedPoolIds,
            existingSessionId: request.SessionId is null ? null : existingSessionId);
        LogAllocationOutcome(websiteId, result);

        return Ok(ToDto(result));
    }

    // FR-041: the allocation-stage log line in the allocation -> attribution ->
    // qualification -> publication trace. SessionId is the join key a later
    // IngestionService log line for the same call will also carry (via Attribution.SessionId),
    // since no Call — and therefore no CallId — exists yet at allocation time.
    private void LogAllocationOutcome(Guid websiteId, AllocateResult result)
    {
        if (result.SessionId is null)
        {
            _logger.LogInformation("Allocation fell back to the default number for website {WebsiteId}: {Reason}", websiteId, result.Reason);
            return;
        }

        _logger.LogInformation(
            "Allocated {Number} to session {SessionId} for website {WebsiteId}", result.Number, result.SessionId, websiteId);
    }

    [HttpPost("heartbeat")]
    public async Task<ActionResult<HeartbeatResponseDto>> Heartbeat([FromBody] HeartbeatRequestDto request)
    {
        if (!Guid.TryParse(request.SessionId, out var sessionId))
        {
            return Ok(new HeartbeatResponseDto { StillValid = false, Number = null });
        }

        var result = await _allocationService.HeartbeatAsync(sessionId, DateTimeOffset.UtcNow);
        return Ok(new HeartbeatResponseDto
        {
            StillValid = result.StillValid,
            Number = result.Number,
            Allocations = result.Allocations?
                .Select(a => new PoolHeartbeatDto { PoolId = a.PoolId.ToString(), StillValid = a.StillValid, Number = a.Number })
                .ToList(),
        });
    }

    [HttpPost("consent")]
    public async Task<ActionResult<AllocateResponseDto>> Consent([FromBody] ConsentRequestDto request)
    {
        if (!Guid.TryParse(request.WebsiteId, out var websiteId))
        {
            return NotFound();
        }

        if (string.Equals(request.Consent, "granted", StringComparison.OrdinalIgnoreCase))
        {
            var arrival = request.ArrivalDetails is null ? ArrivalDetails.Empty : ToArrivalDetails(request.ArrivalDetails);
            // FR-014: if the entry-page landing details are no longer present by the time
            // consent arrives (the visitor navigated away before answering), the session's
            // provenance is recorded as degraded rather than crediting a substitute page.
            var provenance = string.IsNullOrEmpty(arrival.LandingPage) ? SessionProvenance.Degraded : SessionProvenance.Ordinary;
            var result = await _allocationService.AllocateAsync(websiteId, consentGranted: true, arrival, DateTimeOffset.UtcNow, provenance);
            return Ok(ToDto(result));
        }

        if (string.Equals(request.Consent, "withdrawn", StringComparison.OrdinalIgnoreCase))
        {
            if (!Guid.TryParse(request.SessionId, out var sessionId))
            {
                return BadRequest();
            }

            var result = await _allocationService.WithdrawConsentAsync(sessionId, DateTimeOffset.UtcNow);
            return Ok(ToDto(result));
        }

        return BadRequest();
    }

    // FR-049: optional per-website shadow mode. Records the session together with the
    // number another system already displayed, without replacing anything on the page.
    [HttpPost("shadow-observe")]
    public async Task<IActionResult> ShadowObserve([FromBody] ShadowObserveRequestDto request)
    {
        if (!Guid.TryParse(request.WebsiteId, out var websiteId))
        {
            return NotFound();
        }

        var arrival = new ArrivalDetails(
            request.LandingPage, request.Referrer,
            request.Utm?.Source, request.Utm?.Medium, request.Utm?.Campaign, request.Utm?.Term, request.Utm?.Content,
            request.Gclid, request.Gbraid, request.Wbraid, request.Ga4ClientId);

        try
        {
            await _shadowAllocationService.RecordObservationAsync(websiteId, request.ObservedNumber, arrival, DateTimeOffset.UtcNow);
        }
        catch (InvalidOperationException)
        {
            return BadRequest();
        }

        return Ok(new { recorded = true });
    }

    private async Task<bool> IsOriginPermittedAsync(Guid websiteId, string origin)
    {
        if (string.IsNullOrEmpty(origin))
        {
            return true; // same-origin requests (no Origin header) are allowed through.
        }

        var website = await _websiteRepository.GetByIdAsync(websiteId);
        return website is not null
            && website.PermittedOrigins.Contains(origin, StringComparer.OrdinalIgnoreCase);
    }

    private static ArrivalDetails ToArrivalDetails(AllocateRequestDto dto) => new(
        dto.LandingPage, dto.Referrer,
        dto.Utm?.Source, dto.Utm?.Medium, dto.Utm?.Campaign, dto.Utm?.Term, dto.Utm?.Content,
        dto.Gclid, dto.Gbraid, dto.Wbraid, dto.Ga4ClientId);

    // FR-050: a malformed or unparsable id is silently dropped rather than erroring,
    // matching how any other out-of-scope/malformed part of this unauthenticated request
    // is already handled (AllocationService then further drops anything not actually
    // scoped to the requesting website).
    private static List<Guid>? ParsePoolIds(List<string>? poolIds) =>
        poolIds?.Select(id => Guid.TryParse(id, out var parsed) ? parsed : (Guid?)null)
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .ToList();

    private static AllocateResponseDto ToDto(AllocateResult result) => new()
    {
        SessionId = result.SessionId?.ToString(),
        Number = result.Number,
        ExpiresAt = result.ExpiresAt,
        Reason = result.Reason,
        Pools = result.Pools?.Select(p => new PoolNumberDto { PoolId = p.PoolId.ToString(), DefaultNumber = p.DefaultNumber }).ToList(),
        Allocations = result.Allocations?
            .Select(a => new PoolAllocationDto { PoolId = a.PoolId.ToString(), Number = a.Number, ExpiresAt = a.ExpiresAt })
            .ToList(),
    };
}
