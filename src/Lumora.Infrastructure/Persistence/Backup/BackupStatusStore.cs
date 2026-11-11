using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace Lumora.Infrastructure.Persistence.Backup;

public sealed class BackupStatusStore
{
    private const int MaximumStatusLength = 16_384;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };
    private readonly string _directory;

    public BackupStatusStore(IConfiguration configuration)
    {
        _directory = BackupDirectoryResolver.Resolve(configuration);
    }

    public Task RecordBackupSucceededAsync(DateTimeOffset completedAtUtc, CancellationToken cancellationToken) =>
        UpdateAsync(
            status => status with { LastBackupUtc = completedAtUtc },
            cancellationToken);

    public Task RecordRestoreVerifiedAsync(DateTimeOffset verifiedAtUtc, CancellationToken cancellationToken) =>
        UpdateAsync(
            status => status with { LastRestoreVerificationUtc = verifiedAtUtc },
            cancellationToken);

    public Task RecordFailureAsync(
        string operation,
        string errorCode,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);
        ArgumentException.ThrowIfNullOrWhiteSpace(errorCode);
        return UpdateAsync(
            status => status with
            {
                LastFailureUtc = DateTimeOffset.UtcNow,
                LastFailureOperation = operation,
                LastFailureCode = errorCode
            },
            cancellationToken);
    }

    private async Task UpdateAsync(
        Func<BackupOperationalStatus, BackupOperationalStatus> update,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_directory);
        var statusPath = Path.Combine(_directory, "backup-status.json");
        var lockPath = Path.Combine(_directory, ".backup-status.lock");
        var temporaryPath = Path.Combine(_directory, $".{Guid.NewGuid():N}.status.partial");
        await using var fileLock = new FileStream(
            lockPath,
            FileMode.OpenOrCreate,
            FileAccess.ReadWrite,
            FileShare.None);
        try
        {
            var current = await ReadAsync(statusPath, cancellationToken);
            var next = update(current);
            await using (var stream = new FileStream(
                             temporaryPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             4096,
                             FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, next, JsonOptions, cancellationToken);
                await stream.FlushAsync(cancellationToken);
                stream.Flush(flushToDisk: true);
            }

            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporaryPath, statusPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static async Task<BackupOperationalStatus> ReadAsync(
        string statusPath,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(statusPath))
        {
            return new BackupOperationalStatus(null, null, null, null, null);
        }

        if (new FileInfo(statusPath).Length > MaximumStatusLength)
        {
            throw new InvalidDataException("백업 상태 파일의 크기가 허용 범위를 초과합니다.");
        }

        await using var stream = new FileStream(
            statusPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            4096,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        return await JsonSerializer.DeserializeAsync<BackupOperationalStatus>(
                   stream,
                   JsonOptions,
                   cancellationToken)
               ?? throw new InvalidDataException("백업 상태 파일의 형식이 유효하지 않습니다.");
    }
}
