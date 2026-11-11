namespace Lumora.Domain.Entities;

public sealed class MemoryPayload
{
    private MemoryPayload()
    {
        Value = new EncryptedPayload();
    }

    public MemoryPayload(Guid tenantId, Guid userId, Guid memoryId, EncryptedPayload value)
    {
        if (tenantId == Guid.Empty || userId == Guid.Empty || memoryId == Guid.Empty)
        {
            throw new ArgumentException("기억 Payload 소유 식별자는 비어 있을 수 없습니다.");
        }

        ArgumentNullException.ThrowIfNull(value);

        TenantId = tenantId;
        UserId = userId;
        MemoryId = memoryId;
        Value = value;
    }

    public Guid TenantId { get; private set; }

    public Guid UserId { get; private set; }

    public Guid MemoryId { get; private set; }

    public EncryptedPayload Value { get; private set; }

    public void ReplaceValue(EncryptedPayload value)
    {
        ArgumentNullException.ThrowIfNull(value);
        Value = value;
    }
}
