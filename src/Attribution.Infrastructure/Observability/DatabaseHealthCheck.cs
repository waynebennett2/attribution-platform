using Attribution.Infrastructure.Data;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Attribution.Infrastructure.Observability;

// FR-041: exposed via /health so the customer's own monitoring can consume it alongside
// the platform's own alerting (FR-047).
public sealed class DatabaseHealthCheck : IHealthCheck
{
    private readonly IDbConnectionFactory _connectionFactory;

    public DatabaseHealthCheck(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            using var connection = _connectionFactory.CreateOpenConnection();
            return Task.FromResult(HealthCheckResult.Healthy("MySQL connection established."));
        }
        catch (Exception ex)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy("MySQL connection failed.", ex));
        }
    }
}
