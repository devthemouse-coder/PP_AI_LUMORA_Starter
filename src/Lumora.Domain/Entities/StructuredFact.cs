namespace Lumora.Domain.Entities;

public sealed class StructuredFact
{
    private StructuredFact()
    {
        FactType = string.Empty;
        Value = new EncryptedPayload();
    }

    public StructuredFact(
        Guid tenantId,
        Guid userId,
        Guid memoryId,
        string factType,
        EncryptedPayload value)
    {
        if (tenantId == Guid.Empty || userId == Guid.Empty || memoryId == Guid.Empty)
        {
            throw new ArgumentException("구조화 사실 소유 식별자는 비어 있을 수 없습니다.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(factType);
        ArgumentNullException.ThrowIfNull(value);

        TenantId = tenantId;
        UserId = userId;
        MemoryId = memoryId;
        FactType = factType.Trim();
        Value = value;
    }

    public Guid TenantId { get; private set; }

    public Guid UserId { get; private set; }

    public Guid MemoryId { get; private set; }

    public string FactType { get; private set; }

    public EncryptedPayload Value { get; private set; }

    public void ChangeFactType(string factType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(factType);
        FactType = factType.Trim();
    }

    public void ReplaceValue(EncryptedPayload value)
    {
        ArgumentNullException.ThrowIfNull(value);
        Value = value;
    }
}
