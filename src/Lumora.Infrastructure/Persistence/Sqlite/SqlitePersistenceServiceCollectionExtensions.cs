using Lumora.Application.Abstractions.Persistence;
using Lumora.Application.Abstractions.Security;
using Lumora.Infrastructure.Persistence.Backup;
using Lumora.Infrastructure.Persistence.Context;
using Lumora.Infrastructure.Persistence.Repositories;
using Lumora.Infrastructure.Security;
using Lumora.Infrastructure.Security.KeyManagement;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Lumora.Infrastructure.Persistence.Sqlite;

public static class SqlitePersistenceServiceCollectionExtensions
{
    public static IServiceCollection AddLumoraPersistence(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var provider = configuration["Database:Provider"];
        if (!string.Equals(provider, "Sqlite", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"지원하지 않는 Database:Provider 설정입니다: '{provider ?? "(없음)"}'. 현재는 Sqlite만 지원합니다.");
        }

        var connectionString = SqliteConnectionStringFactory.Create(configuration);
        services.AddDbContext<LumoraDbContext>(options =>
            options.UseSqlite(
                connectionString,
                sqlite => sqlite.MigrationsAssembly(typeof(LumoraDbContext).Assembly.FullName)));

        services.AddScoped<ITenantRepository, TenantRepository>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IConversationRepository, ConversationRepository>();
        services.AddScoped<IMessageRepository, MessageRepository>();
        services.AddScoped<IMemoryRepository, MemoryRepository>();
        services.AddScoped<IStructuredFactRepository, StructuredFactRepository>();
        services.AddScoped<IMemoryLookupTokenRepository, MemoryLookupTokenRepository>();
        services.AddScoped<IUnitOfWork, SqliteUnitOfWork>();
        services.AddSingleton(_ => new DpapiKeyMaterialStore(configuration));
        services.AddSingleton<IKeyMaterialProvider>(provider => provider.GetRequiredService<DpapiKeyMaterialStore>());
        services.AddSingleton<IKeyMaterialInitializer>(provider => provider.GetRequiredService<DpapiKeyMaterialStore>());
        services.AddSingleton<IKeyMaterialRecoveryTarget>(provider => provider.GetRequiredService<DpapiKeyMaterialStore>());
        services.AddSingleton<PasswordProtectedKeyRecoveryService>();
        services.AddSingleton<BackupStatusStore>();
        services.AddScoped<SqliteBackupService>();
        services.AddScoped<IEncryptionService, AesGcmEncryptionService>();
        services.AddScoped<ISearchTokenService, HmacSearchTokenService>();
        return services;
    }
}

internal static class SqliteConnectionStringFactory
{
    public static string Create(IConfiguration configuration)
    {
        var configuredConnectionString = configuration.GetConnectionString("Sqlite");
        if (!string.IsNullOrWhiteSpace(configuredConnectionString))
        {
            var configuredBuilder = new SqliteConnectionStringBuilder(configuredConnectionString)
            {
                ForeignKeys = true
            };
            if (!string.Equals(configuredBuilder.DataSource, ":memory:", StringComparison.OrdinalIgnoreCase))
            {
                var configuredPath = Path.GetFullPath(configuredBuilder.DataSource);
                var configuredDirectory = Path.GetDirectoryName(configuredPath)
                    ?? throw new InvalidOperationException("SQLite 데이터베이스 디렉터리를 확인할 수 없습니다.");
                Directory.CreateDirectory(configuredDirectory);
                configuredBuilder.DataSource = configuredPath;
            }

            return configuredBuilder.ToString();
        }

        var dataDirectory = configuration["Database:Sqlite:DataDirectory"];
        if (string.IsNullOrWhiteSpace(dataDirectory))
        {
            dataDirectory = Environment.GetEnvironmentVariable("LUMORA_DATA_DIRECTORY");
        }

        if (string.IsNullOrWhiteSpace(dataDirectory))
        {
            var localApplicationData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrWhiteSpace(localApplicationData))
            {
                throw new InvalidOperationException(
                    "SQLite 데이터 경로가 없습니다. Database:Sqlite:DataDirectory 또는 LUMORA_DATA_DIRECTORY를 설정하십시오.");
            }

            dataDirectory = Path.Combine(localApplicationData, "P.P. AI - LUMORA", "data");
        }

        var fullDataDirectory = Path.GetFullPath(dataDirectory);
        Directory.CreateDirectory(fullDataDirectory);
        return new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(fullDataDirectory, "lumora.db"),
            ForeignKeys = true
        }.ToString();
    }
}
