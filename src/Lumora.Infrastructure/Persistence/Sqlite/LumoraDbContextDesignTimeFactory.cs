using Lumora.Infrastructure.Persistence.Context;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Lumora.Infrastructure.Persistence.Sqlite;

public sealed class LumoraDbContextDesignTimeFactory : IDesignTimeDbContextFactory<LumoraDbContext>
{
    public LumoraDbContext CreateDbContext(string[] args)
    {
        var dataDirectory = Environment.GetEnvironmentVariable("LUMORA_DATA_DIRECTORY");
        if (string.IsNullOrWhiteSpace(dataDirectory))
        {
            var localApplicationData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrWhiteSpace(localApplicationData))
            {
                throw new InvalidOperationException("LUMORA_DATA_DIRECTORY를 지정해야 합니다.");
            }

            dataDirectory = Path.Combine(localApplicationData, "P.P. AI - LUMORA", "data");
        }

        var fullDataDirectory = Path.GetFullPath(dataDirectory);
        Directory.CreateDirectory(fullDataDirectory);
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(fullDataDirectory, "lumora.db"),
            ForeignKeys = true
        }.ToString();
        var options = new DbContextOptionsBuilder<LumoraDbContext>()
            .UseSqlite(
                connectionString,
                sqlite => sqlite.MigrationsAssembly(typeof(LumoraDbContext).Assembly.FullName))
            .Options;

        return new LumoraDbContext(options);
    }
}
