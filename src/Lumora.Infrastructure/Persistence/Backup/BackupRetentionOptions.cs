using Microsoft.Extensions.Configuration;

namespace Lumora.Infrastructure.Persistence.Backup;

public sealed record BackupRetentionOptions(int KeepNewestCount, int KeepForDays)
{
    public static BackupRetentionOptions FromConfiguration(IConfiguration? configuration)
    {
        var count = ParseSetting(configuration?["Backup:RetentionCount"], 10, 1, 1000, "Backup:RetentionCount");
        var days = ParseSetting(configuration?["Backup:RetentionDays"], 90, 1, 3650, "Backup:RetentionDays");
        return new BackupRetentionOptions(count, days);
    }

    private static int ParseSetting(string? value, int defaultValue, int minimum, int maximum, string settingName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return defaultValue;
        }

        if (!int.TryParse(value, out var parsed) || parsed < minimum || parsed > maximum)
        {
            throw new InvalidOperationException(
                $"{settingName} 설정은 {minimum}부터 {maximum} 사이의 정수여야 합니다.");
        }

        return parsed;
    }
}
