using Lumora.Application.Abstractions.Persistence;
using Lumora.Domain.Entities;
using Lumora.Infrastructure.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace Lumora.Infrastructure.Persistence.Repositories;

internal sealed class MemoryRepository(LumoraDbContext dbContext) : IMemoryRepository
{
    public Task<MemoryItem?> GetByIdAsync(
        TenantUserScope scope,
        Guid memoryId,
        CancellationToken cancellationToken) =>
        dbContext.Memories.SingleOrDefaultAsync(
            memory =>
                memory.TenantId == scope.TenantId &&
                memory.UserId == scope.UserId &&
                memory.Id == memoryId,
            cancellationToken);

    public Task<MemoryPayload?> GetPayloadByIdAsync(
        TenantUserScope scope,
        Guid memoryId,
        CancellationToken cancellationToken) =>
        dbContext.MemoryPayloads.AsNoTracking().SingleOrDefaultAsync(
            payload =>
                payload.TenantId == scope.TenantId &&
                payload.UserId == scope.UserId &&
                payload.MemoryId == memoryId,
            cancellationToken);

    public async Task<PageResult<MemoryItem>> ListAsync(
        TenantUserScope scope,
        PageRequest page,
        CancellationToken cancellationToken) =>
        await SearchAsync(scope, new MemorySearchCriteria(IncludeInactive: true), page, cancellationToken);

    public async Task<PageResult<MemoryItem>> SearchAsync(
        TenantUserScope scope,
        MemorySearchCriteria criteria,
        PageRequest page,
        CancellationToken cancellationToken)
    {
        var memories = dbContext.Memories.AsNoTracking()
            .Where(memory =>
                memory.TenantId == scope.TenantId &&
                memory.UserId == scope.UserId);
        if (!criteria.IncludeInactive)
        {
            memories = memories.Where(memory => memory.IsActive);
        }

        if (criteria.MemoryType.HasValue)
        {
            memories = memories.Where(memory => memory.MemoryType == criteria.MemoryType.Value);
        }

        if (criteria.CreatedFromUtc.HasValue)
        {
            memories = memories.Where(memory => memory.CreatedAtUtc >= criteria.CreatedFromUtc.Value);
        }

        if (criteria.CreatedToUtc.HasValue)
        {
            memories = memories.Where(memory => memory.CreatedAtUtc < criteria.CreatedToUtc.Value);
        }

        if (criteria.UpdatedFromUtc.HasValue)
        {
            memories = memories.Where(memory => memory.UpdatedAtUtc >= criteria.UpdatedFromUtc.Value);
        }

        if (criteria.UpdatedToUtc.HasValue)
        {
            memories = memories.Where(memory => memory.UpdatedAtUtc < criteria.UpdatedToUtc.Value);
        }

        var totalCount = await memories.LongCountAsync(cancellationToken);
        var ordered = criteria.SortOrder switch
        {
            MemorySortOrder.CreatedAscending => memories.OrderBy(memory => memory.CreatedAtUtc).ThenBy(memory => memory.Id),
            MemorySortOrder.CreatedDescending => memories.OrderByDescending(memory => memory.CreatedAtUtc).ThenBy(memory => memory.Id),
            MemorySortOrder.UpdatedAscending => memories.OrderBy(memory => memory.UpdatedAtUtc).ThenBy(memory => memory.Id),
            _ => memories.OrderByDescending(memory => memory.UpdatedAtUtc).ThenBy(memory => memory.Id)
        };
        var items = await ordered
            .Skip(page.Offset)
            .Take(page.PageSize)
            .ToListAsync(cancellationToken);

        return new PageResult<MemoryItem>(items, page.PageNumber, page.PageSize, totalCount);
    }

    public async Task<IReadOnlyDictionary<Guid, MemoryPayload>> GetPayloadsByIdsAsync(
        TenantUserScope scope,
        IReadOnlyCollection<Guid> memoryIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(memoryIds);
        if (memoryIds.Count == 0)
        {
            return new Dictionary<Guid, MemoryPayload>();
        }

        var payloads = await dbContext.MemoryPayloads.AsNoTracking()
            .Where(payload =>
                payload.TenantId == scope.TenantId &&
                payload.UserId == scope.UserId &&
                memoryIds.Contains(payload.MemoryId))
            .ToListAsync(cancellationToken);
        return payloads.ToDictionary(payload => payload.MemoryId);
    }

