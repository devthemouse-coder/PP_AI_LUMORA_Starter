using Lumora.Application.Abstractions.Persistence;
using Lumora.Domain.Entities;
using Lumora.Infrastructure.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace Lumora.Infrastructure.Persistence.Repositories;

internal sealed class ConversationRepository(LumoraDbContext dbContext) : IConversationRepository
{
    public Task<Conversation?> GetByIdAsync(
        TenantUserScope scope,
        Guid conversationId,
        CancellationToken cancellationToken) =>
        dbContext.Conversations.SingleOrDefaultAsync(
            conversation => conversation.TenantId == scope.TenantId &&
                            conversation.UserId == scope.UserId &&
                            conversation.Id == conversationId,
            cancellationToken);

    public async Task<PageResult<Conversation>> ListAsync(
        TenantUserScope scope,
        PageRequest page,
        CancellationToken cancellationToken)
    {
        var conversations = dbContext.Conversations.AsNoTracking()
            .Where(conversation =>
                conversation.TenantId == scope.TenantId &&
                conversation.UserId == scope.UserId);
        var totalCount = await conversations.LongCountAsync(cancellationToken);
        var items = await conversations
            .OrderByDescending(conversation => conversation.UpdatedAtUtc)
            .ThenBy(conversation => conversation.Id)
            .Skip(page.Offset)
            .Take(page.PageSize)
            .ToListAsync(cancellationToken);

        return new PageResult<Conversation>(items, page.PageNumber, page.PageSize, totalCount);
    }

    public async Task AddAsync(
        TenantUserScope scope,
        Conversation conversation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(conversation);
        if (conversation.TenantId != scope.TenantId || conversation.UserId != scope.UserId)
        {
            throw new ArgumentException("대화 범위가 추가 대상과 일치하지 않습니다.", nameof(conversation));
        }

        await dbContext.Conversations.AddAsync(conversation, cancellationToken);
    }

    public Task UpdateAsync(
        TenantUserScope scope,
        Conversation conversation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(conversation);
        cancellationToken.ThrowIfCancellationRequested();
        if (conversation.TenantId != scope.TenantId || conversation.UserId != scope.UserId)
        {
            throw new ArgumentException("대화 범위가 변경 대상과 일치하지 않습니다.", nameof(conversation));
        }

        dbContext.Entry(conversation).State = EntityState.Modified;
        return Task.CompletedTask;
    }

    public async Task DeleteAsync(
        TenantUserScope scope,
        Guid conversationId,
        CancellationToken cancellationToken)
    {
        await dbContext.MemorySources
            .Where(source =>
                source.TenantId == scope.TenantId &&
                source.UserId == scope.UserId &&
                source.ConversationId == conversationId)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(source => source.ConversationId, (Guid?)null)
                    .SetProperty(source => source.MessageId, (Guid?)null),
                cancellationToken);

        await dbContext.Conversations
            .Where(conversation =>
                conversation.TenantId == scope.TenantId &&
                conversation.UserId == scope.UserId &&
                conversation.Id == conversationId)
            .ExecuteDeleteAsync(cancellationToken);
    }
}
