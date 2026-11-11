namespace Lumora.Domain.Entities;

public sealed class MemoryLookupToken
{
    private MemoryLookupToken()
    {
        KeyId = string.Empty;
        Token = [];
    }

    public MemoryLookupToken(
        Guid id,
        Guid tenantId,
        Guid userId,
        Guid memoryId,
        LookupTokenType tokenType,
        string keyId,
        byte[] token)
    {
        if (id == Guid.Empty || tenantId == Guid.Empty || userId == Guid.Empty || memoryId == Guid.Empty)
        {
            throw new ArgumentException("검색 토큰 소유 식별자는 비어 있을 수 없습니다.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(keyId);
        ArgumentNullException.ThrowIfNull(token);
        if (token.Length != 32)
        {
            throw new ArgumentException("HMAC-SHA-256 토큰은 32바이트여야 합니다.", nameof(token));
        }

        Id = id;
        TenantId = tenantId;
        UserId = userId;
        MemoryId = memoryId;
        TokenType = tokenType;
        KeyId = keyId;
        Token = token.ToArray();
    }

    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    public Guid UserId { get; private set; }

    public Guid MemoryId { get; private set; }

    public LookupTokenType TokenType { get; private set; }

    public string KeyId { get; private set; }

    public byte[] Token { get; private set; }
}

public enum LookupTokenType : short
{
    BirthdayMonth = 1,
    BirthdayDate = 2,
    BirthdaySubject = 3,
    FactType = 4,
    ExactValue = 5,
    CalendarDate = 6,
    FactSubject = 7
}
