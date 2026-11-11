using Lumora.Application.Abstractions.Persistence;
using Lumora.Domain.Entities;
using Lumora.Infrastructure.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace Lumora.Infrastructure.Persistence.Repositories;

internal sealed class MessageRepository(LumoraDbContext dbContext) : IMessageRepository
{
    public Task<Message?> GetByIdAsync(
        TenantUserScope scope,
        Guid conversationId,
        Guid messageId,
        CancellationToken cancellationToken) =>
        dbContext.Messages.SingleOrDefaultAsync(
            message =>
                message.TenantId == scope.TenantId &&
                message.UserId == scope.UserId &&
                message.ConversationId == conversationId &&
                message.Id == messageId,
            cancellationToken);

    public async Task<PageResult<Message>> ListAsync(
        TenantUserScope scope,
        Guid conversationId,
        PageRequest page,
        CancellationToken cancellationToken)
    {
        var messages = dbContext.Messages.AsNoTracking()
            .Where(message =>
                message.TenantId == scope.TenantId &&
                message.UserId == scope.UserId &&
                message.ConversationId == conversationId);
        var totalCount = await messages.LongCountAsync(cancellationToken);
        var items = await messages
            .OrderBy(message => message.SequenceNumber)
            .ThenBy(message => message.Id)
            .Skip(page.Offset)
            .Take(page.PageSize)
            .ToListAsync(cancellationToken);

        return new PageResult<Message>(items, page.PageNumber, page.PageSize, totalCount);
    }

    public async Task<IReadOnlyList<Message>> ListRecentAsync(
        TenantUserScope scope,
        Guid conversationId,
        int messageCount,
        CancellationToken cancellationToken)
    {
        if (messageCount is < 1 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(messageCount));
        }

        return await dbContext.Messages.AsNoTracking()
            .Where(message =>
                message.TenantId == scope.TenantId &&
                message.UserId == scope.UserId &&
                message.ConversationId == conversationId)
            .OrderByDescending(message => message.SequenceNumber)
            .ThenByDescending(message => message.Id)
            .Take(messageCount)
            .OrderBy(message => message.SequenceNumber)
            .ThenBy(message => message.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task<long> GetLastSequenceNumberAsync(
        TenantUserScope scope,
        Guid conversationId,
        CancellationToken cancellationToken)
    {
        var lastSequenceNumber = await dbContext.Messages
            .Where(message =>
                message.TenantId == scope.TenantId &&
                message.UserId == scope.UserId &&
                message.ConversationId == conversationId)
            .Select(message => (long?)message.SequenceNumber)
            .MaxAsync(cancellationToken);
        return lastSequenceNumber ?? 0;
    }

    public async Task AddAsync(TenantUserScope scope, Message message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (message.TenantId != scope.TenantId || message.UserId != scope.UserId)
        {
            throw new ArgumentException("메시지 범위가 추가 대상과 일치하지 않습니다.", nameof(message));
        }

        await dbContext.Messages.AddAsync(message, cancellationToken);
    }

    public async Task DeleteAsync(
        TenantUserScope scope,
        Guid conversationId,
        Guid messageId,
        CancellationToken cancellationToken)
    {
        await dbContext.MemorySources
            .Where(source =>
                source.TenantId == scope.TenantId &&
                source.UserId == scope.UserId &&
                source.ConversationId == conversationId &&
                source.MessageId == messageId)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(source => source.ConversationId, (Guid?)null)
                    .SetProperty(source => source.MessageId, (Guid?)null),
                cancellationToken);

        await dbContext.Messages
            .Where(message =>
                message.TenantId == scope.TenantId &&
                message.UserId == scope.UserId &&
                message.ConversationId == conversationId &&
                message.Id == messageId)
            .ExecuteDeleteAsync(cancellationToken);
    }
}
