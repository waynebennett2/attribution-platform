namespace Attribution.Application.Administration;

// FR-029: the query layer behind every /v1/reports/* endpoint. Each method returns
// already-shaped rows (grouped where the report calls for grouping, per-call where it
// calls for a listing) — ReportingService derives each report's totals from these exact
// rows, so a report's totals can never disagree with the rows the same call returned.
public interface IReportingRepository
{
    // One row per website (nullable website when a call never resolved to one — e.g.
    // unattributed): total/attributed/qualified call counts.
    Task<IReadOnlyList<IDictionary<string, object?>>> GetDashboardRowsAsync(DateOnly from, DateOnly to);

    // FR-014, Acceptance Scenario 4: one row per campaign captured on the originating session.
    Task<IReadOnlyList<IDictionary<string, object?>>> GetCampaignRowsAsync(DateOnly from, DateOnly to);

    // One row per matching call. state filters on attribution state (attributed |
    // unattributed | ambiguous); q searches dialled number / caller id.
    Task<IReadOnlyList<IDictionary<string, object?>>> GetCallRowsAsync(DateOnly from, DateOnly to, string? state, string? q);

    Task<IReadOnlyList<IDictionary<string, object?>>> GetMissedRowsAsync(DateOnly from, DateOnly to);

    Task<IReadOnlyList<IDictionary<string, object?>>> GetQualifiedRowsAsync(DateOnly from, DateOnly to);

    Task<IReadOnlyList<IDictionary<string, object?>>> GetUnattributedRowsAsync(DateOnly from, DateOnly to);

    // FR-048: one row per (website, state, reason) combination, covering every call —
    // attributed, unattributed and ambiguous alike.
    Task<IReadOnlyList<IDictionary<string, object?>>> GetCoverageRowsAsync(DateOnly from, DateOnly to);
}
