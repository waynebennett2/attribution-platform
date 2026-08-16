using Attribution.Application.Administration;
using Attribution.Domain.Calls;
using Dapper;

namespace Attribution.Infrastructure.Data;

// FR-029, FR-048: cross-entity read queries backing /v1/reports/*. Deliberately raw SQL
// (Dapper's dynamic query) rather than composed from the narrow per-entity repositories —
// reporting needs joins and aggregates no single entity repository's interface expresses,
// and going through the entities themselves would mean re-implementing the same joins in
// C# after the fact. A website is only ever resolved via the current Attribution's
// session — a call with no session (never-allocated, no covering window, ambiguous)
// simply has no resolvable website, which is real: there is no other link from a Call to
// a Website in this data model.
public sealed class ReportingRepository : RepositoryBase, IReportingRepository
{
    public ReportingRepository(IDbConnectionFactory connectionFactory) : base(connectionFactory) { }

    public Task<IReadOnlyList<IDictionary<string, object?>>> GetDashboardRowsAsync(DateOnly from, DateOnly to) =>
        QueryAsync(
            """
            SELECT w.id AS website_id, w.name AS website_name,
                   COUNT(DISTINCT c.id) AS total_calls,
                   COUNT(DISTINCT CASE WHEN a.state = 'Attributed' THEN c.id END) AS attributed_calls,
                   COUNT(DISTINCT CASE WHEN qr.is_qualified = 1 THEN c.id END) AS qualified_calls
            FROM calls c
            LEFT JOIN attributions a ON a.call_id = c.id AND a.is_current = 1
            LEFT JOIN sessions s ON s.id = a.session_id
            LEFT JOIN websites w ON w.id = s.website_id
            LEFT JOIN qualification_results qr ON qr.call_id = c.id AND qr.is_current = 1
            WHERE c.started_at >= @From AND c.started_at < @To
            GROUP BY w.id, w.name
            ORDER BY total_calls DESC
            """,
            PeriodParams(from, to));

    public Task<IReadOnlyList<IDictionary<string, object?>>> GetCampaignRowsAsync(DateOnly from, DateOnly to) =>
        QueryAsync(
            """
            SELECT COALESCE(s.utm_campaign, '(none)') AS campaign,
                   COUNT(*) AS total_calls,
                   SUM(CASE WHEN qr.is_qualified = 1 THEN 1 ELSE 0 END) AS qualified_calls
            FROM calls c
            JOIN attributions a ON a.call_id = c.id AND a.is_current = 1 AND a.state = 'Attributed'
            JOIN sessions s ON s.id = a.session_id
            LEFT JOIN qualification_results qr ON qr.call_id = c.id AND qr.is_current = 1
            WHERE c.started_at >= @From AND c.started_at < @To
            GROUP BY COALESCE(s.utm_campaign, '(none)')
            ORDER BY total_calls DESC
            """,
            PeriodParams(from, to));

    public Task<IReadOnlyList<IDictionary<string, object?>>> GetCallRowsAsync(DateOnly from, DateOnly to, string? state, string? q)
    {
        string? normalizedState = null;
        if (!string.IsNullOrWhiteSpace(state))
        {
            if (!Enum.TryParse<AttributionState>(state, ignoreCase: true, out var parsed))
            {
                throw new ArgumentException($"Unknown attribution state '{state}'.", nameof(state));
            }

            normalizedState = parsed.ToString();
        }

        return QueryAsync(
            """
            SELECT c.id AS call_id, c.started_at, c.direction, c.dialled_number, c.caller_id, c.is_final,
                   c.connected_duration_seconds, a.state AS attribution_state, a.reason AS attribution_reason,
                   a.is_shadow_derived, qr.is_qualified, s.utm_campaign AS campaign
            FROM calls c
            LEFT JOIN attributions a ON a.call_id = c.id AND a.is_current = 1
            LEFT JOIN qualification_results qr ON qr.call_id = c.id AND qr.is_current = 1
            LEFT JOIN sessions s ON s.id = a.session_id
            WHERE c.started_at >= @From AND c.started_at < @To
              AND (@State IS NULL OR a.state = @State)
              AND (@Q IS NULL OR c.dialled_number LIKE CONCAT('%', @Q, '%') OR c.caller_id LIKE CONCAT('%', @Q, '%'))
            ORDER BY c.started_at DESC
            """,
            new { From = From(from), To = To(to), State = normalizedState, Q = string.IsNullOrWhiteSpace(q) ? null : q });
    }

