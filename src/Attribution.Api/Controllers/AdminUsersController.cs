using Attribution.Api.Middleware;
using Attribution.Application.Administration;
using Attribution.Domain.Identity;
using Attribution.Infrastructure.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using DomainUser = Attribution.Domain.Identity.User;

namespace Attribution.Api.Controllers;

// contracts/admin-api.md §Users & roles. FR-032, FR-046. "federated users are provisioned
// on first sign-in, not created here; this creates/edits break-glass accounts and role
// overrides" — there is no federation sign-in path in this codebase (see AuthController),
// so in practice every row this lists today was either seeded or created here.
[ApiController]
[Route("v1/admin/users")]
[Authorize]
public sealed class AdminUsersController : ControllerBase
{
    private readonly IUserRepository _userRepository;
    private readonly BreakGlassAuthenticator _authenticator;
    private readonly IActorContext _actorContext;
    private readonly IAuditLogger _auditLogger;

    public AdminUsersController(
        IUserRepository userRepository, BreakGlassAuthenticator authenticator, IActorContext actorContext, IAuditLogger auditLogger)
    {
        _userRepository = userRepository;
        _authenticator = authenticator;
        _actorContext = actorContext;
        _auditLogger = auditLogger;
    }

    [HttpGet]
    [RequireOperation(Operation.ManageUsers)]
    public async Task<IActionResult> List()
    {
        var users = await _userRepository.GetAllAsync();
        return Ok(users.Select(ToDto));
    }

    // FR-046: provisions a break-glass account — a local password plus a fresh TOTP
    // secret, returned once as an otpauth:// URI for the administrator to hand to whoever
    // will hold the account (an authenticator app scans it; the platform never displays
    // the secret again after this response).
    [HttpPost]
    [RequireOperation(Operation.ManageUsers)]
    public async Task<IActionResult> CreateBreakGlass([FromBody] CreateBreakGlassUserRequest request)
    {
        if (!Enum.TryParse<Role>(request.Role, ignoreCase: true, out var role))
        {
            return BadRequest("role must be one of: SystemAdministrator, MarketingAdministrator, Analyst, IntegrationService.");
        }

        if (await _userRepository.GetByUsernameAsync(request.Username) is not null)
        {
            return BadRequest($"Username '{request.Username}' is already in use.");
        }

        var passwordHash = _authenticator.HashPassword(request.Password);
        var totpSecret = BreakGlassAuthenticator.GenerateTotpSecret();
        var user = DomainUser.CreateBreakGlass(request.Username, role, passwordHash, totpSecret);
        await _userRepository.AddAsync(user);

        await _auditLogger.RecordAsync(
            "CreateBreakGlassUser", "User", user.Id.ToString(), before: null, after: new { user.Username, role = role.ToString() });

        var otpAuthUri = $"otpauth://totp/AttributionPlatform:{Uri.EscapeDataString(request.Username)}"
            + $"?secret={totpSecret}&issuer=AttributionPlatform";
        return CreatedAtAction(nameof(List), null, new { id = user.Id, totp_provisioning_uri = otpAuthUri });
    }

    [HttpPost("{id:guid}/role-override")]
    [RequireOperation(Operation.ManageUsers)]
    public async Task<IActionResult> OverrideRole(Guid id, [FromBody] RoleOverrideRequest request)
    {
        if (!Enum.TryParse<Role>(request.Role, ignoreCase: true, out var newRole))
        {
            return BadRequest("role must be one of: SystemAdministrator, MarketingAdministrator, Analyst, IntegrationService.");
        }

        var user = await _userRepository.GetByIdAsync(id);
        if (user is null)
        {
            return NotFound();
        }

        var previousRole = user.EffectiveRole;
        var actorUserId = _actorContext.ActorUserId ?? "unknown";
        user.ApplyRoleOverride(newRole, actorUserId);
        await _userRepository.UpdateAsync(user);

        await _auditLogger.RecordAsync(
            "OverrideUserRole", "User", user.Id.ToString(),
            before: new { role = previousRole.ToString() }, after: new { role = newRole.ToString(), overriddenBy = actorUserId });

        return Ok(ToDto(user));
    }

    private static object ToDto(DomainUser user) => new
    {
        id = user.Id,
        username = user.Username,
        subject_ref = user.SubjectRef,
        client_id = user.ClientId,
        identity_type = user.IdentityType.ToString(),
        mapped_role = user.MappedRole.ToString(),
        role_override = user.RoleOverride?.ToString(),
        effective_role = user.EffectiveRole.ToString(),
        is_active = user.IsActive,
        created_at = user.CreatedAt,
        last_seen_at = user.LastSeenAt,
    };
}

public sealed record CreateBreakGlassUserRequest(string Username, string Password, string Role);

public sealed record RoleOverrideRequest(string Role);
