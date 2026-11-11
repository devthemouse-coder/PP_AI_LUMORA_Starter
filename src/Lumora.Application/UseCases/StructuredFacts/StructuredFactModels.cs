using System.Text.Json.Serialization;
using Lumora.Application.Abstractions.Persistence;

namespace Lumora.Application.UseCases.StructuredFacts;

public sealed record StructuredFactValue(
    string Subject,
    string? TextValue = null,
    int? Year = null,
    int? Month = null,
    int? Day = null);

public sealed record CreateStructuredFactCommand(string FactType, StructuredFactValue Value);

public sealed record UpdateStructuredFactCommand(string FactType, StructuredFactValue Value);

public sealed record StructuredFactSearch(
    string? FactType = null,
    string? Subject = null,
    string? ExactValue = null,
    int? BirthdayMonth = null,
    DateOnly? DateFrom = null,
    DateOnly? DateTo = null,
    MemorySortOrder SortOrder = MemorySortOrder.UpdatedDescending);

public sealed record StructuredFactDetails(
    Guid MemoryId,
    string FactType,
    StructuredFactValue Value,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    int ConfidenceScore,
    long Version);

public sealed record StructuredFactPage(
    IReadOnlyList<StructuredFactDetails> Items,
    int PageNumber,
    int PageSize,
    long TotalCount);

public interface IStructuredFactUseCases
{
    Task<StructuredFactDetails> CreateAsync(
        TenantUserScope scope,
        CreateStructuredFactCommand command,
        CancellationToken cancellationToken);

    Task<StructuredFactDetails?> GetAsync(
        TenantUserScope scope,
        Guid memoryId,
        CancellationToken cancellationToken);

    Task<StructuredFactPage> SearchAsync(
        TenantUserScope scope,
        StructuredFactSearch search,
        PageRequest page,
        CancellationToken cancellationToken);

    Task<StructuredFactDetails?> UpdateAsync(
        TenantUserScope scope,
        Guid memoryId,
        UpdateStructuredFactCommand command,
        CancellationToken cancellationToken);

    Task<bool> DeleteAsync(
        TenantUserScope scope,
        Guid memoryId,
        CancellationToken cancellationToken);
}

public sealed class DuplicateStructuredFactException() : InvalidOperationException("해당 사실이 이미 저장되어 있습니다.");

public sealed class ConflictingStructuredFactException() : InvalidOperationException("같은 대상과 정보 유형에 다른 값이 이미 존재합니다.");
