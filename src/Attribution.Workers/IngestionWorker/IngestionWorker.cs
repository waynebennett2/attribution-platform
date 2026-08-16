using Attribution.Application.Ingestion;
using Attribution.Domain.Calls;

namespace Attribution.Workers.IngestionWorker;

// FR-016: polls Analytics for 8x8 Work for Call Detail Records and Call Legs on a
// configurable cadence (default hourly, "Ingestion:PollIntervalSeconds"), advancing the
// "8x8-cdr" feed's checkpoint through IngestionService — the same idempotent pipeline
// BackfillService uses, so cadence changes never alter an attribution outcome.
public sealed class IngestionWorker : BackgroundService
{
    private const string Feed = "8x8-cdr";
    private static readonly TimeSpan DefaultPollInterval = TimeSpan.FromHours(1);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<IngestionWorker> _logger;

    public IngestionWorker(IServiceScopeFactory scopeFactory, IConfiguration configuration, ILogger<IngestionWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _configuration = configuration;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var pollInterval = _configuration.GetValue<int?>("Ingestion:PollIntervalSeconds") is { } seconds
            ? TimeSpan.FromSeconds(seconds)
            : DefaultPollInterval;

        using var timer = new PeriodicTimer(pollInterval);
        _logger.LogInformation("IngestionWorker started with poll interval {Interval}", pollInterval);

        do
        {
            try
            {
                await PollOnceAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "IngestionWorker tick failed");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task PollOnceAsync(CancellationToken cancellationToken)
    {
        // Scoped services (Dapper repositories, IngestionService) resolved per tick — this
        // BackgroundService itself is a singleton, so it cannot hold them directly.
        using var scope = _scopeFactory.CreateScope();
        var checkpointRepository = scope.ServiceProvider.GetRequiredService<IIngestionCheckpointRepository>();
        var client = scope.ServiceProvider.GetRequiredService<IAnalytics8x8Client>();
        var ingestionService = scope.ServiceProvider.GetRequiredService<IngestionService>();

        var checkpoint = await checkpointRepository.GetByFeedAsync(Feed);
        var page = await client.PollAsync(checkpoint?.Position, cancellationToken);

        await ingestionService.ProcessPageAsync(Feed, page, DateTimeOffset.UtcNow);

        _logger.LogInformation(
            "IngestionWorker processed {CallCount} calls and {LegCount} call legs for feed {Feed}",
            page.Calls.Count, page.CallLegs.Count, Feed);
    }
}
