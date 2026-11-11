namespace Lumora.Domain.Entities;

public sealed class Message
{
    private Message()
    {
        Body = new EncryptedPayload();
    }

    public Message(
        Guid id,
        Guid tenantId,
        Guid userId,
        Guid conversationId,
        long sequenceNumber,
        MessageRole role,
        EncryptedPayload body,
        DateTime createdAtUtc)
    {
        if (id == Guid.Empty || tenantId == Guid.Empty || userId == Guid.Empty || conversationId == Guid.Empty)
        {
            throw new ArgumentException("메시지 소유 식별자는 비어 있을 수 없습니다.");
        }

        if (sequenceNumber < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(sequenceNumber));
        }

        ArgumentNullException.ThrowIfNull(body);
        if (createdAtUtc.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("시간은 UTC여야 합니다.", nameof(createdAtUtc));
        }

        Id = id;
        TenantId = tenantId;
        UserId = userId;
        ConversationId = conversationId;
        SequenceNumber = sequenceNumber;
        Role = role;
        Body = body;
        CreatedAtUtc = createdAtUtc;
    }

    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    public Guid UserId { get; private set; }

    public Guid ConversationId { get; private set; }

    public long SequenceNumber { get; private set; }

    public MessageRole Role { get; private set; }

    public EncryptedPayload Body { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }
}

public enum MessageRole : short
{
    System = 1,
    User = 2,
    Assistant = 3,
    Tool = 4
}
