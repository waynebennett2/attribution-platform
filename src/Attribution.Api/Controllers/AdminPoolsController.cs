using Attribution.Api.Contracts;
using Attribution.Api.Middleware;
using Attribution.Application.Administration;
using Attribution.Domain.Identity;
using Attribution.Domain.Pools;
using Attribution.Domain.Websites;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Attribution.Api.Controllers;

// contracts/admin-api.md §Number pools & numbers. FR-001, FR-002, FR-004, FR-005, FR-050, FR-051.
[ApiController]
[Route("v1/admin")]
[Authorize]
public sealed class AdminPoolsController : ControllerBase
{
    private readonly INumberPoolRepository _poolRepository;
    private readonly ITrackingNumberRepository _trackingNumberRepository;
    private readonly IWebsiteRepository _websiteRepository;
    private readonly IAuditLogger _auditLogger;
    private readonly NumberImportOptions _importOptions;

    public AdminPoolsController(
        INumberPoolRepository poolRepository,
        ITrackingNumberRepository trackingNumberRepository,
        IWebsiteRepository websiteRepository,
        IAuditLogger auditLogger,
        IOptions<NumberImportOptions> importOptions)
    {
        _poolRepository = poolRepository;
        _trackingNumberRepository = trackingNumberRepository;
        _websiteRepository = websiteRepository;
        _auditLogger = auditLogger;
        _importOptions = importOptions.Value;
    }

    [HttpPost("pools")]
    [RequireOperation(Operation.ManagePools)]
    public async Task<IActionResult> CreatePool([FromBody] CreatePoolRequestDto request)
    {
        if (!Guid.TryParse(request.ScopeRef, out var scopeRef))
        {
            return BadRequest("scope_ref must be a valid id.");
        }

        // FR-050: a matched number must identify exactly one pool, so while multi-pool
        // matching is enabled for this website, two of its pools can never share a
        // (digit-equivalent) default_number — an admin-time check, since by the time a
        // visitor's browser is matching against the pool->number map it must already be
        // collision-free.
        if (request.ScopeType == "website" && !string.IsNullOrWhiteSpace(request.DefaultNumber))
        {
            var website = await _websiteRepository.GetByIdAsync(scopeRef);
            if (website is { MultiPoolEnabled: true })
            {
                var newDigits = DigitsOnly(request.DefaultNumber);
                var siblingPools = await _poolRepository.GetByScopeAsync("website", scopeRef);
                if (siblingPools.Any(p => !string.IsNullOrWhiteSpace(p.DefaultNumber) && DigitsOnly(p.DefaultNumber) == newDigits))
                {
                    return BadRequest("default_number collides, digit-normalized, with another pool already scoped to this multi-pool-enabled website (FR-050).");
                }
            }
        }

        var pool = NumberPool.Create(request.Name, request.ScopeType, scopeRef, request.DefaultNumber);
        await _poolRepository.AddAsync(pool);
        await _auditLogger.RecordAsync("CreatePool", "NumberPool", pool.Id.ToString(), before: null, after: pool);

        return CreatedAtAction(nameof(GetPool), new { id = pool.Id }, new { id = pool.Id });
    }

    private static string DigitsOnly(string value) => new(value.Where(char.IsDigit).ToArray());

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

    // FR-002: CSV bulk import via browser upload. Expects one DID per line (optionally
    // with a header row literally reading "did"); rejects duplicates (within the pool) and
    // malformed entries (DidValidator) with a per-row reason, per contracts/admin-api.md.
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

        using var stream = file.OpenReadStream();
        var results = await ImportCsvRowsAsync(id, stream);

        await _auditLogger.RecordAsync(
            "ImportNumbers", "NumberPool", id.ToString(), before: null,
            after: new { accepted = results.Count(r => r.Accepted), rejected = results.Count(r => !r.Accepted) });

        return Ok(results);
    }

    // FR-051: lists the CSV files currently sitting in the configured server-side import
    // folder, so the admin interface can offer them for the trigger below without an
    // operator needing to know or type a file name.
    [HttpGet("numbers/import-folder/files")]
    [RequireOperation(Operation.ManagePools)]
    public IActionResult ListImportFolderFiles()
    {
        if (string.IsNullOrWhiteSpace(_importOptions.FolderPath) || !Directory.Exists(_importOptions.FolderPath))
        {
            return Ok(Array.Empty<object>());
        }

        var files = new DirectoryInfo(_importOptions.FolderPath)
            .GetFiles("*.csv", SearchOption.TopDirectoryOnly)
            .Select(f => new { file_name = f.Name, size_bytes = f.Length, modified_at = f.LastWriteTimeUtc })
            .OrderBy(f => f.file_name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return Ok(files);
    }

    // FR-051: triggers import of a CSV already placed in the configured folder — an
    // operator exports it from the 8x8 admin portal and drops it there; this is the
    // administrator's alternative to a browser upload, reading the file straight off disk
    // and applying the identical per-row validation as /numbers/import.
    [HttpPost("pools/{id:guid}/numbers/import-from-folder")]
    [RequireOperation(Operation.ManagePools)]
    public async Task<IActionResult> ImportNumbersFromFolder(Guid id, [FromBody] ImportFromFolderRequestDto request)
    {
        var pool = await _poolRepository.GetByIdAsync(id);
        if (pool is null)
        {
            return NotFound();
        }

        // Only a bare file name resolving directly inside the configured folder is
        // accepted — this is what stops a "../../" (or an absolute-path) value from
        // reading anything outside it.
        var fileName = Path.GetFileName(request.FileName);
        if (string.IsNullOrWhiteSpace(fileName) || fileName != request.FileName)
        {
            return BadRequest("file_name must be a plain file name with no path segments.");
        }

        if (string.IsNullOrWhiteSpace(_importOptions.FolderPath))
        {
            return BadRequest("No import folder is configured.");
        }

        var filePath = Path.Combine(_importOptions.FolderPath, fileName);
        if (!System.IO.File.Exists(filePath))
        {
            return NotFound($"'{fileName}' was not found in the configured import folder.");
        }

        List<ImportRowResultDto> results;
        using (var stream = System.IO.File.OpenRead(filePath))
        {
            results = await ImportCsvRowsAsync(id, stream);
        }

        await _auditLogger.RecordAsync(
            "ImportNumbersFromFolder", "NumberPool", id.ToString(), before: null,
            after: new { file_name = fileName, accepted = results.Count(r => r.Accepted), rejected = results.Count(r => !r.Accepted) });

        return Ok(results);
    }

    // FR-002, FR-051: shared per-row validation used by both the browser-upload and the
    // folder-triggered import paths, so the two cannot silently drift apart. One DID per
    // line (optionally with a header row literally reading "did"); rejects duplicates
    // (within the pool, including duplicates newly accepted earlier in the same file) and
    // malformed entries with a per-row reason.
    private async Task<List<ImportRowResultDto>> ImportCsvRowsAsync(Guid poolId, Stream csvStream)
    {
        var existing = (await _trackingNumberRepository.GetByPoolAsync(poolId))
            .Select(n => n.Did)
            .ToHashSet(StringComparer.Ordinal);

        var results = new List<ImportRowResultDto>();
        using var reader = new StreamReader(csvStream);
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

            var number = TrackingNumber.Create(poolId, did);
            await _trackingNumberRepository.AddAsync(number);
            results.Add(new ImportRowResultDto { Row = rowNumber, Did = did, Accepted = true, Reason = null });
        }

        return results;
    }
}

public sealed record ImportFromFolderRequestDto(
    [property: System.Text.Json.Serialization.JsonPropertyName("file_name")] string FileName);
