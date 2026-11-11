using Lumora.Application.Abstractions.Persistence;
using Lumora.Domain.Entities;
using Lumora.Infrastructure.Persistence.Context;
using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;

namespace Lumora.Infrastructure.Persistence.Repositories;

internal sealed class StructuredFactRepository(LumoraDbContext dbContext) : IStructuredFactRepository
{
    public Task<StructuredFact?> GetByMemoryIdAsync(
        TenantUserScope scope,
        Guid memoryId,
        CancellationToken cancellationToken) =>
        dbContext.StructuredFacts.SingleOrDefaultAsync(
            fact =>
                fact.TenantId == scope.TenantId &&
                fact.UserId == scope.UserId &&
                fact.MemoryId == memoryId,
            cancellationToken);

    public async Task<PageResult<StructuredFactSearchItem>> SearchAsync(
        TenantUserScope scope,
        StructuredFactSearchCriteria criteria,
        PageRequest page,
        CancellationToken cancellationToken)
    {
        var query =
            from fact in dbContext.StructuredFacts.AsNoTracking()
            join memory in dbContext.Memories.AsNoTracking()
                on new { fact.TenantId, fact.UserId, Id = fact.MemoryId }
                equals new { memory.TenantId, memory.UserId, memory.Id }
            where fact.TenantId == scope.TenantId &&
                  fact.UserId == scope.UserId &&
                  memory.IsActive
            select new { Fact = fact, Memory = memory };

        if (!string.IsNullOrWhiteSpace(criteria.FactType))
        {
            query = query.Where(item => item.Fact.FactType == criteria.FactType);
        }

        foreach (var tokenGroup in criteria.RequiredTokenGroups)
        {
            if (tokenGroup.Count == 0)
            {
                return new PageResult<StructuredFactSearchItem>([], page.PageNumber, page.PageSize, 0);
            }

            var tokenRows = dbContext.MemoryLookupTokens.AsNoTracking()
                .Where(token => token.TenantId == scope.TenantId && token.UserId == scope.UserId);
            var tokenPredicate = BuildAnyTokenPredicate(tokenGroup);
            var matchingMemoryIds = tokenRows.Where(tokenPredicate).Select(token => token.MemoryId).Distinct();
            query = query.Where(item => matchingMemoryIds.Contains(item.Fact.MemoryId));
        }

        if (criteria.AnyOfTokens is { Count: > 0 } anyTokens)
        {
            var tokenRows = dbContext.MemoryLookupTokens.AsNoTracking()
                .Where(token => token.TenantId == scope.TenantId && token.UserId == scope.UserId);
            var tokenPredicate = BuildAnyTokenPredicate(anyTokens);
            var matchingMemoryIds = tokenRows.Where(tokenPredicate).Select(token => token.MemoryId).Distinct();
            query = query.Where(item => matchingMemoryIds.Contains(item.Fact.MemoryId));
        }

        var totalCount = await query.LongCountAsync(cancellationToken);
        var ordered = criteria.SortOrder switch
        {
            MemorySortOrder.CreatedAscending => query.OrderBy(item => item.Memory.CreatedAtUtc).ThenBy(item => item.Fact.MemoryId),
            MemorySortOrder.CreatedDescending => query.OrderByDescending(item => item.Memory.CreatedAtUtc).ThenBy(item => item.Fact.MemoryId),
            MemorySortOrder.UpdatedAscending => query.OrderBy(item => item.Memory.UpdatedAtUtc).ThenBy(item => item.Fact.MemoryId),
            _ => query.OrderByDescending(item => item.Memory.UpdatedAtUtc).ThenBy(item => item.Fact.MemoryId)
        };
        var items = await ordered
            .Skip(page.Offset)
            .Take(page.PageSize)
            .Select(item => new StructuredFactSearchItem(item.Fact, item.Memory))
            .ToListAsync(cancellationToken);
        return new PageResult<StructuredFactSearchItem>(items, page.PageNumber, page.PageSize, totalCount);
    }

