using Attribution.Workers.AlertingWorker;
using Attribution.Workers.IngestionWorker;
using Attribution.Workers.PublicationWorker;
using Attribution.Workers.RetentionWorker;

var builder = Host.CreateApplicationBuilder(args);

// Gitignored per-developer overrides (real DB credentials, etc.) — never committed.
// Loaded last so it takes precedence over appsettings.{Environment}.json; see
// appsettings.Development.local.json.example for the expected shape.
builder.Configuration.AddJsonFile(
    $"appsettings.{builder.Environment.EnvironmentName}.local.json", optional: true, reloadOnChange: true);

// FR-041: structured (JSON) logs, matching the Api host, so a call can be traced end to
// end across both processes.
builder.Logging.AddJsonConsole();
builder.Services.AddMetrics();

// FR-043: each loop is independently registered so a slow/failing one never blocks the
// others, and the whole host can scale independently from the request/response Api
// (research.md §4).
builder.Services.AddHostedService<IngestionWorker>();
builder.Services.AddHostedService<PublicationWorker>();
builder.Services.AddHostedService<AlertingWorker>();
builder.Services.AddHostedService<RetentionWorker>();

var host = builder.Build();
host.Run();
