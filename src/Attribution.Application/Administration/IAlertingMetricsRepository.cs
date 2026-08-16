using Attribution.Domain.Publication;

namespace Attribution.Application.Administration;

// FR-047: the raw signals AlertingService evaluates each condition type against. Kept
// separate from the narrow per-entity repositories (IIngestionCheckpointRepository etc.)
// the same way IReportingRepository is — these are cross-cutting aggregate reads no single
// entity repository's interface expresses.
public interface IAlertingMetricsRepository
{
    Task<DateTimeOffset?> GetLastIngestionCheckpointUpdateAsync(string feed);

    Task<(int Sent, int Failed)> GetRecentPublicationOutcomeCountsAsync(PublicationDestination destination, TimeSpan window);

    Task<IReadOnlyList<PoolUtilisation>> GetPoolUtilisationAsync();
}

// HeldNumbers: active numbers in the pool currently inside an open allocation window
// (window_end in the future) — the practical "tied up right now" signal FR-034's
// exhaustion warning cares about, not merely the number's Active/Suspended/Retired status.
public sealed record PoolUtilisation(Guid PoolId, string PoolName, int HeldNumbers, int TotalNumbers);
