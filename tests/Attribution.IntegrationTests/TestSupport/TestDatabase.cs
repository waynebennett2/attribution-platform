using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Attribution.IntegrationTests.TestSupport;

// This project's integration tests run against the shared MySQL database configured via
// Attribution.Api's gitignored appsettings.{Environment}.local.json — the same database
// that serves production — rather than a disposable per-test Testcontainer, so that
// testing exercises the real target rather than an approximation of it. Resolved once via
// a throwaway WebApplicationFactory (the same configuration-loading path the app itself
// uses, local-override precedence included) and cached for the process's lifetime so
// every test class sharing this connection string doesn't each boot its own host just to
// read it.
public static class TestDatabase
{
    public static string ConnectionString { get; } = Resolve();

    private static string Resolve()
    {
        using var factory = new WebApplicationFactory<Program>();
        return factory.Services.GetRequiredService<IConfiguration>().GetConnectionString("AttributionDb")
            ?? throw new InvalidOperationException("ConnectionStrings:AttributionDb is not configured.");
    }
}
