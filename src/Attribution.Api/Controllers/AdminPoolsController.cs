using Attribution.Api.Contracts;
using Attribution.Api.Middleware;
using Attribution.Application.Administration;
using Attribution.Domain.Identity;
using Attribution.Domain.Pools;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Attribution.Api.Controllers;

// contracts/admin-api.md §Number pools & numbers. FR-001, FR-002, FR-004, FR-005.
[ApiController]
[Route("v1/admin")]
[Authorize]
public sealed class AdminPoolsController : ControllerBase
{
    private readonly INumberPoolRepository _poolRepository;
    private readonly ITrackingNumberRepository _trackingNumberRepository;
    private readonly IAuditLogger _auditLogger;

    public AdminPoolsController(
        INumberPoolRepository poolRepository,
        ITrackingNumberRepository trackingNumberRepository,
        IAuditLogger auditLogger)
    {
        _poolRepository = poolRepository;
        _trackingNumberRepository = trackingNumberRepository;
        _auditLogger = auditLogger;
    }

    [HttpPost("pools")]
    [RequireOperation(Operation.ManagePools)]
    public async Task<IActionResult> CreatePool([FromBody] CreatePoolRequestDto request)
    {
        if (!Guid.TryParse(request.ScopeRef, out var scopeRef))
        {
            return BadRequest("scope_ref must be a valid id.");
        }

        var pool = NumberPool.Create(request.Name, request.ScopeType, scopeRef, request.DefaultNumber);
        await _poolRepository.AddAsync(pool);
        await _auditLogger.RecordAsync("CreatePool", "NumberPool", pool.Id.ToString(), before: null, after: pool);

        return CreatedAtAction(nameof(GetPool), new { id = pool.Id }, new { id = pool.Id });
    }

    [HttpGet("pools/{id:guid}")]
    [RequireOperation(Operation.ManagePools)]
    public async Task<IActionResult> GetPool(Guid id)
    {
        var pool = await _poolRepository.GetByIdAsync(id);
        if (pool is null)
        {
            return NotFound();
        }

        var numbers = await _trackingNumberRepository.GetByPoolAsync(id);
        var utilisation = numbers.Count == 0
            ? 0
            : numbers.Count(n => n.Status == TrackingNumberStatus.Active) / (double)numbers.Count;

        return Ok(new
        {
            id = pool.Id,
            pool.Name,
            pool.ScopeType,
            pool.ScopeRef,
            pool.DefaultNumber,
            number_count = numbers.Count,
            utilisation,
        });
    }

    // FR-002: CSV bulk import. Expects one DID per line (optionally with a header row
    // literally reading "did"); rejects duplicates (within the pool) and malformed
    // entries (DidValidator) with a per-row reason, per contracts/admin-api.md.
    [HttpPost("pools/{id:guid}/numbers/import")]
    [RequireOperation(Operation.ManagePools)]
    [RequestSizeLimit(10 * 1024 * 1024)]
    public async Task<IActionResult> ImportNumbers(Guid id, IFormFile file)
    {
        var pool = await _poolRepository.GetByIdAsync(id);
        if (pool is null)
        {
            return NotFound();
        }

        var existing = (await _trackingNumberRepository.GetByPoolAsync(id))
            .Select(n => n.Did)
            .ToHashSet(StringComparer.Ordinal);

        var results = new List<ImportRowResultDto>();
        using var reader = new StreamReader(file.OpenReadStream());
        var rowNumber = 0;
        string? line;
        while ((line = await reader.ReadLineAsync()) is not null)
        {
            rowNumber++;
            var did = line.Trim();
            if (did.Length == 0 || did.Equals("did", StringComparison.OrdinalIgnoreCase))
            {
                continue; // blank line or header row
            }

            if (!DidValidator.IsValidE164(did))
            {
                results.Add(new ImportRowResultDto { Row = rowNumber, Did = did, Accepted = false, Reason = "malformed" });
                continue;
            }

            if (!existing.Add(did))
            {
                results.Add(new ImportRowResultDto { Row = rowNumber, Did = did, Accepted = false, Reason = "duplicate" });
                continue;
            }

            var number = TrackingNumber.Create(id, did);
            await _trackingNumberRepository.AddAsync(number);
            results.Add(new ImportRowResultDto { Row = rowNumber, Did = did, Accepted = true, Reason = null });
        }

        await _auditLogger.RecordAsync(
            "ImportNumbers", "NumberPool", id.ToString(), before: null,
            after: new { accepted = results.Count(r => r.Accepted), rejected = results.Count(r => !r.Accepted) });

        return Ok(results);
    }
}
