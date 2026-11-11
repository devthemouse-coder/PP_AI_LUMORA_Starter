using System.ComponentModel.DataAnnotations;

namespace Lumora.Contracts.Memories;

/// <summary>기억 생성 요청입니다. 소유자 범위는 서버 설정 문맥에서만 결정합니다.</summary>
public sealed record CreateMemoryRequest
{
    [Required, MaxLength(32)]
    public required string Type { get; init; }

    [Required, MaxLength(32_000)]
    public required string Content { get; init; }

    [Range(0, 10_000)]
    public int ConfidenceScore { get; init; } = 10_000;

    [MaxLength(32)]
    public string Sensitivity { get; init; } = "Personal";
}

/// <summary>기억 수정 요청입니다.</summary>
public sealed record UpdateMemoryRequest
{
    [Required, MaxLength(32)]
    public required string Type { get; init; }

    [Required, MaxLength(32_000)]
    public required string Content { get; init; }

    [Range(0, 10_000)]
    public int ConfidenceScore { get; init; }

    [MaxLength(32)]
    public string VerificationStatus { get; init; } = "Verified";

    [MaxLength(32)]
    public string Sensitivity { get; init; } = "Personal";
}
