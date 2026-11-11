using Lumora.Domain.Entities;

namespace Lumora.Application.Abstractions.Persistence;

public interface IStructuredFactRepository
{
    Task<StructuredFact?> GetByMemoryIdAsync(
        TenantUserScope scope,
        Guid memoryId,
        CancellationToken cancellationToken);

    Task<PageResult<StructuredFactSearchItem>> SearchAsync(
        TenantUserScope scope,
        StructuredFactSearchCriteria criteria,
        PageRequest page,
        CancellationToken cancellationToken);

    Task<bool> ExistsMatchingAsync(
        TenantUserScope scope,
        IReadOnlyCollection<LookupTokenQuery> requiredTokens,
        Guid? exceptMemoryId,
        CancellationToken cancellationToken);

    Task AddAsync(TenantUserScope scope, StructuredFact fact, CancellationToken cancellationToken);

    Task UpdateAsync(TenantUserScope scope, StructuredFact fact, CancellationToken cancellationToken);

    Task DeleteByMemoryIdAsync(TenantUserScope scope, Guid memoryId, CancellationToken cancellationToken);
}