    public async Task<bool> ExistsMatchingAsync(
        TenantUserScope scope,
        IReadOnlyCollection<LookupTokenQuery> requiredTokens,
        Guid? exceptMemoryId,
        CancellationToken cancellationToken)
    {
        if (requiredTokens.Count == 0)
        {
            throw new ArgumentException("중복 검증에는 하나 이상의 검색 토큰이 필요합니다.", nameof(requiredTokens));
        }

        var tokenRows = dbContext.MemoryLookupTokens.AsNoTracking()
            .Where(token => token.TenantId == scope.TenantId && token.UserId == scope.UserId);
        var firstPredicate = BuildAnyTokenPredicate([requiredTokens.First()]);
        var matchingMemoryIds = tokenRows.Where(firstPredicate).Select(token => token.MemoryId).Distinct();
        foreach (var lookupToken in requiredTokens.Skip(1))
        {
            var tokenType = lookupToken.TokenType;
            var keyId = lookupToken.KeyId;
            var tokenBytes = lookupToken.Token;
            matchingMemoryIds = matchingMemoryIds.Where(memoryId =>
                tokenRows.Any(token =>
                    token.MemoryId == memoryId &&
                    token.TokenType == tokenType &&
                    token.KeyId == keyId &&
                    token.Token == tokenBytes));
        }

        var facts = dbContext.StructuredFacts.AsNoTracking()
            .Where(fact =>
                fact.TenantId == scope.TenantId &&
                fact.UserId == scope.UserId &&
                matchingMemoryIds.Contains(fact.MemoryId));
        if (exceptMemoryId.HasValue)
        {
            facts = facts.Where(fact => fact.MemoryId != exceptMemoryId.Value);
        }

        return await facts.AnyAsync(cancellationToken);
    }

    public async Task AddAsync(TenantUserScope scope, StructuredFact fact, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(fact);
        if (fact.TenantId != scope.TenantId || fact.UserId != scope.UserId)
        {
            throw new ArgumentException("구조화 사실 범위가 추가 대상과 일치하지 않습니다.", nameof(fact));
        }

        await dbContext.StructuredFacts.AddAsync(fact, cancellationToken);
    }

    public Task UpdateAsync(TenantUserScope scope, StructuredFact fact, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(fact);
        cancellationToken.ThrowIfCancellationRequested();
        if (fact.TenantId != scope.TenantId || fact.UserId != scope.UserId)
        {
            throw new ArgumentException("구조화 사실 범위가 변경 대상과 일치하지 않습니다.", nameof(fact));
        }

        dbContext.Entry(fact).State = EntityState.Modified;
        return Task.CompletedTask;
    }

    public async Task DeleteByMemoryIdAsync(
        TenantUserScope scope,
        Guid memoryId,
        CancellationToken cancellationToken)
    {
        await dbContext.StructuredFacts
            .Where(fact =>
                fact.TenantId == scope.TenantId &&
                fact.UserId == scope.UserId &&
                fact.MemoryId == memoryId)
            .ExecuteDeleteAsync(cancellationToken);
    }

    private static Expression<Func<MemoryLookupToken, bool>> BuildAnyTokenPredicate(
        IReadOnlyCollection<LookupTokenQuery> tokens)
    {
        var parameter = Expression.Parameter(typeof(MemoryLookupToken), "token");
        Expression body = Expression.Constant(false);
        foreach (var item in tokens)
        {
            var tokenType = Expression.Equal(
                Expression.Property(parameter, nameof(MemoryLookupToken.TokenType)),
                Expression.Constant(item.TokenType));
            var keyId = Expression.Equal(
                Expression.Property(parameter, nameof(MemoryLookupToken.KeyId)),
                Expression.Constant(item.KeyId));
            var token = Expression.Equal(
                Expression.Property(parameter, nameof(MemoryLookupToken.Token)),
                Expression.Constant(item.Token));
            body = Expression.OrElse(body, Expression.AndAlso(tokenType, Expression.AndAlso(keyId, token)));
        }

        return Expression.Lambda<Func<MemoryLookupToken, bool>>(body, parameter);
    }
}
