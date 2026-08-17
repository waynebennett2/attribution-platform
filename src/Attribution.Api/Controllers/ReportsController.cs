using System.Text;
using Attribution.Api.Middleware;
using Attribution.Application.Administration;
using Attribution.Domain.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;

namespace Attribution.Api.Controllers;

// contracts/reporting-api.md. FR-029-FR-031, FR-048. Every report has a JSON endpoint and
// an identically-filtered CSV export twin (FR-030), both gated by the same RBAC policy
// (FR-031) — Analyst and Marketing Administrator both hold ViewReports/ExportReports,
// Integration Service holds neither (FR-038), matching contracts/reporting-api.md exactly.
[ApiController]
[EnableCors("AdminUi")]
[Route("v1/reports")]
[Authorize]
public sealed class ReportsController : ControllerBase
{
    private readonly ReportingService _reportingService;

    public ReportsController(ReportingService reportingService)
    {
        _reportingService = reportingService;
    }

    [HttpGet("dashboard")]
    [RequireOperation(Operation.ViewReports)]
    public Task<IActionResult> Dashboard([FromQuery] string from, [FromQuery] string to) =>
        RunAsync(from, to, _reportingService.DashboardAsync);

    [HttpGet("dashboard/export.csv")]
    [RequireOperation(Operation.ExportReports)]
    public Task<IActionResult> DashboardCsv([FromQuery] string from, [FromQuery] string to) =>
        RunCsvAsync(from, to, "dashboard", _reportingService.DashboardAsync);

    [HttpGet("campaigns")]
    [RequireOperation(Operation.ViewReports)]
    public Task<IActionResult> Campaigns([FromQuery] string from, [FromQuery] string to) =>
        RunAsync(from, to, _reportingService.CampaignsAsync);

    [HttpGet("campaigns/export.csv")]
    [RequireOperation(Operation.ExportReports)]
    public Task<IActionResult> CampaignsCsv([FromQuery] string from, [FromQuery] string to) =>
        RunCsvAsync(from, to, "campaigns", _reportingService.CampaignsAsync);

    [HttpGet("calls")]
    [RequireOperation(Operation.ViewReports)]
    public Task<IActionResult> Calls([FromQuery] string from, [FromQuery] string to, [FromQuery] string? state, [FromQuery] string? q) =>
        RunAsync(from, to, (f, t) => _reportingService.CallsAsync(f, t, state, q));

    [HttpGet("calls/export.csv")]
    [RequireOperation(Operation.ExportReports)]
    public Task<IActionResult> CallsCsv([FromQuery] string from, [FromQuery] string to, [FromQuery] string? state, [FromQuery] string? q) =>
        RunCsvAsync(from, to, "calls", (f, t) => _reportingService.CallsAsync(f, t, state, q));

    [HttpGet("missed")]
    [RequireOperation(Operation.ViewReports)]
    public Task<IActionResult> Missed([FromQuery] string from, [FromQuery] string to) =>
        RunAsync(from, to, _reportingService.MissedAsync);

    [HttpGet("missed/export.csv")]
    [RequireOperation(Operation.ExportReports)]
    public Task<IActionResult> MissedCsv([FromQuery] string from, [FromQuery] string to) =>
        RunCsvAsync(from, to, "missed", _reportingService.MissedAsync);

    [HttpGet("qualified")]
    [RequireOperation(Operation.ViewReports)]
    public Task<IActionResult> Qualified([FromQuery] string from, [FromQuery] string to) =>
        RunAsync(from, to, _reportingService.QualifiedAsync);

    [HttpGet("qualified/export.csv")]
    [RequireOperation(Operation.ExportReports)]
    public Task<IActionResult> QualifiedCsv([FromQuery] string from, [FromQuery] string to) =>
        RunCsvAsync(from, to, "qualified", _reportingService.QualifiedAsync);

