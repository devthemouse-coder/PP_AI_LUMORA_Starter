using Lumora.Domain.Entities;

namespace Lumora.Application.Abstractions.Persistence;

public interface ITenantRepository
{
    Task<Tenant?> GetByIdAsync(Guid tenantId, CancellationToken cancellationToken);

    Task AddAsync(Tenant tenant, CancellationToken cancellationToken);

    Task UpdateAsync(Guid tenantId, Tenant tenant, CancellationToken cancellationToken);

    Task DeleteAsync(Guid tenantId, CancellationToken cancellationToken);
}
