using Microsoft.Extensions.Configuration;

namespace Lumora.Infrastructure.Persistence.Backup;

public static class BackupDirectoryResolver
{
    public static string Resolve(IConfiguration? configuration)
    {
        var directory = configuration?["Backup:Directory"];
        if (string.IsNullOrWhiteSpace(directory))
        {
            directory = Environment.GetEnvironmentVariable("LUMORA_BACKUP_DIRECTORY");
        }

        if (string.IsNullOrWhiteSpace(directory))
        {
            var localApplicationData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrWhiteSpace(localApplicationData))
            {
                throw new InvalidOperationException(
                    "백업 경로가 없습니다. Backup:Directory 또는 LUMORA_BACKUP_DIRECTORY를 설정하십시오.");
            }

            directory = Path.Combine(localApplicationData, "P.P. AI - LUMORA", "backups");
        }

        return Path.GetFullPath(directory);
    }
}
