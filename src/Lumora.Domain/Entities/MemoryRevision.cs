namespace Lumora.Domain.Entities;

public sealed class MemoryRevision
{
    private MemoryRevision()
    {
        PreviousValue = new EncryptedPayload();
    }

    public MemoryRevision(
        Guid id,
        Guid tenantId,
        Guid userId,
        Guid memoryId,
        long revisionNumber,
        MemoryChangeType changeType,
        EncryptedPayload previousValue,
        DateTime changedAtUtc,
        Guid? changedByUserId = null,
        Guid? sourceId = null,
        EncryptedPayload? reason = null)
    {
        if (id == Guid.Empty || tenantId == Guid.Empty || userId == Guid.Empty || memoryId == Guid.Empty)
        {
            throw new ArgumentException("기억 개정 이력 소유 식별자는 비어 있을 수 없습니다.");
        }

        if (revisionNumber < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(revisionNumber));
        }

        ArgumentNullException.ThrowIfNull(previousValue);
        if (changedAtUtc.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("시간은 UTC여야 합니다.", nameof(changedAtUtc));
        }

        Id = id;
        TenantId = tenantId;
        UserId = userId;
        MemoryId = memoryId;
        RevisionNumber = revisionNumber;
        ChangeType = changeType;
        PreviousValue = previousValue;
        ChangedAtUtc = changedAtUtc;
        ChangedByUserId = changedByUserId;
        SourceId = sourceId;
        Reason = reason;
    }

    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    public Guid UserId { get; private set; }

    public Guid MemoryId { get; private set; }

    public long RevisionNumber { get; private set; }

    public MemoryChangeType ChangeType { get; private set; }

    public EncryptedPayload PreviousValue { get; private set; }

    public DateTime ChangedAtUtc { get; private set; }

    public Guid? ChangedByUserId { get; private set; }

    public Guid? SourceId { get; private set; }

    public EncryptedPayload? Reason { get; private set; }
}

public enum MemoryChangeType : short
{
    Updated = 1,
    Deactivated = 2,
    Deleted = 3,
    Reactivated = 4
}
