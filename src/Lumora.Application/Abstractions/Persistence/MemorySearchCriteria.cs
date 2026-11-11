using Lumora.Domain.Entities;

namespace Lumora.Application.Abstractions.Persistence;

public sealed record MemorySearchCriteria(
    MemoryType? MemoryType = null,
    DateTime? CreatedFromUtc = null,
    DateTime? CreatedToUtc = null,
    DateTime? UpdatedFromUtc = null,
    DateTime? UpdatedToUtc = null,
    bool IncludeInactive = false,
    MemorySortOrder SortOrder = MemorySortOrder.UpdatedDescending);

public enum MemorySortOrder
{
    CreatedAscending,
    CreatedDescending,
    UpdatedAscending,
    UpdatedDescending
}

public sealed record LookupTokenQuery(LookupTokenType TokenType, string KeyId, byte[] Token);

public sealed record StructuredFactSearchCriteria(
    string? FactType,
    IReadOnlyCollection<IReadOnlyCollection<LookupTokenQuery>> RequiredTokenGroups,
    IReadOnlyCollection<LookupTokenQuery>? AnyOfTokens = null,
    MemorySortOrder SortOrder = MemorySortOrder.UpdatedDescending);

public sealed record StructuredFactSearchItem(StructuredFact Fact, MemoryItem Memory);
