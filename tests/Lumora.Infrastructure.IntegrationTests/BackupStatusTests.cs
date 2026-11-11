using System.Text.Json;
using Lumora.Infrastructure.Persistence.Backup;
using Microsoft.Extensions.Configuration;

namespace Lumora.Infrastructure.IntegrationTests;

public sealed class BackupStatusTests
{
    [Fact]
    public async Task Operational_status_keeps_success_times_and_only_stores_failure_codes()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"lumora-backup-status-{Guid.NewGuid():N}");
        try
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Backup:Directory"] = directory
                })
                .Build();
            var store = new BackupStatusStore(configuration);
            var backupTime = DateTimeOffset.UtcNow.AddMinutes(-3);
            var restoreTime = DateTimeOffset.UtcNow.AddMinutes(-2);
            await store.RecordBackupSucceededAsync(backupTime, CancellationToken.None);
            await store.RecordRestoreVerifiedAsync(restoreTime, CancellationToken.None);
            await store.RecordFailureAsync("--backup", "IOException", CancellationToken.None);

            var statusPath = Path.Combine(directory, "backup-status.json");
            using var document = JsonDocument.Parse(await File.ReadAllTextAsync(statusPath));
            var status = document.RootElement;
            Assert.Equal(backupTime, status.GetProperty("lastBackupUtc").GetDateTimeOffset());
            Assert.Equal(restoreTime, status.GetProperty("lastRestoreVerificationUtc").GetDateTimeOffset());
            Assert.Equal("--backup", status.GetProperty("lastFailureOperation").GetString());
            Assert.Equal("IOException", status.GetProperty("lastFailureCode").GetString());
            Assert.True(status.GetProperty("lastFailureUtc").GetDateTimeOffset() >= restoreTime);
            Assert.DoesNotContain(directory, await File.ReadAllTextAsync(statusPath), StringComparison.OrdinalIgnoreCase);
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
