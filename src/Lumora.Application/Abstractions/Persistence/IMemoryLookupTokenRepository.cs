using Lumora.Domain.Entities;

namespace Lumora.Application.Abstractions.Persistence;

public interface IMemoryLookupTokenRepository
{
    Task<PageResult<Guid>> FindMemoryIdsAsync(
        TenantUserScope scope,
        LookupTokenType tokenType,
        string keyId,
        ReadOnlyMemory<byte> token,
        PageRequest page,
        CancellationToken cancellationToken);

    Task AddAsync(TenantUserScope scope, MemoryLookupToken token, CancellationToken cancellationToken);
}
