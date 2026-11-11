namespace Lumora.Domain.Entities;

public sealed class MemorySource
{
    private MemorySource()
    {
    }

    public MemorySource(
        Guid id,
        Guid tenantId,
        Guid userId,
        Guid memoryId,
        MemorySourceType sourceType,
        int confidenceScore,
        DateTime createdAtUtc,
        Guid? conversationId = null,
        Guid? messageId = null)
    {
        if (id == Guid.Empty || tenantId == Guid.Empty || userId == Guid.Empty || memoryId == Guid.Empty)
        {
            throw new ArgumentException("기억 출처 소유 식별자는 비어 있을 수 없습니다.");
        }

        if (confidenceScore is < 0 or > 10_000)
        {
            throw new ArgumentOutOfRangeException(nameof(confidenceScore));
        }

        if ((conversationId is null) != (messageId is null))
        {
            throw new ArgumentException("대화 및 메시지 출처 식별자는 함께 지정해야 합니다.");
        }

        if (createdAtUtc.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("시간은 UTC여야 합니다.", nameof(createdAtUtc));
        }

        Id = id;
        TenantId = tenantId;
        UserId = userId;
        MemoryId = memoryId;
        SourceType = sourceType;
        ConfidenceScore = confidenceScore;
        CreatedAtUtc = createdAtUtc;
        ConversationId = conversationId;
        MessageId = messageId;
    }

    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    public Guid UserId { get; private set; }

    public Guid MemoryId { get; private set; }

    public MemorySourceType SourceType { get; private set; }

    public int ConfidenceScore { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public Guid? ConversationId { get; private set; }

    public Guid? MessageId { get; private set; }

    public void ClearMessageReference()
    {
        ConversationId = null;
        MessageId = null;
    }
}

public enum MemorySourceType : short
{
    DirectInput = 1,
    ConversationMessage = 2,
    UserCorrection = 3
}
