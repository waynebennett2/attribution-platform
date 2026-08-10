using System.Reflection;
using FluentMigrator.Runner;
using Microsoft.Extensions.DependencyInjection;

namespace Attribution.Infrastructure.Data.Migrations;

// FluentMigrator runner entry point (research.md §13). Called once at deployment/startup
// time (see Attribution.Api's Program.cs) to bring the MySQL schema up to date.
public static class MigrationRunner
{
    public static void ApplyMigrations(string connectionString)
    {
        using var serviceProvider = new ServiceCollection()
            .AddFluentMigratorCore()
            .ConfigureRunner(rb => rb
                .AddMySql5()
                .WithGlobalConnectionString(connectionString)
                .ScanIn(Assembly.GetExecutingAssembly()).For.Migrations())
            .AddLogging(lb => lb.AddFluentMigratorConsole())
            .BuildServiceProvider(validateScopes: false);

        using var scope = serviceProvider.CreateScope();
        var runner = scope.ServiceProvider.GetRequiredService<FluentMigrator.Runner.IMigrationRunner>();
        runner.MigrateUp();
    }
}
