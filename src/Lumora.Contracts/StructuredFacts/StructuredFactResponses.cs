namespace Lumora.Contracts.StructuredFacts;

/// <summary>복호화된 구조화 사실입니다.</summary>
public sealed record StructuredFactValueResponse(
    string Subject,
    string? TextValue,
    int? Year,
    int? Month,
    int? Day);

/// <summary>구조화 사실 및 버전 정보입니다.</summary>
public sealed record StructuredFactResponse(
    Guid MemoryId,
    string FactType,
    StructuredFactValueResponse Value,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    int ConfidenceScore,
    long Version);

/// <summary>구조화 사실 목록 및 페이지 정보입니다.</summary>
public sealed record StructuredFactPageResponse(
    IReadOnlyList<StructuredFactResponse> Items,
    int PageNumber,
    int PageSize,
    long TotalCount);
