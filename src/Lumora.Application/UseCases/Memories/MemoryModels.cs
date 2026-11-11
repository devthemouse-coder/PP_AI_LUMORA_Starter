using Lumora.Application.Abstractions.Persistence;
using Lumora.Domain.Entities;

namespace Lumora.Application.UseCases.Memories;

public sealed record CreateMemoryCommand(
    MemoryType MemoryType,
    string Content,
    int ConfidenceScore = 10_000,
    MemorySensitivity Sensitivity = MemorySensitivity.Personal);

public sealed record UpdateMemoryCommand(
    MemoryType MemoryType,
    string Content,
    int ConfidenceScore,
    MemoryVerificationStatus VerificationStatus,
    MemorySensitivity Sensitivity);

public sealed record MemorySearch(
    MemoryType? MemoryType = null,
    DateTime? CreatedFromUtc = null,
    DateTime? CreatedToUtc = null,
    DateTime? UpdatedFromUtc = null,
    DateTime? UpdatedToUtc = null,
    bool IncludeInactive = false,
    MemorySortOrder SortOrder = MemorySortOrder.UpdatedDescending);

public sealed record MemoryDetails(
    Guid Id,
    MemoryType MemoryType,
    string Content,
    int ConfidenceScore,
    MemoryVerificationStatus VerificationStatus,
    MemorySensitivity Sensitivity,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    bool IsActive,
    long Version);

public sealed record MemoryPage(
    IReadOnlyList<MemoryDetails> Items,
    int PageNumber,
    int PageSize,
    long TotalCount);

public interface IMemoryUseCases
{
    Task<MemoryDetails> CreateAsync(
        TenantUserScope scope,
        CreateMemoryCommand command,
        CancellationToken cancellationToken);

    Task<MemoryDetails?> GetAsync(
        TenantUserScope scope,
        Guid memoryId,
        CancellationToken cancellationToken);

    Task<MemoryPage> SearchAsync(
        TenantUserScope scope,
        MemorySearch search,
        PageRequest page,
        CancellationToken cancellationToken);

    Task<MemoryDetails?> UpdateAsync(
        TenantUserScope scope,
        Guid memoryId,
        UpdateMemoryCommand command,
        CancellationToken cancellationToken);

    Task<bool> DeleteAsync(
        TenantUserScope scope,
        Guid memoryId,
        CancellationToken cancellationToken);
}
