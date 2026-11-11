using Lumora.Infrastructure.Persistence.Context;
using Lumora.Infrastructure.Persistence.Sqlite;
using Lumora.Application.Abstractions.Persistence;
using Lumora.Application.Abstractions.Security;
using Lumora.Application.UseCases.Memories;
using Lumora.Application.UseCases.StructuredFacts;
using Lumora.Application.UseCases.Conversations;
using Lumora.Infrastructure.Security.KeyManagement;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Lumora.Infrastructure.IntegrationTests;

internal sealed class SqliteTestDatabase : IAsyncDisposable
{
    private readonly string _directory;
    private readonly ServiceProvider _services;

    private SqliteTestDatabase(string directory, ServiceProvider services, string connectionString)
    {
        _directory = directory;
        _services = services;
        DatabasePath = new SqliteConnectionStringBuilder(connectionString).DataSource;
        KeyDirectory = Path.Combine(directory, "keys");
        Options = new DbContextOptionsBuilder<LumoraDbContext>()
            .UseSqlite(connectionString)
            .Options;
    }

    public DbContextOptions<LumoraDbContext> Options { get; }

    public string DatabasePath { get; }

    public string KeyDirectory { get; }

    public static async Task<SqliteTestDatabase> CreateAsync()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"lumora-sqlite-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(directory, "test.db"),
            ForeignKeys = true,
            Pooling = false
        }.ToString();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:Provider"] = "Sqlite",
                ["ConnectionStrings:Sqlite"] = connectionString,
                ["Security:KeyDirectory"] = Path.Combine(directory, "keys")
            })
            .Build();
        var services = new ServiceCollection()
            .AddLogging()
            .AddLumoraPersistence(configuration)
            .AddScoped<IMemoryUseCases, MemoryUseCases>()
            .AddScoped<IStructuredFactUseCases, StructuredFactUseCases>()
            .AddScoped<IConversationUseCases, ConversationUseCases>()
            .BuildServiceProvider();
        var database = new SqliteTestDatabase(directory, services, connectionString);

        await using var dbContext = database.CreateContext();
        await dbContext.Database.MigrateAsync();
        return database;
    }

    public LumoraDbContext CreateContext() => new(Options);

    public AsyncServiceScope CreateScope() => _services.CreateAsyncScope();

    public Task InitializeKeysAsync() =>
        _services.GetRequiredService<IKeyMaterialInitializer>().InitializeAsync(CancellationToken.None);

    public async ValueTask DisposeAsync()
    {
        await _services.DisposeAsync();
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