    public async Task<PageResult<MemoryRevision>> ListRevisionsAsync(
        TenantUserScope scope,
        Guid memoryId,
        PageRequest page,
        CancellationToken cancellationToken)
    {
        var revisions = dbContext.MemoryRevisions.AsNoTracking()
            .Where(revision =>
                revision.TenantId == scope.TenantId &&
                revision.UserId == scope.UserId &&
                revision.MemoryId == memoryId);
        var totalCount = await revisions.LongCountAsync(cancellationToken);
        var items = await revisions
            .OrderBy(revision => revision.RevisionNumber)
            .ThenBy(revision => revision.Id)
            .Skip(page.Offset)
            .Take(page.PageSize)
            .ToListAsync(cancellationToken);

        return new PageResult<MemoryRevision>(items, page.PageNumber, page.PageSize, totalCount);
    }

    public async Task AddAsync(
        TenantUserScope scope,
        MemoryItem memory,
        MemoryPayload payload,
        MemorySource source,
        IReadOnlyCollection<MemoryLookupToken> lookupTokens,
        CancellationToken cancellationToken)
    {
        ValidateMemoryParts(scope, memory, payload, source, lookupTokens);
        ArgumentNullException.ThrowIfNull(memory);

        await dbContext.Memories.AddAsync(memory, cancellationToken);
        await dbContext.MemoryPayloads.AddAsync(payload, cancellationToken);
        await dbContext.MemorySources.AddAsync(source, cancellationToken);
        await dbContext.MemoryLookupTokens.AddRangeAsync(lookupTokens, cancellationToken);
    }

    public async Task UpdateAsync(
        TenantUserScope scope,
        MemoryItem memory,
        MemoryPayload payload,
        MemoryRevision revision,
        MemorySource source,
        IReadOnlyCollection<MemoryLookupToken> lookupTokens,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(memory);
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentNullException.ThrowIfNull(revision);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(lookupTokens);
        ValidateMemoryParts(scope, memory, payload, source, lookupTokens);
        if (revision.TenantId != scope.TenantId ||
            revision.UserId != scope.UserId ||
            revision.MemoryId != memory.Id)
        {
            throw new ArgumentException("개정 이력 범위가 변경 대상과 일치하지 않습니다.", nameof(revision));
        }

        var existingPayload = await dbContext.MemoryPayloads.SingleOrDefaultAsync(
            candidate =>
                candidate.TenantId == scope.TenantId &&
                candidate.UserId == scope.UserId &&
                candidate.MemoryId == memory.Id,
            cancellationToken);
        if (existingPayload is null)
        {
            throw new InvalidOperationException("기억 Payload가 없어 갱신할 수 없습니다.");
        }

        existingPayload.ReplaceValue(payload.Value);
        dbContext.Entry(memory).State = EntityState.Modified;
        await dbContext.MemoryRevisions.AddAsync(revision, cancellationToken);
        await dbContext.MemorySources.AddAsync(source, cancellationToken);

        var existingTokens = await dbContext.MemoryLookupTokens
            .Where(token =>
                token.TenantId == scope.TenantId &&
                token.UserId == scope.UserId &&
                token.MemoryId == memory.Id)
            .ToListAsync(cancellationToken);
        dbContext.MemoryLookupTokens.RemoveRange(existingTokens);
        await dbContext.MemoryLookupTokens.AddRangeAsync(lookupTokens, cancellationToken);
    }

    public async Task DeleteAsync(
        TenantUserScope scope,
        Guid memoryId,
        CancellationToken cancellationToken)
    {
        await dbContext.Memories
            .Where(memory =>
                memory.TenantId == scope.TenantId &&
                memory.UserId == scope.UserId &&
                memory.Id == memoryId)
            .ExecuteDeleteAsync(cancellationToken);
    }

    private static void ValidateMemoryParts(
        TenantUserScope scope,
        MemoryItem memory,
        MemoryPayload payload,
        MemorySource source,
        IReadOnlyCollection<MemoryLookupToken> lookupTokens)
    {
        ArgumentNullException.ThrowIfNull(memory);
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(lookupTokens);

        if (!HasScope(scope, memory.TenantId, memory.UserId) ||
            !HasScope(scope, payload.TenantId, payload.UserId) ||
            payload.MemoryId != memory.Id ||
            !HasScope(scope, source.TenantId, source.UserId) ||
            source.MemoryId != memory.Id ||
            lookupTokens.Any(token =>
                token is null ||
                !HasScope(scope, token.TenantId, token.UserId) ||
                token.MemoryId != memory.Id))
        {
            throw new ArgumentException("기억, Payload, 출처, 검색 토큰의 소유 범위가 일치하지 않습니다.");
        }
    }

    private static bool HasScope(TenantUserScope scope, Guid tenantId, Guid userId) =>
        scope.TenantId == tenantId && scope.UserId == userId;
}
