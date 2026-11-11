namespace Lumora.Infrastructure.Persistence.Backup;

public sealed record BackupOperationalStatus(
    DateTimeOffset? LastBackupUtc,
    DateTimeOffset? LastRestoreVerificationUtc,
    DateTimeOffset? LastFailureUtc,
    string? LastFailureOperation,
    string? LastFailureCode);
