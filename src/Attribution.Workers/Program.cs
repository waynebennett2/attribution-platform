using Attribution.Application.Administration;
using Attribution.Application.Attribution;
using Attribution.Application.Ingestion;
using Attribution.Application.Publication;
using Attribution.Application.Qualification;
using Attribution.Domain.Audit;
using Attribution.Domain.Calls;
using Attribution.Domain.Pools;
using Attribution.Domain.Publication;
using Attribution.Domain.Qualification;
using Attribution.Domain.Sessions;
using Attribution.Domain.Websites;
using Attribution.Infrastructure.Data;
using Attribution.Infrastructure.GA4;
using Attribution.Infrastructure.GoogleAds;
using Attribution.Infrastructure.Ingestion8x8;
using Attribution.Workers.AlertingWorker;
using Attribution.Workers.IngestionWorker;
using Attribution.Workers.PublicationWorker;
using Attribution.Workers.RetentionWorker;
using Dapper;

// Every repository's Dapper row type uses PascalCase properties against snake_case
// columns — see Attribution.Api/Program.cs for the full rationale. Needed here too now
// that IngestionWorker resolves the same Dapper repositories.
DefaultTypeMap.MatchNamesWithUnderscores = true;

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

// --- Data access, mirroring Attribution.Api/Program.cs (T011-T013, T047-T049) ---
var connectionString = builder.Configuration.GetConnectionString("AttributionDb")
    ?? throw new InvalidOperationException("ConnectionStrings:AttributionDb is not configured.");
builder.Services.AddSingleton<IDbConnectionFactory>(_ => new MySqlConnectionFactory(connectionString));
builder.Services.AddScoped<ITrackingNumberRepository, TrackingNumberRepository>();
builder.Services.AddScoped<IAllocationRepository, AllocationRepository>();
builder.Services.AddScoped<ICallRepository, CallRepository>();
builder.Services.AddScoped<ICallLegRepository, CallLegRepository>();
builder.Services.AddScoped<IAttributionRepository, AttributionRepository>();
builder.Services.AddScoped<IIngestionCheckpointRepository, IngestionCheckpointRepository>();
builder.Services.AddScoped<IReviewCaseRepository, ReviewCaseRepository>();
builder.Services.AddScoped<IAuditRepository, AuditRepository>();
builder.Services.AddScoped<IActorContext, SystemActorContext>();
builder.Services.AddScoped<IAuditLogger, AuditLogger>();
builder.Services.AddScoped<ISessionRepository, SessionRepository>();
builder.Services.AddScoped<IWebsiteRepository, WebsiteRepository>();
builder.Services.AddScoped<IQualificationRuleRepository, QualificationRuleRepository>();
builder.Services.AddScoped<IQualificationResultRepository, QualificationResultRepository>();
builder.Services.AddScoped<IConversionPublicationRepository, ConversionPublicationRepository>();
builder.Services.AddScoped<AttributionService>();
builder.Services.AddScoped<QualificationService>();
builder.Services.AddScoped<ReDerivationService>();
builder.Services.AddScoped<IngestionService>();
builder.Services.AddScoped<BackfillService>();
builder.Services.AddScoped<PublicationService>();
builder.Services.AddScoped<CorrectionService>();

// --- Analytics for 8x8 Work client (T050) ---
builder.Services.Configure<Analytics8x8ClientOptions>(builder.Configuration.GetSection("Analytics8x8"));
builder.Services.AddHttpClient<IAnalytics8x8Client, Analytics8x8Client>();

// --- Google Ads / GA4 publication clients (T079, T080) ---
builder.Services.Configure<GoogleAdsClientOptions>(builder.Configuration.GetSection("GoogleAds"));
builder.Services.AddHttpClient<IGoogleAdsClient, GoogleAdsClient>();
builder.Services.Configure<Ga4ClientOptions>(builder.Configuration.GetSection("Ga4"));
builder.Services.AddHttpClient<IGa4Client, Ga4Client>();

// FR-043: each loop is independently registered so a slow/failing one never blocks the
// others, and the whole host can scale independently from the request/response Api
// (research.md §4).
builder.Services.AddHostedService<IngestionWorker>();
builder.Services.AddHostedService<PublicationWorker>();
builder.Services.AddHostedService<AlertingWorker>();
builder.Services.AddHostedService<RetentionWorker>();

var host = builder.Build();
host.Run();
