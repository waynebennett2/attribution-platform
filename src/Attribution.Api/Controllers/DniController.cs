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
    private readonly IWebsiteRepository _websiteRepository;

    public DniController(AllocationService allocationService, IWebsiteRepository websiteRepository)
    {
        _allocationService = allocationService;
        _websiteRepository = websiteRepository;
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
        var result = await _allocationService.AllocateAsync(websiteId, request.ConsentGranted, arrival, DateTimeOffset.UtcNow);

        return Ok(ToDto(result));
    }

    [HttpPost("heartbeat")]
    public async Task<ActionResult<HeartbeatResponseDto>> Heartbeat([FromBody] HeartbeatRequestDto request)
    {
        if (!Guid.TryParse(request.SessionId, out var sessionId))
        {
            return Ok(new HeartbeatResponseDto { StillValid = false, Number = null });
        }

        var (stillValid, number) = await _allocationService.HeartbeatAsync(sessionId, DateTimeOffset.UtcNow);
        return Ok(new HeartbeatResponseDto { StillValid = stillValid, Number = number });
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
            var result = await _allocationService.AllocateAsync(websiteId, consentGranted: true, arrival, DateTimeOffset.UtcNow);
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

    private static AllocateResponseDto ToDto(AllocateResult result) => new()
    {
        SessionId = result.SessionId?.ToString(),
        Number = result.Number,
        ExpiresAt = result.ExpiresAt,
        Reason = result.Reason,
    };
}
