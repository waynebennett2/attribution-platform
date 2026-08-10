using Attribution.Api.Middleware;
using Attribution.Application.Administration;
using Attribution.Application.Allocation;
using Attribution.Domain.Audit;
using Attribution.Domain.Identity;
using Attribution.Domain.Pools;
using Attribution.Domain.Sessions;
using Attribution.Domain.Websites;
using Attribution.Infrastructure.Data;
using Attribution.Infrastructure.Identity;
using Attribution.Infrastructure.Observability;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;

var builder = WebApplication.CreateBuilder(args);

// FR-041: structured (JSON) logs so a single call can be traced end to end by any
// downstream log aggregator.
builder.Logging.AddJsonConsole();

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

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
builder.Services.AddScoped<AllocationService>();

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
    .AddJwtBearer(options => options.TokenValidationParameters = tokenIssuer.BuildValidationParameters());

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

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseMiddleware<RateLimitingMiddleware>();

app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<AuditLoggingMiddleware>();

app.MapControllers();
app.MapHealthChecks("/health");

app.Run();

// Exposed for WebApplicationFactory-based integration/contract tests.
public partial class Program { }
