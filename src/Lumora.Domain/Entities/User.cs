namespace Lumora.Domain.Entities;

public sealed class User : IOptimisticConcurrencyEntity
{
    private User()
    {
        DisplayName = new EncryptedPayload();
    }

    public User(Guid id, Guid tenantId, EncryptedPayload displayName, DateTime createdAtUtc)
    {
        if (id == Guid.Empty || tenantId == Guid.Empty)
        {
            throw new ArgumentException("사용자와 테넌트 식별자는 비어 있을 수 없습니다.");
        }

        ArgumentNullException.ThrowIfNull(displayName);
        EnsureUtc(createdAtUtc);

        Id = id;
        TenantId = tenantId;
        DisplayName = displayName;
        Status = EntityStatus.Active;
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = createdAtUtc;
        Version = 1;
    }

    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    public EncryptedPayload DisplayName { get; private set; }

    public EntityStatus Status { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public DateTime UpdatedAtUtc { get; private set; }

    public long Version { get; private set; }

    public void ChangeDisplayName(EncryptedPayload displayName, DateTime updatedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(displayName);
        EnsureUtc(updatedAtUtc);
        DisplayName = displayName;
        UpdatedAtUtc = updatedAtUtc;
    }

    private static void EnsureUtc(DateTime value)
    {
        if (value.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("시간은 UTC여야 합니다.", nameof(value));
        }
    }
}