    public Task<IReadOnlyList<IDictionary<string, object?>>> GetMissedRowsAsync(DateOnly from, DateOnly to) =>
        QueryAsync(
            """
            SELECT c.id AS call_id, c.started_at, c.dialled_number, c.caller_id,
                   s.utm_campaign AS campaign, w.id AS website_id, w.name AS website_name
            FROM calls c
            LEFT JOIN attributions a ON a.call_id = c.id AND a.is_current = 1
            LEFT JOIN sessions s ON s.id = a.session_id
            LEFT JOIN websites w ON w.id = s.website_id
            WHERE c.started_at >= @From AND c.started_at < @To
              AND c.direction = 'Inbound' AND c.answered_at IS NULL
            ORDER BY c.started_at DESC
            """,
            PeriodParams(from, to));

    public Task<IReadOnlyList<IDictionary<string, object?>>> GetQualifiedRowsAsync(DateOnly from, DateOnly to) =>
        QueryAsync(
            """
            SELECT c.id AS call_id, c.started_at, c.dialled_number, c.caller_id, c.connected_duration_seconds,
                   s.utm_campaign AS campaign, w.id AS website_id, w.name AS website_name
            FROM calls c
            JOIN qualification_results qr ON qr.call_id = c.id AND qr.is_current = 1 AND qr.is_qualified = 1
            LEFT JOIN attributions a ON a.call_id = c.id AND a.is_current = 1
            LEFT JOIN sessions s ON s.id = a.session_id
            LEFT JOIN websites w ON w.id = s.website_id
            WHERE c.started_at >= @From AND c.started_at < @To
            ORDER BY c.started_at DESC
            """,
            PeriodParams(from, to));

    public Task<IReadOnlyList<IDictionary<string, object?>>> GetUnattributedRowsAsync(DateOnly from, DateOnly to) =>
        QueryAsync(
            """
            SELECT c.id AS call_id, c.started_at, c.dialled_number, c.caller_id,
                   a.state AS attribution_state, a.reason AS reason, a.is_shadow_derived
            FROM calls c
            JOIN attributions a ON a.call_id = c.id AND a.is_current = 1 AND a.state IN ('Unattributed', 'Ambiguous')
            WHERE c.started_at >= @From AND c.started_at < @To
            ORDER BY c.started_at DESC
            """,
            PeriodParams(from, to));

    public Task<IReadOnlyList<IDictionary<string, object?>>> GetCoverageRowsAsync(DateOnly from, DateOnly to) =>
        QueryAsync(
            """
            SELECT w.id AS website_id, w.name AS website_name, a.state AS state, a.reason AS reason,
                   a.is_shadow_derived, COUNT(*) AS count
            FROM calls c
            LEFT JOIN attributions a ON a.call_id = c.id AND a.is_current = 1
            LEFT JOIN sessions s ON s.id = a.session_id
            LEFT JOIN websites w ON w.id = s.website_id
            WHERE c.started_at >= @From AND c.started_at < @To
            GROUP BY w.id, w.name, a.state, a.reason, a.is_shadow_derived
            ORDER BY w.name, a.state
            """,
            PeriodParams(from, to));

    private static object PeriodParams(DateOnly from, DateOnly to) => new { From = From(from), To = To(to) };

    private static DateTime From(DateOnly from) => from.ToDateTime(TimeOnly.MinValue);

    // `to` is inclusive as a calendar day, so the exclusive upper bound is the day after.
    private static DateTime To(DateOnly to) => to.ToDateTime(TimeOnly.MinValue).AddDays(1);

    private async Task<IReadOnlyList<IDictionary<string, object?>>> QueryAsync(string sql, object param)
    {
        using var connection = OpenConnection();
        var rows = await connection.QueryAsync(sql, param);
        return rows.Select(r => (IDictionary<string, object?>)new Dictionary<string, object?>((IDictionary<string, object?>)r)).ToList();
    }
}
