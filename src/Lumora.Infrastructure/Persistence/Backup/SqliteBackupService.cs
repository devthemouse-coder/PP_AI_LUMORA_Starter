using System.Security.Cryptography;
using Lumora.Application.Abstractions.Security;
using Lumora.Domain.Entities;
using Lumora.Infrastructure.Persistence.Context;
using Lumora.Infrastructure.Security;
using Lumora.Infrastructure.Security.KeyManagement;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Lumora.Infrastructure.Persistence.Backup;

public sealed class SqliteBackupService
{
    private const int SqliteBusyResult = 1;
    private readonly LumoraDbContext _dbContext;
    private readonly IKeyMaterialProvider _keyMaterialProvider;
    private readonly IConfiguration? _configuration;

    public SqliteBackupService(
        LumoraDbContext dbContext,
        IKeyMaterialProvider keyMaterialProvider,
        IConfiguration? configuration = null)
    {
        _dbContext = dbContext;
        _keyMaterialProvider = keyMaterialProvider;
        _configuration = configuration;
    }

    public async Task<BackupFileMetadata> CreateAsync(
        string backupFilePath,
        CancellationToken cancellationToken,
        IReadOnlyCollection<string>? retentionExclusions = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(backupFilePath);
        var retentionOptions = BackupRetentionOptions.FromConfiguration(_configuration);
        var configuredBackupDirectory = BackupDirectoryResolver.Resolve(_configuration);
        var sourcePath = ResolveDatabasePath(_dbContext.Database.GetDbConnection().DataSource);
        var backupPath = Path.GetFullPath(backupFilePath);
        if (PathsEqual(sourcePath, backupPath))
        {
            throw new ArgumentException("백업 파일 경로는 운영 DB 경로와 달라야 합니다.", nameof(backupFilePath));
        }

        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException("백업할 SQLite 데이터베이스를 찾을 수 없습니다.");
        }

        var directory = Path.GetDirectoryName(backupPath)
            ?? throw new InvalidOperationException("백업 파일 디렉터리를 확인할 수 없습니다.");
        Directory.CreateDirectory(directory);
        if (File.Exists(backupPath))
        {
            throw new IOException("기존 백업 파일은 덮어쓰지 않습니다. 다른 경로를 선택하십시오.");
        }

        var migrationIds = _dbContext.Database.GetMigrations().ToArray();
        if (migrationIds.Length == 0)
        {
            throw new InvalidOperationException("SQLite Migration을 찾을 수 없어 백업 스키마를 기록할 수 없습니다.");
        }