    [HttpGet("unattributed")]
    [RequireOperation(Operation.ViewReports)]
    public Task<IActionResult> Unattributed([FromQuery] string from, [FromQuery] string to) =>
        RunAsync(from, to, _reportingService.UnattributedAsync);

    [HttpGet("unattributed/export.csv")]
    [RequireOperation(Operation.ExportReports)]
    public Task<IActionResult> UnattributedCsv([FromQuery] string from, [FromQuery] string to) =>
        RunCsvAsync(from, to, "unattributed", _reportingService.UnattributedAsync);

    // FR-048: the sole evidence for SC-018.
    [HttpGet("coverage")]
    [RequireOperation(Operation.ViewReports)]
    public Task<IActionResult> Coverage([FromQuery] string from, [FromQuery] string to) =>
        RunAsync(from, to, _reportingService.CoverageAsync);

    [HttpGet("coverage/export.csv")]
    [RequireOperation(Operation.ExportReports)]
    public Task<IActionResult> CoverageCsv([FromQuery] string from, [FromQuery] string to) =>
        RunCsvAsync(from, to, "coverage", _reportingService.CoverageAsync);

    private async Task<IActionResult> RunAsync(string from, string to, Func<DateOnly, DateOnly, Task<ReportResult>> query)
    {
        if (!TryParsePeriod(from, to, out var f, out var t, out var error))
        {
            return BadRequest(error);
        }

        return Ok(ToJson(await query(f, t)));
    }

    private async Task<IActionResult> RunCsvAsync(
        string from, string to, string reportName, Func<DateOnly, DateOnly, Task<ReportResult>> query)
    {
        if (!TryParsePeriod(from, to, out var f, out var t, out var error))
        {
            return BadRequest(error);
        }

        var result = await query(f, t);
        return File(Encoding.UTF8.GetBytes(ToCsv(result.Rows)), "text/csv", $"{reportName}.csv");
    }

    private static bool TryParsePeriod(string from, string to, out DateOnly parsedFrom, out DateOnly parsedTo, out string? error)
    {
        parsedFrom = default;
        parsedTo = default;
        if (!DateOnly.TryParse(from, out parsedFrom) || !DateOnly.TryParse(to, out parsedTo))
        {
            error = "from/to must be valid dates (e.g. \"2026-07-13\").";
            return false;
        }

        if (parsedTo < parsedFrom)
        {
            error = "to must not be before from.";
            return false;
        }

        error = null;
        return true;
    }

    // contracts/reporting-api.md's response shape — period/filters echoed back verbatim
    // alongside the exact rows/totals FR-029 requires to reconcile.
    private static object ToJson(ReportResult result) => new
    {
        period = new { from = result.From.ToString("yyyy-MM-dd"), to = result.To.ToString("yyyy-MM-dd") },
        filters = result.Filters,
        rows = result.Rows,
        totals = result.Totals,
    };

    // FR-030: the same rows the JSON report returned, serialized as CSV — never a
    // separately-derived query, so an export and its report can never disagree.
    private static string ToCsv(IReadOnlyList<IDictionary<string, object?>> rows)
    {
        if (rows.Count == 0)
        {
            return string.Empty;
        }

        var columns = rows[0].Keys.ToList();
        var csv = new StringBuilder();
        csv.AppendLine(string.Join(',', columns.Select(CsvField)));
        foreach (var row in rows)
        {
            csv.AppendLine(string.Join(',', columns.Select(c => CsvField(row.TryGetValue(c, out var v) ? v : null))));
        }

        return csv.ToString();
    }

    private static string CsvField(object? value)
    {
        var text = value switch
        {
            null => string.Empty,
            DateTime dt => dt.ToString("O"),
            DateTimeOffset dto => dto.ToString("O"),
            bool b => b ? "true" : "false",
            _ => value.ToString() ?? string.Empty,
        };

        return text.Contains(',') || text.Contains('"') || text.Contains('\n')
            ? $"\"{text.Replace("\"", "\"\"")}\""
            : text;
    }
}
