using Lumora.Application.Abstractions.Persistence;
using Lumora.Domain.Entities;
using Lumora.Infrastructure.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace Lumora.Infrastructure.Persistence.Repositories;

internal sealed class MemoryLookupTokenRepository(LumoraDbContext dbContext) : IMemoryLookupTokenRepository
{
    public async Task<PageResult<Guid>> FindMemoryIdsAsync(
        TenantUserScope scope,
        LookupTokenType tokenType,
        string keyId,
        ReadOnlyMemory<byte> token,
        PageRequest page,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyId);
        if (token.Length != 32)
        {
            throw new ArgumentException("HMAC-SHA-256 토큰은 32바이트여야 합니다.", nameof(token));
        }

        var tokenBytes = token.ToArray();
        var matches = dbContext.MemoryLookupTokens.AsNoTracking()
            .Where(candidate =>
                candidate.TenantId == scope.TenantId &&
                candidate.UserId == scope.UserId &&
                candidate.TokenType == tokenType &&
                candidate.KeyId == keyId &&
                candidate.Token == tokenBytes)
            .Select(candidate => candidate.MemoryId)
            .Distinct();
        var totalCount = await matches.LongCountAsync(cancellationToken);
        var memoryIds = await matches
            .OrderBy(memoryId => memoryId)
            .Skip(page.Offset)
            .Take(page.PageSize)
            .ToListAsync(cancellationToken);

        return new PageResult<Guid>(memoryIds, page.PageNumber, page.PageSize, totalCount);
    }

    public async Task AddAsync(
        TenantUserScope scope,
        MemoryLookupToken token,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(token);
        if (token.TenantId != scope.TenantId || token.UserId != scope.UserId)
        {
            throw new ArgumentException("검색 토큰 범위가 추가 대상과 일치하지 않습니다.", nameof(token));
        }

        await dbContext.MemoryLookupTokens.AddAsync(token, cancellationToken);
    }
}
