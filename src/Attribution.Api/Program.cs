using Attribution.Api.Middleware;
using Dapper;
using Attribution.Application.Administration;
using Attribution.Application.Allocation;
using Attribution.Application.Qualification;
using Attribution.Domain.Audit;
using Attribution.Domain.Calls;
using Attribution.Domain.Identity;
using Attribution.Domain.Pools;
using Attribution.Domain.Qualification;
using Attribution.Domain.Sessions;
using Attribution.Domain.Websites;
using Attribution.Application.Attribution;
using Attribution.Application.Publication;
using Attribution.Domain.Publication;
using Attribution.Infrastructure.Alerting;
using Attribution.Infrastructure.Data;
using Attribution.Infrastructure.Data.Migrations;
using Attribution.Infrastructure.GoogleAds;
using Attribution.Infrastructure.Identity;
using Attribution.Infrastructure.Observability;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;

// Every repository's Dapper row type uses PascalCase properties (e.g. PermittedOrigins)
// against snake_case columns (permitted_origins). Dapper only matches those by exact name
// unless told otherwise, so without this every such column silently binds to the
// property's default value instead of erroring.
DefaultTypeMap.MatchNamesWithUnderscores = true;

var builder = WebApplication.CreateBuilder(args);

// Gitignored per-developer overrides (real DB credentials, etc.) — never committed.
// Loaded last so it takes precedence over appsettings.{Environment}.json; see
// appsettings.Development.local.json.example for the expected shape.
builder.Configuration.AddJsonFile(
    $"appsettings.{builder.Environment.EnvironmentName}.local.json", optional: true, reloadOnChange: true);

// FR-041: structured (JSON) logs so a single call can be traced end to end by any
// downstream log aggregator.
builder.Logging.AddJsonConsole();

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// FR-037: the /v1/dni/* endpoints are unauthenticated and called from the visitor's
// browser on the customer's own website — a different origin than this API — so the
// CORS preflight must be allowed through before DniController's own per-website
// permitted-origins check (which needs the request body, unavailable during preflight)
// ever runs. The actual allow-list enforcement stays in the controller.
builder.Services.AddCors(options =>
{
    options.AddPolicy("DniClient", policy => policy
        .SetIsOriginAllowed(_ => true)
        .AllowAnyHeader()
        .WithMethods("POST", "OPTIONS"));
});

// --- Data access (T011-T013) ---
var connectionString = builder.Configuration.GetConnectionString("AttributionDb")
    ?? throw new InvalidOperationException("ConnectionStrings:AttributionDb is not configured.");
builder.Services.AddSingleton<IDbConnectionFactory>(_ => new MySqlConnectionFactory(connectionString));
builder.Services.AddScoped<IUnitOfWork, UnitOfWork>();
builder.Services.AddScoped<IWebsiteRepository, WebsiteRepository>();
builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<IAuditRepository, AuditRepository>();
builder.Services.AddScoped<INumberPoolRepository, NumberPoolRepository>();
builder.Services.AddScoped<ITrackingNumberRepository, TrackingNumberRepository>();
builder.Services.AddScoped<IVisitorRepository, VisitorRepository>();
builder.Services.AddScoped<ISessionRepository, SessionRepository>();
builder.Services.AddScoped<IAllocationRepository, AllocationRepository>();
builder.Services.AddScoped<IAtomicAllocator, AtomicAllocator>();
builder.Services.AddScoped<ICallRepository, CallRepository>();
builder.Services.AddScoped<ICallLegRepository, CallLegRepository>();
builder.Services.AddScoped<IAttributionRepository, AttributionRepository>();
builder.Services.AddScoped<IIngestionCheckpointRepository, IngestionCheckpointRepository>();
builder.Services.AddScoped<IReviewCaseRepository, ReviewCaseRepository>();
builder.Services.AddScoped<IQualificationRuleRepository, QualificationRuleRepository>();
builder.Services.AddScoped<IQualificationResultRepository, QualificationResultRepository>();
builder.Services.AddScoped<IConversionPublicationRepository, ConversionPublicationRepository>();
builder.Services.AddScoped<IAlertRepository, AlertRepository>();
builder.Services.AddScoped<IAlertingMetricsRepository, AlertingRepository>();
builder.Services.AddScoped<INotificationDeliveryStatusRepository, NotificationDeliveryStatusRepository>();
builder.Services.AddScoped<IReportingRepository, ReportingRepository>();
builder.Services.AddScoped<Attribution.Application.Attribution.AttributionService>();
builder.Services.AddScoped<AllocationService>();
builder.Services.AddScoped<ShadowAllocationService>();
builder.Services.AddScoped<RuleVersioningService>();
builder.Services.AddScoped<ReportingService>();
builder.Services.AddScoped<PublicationService>();
builder.Services.AddScoped<QualificationService>();
// CorrectionService/ReviewResolutionService: manual review resolution (T096) can correct
// an already-published call, exactly like FR-045 re-derivation does — the same
// Google-Ads-retract-or-adjust path Attribution.Workers uses, needed here too since this
// is the only trigger for that path that runs inside the Api host rather than the Workers
// host's own re-derivation loop.
builder.Services.Configure<GoogleAdsClientOptions>(builder.Configuration.GetSection("GoogleAds"));
builder.Services.AddHttpClient<IGoogleAdsClient, GoogleAdsClient>();
builder.Services.AddScoped<CorrectionService>();
builder.Services.AddScoped<ReviewResolutionService>();

