using System.Security.Cryptography;
using System.Text;
using Lumora.Application.Abstractions.Persistence;
using Lumora.Application.Abstractions.Security;
using Lumora.Domain.Entities;
using Lumora.Infrastructure.Persistence.Backup;
using Lumora.Infrastructure.Persistence.Context;
using Lumora.Infrastructure.Security;
using Lumora.Infrastructure.Security.KeyManagement;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Lumora.Infrastructure.Persistence.Sqlite;

namespace Lumora.Infrastructure.IntegrationTests;

public sealed class BackupRestoreTests
{
    [Fact]
    public async Task Online_backup_restores_wal_data_and_verifies_encrypted_payload()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var keyStore = new DpapiKeyMaterialStore(database.KeyDirectory);
        await keyStore.InitializeAsync(CancellationToken.None);
        var backupPath = Path.Combine(Path.GetDirectoryName(database.DatabasePath)!, "backup.lumora-backup");
        var restoredPath = Path.Combine(Path.GetDirectoryName(database.DatabasePath)!, "restored.db");
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        await using (var context = database.CreateContext())
        {
            await context.Database.OpenConnectionAsync();
            await using (var journalMode = context.Database.GetDbConnection().CreateCommand())
            {
                journalMode.CommandText = "PRAGMA journal_mode=WAL;";
                Assert.Equal("wal", (string?)await journalMode.ExecuteScalarAsync());
            }

            var encryption = new AesGcmEncryptionService(keyStore);
            var payload = await encryption.EncryptAsync(
                Encoding.UTF8.GetBytes("synthetic backup verification payload"),
                new EncryptionContext(tenantId, userId, userId, "User", "DisplayName"),
                CancellationToken.None);
            var now = DateTime.UtcNow;
            context.Tenants.Add(new Tenant(tenantId, "합성 테스트 조직", now));
            context.Users.Add(new User(userId, tenantId, payload, now));
            await context.SaveChangesAsync();

            Assert.True(File.Exists($"{database.DatabasePath}-wal"));
            var backupService = new SqliteBackupService(context, keyStore);
            var backupTask = backupService.CreateAsync(backupPath, CancellationToken.None);
            var concurrentWriteTask = Task.Run(async () =>
            {
                await using var concurrentContext = database.CreateContext();
                for (var index = 0; index < 20; index++)
                {
                    concurrentContext.Tenants.Add(new Tenant(
                        Guid.NewGuid(),
                        $"합성 동시 쓰기 {index}",
                        DateTime.UtcNow));
                    await concurrentContext.SaveChangesAsync();
                }
            });

            var backup = await backupTask;
            await concurrentWriteTask;
            Assert.True(backup.FileSizeBytes > 0);
            Assert.True(backup.ContainsEncryptedData);
            Assert.False(string.IsNullOrWhiteSpace(backup.SchemaVersion));

            var restored = await backupService.RestoreAsync(backupPath, restoredPath, CancellationToken.None);
            Assert.True(restored.ContainsEncryptedData);
            Assert.True(restored.FileSizeBytes > 0);
        }

