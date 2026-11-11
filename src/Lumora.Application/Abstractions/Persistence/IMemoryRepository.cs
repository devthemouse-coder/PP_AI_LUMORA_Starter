using Lumora.Domain.Entities;

namespace Lumora.Application.Abstractions.Persistence;

public interface IMemoryRepository
{
    Task<MemoryItem?> GetByIdAsync(TenantUserScope scope, Guid memoryId, CancellationToken cancellationToken);

    Task<MemoryPayload?> GetPayloadByIdAsync(
        TenantUserScope scope,
        Guid memoryId,
        CancellationToken cancellationToken);

    Task<PageResult<MemoryItem>> ListAsync(TenantUserScope scope, PageRequest page, CancellationToken cancellationToken);

    Task<PageResult<MemoryItem>> SearchAsync(
        TenantUserScope scope,
        MemorySearchCriteria criteria,
        PageRequest page,
        CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<Guid, MemoryPayload>> GetPayloadsByIdsAsync(
        TenantUserScope scope,
        IReadOnlyCollection<Guid> memoryIds,
        CancellationToken cancellationToken);

    Task<PageResult<MemoryRevision>> ListRevisionsAsync(
        TenantUserScope scope,
        Guid memoryId,
        PageRequest page,
        CancellationToken cancellationToken);

    Task AddAsync(
        TenantUserScope scope,
        MemoryItem memory,
        MemoryPayload payload,
        MemorySource source,
        IReadOnlyCollection<MemoryLookupToken> lookupTokens,
        CancellationToken cancellationToken);

    Task UpdateAsync(
        TenantUserScope scope,
        MemoryItem memory,
        MemoryPayload payload,
        MemoryRevision revision,
        MemorySource source,
        IReadOnlyCollection<MemoryLookupToken> lookupTokens,
        CancellationToken cancellationToken);

    Task DeleteAsync(TenantUserScope scope, Guid memoryId, CancellationToken cancellationToken);
}