// --- Alerting (T091-T093, FR-047) ---
builder.Services.AddSingleton(builder.Configuration.GetSection("Alerting:Thresholds").Get<AlertingThresholds>() ?? new AlertingThresholds());
builder.Services.Configure<AlertingNotificationOptions>(builder.Configuration.GetSection("Alerting:Notifications"));
builder.Services.Configure<SmtpOptions>(builder.Configuration.GetSection("Smtp"));
builder.Services.AddScoped<AlertingService>();
builder.Services.AddScoped<IAlertEmailSender, SmtpAlertEmailSender>();
builder.Services.AddHttpClient<IAlertWebhookSender, AlertWebhookSender>(client => client.Timeout = TimeSpan.FromSeconds(10));

// --- Audit logging (T017, T018) ---
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IActorContext, HttpContextActorContext>();
builder.Services.AddScoped<IAuditLogger, AuditLogger>();

// --- Identity: OIDC federation + JWT issuance + break-glass (T014, T015) ---
var jwtSigningSecret = builder.Configuration["Jwt:SigningSecret"]
    ?? throw new InvalidOperationException("Jwt:SigningSecret is not configured.");
var jwtIssuer = builder.Configuration["Jwt:Issuer"] ?? "attribution-platform";
var jwtAudience = builder.Configuration["Jwt:Audience"] ?? "attribution-platform-api";
var tokenIssuer = new JwtTokenIssuer(jwtSigningSecret, jwtIssuer, jwtAudience);
builder.Services.AddSingleton<ITokenIssuer>(tokenIssuer);
builder.Services.AddSingleton(tokenIssuer); // also exposed concretely for BuildValidationParameters()
builder.Services.AddSingleton<BreakGlassAuthenticator>();
builder.Services.AddSingleton(_ =>
{
    // FR-046: provider-group -> platform-role mapping; configured per deployment.
    var mapping = builder.Configuration.GetSection("Identity:GroupRoleMapping")
        .GetChildren()
        .ToDictionary(c => c.Key, c => Enum.Parse<Role>(c.Value!));
    return new GroupRoleMapper(mapping);
});

// --- Authentication: validates the platform-issued JWT (T016) ---
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        // Without this, the handler remaps short claim names (JwtTokenIssuer's "role", "sub")
        // to long ClaimTypes.* URIs on inbound validation, so OperationAuthorizationHandler's
        // FindFirst("role") never matches and every RBAC-gated endpoint 403s regardless of role.
        options.MapInboundClaims = false;
        options.TokenValidationParameters = tokenIssuer.BuildValidationParameters();
    });

// --- Authorization: one policy per Operation, backed by RbacPolicy (T016, FR-038) ---
builder.Services.AddAuthorization(options =>
{
    foreach (var operation in Enum.GetValues<Operation>())
    {
        options.AddPolicy(operation.ToString(), policy =>
            policy.Requirements.Add(new OperationRequirement(operation)));
    }
});
builder.Services.AddSingleton<IAuthorizationHandler, OperationAuthorizationHandler>();

// --- Observability (T020, FR-041) ---
builder.Services.AddMetrics();
builder.Services.AddHealthChecks()
    .AddCheck<DatabaseHealthCheck>("database");

var app = builder.Build();

// Convenience for local/dev use (and anyone testing this build without a separate
// deploy-time migration step): apply pending migrations on startup. FluentMigrator
// tracks applied versions itself, so this is a safe no-op on an already-current schema.
// Off by default outside Development — a real deployment should run migrations as an
// explicit step, not implicitly on every instance's startup.
var runMigrationsOnStartup = builder.Configuration.GetValue(
    "Migrations:RunOnStartup", defaultValue: app.Environment.IsDevelopment());
if (runMigrationsOnStartup)
{
    MigrationRunner.ApplyMigrations(connectionString);
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseCors("DniClient");

app.UseMiddleware<RateLimitingMiddleware>();

app.UseMiddleware<AuthorizationFailureAuditMiddleware>();
app.UseAuthentication();
app.UseMiddleware<IntegrationServiceAccessMiddleware>();
app.UseAuthorization();
app.UseMiddleware<AuditLoggingMiddleware>();

app.MapControllers();
app.MapHealthChecks("/health");

app.Run();

// Exposed for WebApplicationFactory-based integration/contract tests.
public partial class Program { }
