namespace Lumora.Domain.Entities;

public sealed class MemoryItem : IOptimisticConcurrencyEntity
{
    private MemoryItem()
    {
    }

    public MemoryItem(Guid id, Guid tenantId, Guid userId, MemoryType memoryType, DateTime createdAtUtc)
    {
        if (id == Guid.Empty || tenantId == Guid.Empty || userId == Guid.Empty)
        {
            throw new ArgumentException("기억 소유 식별자는 비어 있을 수 없습니다.");
        }

        if (createdAtUtc.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("시간은 UTC여야 합니다.", nameof(createdAtUtc));
        }

        Id = id;
        TenantId = tenantId;
        UserId = userId;
        MemoryType = memoryType;
        VerificationStatus = MemoryVerificationStatus.Proposed;
        Sensitivity = MemorySensitivity.Personal;
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = createdAtUtc;
        IsActive = true;
        ConfidenceScore = 0;
        Version = 1;
    }

    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    public Guid UserId { get; private set; }

    public MemoryType MemoryType { get; private set; }

    public int ConfidenceScore { get; private set; }

    public MemoryVerificationStatus VerificationStatus { get; private set; }

    public MemorySensitivity Sensitivity { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public DateTime UpdatedAtUtc { get; private set; }

    public DateTime? ValidFromUtc { get; private set; }

    public DateTime? ValidToUtc { get; private set; }

    public bool IsActive { get; private set; }

    public DateTime? DeletedAtUtc { get; private set; }

    public long Version { get; private set; }

    public void UpdateMetadata(
        int confidenceScore,
        MemoryVerificationStatus verificationStatus,
        MemorySensitivity sensitivity,
        DateTime updatedAtUtc,
        DateTime? validFromUtc,
        DateTime? validToUtc)
    {
        if (confidenceScore is < 0 or > 10_000)
        {
            throw new ArgumentOutOfRangeException(nameof(confidenceScore));
        }

        EnsureUtc(updatedAtUtc, nameof(updatedAtUtc));
        if (validFromUtc.HasValue)
        {
            EnsureUtc(validFromUtc.Value, nameof(validFromUtc));
        }

        if (validToUtc.HasValue)
        {
            EnsureUtc(validToUtc.Value, nameof(validToUtc));
        }

        if (validFromUtc.HasValue && validToUtc.HasValue && validFromUtc >= validToUtc)
        {
            throw new ArgumentException("유효 기간은 시작 시각을 포함하고 종료 시각을 제외하는 구간이어야 합니다.");
        }

        ConfidenceScore = confidenceScore;
        VerificationStatus = verificationStatus;
        Sensitivity = sensitivity;
        UpdatedAtUtc = updatedAtUtc;
        ValidFromUtc = validFromUtc;
        ValidToUtc = validToUtc;
    }

    public void Update(
        MemoryType memoryType,
        int confidenceScore,
        MemoryVerificationStatus verificationStatus,
        MemorySensitivity sensitivity,
        DateTime updatedAtUtc)
    {
        UpdateMetadata(
            confidenceScore,
            verificationStatus,
            sensitivity,
            updatedAtUtc,
            ValidFromUtc,
            ValidToUtc);
        MemoryType = memoryType;
    }

    public void Deactivate(DateTime deletedAtUtc)
    {
        EnsureUtc(deletedAtUtc, nameof(deletedAtUtc));
        IsActive = false;
        DeletedAtUtc = deletedAtUtc;
        UpdatedAtUtc = deletedAtUtc;
    }

    private static void EnsureUtc(DateTime value, string parameterName)
    {
        if (value.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("시간은 UTC여야 합니다.", parameterName);
        }
    }
}

public enum MemoryType : short
{
    Profile = 1,
    Preference = 2,
    Fact = 3,
    Work = 4,
    Procedure = 5,
    Decision = 6,
    ConversationExtract = 7
}

public enum MemoryVerificationStatus : short
{
    Proposed = 1,
    Verified = 2,
    Rejected = 3
}

public enum MemorySensitivity : short
{
    General = 1,
    Personal = 2,
    Sensitive = 3
}
