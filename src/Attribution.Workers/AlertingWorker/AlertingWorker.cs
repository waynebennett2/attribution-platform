namespace Attribution.Workers.AlertingWorker;

// FR-047: evaluates ingestion lag, publication failure rate, allocation failure rate,
// pool utilisation and review-case age against configured thresholds, within 15 minutes
// (SC-017). Stub loop wired up here (T021); the actual evaluation logic is implemented
// in User Story 6 (T093).
public sealed class AlertingWorker : BackgroundService
{
    private static readonly TimeSpan EvaluationInterval = TimeSpan.FromMinutes(1);

    private readonly ILogger<AlertingWorker> _logger;

    public AlertingWorker(ILogger<AlertingWorker> logger)
    {
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(EvaluationInterval);
        _logger.LogInformation("AlertingWorker started with evaluation interval {Interval}", EvaluationInterval);

        do
        {
            try
            {
                // T093 (US6) implements the actual threshold evaluation + notification dispatch.
                _logger.LogDebug("AlertingWorker tick (no-op until US6 lands)");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AlertingWorker tick failed");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
