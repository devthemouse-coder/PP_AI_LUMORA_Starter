namespace Lumora.Domain.Entities;

public sealed class Tenant : IOptimisticConcurrencyEntity
{
    private Tenant()
    {
        Name = string.Empty;
    }

    public Tenant(Guid id, string name, DateTime createdAtUtc)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("테넌트 식별자는 비어 있을 수 없습니다.", nameof(id));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        EnsureUtc(createdAtUtc, nameof(createdAtUtc));

        Id = id;
        Name = name.Trim();
        Status = EntityStatus.Active;
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = createdAtUtc;
        Version = 1;
    }

    public Guid Id { get; private set; }

    public string Name { get; private set; }

    public EntityStatus Status { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public DateTime UpdatedAtUtc { get; private set; }

    public long Version { get; private set; }

    public void Rename(string name, DateTime updatedAtUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        EnsureUtc(updatedAtUtc, nameof(updatedAtUtc));
        Name = name.Trim();
        UpdatedAtUtc = updatedAtUtc;
    }

    private static void EnsureUtc(DateTime value, string parameterName)
    {
        if (value.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("시간은 UTC여야 합니다.", parameterName);
        }
    }
}

public enum EntityStatus : short
{
    Active = 1,
    Inactive = 2
}
