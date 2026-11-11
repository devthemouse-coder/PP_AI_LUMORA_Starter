namespace Lumora.Infrastructure.Persistence.Backup;

public sealed record BackupFileMetadata(
    Guid BackupId,
    DateTimeOffset CreatedAtUtc,
    string SchemaVersion,
    string EncryptionKeyId,
    long FileSizeBytes,
    bool ContainsEncryptedData);
