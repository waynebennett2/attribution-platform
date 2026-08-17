using Attribution.Api.Controllers;
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

// A no-op everywhere except when actually started by the Windows Service Control Manager
// (e.g. `sc.exe start "Attribution Api"` on a Windows Server deployment with no Docker) —
// picks the right lifetime/logging so `net stop`/`net start` and Windows' own crash
// recovery work correctly. Harmless for `dotnet run`, IIS, Linux/systemd, or a container.
builder.Services.AddWindowsService(options => options.ServiceName = "Attribution Api");

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
// Constitution Principle III (API-First): every backend capability is exposed through
// versioned REST APIs, so this generated document is the actual interface contract for
// every consumer (reporting frontend, DNI client, future portals) — not merely a
// developer convenience. A static snapshot is published to docs/openapi.json (T102);
// regenerate it by running the API in Development and saving the response of
// GET /swagger/v1/swagger.json whenever an endpoint's shape changes.
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Title = "Call Attribution Platform API",
        Version = "v1",
        Description = "Deterministic call attribution: number allocation, ingestion, qualification, publication and administration. "
            + "See specs/001-call-attribution-platform/contracts/ for the narrative wire contracts this schema implements.",
    });

    // Every admin/report endpoint requires the platform-issued JWT (FR-037); this lets
    // Swagger UI's "Authorize" button attach one, matching how a real consumer calls in.
    var bearerScheme = new Microsoft.OpenApi.Models.OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = Microsoft.OpenApi.Models.ParameterLocation.Header,
        Reference = new Microsoft.OpenApi.Models.OpenApiReference { Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme, Id = "Bearer" },
    };
    options.AddSecurityDefinition("Bearer", bearerScheme);
    options.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement { [bearerScheme] = Array.Empty<string>() });
});

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

    // The separate admin+reporting UI (attribution-ui) is a genuinely different origin in
    // any real deployment (its own host/port), unlike the DNI script above which runs
    // embedded on an arbitrary customer site. Unlike that endpoint, every request here
    // already carries an Authorization header and is authenticated/RBAC-checked server-side
    // (FR-037, FR-038), so the CORS policy's job is only to name which UI origins may reach
    // it at all, not to substitute for that authorization.
    var adminUiOrigins = builder.Configuration.GetSection("Cors:AdminUiOrigins").Get<string[]>() ?? [];
    options.AddPolicy("AdminUi", policy => policy
        .WithOrigins(adminUiOrigins)
        .AllowAnyHeader()
        .WithMethods("GET", "POST", "DELETE", "OPTIONS"));
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

// --- Tracking number import (T144, T145, FR-051) ---
builder.Services.Configure<NumberImportOptions>(builder.Configuration.GetSection("NumberImport"));

// --- Alerting (T091-T093, FR-047) ---
builder.Services.AddSingleton(builder.Configuration.GetSection("Alerting:Thresholds").Get<AlertingThresholds>() ?? new AlertingThresholds());
builder.Services.Configure<AlertingNotificationOptions>(builder.Configuration.GetSection("Alerting:Notifications"));
builder.Services.Configure<SmtpOptions>(builder.Configuration.GetSection("Smtp"));
builder.Services.AddScoped<AlertingService>();
builder.Services.AddScoped<IAlertEmailSender, SmtpAlertEmailSender>();
builder.Services.AddHttpClient<IAlertWebhookSender, AlertWebhookSender>(client => client.Timeout = TimeSpan.FromSeconds(10));

// --- Retention (T100-T101, FR-039, FR-040) ---
var retentionPolicy = builder.Configuration.GetSection("Retention").Get<RetentionPolicy>() ?? new RetentionPolicy();
if (string.IsNullOrWhiteSpace(retentionPolicy.HmacKey) || retentionPolicy.HmacKey.Length < 32)
{
    throw new InvalidOperationException("Retention:HmacKey must be configured and at least 32 characters.");
}
builder.Services.AddSingleton(retentionPolicy);
builder.Services.AddScoped<IRetentionRepository, RetentionRepository>();
builder.Services.AddScoped<RetentionService>();

// --- Audit logging (T017, T018) ---
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IActorContext, HttpContextActorContext>();
builder.Services.AddScoped<IAuditLogger, AuditLogger>();

// --- Identity: local username/password + TOTP MFA + JWT access/refresh tokens (T014, T015, T140, T141) ---
var jwtSigningSecret = builder.Configuration["Jwt:SigningSecret"]
    ?? throw new InvalidOperationException("Jwt:SigningSecret is not configured.");
