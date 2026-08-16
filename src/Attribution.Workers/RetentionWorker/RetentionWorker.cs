using Attribution.Application.Administration;

namespace Attribution.Workers.RetentionWorker;

// FR-040: tiered purge/de-identification (14/25-month thresholds, 7-year audit log).
// FR-039's erasure-on-request path is separate (AdminPrivacyController, T101) since it must
// complete within 30 days of a specific request rather than wait for this daily sweep.
public sealed class RetentionWorker : BackgroundService
{
    private static readonly TimeSpan EvaluationInterval = TimeSpan.FromHours(24);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<RetentionWorker> _logger;

    public RetentionWorker(IServiceScopeFactory scopeFactory, ILogger<RetentionWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(EvaluationInterval);
        _logger.LogInformation("RetentionWorker started with evaluation interval {Interval}", EvaluationInterval);

        do
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var retentionService = scope.ServiceProvider.GetRequiredService<RetentionService>();
                var now = DateTimeOffset.UtcNow;
                await retentionService.DeIdentifyExpiredAsync(now);
                await retentionService.PurgeExpiredAsync(now);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "RetentionWorker tick failed");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
