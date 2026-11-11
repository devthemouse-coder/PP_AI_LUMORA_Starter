using System.ComponentModel.DataAnnotations;

namespace Lumora.Contracts.StructuredFacts;

/// <summary>구조화된 사실 값입니다. 날짜의 Year는 제공된 경우에만 지정합니다.</summary>
public sealed record StructuredFactValueRequest
{
    [Required, MaxLength(512)]
    public required string Subject { get; init; }

    [MaxLength(8_000)]
    public string? TextValue { get; init; }

    [Range(1, 9999)]
    public int? Year { get; init; }

    [Range(1, 12)]
    public int? Month { get; init; }

    [Range(1, 31)]
    public int? Day { get; init; }
}

/// <summary>구조화된 사실 생성 요청입니다.</summary>
public sealed record CreateStructuredFactRequest
{
    [Required, MaxLength(160)]
    public required string FactType { get; init; }

    [Required]
    public required StructuredFactValueRequest Value { get; init; }
}

/// <summary>구조화된 사실 수정 요청입니다.</summary>
public sealed record UpdateStructuredFactRequest
{
    [Required, MaxLength(160)]
    public required string FactType { get; init; }

    [Required]
    public required StructuredFactValueRequest Value { get; init; }
}