var jwtIssuer = builder.Configuration["Jwt:Issuer"] ?? "attribution-platform";
var jwtAudience = builder.Configuration["Jwt:Audience"] ?? "attribution-platform-api";
var tokenIssuer = new JwtTokenIssuer(jwtSigningSecret, jwtIssuer, jwtAudience);
builder.Services.AddSingleton<ITokenIssuer>(tokenIssuer);
builder.Services.AddSingleton(tokenIssuer); // also exposed concretely for BuildValidationParameters()
builder.Services.AddSingleton<LocalAuthenticator>();

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

// `dotnet Attribution.Api.dll migrate` applies pending migrations then exits, without
// starting Kestrel — the "explicit step" the comment below refers to, run once before the
// very first start and again before each upgrade that ships a new migration. FluentMigrator
// tracks applied versions itself, so re-running it against an already-current schema is a
// safe no-op.
if (args.Length > 0 && args[0] == "migrate")
{
    MigrationRunner.ApplyMigrations(connectionString);
    Console.WriteLine("Migrations applied.");
    return 0;
}

// Convenience for local/dev use (and anyone testing this build without a separate
// deploy-time migration step): apply pending migrations on startup. FluentMigrator
// tracks applied versions itself, so this is a safe no-op on an already-current schema.
// Off by default outside Development — a real deployment should run migrations as an
// explicit step (`migrate`, above), not implicitly on every instance's startup.
var runMigrationsOnStartup = builder.Configuration.GetValue(
    "Migrations:RunOnStartup", defaultValue: app.Environment.IsDevelopment());
if (runMigrationsOnStartup)
{
    MigrationRunner.ApplyMigrations(connectionString);
}

// FR-046: there is no self-registration, so a brand-new deployment has no way to sign in
// at all until one System Administrator account exists. `dotnet Attribution.Api.dll
// seed-admin <username> <password>` provisions exactly one, using the same DI-registered
// IUserRepository/LocalAuthenticator every other admin-account creation path uses (not a
// separate ad-hoc script), then exits without starting the Kestrel listener. Safe to run
// again later for a second account (e.g. a break-in-case-of-lockout spare) — it only
// refuses a username already in use.
if (args.Length > 0 && args[0] == "seed-admin")
{
    return SeedAdminAsync(app.Services, args).GetAwaiter().GetResult();
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
else
{
    // Constitution Principle VI: "All endpoints require TLS." HSTS tells the browser to
    // upgrade every future request itself, so a stray plain-HTTP link or bookmark can
    // never even reach the redirect below in the first place. Skipped in Development,
    // where the local cert is often self-signed and HSTS's caching would be a nuisance
    // to clear.
    app.UseHsts();
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
return 0;

// FR-046: provisions one System Administrator local account directly against
// IUserRepository/LocalAuthenticator — the same path CreateBreakGlassUserRequest.../
// AdminUsersController.Create uses, just reachable before any account exists to sign in
// and call that endpoint. Prints the TOTP provisioning URI once, exactly like the HTTP
// endpoint does, since there is nowhere else it is ever shown again.
static async Task<int> SeedAdminAsync(IServiceProvider services, string[] args)
{
    if (args.Length < 3)
    {
        Console.Error.WriteLine("Usage: dotnet Attribution.Api.dll seed-admin <username> <password>");
        return 1;
    }

    var username = args[1];
    var password = args[2];

    using var scope = services.CreateScope();
    var userRepository = scope.ServiceProvider.GetRequiredService<IUserRepository>();
    var authenticator = scope.ServiceProvider.GetRequiredService<LocalAuthenticator>();

    if (await userRepository.GetByUsernameAsync(username) is not null)
    {
        Console.Error.WriteLine($"Username '{username}' is already in use.");
        return 1;
    }

    var totpSecret = LocalAuthenticator.GenerateTotpSecret();
    var user = User.CreateLocal(username, Role.SystemAdministrator, authenticator.HashPassword(password), totpSecret);
    await userRepository.AddAsync(user);

    var otpAuthUri = $"otpauth://totp/AttributionPlatform:{Uri.EscapeDataString(username)}?secret={totpSecret}&issuer=AttributionPlatform";
    Console.WriteLine($"Created System Administrator '{username}'.");
    Console.WriteLine("Add this to an authenticator app now — it is never shown again:");
    Console.WriteLine(otpAuthUri);
    return 0;
}

// Exposed for WebApplicationFactory-based integration/contract tests.
public partial class Program { }
