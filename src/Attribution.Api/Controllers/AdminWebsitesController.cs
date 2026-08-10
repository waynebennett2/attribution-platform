using Attribution.Api.Middleware;
using Attribution.Application.Administration;
using Attribution.Domain.Identity;
using Attribution.Domain.Websites;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Attribution.Api.Controllers;

// FR-049: per-website shadow-mode toggle, switchable through configuration without code
// change, per contracts/admin-api.md.
[ApiController]
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

    [HttpPost("{id:guid}/shadow-mode/enable")]
    [RequireOperation(Operation.ManagePools)]
    public Task<IActionResult> Enable(Guid id) => SetShadowMode(id, enable: true);

    [HttpPost("{id:guid}/shadow-mode/disable")]
    [RequireOperation(Operation.ManagePools)]
    public Task<IActionResult> Disable(Guid id) => SetShadowMode(id, enable: false);

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
