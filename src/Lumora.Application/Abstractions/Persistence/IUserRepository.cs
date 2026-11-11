using Lumora.Domain.Entities;

namespace Lumora.Application.Abstractions.Persistence;

public interface IUserRepository
{
    Task<User?> GetByIdAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken);

    Task<PageResult<User>> ListAsync(Guid tenantId, PageRequest page, CancellationToken cancellationToken);

    Task AddAsync(Guid tenantId, User user, CancellationToken cancellationToken);

    Task UpdateAsync(Guid tenantId, Guid userId, User user, CancellationToken cancellationToken);

    Task DeleteAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken);
}
