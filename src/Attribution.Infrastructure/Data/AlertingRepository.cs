using Attribution.Application.Administration;
using Attribution.Domain.Publication;
using Dapper;

namespace Attribution.Infrastructure.Data;

public sealed class AlertingRepository : RepositoryBase, IAlertingMetricsRepository
{
    public AlertingRepository(IDbConnectionFactory connectionFactory) : base(connectionFactory) { }

    public async Task<DateTimeOffset?> GetLastIngestionCheckpointUpdateAsync(string feed)
    {
        using var connection = OpenConnection();
        return await connection.ExecuteScalarAsync<DateTimeOffset?>(
            "SELECT updated_at FROM ingestion_checkpoints WHERE feed = @Feed", new { Feed = feed });
    }

    // `window` is currently unused: conversion_publications has no created_at/attempted_at
    // column to filter by (only sent_at, set only once a row actually succeeds), so this
    // evaluates every row on record for the destination rather than a true recent window —
    // a documented simplification for this increment, kept as a parameter so a future
    // schema addition of an attempt timestamp can honor it without an interface change.
    public async Task<(int Sent, int Failed)> GetRecentPublicationOutcomeCountsAsync(PublicationDestination destination, TimeSpan window)
    {
        using var connection = OpenConnection();
        var row = await connection.QuerySingleAsync<(int Sent, int Failed)>(
            """
            SELECT
                SUM(CASE WHEN status IN ('Sent', 'Adjusted') THEN 1 ELSE 0 END) AS Sent,
                SUM(CASE WHEN status IN ('Failed', 'Rejected') THEN 1 ELSE 0 END) AS Failed
            FROM conversion_publications
            WHERE destination = @Destination
            """,
            new { Destination = destination.ToString() });
        return row;
    }

    public async Task<IReadOnlyList<PoolUtilisation>> GetPoolUtilisationAsync()
    {
        using var connection = OpenConnection();
        var rows = await connection.QueryAsync<PoolUtilisationRow>(
            """
            SELECT np.id AS PoolId, np.name AS PoolName,
                   COUNT(DISTINCT tn.id) AS TotalNumbers,
                   COUNT(DISTINCT CASE WHEN a.id IS NOT NULL THEN tn.id END) AS HeldNumbers
            FROM number_pools np
            JOIN tracking_numbers tn ON tn.pool_id = np.id AND tn.status = 'Active'
            LEFT JOIN allocations a ON a.tracking_number_id = tn.id AND a.window_end > UTC_TIMESTAMP()
            GROUP BY np.id, np.name
            """);
        return rows.Select(r => new PoolUtilisation(Guid.Parse(r.PoolId), r.PoolName, r.HeldNumbers, r.TotalNumbers)).ToList();
    }

    private sealed class PoolUtilisationRow
    {
        public string PoolId { get; set; } = string.Empty;
        public string PoolName { get; set; } = string.Empty;
        public int HeldNumbers { get; set; }
        public int TotalNumbers { get; set; }
    }
}
