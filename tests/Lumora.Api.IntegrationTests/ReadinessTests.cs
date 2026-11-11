using System.Net;
using Lumora.Infrastructure.Persistence.Context;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Lumora.Api.IntegrationTests;

public sealed class ReadinessTests
{
    [Fact]
    public async Task Readiness_tracks_database_migrations_without_changing_liveness()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"lumora-api-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);

        try
        {
            var connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = Path.Combine(directory, "api-test.db"),
                ForeignKeys = true,
                Pooling = false
            }.ToString();
            await using var factory = new WebApplicationFactory<Program>()
                .WithWebHostBuilder(webHost =>
                {
                    webHost.ConfigureAppConfiguration((_, configuration) =>
                        configuration.AddInMemoryCollection(new Dictionary<string, string?>
                        {
                            ["ConnectionStrings:Sqlite"] = connectionString
                        }));
                    webHost.ConfigureServices(services =>
                        services.AddDbContext<LumoraDbContext>(options =>
                            options.UseSqlite(
                                connectionString,
                                sqlite => sqlite.MigrationsAssembly(typeof(LumoraDbContext).Assembly.FullName))));
                });
            using var client = factory.CreateClient();
            await using var scope = factory.Services.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<LumoraDbContext>();
            Assert.Equal(
                Path.GetFullPath(Path.Combine(directory, "api-test.db")),
                Path.GetFullPath(dbContext.Database.GetDbConnection().DataSource));
            Assert.NotEmpty(await dbContext.Database.GetPendingMigrationsAsync());

            using var healthBeforeMigration = await client.GetAsync("/api/health");
            using var readinessBeforeMigration = await client.GetAsync("/api/ready");
            Assert.Equal(HttpStatusCode.OK, healthBeforeMigration.StatusCode);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, readinessBeforeMigration.StatusCode);

            await dbContext.Database.MigrateAsync();

            using var healthAfterMigration = await client.GetAsync("/api/health");
            using var readinessAfterMigration = await client.GetAsync("/api/ready");
            Assert.Equal(HttpStatusCode.OK, healthAfterMigration.StatusCode);
            Assert.Equal(HttpStatusCode.OK, readinessAfterMigration.StatusCode);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}
