using Lumora.Domain.Entities;

namespace Lumora.Application.Abstractions.Persistence;

public interface IMessageRepository
{
    Task<Message?> GetByIdAsync(
        TenantUserScope scope,
        Guid conversationId,
        Guid messageId,
        CancellationToken cancellationToken);

    Task<PageResult<Message>> ListAsync(
        TenantUserScope scope,
        Guid conversationId,
        PageRequest page,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<Message>> ListRecentAsync(
        TenantUserScope scope,
        Guid conversationId,
        int messageCount,
        CancellationToken cancellationToken);

    Task<long> GetLastSequenceNumberAsync(
        TenantUserScope scope,
        Guid conversationId,
        CancellationToken cancellationToken);

    Task AddAsync(TenantUserScope scope, Message message, CancellationToken cancellationToken);

    Task DeleteAsync(
        TenantUserScope scope,
        Guid conversationId,
        Guid messageId,
        CancellationToken cancellationToken);
}
