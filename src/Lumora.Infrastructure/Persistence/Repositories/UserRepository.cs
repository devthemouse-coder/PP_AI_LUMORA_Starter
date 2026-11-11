using Lumora.Application.Abstractions.Persistence;
using Lumora.Domain.Entities;
using Lumora.Infrastructure.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace Lumora.Infrastructure.Persistence.Repositories;

internal sealed class UserRepository(LumoraDbContext dbContext) : IUserRepository
{
    public Task<User?> GetByIdAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken) =>
        dbContext.Users.SingleOrDefaultAsync(
            user => user.TenantId == tenantId && user.Id == userId,
            cancellationToken);

    public async Task<PageResult<User>> ListAsync(
        Guid tenantId,
        PageRequest page,
        CancellationToken cancellationToken)
    {
        var users = dbContext.Users.AsNoTracking().Where(user => user.TenantId == tenantId);
        var totalCount = await users.LongCountAsync(cancellationToken);
        var items = await users
            .OrderBy(user => user.CreatedAtUtc)
            .ThenBy(user => user.Id)
            .Skip(page.Offset)
            .Take(page.PageSize)
            .ToListAsync(cancellationToken);

        return new PageResult<User>(items, page.PageNumber, page.PageSize, totalCount);
    }

    public async Task AddAsync(Guid tenantId, User user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        if (user.TenantId != tenantId)
        {
            throw new ArgumentException("사용자 테넌트 범위가 변경 대상과 일치하지 않습니다.", nameof(user));
        }

        await dbContext.Users.AddAsync(user, cancellationToken);
    }

    public Task UpdateAsync(Guid tenantId, Guid userId, User user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        cancellationToken.ThrowIfCancellationRequested();
        if (user.TenantId != tenantId || user.Id != userId)
        {
            throw new ArgumentException("사용자 범위가 변경 대상과 일치하지 않습니다.", nameof(user));
        }

        dbContext.Entry(user).State = EntityState.Modified;
        return Task.CompletedTask;
    }

    public async Task DeleteAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken)
    {
        await dbContext.MemorySources
            .Where(source => source.TenantId == tenantId &&
                             source.UserId == userId &&
                             source.MessageId != null)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(source => source.ConversationId, (Guid?)null)
                    .SetProperty(source => source.MessageId, (Guid?)null),
                cancellationToken);

        await dbContext.Messages
            .Where(message => message.TenantId == tenantId && message.UserId == userId)
            .ExecuteDeleteAsync(cancellationToken);

        await dbContext.Conversations
            .Where(conversation => conversation.TenantId == tenantId && conversation.UserId == userId)
            .ExecuteDeleteAsync(cancellationToken);

        await dbContext.Memories
            .Where(memory => memory.TenantId == tenantId && memory.UserId == userId)
            .ExecuteDeleteAsync(cancellationToken);

        await dbContext.Users
            .Where(user => user.TenantId == tenantId && user.Id == userId)
            .ExecuteDeleteAsync(cancellationToken);
    }
}
