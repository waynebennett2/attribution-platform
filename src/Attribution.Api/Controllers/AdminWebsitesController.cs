using Attribution.Api.Middleware;
using Attribution.Application.Administration;
using Attribution.Domain.Identity;
using Attribution.Domain.Websites;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;

namespace Attribution.Api.Controllers;

// FR-049: per-website shadow-mode toggle, switchable through configuration without code
// change, per contracts/admin-api.md.
[ApiController]
[EnableCors("AdminUi")]
[Route("v1/admin/websites")]
[Authorize]
public sealed class AdminWebsitesController : ControllerBase
{
    private readonly IWebsiteRepository _websiteRepository;
    private readonly IAuditLogger _auditLogger;

    public AdminWebsitesController(IWebsiteRepository websiteRepository, IAuditLogger auditLogger)
    {
        _websiteRepository = websiteRepository;
        _auditLogger = auditLogger;
    }

    // Lists every website — there is no other browsing surface for websites, and a
    // website can only be created directly against the database today (no admin endpoint
    // exists for that yet), so this is at minimum what an admin UI needs to see what's there.
    [HttpGet]
    [RequireOperation(Operation.ManagePools)]
    public async Task<IActionResult> List()
    {
        var websites = await _websiteRepository.GetAllAsync();
        return Ok(websites.Select(w => new
        {
            id = w.Id,
            w.Name,
            w.PermittedOrigins,
            w.DefaultNumber,
            w.SessionTimeoutSeconds,
            w.HeartbeatIntervalSeconds,
            w.AllocationWindowExtensionSeconds,
            w.CooldownSeconds,
            w.ConsentRequired,
            w.ShadowModeEnabled,
            w.MultiPoolEnabled,
            w.BusinessUnit,
            w.LocalTimezone,
        }));
    }

    [HttpPost("{id:guid}/shadow-mode/enable")]
    [RequireOperation(Operation.ManagePools)]
    public Task<IActionResult> Enable(Guid id) => SetShadowMode(id, enable: true);

    [HttpPost("{id:guid}/shadow-mode/disable")]
    [RequireOperation(Operation.ManagePools)]
    public Task<IActionResult> Disable(Guid id) => SetShadowMode(id, enable: false);

    // FR-050: per-website opt-in, same configuration-only pattern as shadow mode above.
    [HttpPost("{id:guid}/multi-pool/enable")]
    [RequireOperation(Operation.ManagePools)]
    public Task<IActionResult> EnableMultiPool(Guid id) => SetMultiPool(id, enable: true);

    [HttpPost("{id:guid}/multi-pool/disable")]
    [RequireOperation(Operation.ManagePools)]
    public Task<IActionResult> DisableMultiPool(Guid id) => SetMultiPool(id, enable: false);

    private async Task<IActionResult> SetMultiPool(Guid id, bool enable)
    {
        var website = await _websiteRepository.GetByIdAsync(id);
        if (website is null)
        {
            return NotFound();
        }

        var before = new { website.MultiPoolEnabled };
        if (enable)
        {
            website.EnableMultiPool();
        }
        else
        {
            website.DisableMultiPool();
        }

        await _websiteRepository.UpdateAsync(website);
        await _auditLogger.RecordAsync("SetMultiPoolEnabled", "Website", id.ToString(), before, new { website.MultiPoolEnabled });

        return NoContent();
    }

    private async Task<IActionResult> SetShadowMode(Guid id, bool enable)
    {
        var website = await _websiteRepository.GetByIdAsync(id);
        if (website is null)
        {
            return NotFound();
        }

        var before = new { website.ShadowModeEnabled };
        if (enable)
        {
            website.EnableShadowMode();
        }
        else
        {
            website.DisableShadowMode();
        }

        await _websiteRepository.UpdateAsync(website);
        await _auditLogger.RecordAsync("SetShadowMode", "Website", id.ToString(), before, new { website.ShadowModeEnabled });

        return NoContent();
    }
}
