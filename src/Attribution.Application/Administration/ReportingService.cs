namespace Attribution.Application.Administration;

// FR-029, FR-030: the shared response envelope every /v1/reports/* endpoint (and its CSV
// export twin) returns — period and filters echoed back verbatim so the reporting portal
// never has to reconstruct what it asked for, rows and totals from the exact same query.
public sealed record ReportResult(
    DateOnly From,
    DateOnly To,
    IReadOnlyDictionary<string, string?> Filters,
    IReadOnlyList<IDictionary<string, object?>> Rows,
    IReadOnlyDictionary<string, object?> Totals);

// FR-029, FR-048: assembles each report from IReportingRepository's rows, deriving every
// report's totals from those exact rows so a total can never disagree with what's
// displayed (T066/FR-029's "reconciles exactly" requirement is then structural, not just
// asserted). Role-based access itself (FR-031) is enforced once, generically, by the
// existing RBAC middleware on ReportsController — every /v1/reports/* route requires the
// same ViewReports/ExportReports operation, matching contracts/reporting-api.md's
// Analyst-vs-Marketing-Administrator split, so this service has no separate per-role logic.
public sealed class ReportingService
{
    private readonly IReportingRepository _repository;

    public ReportingService(IReportingRepository repository)
    {
        _repository = repository;
    }

    public async Task<ReportResult> DashboardAsync(DateOnly from, DateOnly to)
    {
        var rows = await _repository.GetDashboardRowsAsync(from, to);
        var totalCalls = SumInt(rows, "total_calls");
        var attributedCalls = SumInt(rows, "attributed_calls");
        var totals = new Dictionary<string, object?>
        {
            ["total_calls"] = totalCalls,
            ["attributed_calls"] = attributedCalls,
            ["attribution_rate"] = totalCalls == 0 ? 0d : Math.Round(attributedCalls / (double)totalCalls, 4),
            ["qualified_calls"] = SumInt(rows, "qualified_calls"),
        };
        return new ReportResult(from, to, NoFilters, rows, totals);
    }

    public async Task<ReportResult> CampaignsAsync(DateOnly from, DateOnly to)
    {
        var rows = await _repository.GetCampaignRowsAsync(from, to);
        var totals = new Dictionary<string, object?>
        {
            ["total_calls"] = SumInt(rows, "total_calls"),
            ["qualified_calls"] = SumInt(rows, "qualified_calls"),
        };
        return new ReportResult(from, to, NoFilters, rows, totals);
    }

    public async Task<ReportResult> CallsAsync(DateOnly from, DateOnly to, string? state, string? q)
    {
        var rows = await _repository.GetCallRowsAsync(from, to, state, q);
        var totals = new Dictionary<string, object?> { ["count"] = rows.Count };
        var filters = new Dictionary<string, string?> { ["state"] = state, ["q"] = q };
        return new ReportResult(from, to, filters, rows, totals);
    }

    public async Task<ReportResult> MissedAsync(DateOnly from, DateOnly to)
    {
        var rows = await _repository.GetMissedRowsAsync(from, to);
        return new ReportResult(from, to, NoFilters, rows, new Dictionary<string, object?> { ["count"] = rows.Count });
    }

    public async Task<ReportResult> QualifiedAsync(DateOnly from, DateOnly to)
    {
        var rows = await _repository.GetQualifiedRowsAsync(from, to);
        return new ReportResult(from, to, NoFilters, rows, new Dictionary<string, object?> { ["count"] = rows.Count });
    }

    public async Task<ReportResult> UnattributedAsync(DateOnly from, DateOnly to)
    {
        var rows = await _repository.GetUnattributedRowsAsync(from, to);
        var byReason = rows
            .GroupBy(r => r["reason"]?.ToString() ?? "(none)")
            .ToDictionary(g => g.Key, g => (object?)g.Count());
        var totals = new Dictionary<string, object?> { ["count"] = rows.Count, ["by_reason"] = byReason };
        return new ReportResult(from, to, NoFilters, rows, totals);
    }

    // FR-048: the sole evidence for SC-018 — count and proportion of every inbound call
    // in each attribution state, broken down by reason and website. FR-049: shadow-derived
    // rows are already tagged (is_shadow_derived) rather than folded into ordinary counts.
    public async Task<ReportResult> CoverageAsync(DateOnly from, DateOnly to)
    {
        var rows = await _repository.GetCoverageRowsAsync(from, to);
        var total = SumInt(rows, "count");
        var totals = new Dictionary<string, object?>
        {
            ["total"] = total,
            ["attributed"] = SumIntWhere(rows, "count", "state", "Attributed"),
            ["unattributed"] = SumIntWhere(rows, "count", "state", "Unattributed"),
            ["ambiguous"] = SumIntWhere(rows, "count", "state", "Ambiguous"),
            ["ambiguous_shadow_derived"] = rows
                .Where(r => string.Equals(r["state"]?.ToString(), "Ambiguous", StringComparison.Ordinal) && r["is_shadow_derived"] is true or 1)
                .Sum(r => Convert.ToInt32(r["count"])),
        };
        return new ReportResult(from, to, NoFilters, rows, totals);
    }

    private static readonly IReadOnlyDictionary<string, string?> NoFilters = new Dictionary<string, string?>();

    private static int SumInt(IEnumerable<IDictionary<string, object?>> rows, string column) =>
        rows.Sum(r => r.TryGetValue(column, out var value) && value is not null ? Convert.ToInt32(value) : 0);

    private static int SumIntWhere(
        IEnumerable<IDictionary<string, object?>> rows, string column, string whereColumn, string whereValue) =>
        rows.Where(r => string.Equals(r[whereColumn]?.ToString(), whereValue, StringComparison.Ordinal))
            .Sum(r => r.TryGetValue(column, out var value) && value is not null ? Convert.ToInt32(value) : 0);
}
