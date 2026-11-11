using Lumora.Domain.Entities;

namespace Lumora.Application.Abstractions.Persistence;

public interface IConversationRepository
{
    Task<Conversation?> GetByIdAsync(TenantUserScope scope, Guid conversationId, CancellationToken cancellationToken);

    Task<PageResult<Conversation>> ListAsync(TenantUserScope scope, PageRequest page, CancellationToken cancellationToken);

    Task AddAsync(TenantUserScope scope, Conversation conversation, CancellationToken cancellationToken);

    Task UpdateAsync(TenantUserScope scope, Conversation conversation, CancellationToken cancellationToken);

    Task DeleteAsync(TenantUserScope scope, Guid conversationId, CancellationToken cancellationToken);
}