        var restoredOptions = new DbContextOptionsBuilder<LumoraDbContext>()
            .UseSqlite(new SqliteConnectionStringBuilder
            {
                DataSource = restoredPath,
                ForeignKeys = true,
                Pooling = false
            }.ToString())
            .Options;
        await using var restoredContext = new LumoraDbContext(restoredOptions);
        var restoredUser = await restoredContext.Users.AsNoTracking().SingleOrDefaultAsync(user => user.Id == userId);
        Assert.NotNull(restoredUser);
        var restoredPlaintext = await new AesGcmEncryptionService(keyStore).DecryptAsync(
            restoredUser.DisplayName,
            new EncryptionContext(tenantId, userId, userId, "User", "DisplayName"),
            CancellationToken.None);
        Assert.Equal("synthetic backup verification payload", Encoding.UTF8.GetString(restoredPlaintext));
        CryptographicOperations.ZeroMemory(restoredPlaintext);
        Assert.Equal("ok", await ReadScalarAsync(restoredPath, "PRAGMA integrity_check;"));
        Assert.Empty(await ReadRowsAsync(restoredPath, "PRAGMA foreign_key_check;"));
    }

    [Fact]
    public async Task Restored_database_keeps_memory_and_structured_fact_encryption_and_hmac_search()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var keyStore = new DpapiKeyMaterialStore(database.KeyDirectory);
        await keyStore.InitializeAsync(CancellationToken.None);
        var directory = Path.GetDirectoryName(database.DatabasePath)!;
        var backupPath = Path.Combine(directory, "memory-facts.lumora-backup");
        var restoredPath = Path.Combine(directory, "memory-facts-restored.db");
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var memoryId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var scope = new TenantUserScope(tenantId, userId);

        var encryption = new AesGcmEncryptionService(keyStore);
        var userName = await encryption.EncryptAsync(
            Encoding.UTF8.GetBytes("Synthetic User"),
            new EncryptionContext(tenantId, userId, userId, "User", "DisplayName"),
            CancellationToken.None);
        var memoryValue = await encryption.EncryptAsync(
            Encoding.UTF8.GetBytes("Synthetic memory payload"),
            new EncryptionContext(tenantId, userId, memoryId, "MemoryPayload", "Value"),
            CancellationToken.None);
        var factValue = await encryption.EncryptAsync(
            Encoding.UTF8.GetBytes("2000-07-15"),
            new EncryptionContext(tenantId, userId, memoryId, "StructuredFact", "Value"),
            CancellationToken.None);
        var birthdayToken = await new HmacSearchTokenService(keyStore)
            .CreateBirthdayMonthTokenAsync(scope, 7, CancellationToken.None);

        await using (var context = database.CreateContext())
        {
            context.Tenants.Add(new Tenant(tenantId, "Synthetic tenant", now));
            context.Users.Add(new User(userId, tenantId, userName, now));
            context.Memories.Add(new MemoryItem(memoryId, tenantId, userId, MemoryType.Fact, now));
            context.MemoryPayloads.Add(new MemoryPayload(tenantId, userId, memoryId, memoryValue));
            context.StructuredFacts.Add(
                new StructuredFact(tenantId, userId, memoryId, "Person.Birthday", factValue));
            context.MemoryLookupTokens.Add(new MemoryLookupToken(
                Guid.NewGuid(),
                tenantId,
                userId,
                memoryId,
                LookupTokenType.BirthdayMonth,
                birthdayToken.KeyId,
                birthdayToken.Token));
            await context.SaveChangesAsync();

            var backupService = new SqliteBackupService(context, keyStore);
            var backup = await backupService.CreateAsync(backupPath, CancellationToken.None);
            Assert.True(backup.ContainsEncryptedData);

            context.Tenants.Add(new Tenant(Guid.NewGuid(), "Post-snapshot tenant", now));
            await context.SaveChangesAsync();
            Assert.Equal(2, await context.Tenants.CountAsync());

            await backupService.RestoreAsync(backupPath, restoredPath, CancellationToken.None);
        }

        var restoredConnectionString = new SqliteConnectionStringBuilder
        {
            DataSource = restoredPath,
            ForeignKeys = true,
            Pooling = false
        }.ToString();
        var restoredConfiguration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:Provider"] = "Sqlite",
                ["ConnectionStrings:Sqlite"] = restoredConnectionString,
                ["Security:KeyDirectory"] = database.KeyDirectory
            })
            .Build();
        await using var restoredServices = new ServiceCollection()
            .AddLogging()
            .AddLumoraPersistence(restoredConfiguration)
            .BuildServiceProvider();
        await using var scopeServices = restoredServices.CreateAsyncScope();
        var restoredContext = scopeServices.ServiceProvider.GetRequiredService<LumoraDbContext>();

        Assert.Equal(1, await restoredContext.Tenants.CountAsync());
        Assert.Single(await restoredContext.MemoryPayloads.AsNoTracking().ToListAsync());
        Assert.Single(await restoredContext.StructuredFacts.AsNoTracking().ToListAsync());
        Assert.Single(await restoredContext.MemoryLookupTokens.AsNoTracking().ToListAsync());

        var restoredPayload = await restoredContext.MemoryPayloads.AsNoTracking().SingleAsync();
        var memoryPlaintext = await encryption.DecryptAsync(
            restoredPayload.Value,
            new EncryptionContext(tenantId, userId, memoryId, "MemoryPayload", "Value"),
            CancellationToken.None);
        Assert.Equal("Synthetic memory payload", Encoding.UTF8.GetString(memoryPlaintext));
        CryptographicOperations.ZeroMemory(memoryPlaintext);

        var restoredFact = await restoredContext.StructuredFacts.AsNoTracking().SingleAsync();
        var factPlaintext = await encryption.DecryptAsync(
            restoredFact.Value,
            new EncryptionContext(tenantId, userId, memoryId, "StructuredFact", "Value"),
            CancellationToken.None);
        Assert.Equal("2000-07-15", Encoding.UTF8.GetString(factPlaintext));
        CryptographicOperations.ZeroMemory(factPlaintext);

        var tokenService = scopeServices.ServiceProvider.GetRequiredService<IMemoryLookupTokenRepository>();
        var matches = await tokenService.FindMemoryIdsAsync(
            scope,
            LookupTokenType.BirthdayMonth,
            birthdayToken.KeyId,
            birthdayToken.Token,
            new PageRequest(1, 10),
            CancellationToken.None);
        Assert.Equal(memoryId, Assert.Single(matches.Items));
        Assert.Equal("ok", await ReadScalarAsync(restoredPath, "PRAGMA integrity_check;"));
        Assert.Empty(await ReadRowsAsync(restoredPath, "PRAGMA foreign_key_check;"));
    }

    [Fact]
    public async Task Backup_files_are_authenticated_and_failed_restore_does_not_replace_existing_files()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var keyStore = new DpapiKeyMaterialStore(database.KeyDirectory);
        await keyStore.InitializeAsync(CancellationToken.None);
        var backupPath = Path.Combine(Path.GetDirectoryName(database.DatabasePath)!, "valid.lumora-backup");
        var corruptPath = Path.Combine(Path.GetDirectoryName(database.DatabasePath)!, "corrupt.lumora-backup");
        var truncatedPath = Path.Combine(Path.GetDirectoryName(database.DatabasePath)!, "truncated.lumora-backup");
        var existingPath = Path.Combine(Path.GetDirectoryName(database.DatabasePath)!, "existing.db");

        await using var context = database.CreateContext();
        var backupService = new SqliteBackupService(context, keyStore);
        await backupService.CreateAsync(backupPath, CancellationToken.None);
        var contents = await File.ReadAllBytesAsync(backupPath);
        contents[^1] ^= 0x80;
        await File.WriteAllBytesAsync(corruptPath, contents);
        await File.WriteAllBytesAsync(truncatedPath, contents.AsMemory(0, contents.Length - 1).ToArray());
        await File.WriteAllTextAsync(existingPath, "keep-existing");

        await Assert.ThrowsAnyAsync<CryptographicException>(
            () => backupService.RestoreAsync(corruptPath, Path.Combine(Path.GetDirectoryName(existingPath)!, "failed.db"), CancellationToken.None));
        await Assert.ThrowsAnyAsync<IOException>(
            () => backupService.RestoreAsync(truncatedPath, Path.Combine(Path.GetDirectoryName(existingPath)!, "truncated.db"), CancellationToken.None));
        await Assert.ThrowsAsync<IOException>(
            () => backupService.RestoreAsync(backupPath, existingPath, CancellationToken.None));
        Assert.Equal("keep-existing", await File.ReadAllTextAsync(existingPath));
        Assert.False(File.Exists(Path.Combine(Path.GetDirectoryName(existingPath)!, "failed.db")));

        var wrongKeyDirectory = Path.Combine(Path.GetDirectoryName(database.DatabasePath)!, "wrong-keys");
        var wrongKeyStore = new DpapiKeyMaterialStore(wrongKeyDirectory);
        await wrongKeyStore.InitializeAsync(CancellationToken.None);
        await Assert.ThrowsAnyAsync<CryptographicException>(
            () => new SqliteBackupService(context, wrongKeyStore).RestoreAsync(
                backupPath,
                Path.Combine(Path.GetDirectoryName(existingPath)!, "wrong-key.db"),
                CancellationToken.None));
        Assert.False(File.Exists(Path.Combine(Path.GetDirectoryName(existingPath)!, "wrong-key.db")));
    }

    [Fact]
    public async Task Failed_replacement_keeps_the_existing_database_unchanged()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var keyStore = new DpapiKeyMaterialStore(database.KeyDirectory);
        await keyStore.InitializeAsync(CancellationToken.None);
        var directory = Path.GetDirectoryName(database.DatabasePath)!;
        var corruptBackupPath = Path.Combine(directory, "invalid-source.lumora-backup");
        var missingBackupPath = Path.Combine(directory, "missing-source.lumora-backup");
        var safetyBackupPath = Path.Combine(directory, "failed-replacement-safety.lumora-backup");
        await File.WriteAllTextAsync(corruptBackupPath, "not an authenticated backup");
        var tenantId = Guid.NewGuid();

        await using (var context = database.CreateContext())
        {
            context.Tenants.Add(new Tenant(tenantId, "Preserve existing data", DateTime.UtcNow));
            await context.SaveChangesAsync();

            await Assert.ThrowsAsync<FileNotFoundException>(
                () => new SqliteBackupService(context, keyStore).ReplaceDatabaseAsync(
                    missingBackupPath,
                    database.DatabasePath,
                    safetyBackupPath,
                    userApprovedReplacement: true,
                    CancellationToken.None));
            await Assert.ThrowsAnyAsync<IOException>(
                () => new SqliteBackupService(context, keyStore).ReplaceDatabaseAsync(
                    corruptBackupPath,
                    database.DatabasePath,
                    safetyBackupPath,
                    userApprovedReplacement: true,
                    CancellationToken.None));
        }

        await using var verificationContext = database.CreateContext();
        var tenant = await verificationContext.Tenants.AsNoTracking().SingleAsync();
        Assert.Equal(tenantId, tenant.Id);
        Assert.Equal("Preserve existing data", tenant.Name);
        Assert.Equal("ok", await ReadScalarAsync(database.DatabasePath, "PRAGMA integrity_check;"));
        Assert.False(File.Exists(safetyBackupPath));
    }

    [Fact]
    public async Task Database_replacement_requires_explicit_approval_and_keeps_encrypted_safety_backup()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var keyStore = new DpapiKeyMaterialStore(database.KeyDirectory);
        await keyStore.InitializeAsync(CancellationToken.None);
        var directory = Path.GetDirectoryName(database.DatabasePath)!;
        var replacementPath = Path.Combine(directory, "replacement.lumora-backup");
        var safetyPath = Path.Combine(directory, "before-replacement.lumora-backup");

        await using (var context = database.CreateContext())
        {
            await new SqliteBackupService(context, keyStore).CreateAsync(replacementPath, CancellationToken.None);
        }

        await using (var context = database.CreateContext())
        {
            var backupService = new SqliteBackupService(context, keyStore);
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => backupService.ReplaceDatabaseAsync(
                    replacementPath,
                    database.DatabasePath,
                    safetyPath,
                    userApprovedReplacement: false,
                    CancellationToken.None));
        }

        Assert.False(File.Exists(safetyPath));
        await using (var context = database.CreateContext())
        {
            var now = DateTime.UtcNow;
            context.Tenants.Add(new Tenant(Guid.NewGuid(), "교체 전 합성 데이터", now));
            await context.SaveChangesAsync();
        }

        await using (var context = database.CreateContext())
        {
            var restored = await new SqliteBackupService(context, keyStore).ReplaceDatabaseAsync(
                replacementPath,
                database.DatabasePath,
                safetyPath,
                userApprovedReplacement: true,
                CancellationToken.None);
            Assert.False(restored.ContainsEncryptedData);
        }

        Assert.True(File.Exists(safetyPath));
        Assert.Equal("ok", await ReadScalarAsync(database.DatabasePath, "PRAGMA integrity_check;"));
        await using (var context = database.CreateContext())
        {
            Assert.Empty(await context.Tenants.AsNoTracking().ToListAsync());
            var safetyRestoredPath = Path.Combine(directory, "safety-restored.db");
            await new SqliteBackupService(context, keyStore).RestoreAsync(
                safetyPath,
                safetyRestoredPath,
                CancellationToken.None);
            var safetyOptions = new DbContextOptionsBuilder<LumoraDbContext>()
                .UseSqlite(new SqliteConnectionStringBuilder
                {
                    DataSource = safetyRestoredPath,
                    ForeignKeys = true,
                    Pooling = false
                }.ToString())
                .Options;
            await using var safetyContext = new LumoraDbContext(safetyOptions);
            Assert.Single(await safetyContext.Tenants.AsNoTracking().ToListAsync());
        }
    }

    [Fact]
    public async Task Large_database_backup_is_streamed_across_authenticated_chunks()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var keyStore = new DpapiKeyMaterialStore(database.KeyDirectory);
        await keyStore.InitializeAsync(CancellationToken.None);
        var directory = Path.GetDirectoryName(database.DatabasePath)!;
        var backupPath = Path.Combine(directory, "large.lumora-backup");
        var restoredPath = Path.Combine(directory, "large-restored.db");
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var expectedHash = Array.Empty<byte>();

        await using (var context = database.CreateContext())
        {
            var encryption = new AesGcmEncryptionService(keyStore);
            var plaintext = RandomNumberGenerator.GetBytes(2_500_000);
            expectedHash = SHA256.HashData(plaintext);
            try
            {
                var encrypted = await encryption.EncryptAsync(
                    plaintext,
                    new EncryptionContext(tenantId, userId, userId, "User", "DisplayName"),
                    CancellationToken.None);
                var now = DateTime.UtcNow;
                context.Tenants.Add(new Tenant(tenantId, "대용량 합성 데이터", now));
                context.Users.Add(new User(userId, tenantId, encrypted, now));
                await context.SaveChangesAsync();
            }
            finally
            {
                CryptographicOperations.ZeroMemory(plaintext);
            }

            var result = await new SqliteBackupService(context, keyStore)
                .CreateAsync(backupPath, CancellationToken.None);
            Assert.True(result.FileSizeBytes > 2_500_000);
            Assert.True(result.ContainsEncryptedData);
            var restored = await new SqliteBackupService(context, keyStore)
                .RestoreAsync(backupPath, restoredPath, CancellationToken.None);
            Assert.True(restored.ContainsEncryptedData);
        }

        var restoredOptions = new DbContextOptionsBuilder<LumoraDbContext>()
            .UseSqlite(new SqliteConnectionStringBuilder
            {
                DataSource = restoredPath,
                ForeignKeys = true,
                Pooling = false
            }.ToString())
            .Options;
        await using var restoredContext = new LumoraDbContext(restoredOptions);
        var restoredUser = await restoredContext.Users.AsNoTracking().SingleAsync();
        var restoredPlaintext = await new AesGcmEncryptionService(keyStore).DecryptAsync(
            restoredUser.DisplayName,
            new EncryptionContext(tenantId, userId, userId, "User", "DisplayName"),
            CancellationToken.None);
        Assert.True(CryptographicOperations.FixedTimeEquals(expectedHash, SHA256.HashData(restoredPlaintext)));
        CryptographicOperations.ZeroMemory(expectedHash);
        CryptographicOperations.ZeroMemory(restoredPlaintext);
    }

    [Fact]
    public async Task Retention_runs_only_after_a_new_verified_backup_and_honors_count_and_age()
    {
        await using var database = await SqliteTestDatabase.CreateAsync();
        var keyStore = new DpapiKeyMaterialStore(database.KeyDirectory);
        await keyStore.InitializeAsync(CancellationToken.None);
        var directory = Path.Combine(Path.GetDirectoryName(database.DatabasePath)!, "configured-backups");
        Directory.CreateDirectory(directory);
        var recentOld = Path.Combine(directory, "recent-old.lumora-backup");
        var expiredOld = Path.Combine(directory, "expired-old.lumora-backup");
        var existingTarget = Path.Combine(directory, "already-exists.db");
        await File.WriteAllTextAsync(recentOld, "older backup");
        await File.WriteAllTextAsync(expiredOld, "expired backup");
        await File.WriteAllTextAsync(existingTarget, "do not overwrite");
        File.SetLastWriteTimeUtc(recentOld, DateTime.UtcNow.AddDays(-1));
        File.SetLastWriteTimeUtc(expiredOld, DateTime.UtcNow.AddDays(-120));
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Backup:Directory"] = directory,
                ["Backup:RetentionCount"] = "2",
                ["Backup:RetentionDays"] = "90"
            })
            .Build();

        await using var context = database.CreateContext();
        var backupService = new SqliteBackupService(context, keyStore, configuration);
        await Assert.ThrowsAsync<IOException>(
            () => backupService.CreateAsync(existingTarget, CancellationToken.None));
        Assert.True(File.Exists(recentOld));
        Assert.True(File.Exists(expiredOld));

        var latest = Path.Combine(directory, "latest.lumora-backup");
        await backupService.CreateAsync(latest, CancellationToken.None);
        Assert.True(File.Exists(latest));
        Assert.True(File.Exists(recentOld));
        Assert.False(File.Exists(expiredOld));
        Assert.Equal("do not overwrite", await File.ReadAllTextAsync(existingTarget));
    }

    private static async Task<string?> ReadScalarAsync(string databasePath, string sql)
    {
        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false
        }.ToString());
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return (string?)await command.ExecuteScalarAsync();
    }

    private static async Task<List<string>> ReadRowsAsync(string databasePath, string sql)
    {
        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false
        }.ToString());
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<string>();
        while (await reader.ReadAsync())
        {
            rows.Add(reader.GetValue(0).ToString() ?? string.Empty);
        }

        return rows;
    }
}