        var snapshotPath = Path.Combine(Path.GetTempPath(), $"lumora-{Guid.NewGuid():N}.snapshot");
        var encryptedTemporaryPath = $"{backupPath}.{Guid.NewGuid():N}.partial";
        try
        {
            await CreateOnlineSnapshotAsync(sourcePath, snapshotPath, cancellationToken);
            using var keys = await _keyMaterialProvider.LoadAsync(cancellationToken);
            var containsEncryptedData = await ValidateDatabaseAsync(
                snapshotPath,
                migrationIds[^1],
                keys,
                cancellationToken);
            var metadata = await AesGcmBackupFile.EncryptAsync(
                snapshotPath,
                encryptedTemporaryPath,
                keys,
                migrationIds[^1],
                DateTimeOffset.UtcNow,
                cancellationToken);

            var verificationPath = Path.Combine(Path.GetTempPath(), $"lumora-{Guid.NewGuid():N}.verify");
            try
            {
                var (decryptedMetadata, _) = await AesGcmBackupFile.DecryptAsync(
                    encryptedTemporaryPath,
                    verificationPath,
                    keys,
                    cancellationToken);
                if (!string.Equals(decryptedMetadata.SchemaVersion, migrationIds[^1], StringComparison.Ordinal))
                {
                    throw new CryptographicException("백업 파일의 스키마 버전 검증에 실패했습니다.");
                }

                await ValidateDatabaseAsync(
                    verificationPath,
                    migrationIds[^1],
                    keys,
                    cancellationToken);
            }
            finally
            {
                DeleteTemporaryFile(verificationPath);
            }

            cancellationToken.ThrowIfCancellationRequested();
            File.Move(encryptedTemporaryPath, backupPath);
            ApplyRetentionPolicy(
                backupPath,
                configuredBackupDirectory,
                retentionOptions,
                retentionExclusions ?? []);
            return metadata with { ContainsEncryptedData = containsEncryptedData };
        }
        finally
        {
            DeleteTemporaryFile(snapshotPath);
            DeleteTemporaryFile(encryptedTemporaryPath);
        }
    }

    public async Task<BackupFileMetadata> RestoreAsync(
        string backupFilePath,
        string restoredDatabasePath,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(backupFilePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(restoredDatabasePath);
        var backupPath = Path.GetFullPath(backupFilePath);
        var destinationPath = Path.GetFullPath(restoredDatabasePath);
        if (PathsEqual(backupPath, destinationPath))
        {
            throw new ArgumentException("복원 DB 경로는 백업 파일 경로와 달라야 합니다.", nameof(restoredDatabasePath));
        }

        if (!File.Exists(backupPath))
        {
            throw new FileNotFoundException("복원할 백업 파일을 찾을 수 없습니다.");
        }

        if (File.Exists(destinationPath))
        {
            throw new IOException("복원은 기존 파일을 덮어쓰지 않습니다. 새로운 대상 경로를 사용하십시오.");
        }

        if (File.Exists($"{destinationPath}-wal") || File.Exists($"{destinationPath}-shm"))
        {
            throw new IOException("대상 DB에 WAL/SHM 파일이 이미 있어 안전한 복원을 중단했습니다.");
        }

        var directory = Path.GetDirectoryName(destinationPath)
            ?? throw new InvalidOperationException("복원 DB 디렉터리를 확인할 수 없습니다.");
        Directory.CreateDirectory(directory);
        var stagedPath = Path.Combine(directory, $".{Guid.NewGuid():N}.restore");
        try
        {
            using var keys = await _keyMaterialProvider.LoadAsync(cancellationToken);
            var (metadata, _) = await AesGcmBackupFile.DecryptAsync(
                backupPath,
                stagedPath,
                keys,
                cancellationToken);
            var migrations = _dbContext.Database.GetMigrations().ToArray();
            if (migrations.Length == 0 ||
                !string.Equals(metadata.SchemaVersion, migrations[^1], StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "백업 스키마가 현재 애플리케이션 Migration과 일치하지 않습니다. 자동 스키마 변경은 수행하지 않습니다.");
            }

            var containsEncryptedData = await ValidateDatabaseAsync(
                stagedPath,
                metadata.SchemaVersion,
                keys,
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(stagedPath, destinationPath);
            return metadata with
            {
                FileSizeBytes = new FileInfo(backupPath).Length,
                ContainsEncryptedData = containsEncryptedData
            };
        }
        finally
        {
            DeleteTemporaryFile(stagedPath);
        }
    }

    public async Task<BackupFileMetadata> ReplaceDatabaseAsync(
        string backupFilePath,
        string activeDatabasePath,
        string safetyBackupPath,
        bool userApprovedReplacement,
        CancellationToken cancellationToken)
    {
        if (!userApprovedReplacement)
        {
            throw new InvalidOperationException("기존 DB 교체는 사용자의 명시적 승인이 필요합니다.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(activeDatabasePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(safetyBackupPath);
        var activePath = Path.GetFullPath(activeDatabasePath);
        var safetyPath = Path.GetFullPath(safetyBackupPath);
        if (!File.Exists(activePath))
        {
            return await RestoreAsync(backupFilePath, activePath, cancellationToken);
        }

        if (PathsEqual(activePath, safetyPath) ||
            PathsEqual(activePath, backupFilePath) ||
            PathsEqual(safetyPath, backupFilePath))
        {
            throw new ArgumentException("사전 보호 백업, 복원 백업, 운영 DB 경로는 서로 달라야 합니다.");
        }

        var directory = Path.GetDirectoryName(activePath)
            ?? throw new InvalidOperationException("운영 DB 디렉터리를 확인할 수 없습니다.");
        var stagedPath = Path.Combine(directory, $".{Guid.NewGuid():N}.replacement");
        try
        {
            var metadata = await RestoreAsync(backupFilePath, stagedPath, cancellationToken);
            await CreateAsync(safetyPath, cancellationToken, [backupFilePath]);
            await CheckpointAndCloseDatabaseAsync(activePath, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            if (File.Exists(activePath))
            {
                File.Replace(stagedPath, activePath, destinationBackupFileName: null, ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(stagedPath, activePath);
            }

            return metadata;
        }
        finally
        {
            DeleteTemporaryFile(stagedPath);
        }
    }

    private async Task<bool> ValidateDatabaseAsync(
        string databasePath,
        string expectedSchemaVersion,
        KeyMaterial keys,
        CancellationToken cancellationToken)
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWrite,
            ForeignKeys = true,
            Pooling = false
        }.ToString();
        await using (var connection = new SqliteConnection(connectionString))
        {
            await connection.OpenAsync(cancellationToken);
            await using (var integrity = connection.CreateCommand())
            {
                integrity.CommandText = "PRAGMA integrity_check;";
                var integrityResult = (string?)await integrity.ExecuteScalarAsync(cancellationToken);
                if (!string.Equals(integrityResult, "ok", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException("SQLite integrity_check가 실패했습니다.");
                }
            }

            await using (var foreignKeys = connection.CreateCommand())
            {
                foreignKeys.CommandText = "PRAGMA foreign_key_check;";
                await using var reader = await foreignKeys.ExecuteReaderAsync(cancellationToken);
                if (await reader.ReadAsync(cancellationToken))
                {
                    throw new InvalidDataException("SQLite foreign_key_check가 실패했습니다.");
                }
            }

            await using var migrations = connection.CreateCommand();
            migrations.CommandText =
                "SELECT MigrationId FROM __EFMigrationsHistory ORDER BY MigrationId DESC LIMIT 1;";
            var appliedSchemaVersion = (string?)await migrations.ExecuteScalarAsync(cancellationToken);
            if (!string.Equals(appliedSchemaVersion, expectedSchemaVersion, StringComparison.Ordinal))
            {
                throw new InvalidDataException("SQLite Migration 버전이 백업 메타데이터와 일치하지 않습니다.");
            }
        }

        var options = new DbContextOptionsBuilder<LumoraDbContext>()
            .UseSqlite(
                connectionString,
                sqlite => sqlite.MigrationsAssembly(typeof(LumoraDbContext).Assembly.FullName))
            .Options;
        await using var context = new LumoraDbContext(options);
        var user = await context.Users.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
        if (user is null)
        {
            if (await context.MemoryLookupTokens.AsNoTracking()
                    .AnyAsync(token => token.KeyId != keys.SearchKeyId, cancellationToken))
            {
                throw new CryptographicException("백업의 HMAC 토큰에 필요한 검색 키가 현재 키와 일치하지 않습니다.");
            }

            return false;
        }

        await ValidateEncryptedPayloadAsync(
            user.DisplayName,
            user.TenantId,
            user.Id,
            user.Id,
            nameof(User),
            nameof(User.DisplayName),
            keys,
            cancellationToken);

        var memoryPayload = await context.MemoryPayloads.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
        if (memoryPayload is not null)
        {
            await ValidateEncryptedPayloadAsync(
                memoryPayload.Value,
                memoryPayload.TenantId,
                memoryPayload.UserId,
                memoryPayload.MemoryId,
                nameof(MemoryPayload),
                nameof(MemoryPayload.Value),
                keys,
                cancellationToken);
        }

        var structuredFact = await context.StructuredFacts.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
        if (structuredFact is not null)
        {
            await ValidateEncryptedPayloadAsync(
                structuredFact.Value,
                structuredFact.TenantId,
                structuredFact.UserId,
                structuredFact.MemoryId,
                nameof(StructuredFact),
                nameof(StructuredFact.Value),
                keys,
                cancellationToken);
        }

        if (await context.MemoryLookupTokens.AsNoTracking()
                .AnyAsync(token => token.KeyId != keys.SearchKeyId, cancellationToken))
        {
            throw new CryptographicException("백업의 HMAC 토큰에 필요한 검색 키가 현재 키와 일치하지 않습니다.");
        }

        return true;
    }

    private async Task ValidateEncryptedPayloadAsync(
        EncryptedPayload payload,
        Guid tenantId,
        Guid userId,
        Guid recordId,
        string entityType,
        string fieldName,
        KeyMaterial keys,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(payload.KeyId, keys.EncryptionKeyId, StringComparison.Ordinal))
        {
            throw new CryptographicException("백업의 암호화 데이터에 필요한 키 버전을 찾을 수 없습니다.");
        }

        var plaintext = await new AesGcmEncryptionService(_keyMaterialProvider).DecryptAsync(
            payload,
            new EncryptionContext(tenantId, userId, recordId, entityType, fieldName),
            cancellationToken);
        CryptographicOperations.ZeroMemory(plaintext);
    }

    private static async Task CreateOnlineSnapshotAsync(
        string sourcePath,
        string snapshotPath,
        CancellationToken cancellationToken)
    {
        var sourceConnectionString = new SqliteConnectionStringBuilder
        {
            DataSource = sourcePath,
            Mode = SqliteOpenMode.ReadWrite,
            ForeignKeys = true,
            Pooling = false
        }.ToString();
        var destinationConnectionString = new SqliteConnectionStringBuilder
        {
            DataSource = snapshotPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            ForeignKeys = true,
            Pooling = false
        }.ToString();

        await using var source = new SqliteConnection(sourceConnectionString);
        await using var destination = new SqliteConnection(destinationConnectionString);
        await source.OpenAsync(cancellationToken);
        await destination.OpenAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        await Task.Run(() => source.BackupDatabase(destination), cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
    }

    private static async Task CheckpointAndCloseDatabaseAsync(
        string databasePath,
        CancellationToken cancellationToken)
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWrite,
            Pooling = false
        }.ToString();
        await using (var connection = new SqliteConnection(connectionString))
        {
            await connection.OpenAsync(cancellationToken);
            await using var checkpoint = connection.CreateCommand();
            checkpoint.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
            await using var reader = await checkpoint.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken) ||
                reader.GetInt32(0) == SqliteBusyResult ||
                reader.GetInt32(1) != reader.GetInt32(2))
            {
                throw new IOException("SQLite WAL을 안전하게 Checkpoint할 수 없습니다. API와 DB 사용 프로세스를 종료하십시오.");
            }
        }

        foreach (var sidecarPath in new[] { $"{databasePath}-wal", $"{databasePath}-shm" })
        {
            if (File.Exists(sidecarPath))
            {
                if (new FileInfo(sidecarPath).Length != 0)
                {
                    throw new IOException("SQLite WAL/SHM 파일이 남아 있어 DB를 교체하지 않았습니다.");
                }

                File.Delete(sidecarPath);
            }
        }
    }

    private static string ResolveDatabasePath(string dataSource)
    {
        if (string.IsNullOrWhiteSpace(dataSource) ||
            string.Equals(dataSource, ":memory:", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("백업에는 파일 기반 SQLite 데이터베이스가 필요합니다.");
        }

        return Path.GetFullPath(dataSource);
    }

    private static bool PathsEqual(string first, string second) =>
        string.Equals(Path.GetFullPath(first), Path.GetFullPath(second), StringComparison.OrdinalIgnoreCase);

    private static void ApplyRetentionPolicy(
        string newlyVerifiedBackupPath,
        string backupDirectory,
        BackupRetentionOptions policy,
        IReadOnlyCollection<string> retentionExclusions)
    {
        var outputDirectory = Path.GetDirectoryName(newlyVerifiedBackupPath)
            ?? throw new InvalidOperationException("백업 파일 디렉터리를 확인할 수 없습니다.");
        if (!PathsEqual(backupDirectory, outputDirectory))
        {
            return;
        }

        var cutoff = DateTime.UtcNow.AddDays(-policy.KeepForDays);
        var excludedPaths = retentionExclusions
            .Select(path => Path.GetFullPath(path))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var backups = Directory.EnumerateFiles(backupDirectory, "*.lumora-backup")
            .Where(path =>
                !PathsEqual(path, newlyVerifiedBackupPath) &&
                !excludedPaths.Contains(Path.GetFullPath(path)))
            .Select(path => new FileInfo(path))
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .Prepend(new FileInfo(newlyVerifiedBackupPath))
            .ToArray();
        for (var index = 1; index < backups.Length; index++)
        {
            var backup = backups[index];
            var isWithinCount = index < policy.KeepNewestCount;
            if (isWithinCount && backup.LastWriteTimeUtc >= cutoff)
            {
                continue;
            }

            File.Delete(backup.FullName);
        }

        if (!File.Exists(newlyVerifiedBackupPath))
        {
            throw new IOException("보존 정리 후 새로 검증한 백업 파일을 찾을 수 없습니다.");
        }
    }

    private static void DeleteTemporaryFile(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}
