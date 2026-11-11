using Lumora.Application.Abstractions.Persistence;
using Lumora.Domain.Entities;
using Lumora.Infrastructure.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace Lumora.Infrastructure.Persistence.Repositories;

internal sealed class TenantRepository(LumoraDbContext dbContext) : ITenantRepository
{
    public Task<Tenant?> GetByIdAsync(Guid tenantId, CancellationToken cancellationToken) =>
        dbContext.Tenants.SingleOrDefaultAsync(tenant => tenant.Id == tenantId, cancellationToken);

    public async Task AddAsync(Tenant tenant, CancellationToken cancellationToken)
    {
        await dbContext.Tenants.AddAsync(tenant, cancellationToken);
    }

    public Task UpdateAsync(Guid tenantId, Tenant tenant, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tenant);
        cancellationToken.ThrowIfCancellationRequested();
        if (tenant.Id != tenantId)
        {
            throw new ArgumentException("테넌트 범위가 변경 대상과 일치하지 않습니다.", nameof(tenant));
        }

        dbContext.Entry(tenant).State = EntityState.Modified;
        return Task.CompletedTask;
    }

    public async Task DeleteAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        await dbContext.MemorySources
            .Where(source => source.TenantId == tenantId && source.MessageId != null)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(source => source.ConversationId, (Guid?)null)
                    .SetProperty(source => source.MessageId, (Guid?)null),
                cancellationToken);

        await dbContext.Messages
            .Where(message => message.TenantId == tenantId)
            .ExecuteDeleteAsync(cancellationToken);

        await dbContext.Conversations
            .Where(conversation => conversation.TenantId == tenantId)
            .ExecuteDeleteAsync(cancellationToken);

        await dbContext.Memories
            .Where(memory => memory.TenantId == tenantId)
            .ExecuteDeleteAsync(cancellationToken);

        await dbContext.Users
            .Where(user => user.TenantId == tenantId)
            .ExecuteDeleteAsync(cancellationToken);

        await dbContext.Tenants
            .Where(tenant => tenant.Id == tenantId)
            .ExecuteDeleteAsync(cancellationToken);
    }
}
