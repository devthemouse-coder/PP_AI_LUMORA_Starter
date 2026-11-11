namespace Lumora.Domain.Entities;

public sealed class Conversation : IOptimisticConcurrencyEntity
{
    private Conversation()
    {
    }

    public Conversation(Guid id, Guid tenantId, Guid userId, DateTime createdAtUtc)
    {
        if (id == Guid.Empty || tenantId == Guid.Empty || userId == Guid.Empty)
        {
            throw new ArgumentException("대화 소유 식별자는 비어 있을 수 없습니다.");
        }

        EnsureUtc(createdAtUtc);

        Id = id;
        TenantId = tenantId;
        UserId = userId;
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = createdAtUtc;
        LastMessageAtUtc = createdAtUtc;
        Status = ConversationStatus.Active;
        Version = 1;
    }

    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    public Guid UserId { get; private set; }

    public EncryptedPayload? Summary { get; private set; }

    public DateTime? SummaryUpdatedAtUtc { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public DateTime UpdatedAtUtc { get; private set; }

    public DateTime LastMessageAtUtc { get; private set; }

    public ConversationStatus Status { get; private set; }

    public long Version { get; private set; }

    public void SetSummary(EncryptedPayload? summary, DateTime updatedAtUtc)
    {
        EnsureUtc(updatedAtUtc);
        Summary = summary;
        SummaryUpdatedAtUtc = summary is null ? null : updatedAtUtc;
        UpdatedAtUtc = updatedAtUtc;
    }

    public void RecordMessage(DateTime messageAtUtc)
    {
        EnsureUtc(messageAtUtc);
        if (messageAtUtc < LastMessageAtUtc)
        {
            throw new ArgumentOutOfRangeException(nameof(messageAtUtc), "메시지 시각은 마지막 메시지보다 이전일 수 없습니다.");
        }

        LastMessageAtUtc = messageAtUtc;
        UpdatedAtUtc = messageAtUtc;
    }

    private static void EnsureUtc(DateTime value)
    {
        if (value.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("시간은 UTC여야 합니다.", nameof(value));
        }
    }
}

public enum ConversationStatus : short
{
    Active = 1,
    Archived = 2
}
