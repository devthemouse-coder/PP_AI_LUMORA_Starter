namespace Lumora.Contracts.Memories;

/// <summary>복호화가 승인된 기억 상세 정보입니다.</summary>
public sealed record MemoryResponse(
    Guid Id,
    string Type,
    string Content,
    int ConfidenceScore,
    string VerificationStatus,
    string Sensitivity,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    bool IsActive,
    long Version);

/// <summary>기억 목록 및 페이지 정보입니다.</summary>
public sealed record MemoryPageResponse(
    IReadOnlyList<MemoryResponse> Items,
    int PageNumber,
    int PageSize,
    long TotalCount);
