using System.Security.Cryptography;
using System.Text;
using Lumora.Application.Abstractions.Persistence;
using Lumora.Application.Abstractions.Security;
using Lumora.Domain.Entities;

namespace Lumora.Application.UseCases.Memories;

public sealed class MemoryUseCases(
    IMemoryRepository memoryRepository,
    IEncryptionService encryptionService,
    IUnitOfWork unitOfWork) : IMemoryUseCases
{
    private const int MaximumContentLength = 32_000;

    public async Task<MemoryDetails> CreateAsync(
        TenantUserScope scope,
        CreateMemoryCommand command,
        CancellationToken cancellationToken)
    {
        ValidateContent(command.Content);
        ValidateConfidence(command.ConfidenceScore);
        var id = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var encrypted = await EncryptAsync(
            command.Content,
            scope,
            id,
            nameof(MemoryPayload),
            nameof(MemoryPayload.Value),
            cancellationToken);
        var memory = new MemoryItem(id, scope.TenantId, scope.UserId, command.MemoryType, now);
        memory.Update(
            command.MemoryType,
            command.ConfidenceScore,
            MemoryVerificationStatus.Verified,
            command.Sensitivity,
            now);
        var payload = new MemoryPayload(scope.TenantId, scope.UserId, id, encrypted);
        var source = new MemorySource(
            Guid.NewGuid(),
            scope.TenantId,
            scope.UserId,
            id,
            MemorySourceType.DirectInput,
            command.ConfidenceScore,
            now);

        await unitOfWork.ExecuteInTransactionAsync(
            async token =>
            {
                await memoryRepository.AddAsync(scope, memory, payload, source, [], token);
            },
            cancellationToken);
        return ToDetails(memory, command.Content);
    }

    public async Task<MemoryDetails?> GetAsync(
        TenantUserScope scope,
        Guid memoryId,
        CancellationToken cancellationToken)
    {
        var memory = await memoryRepository.GetByIdAsync(scope, memoryId, cancellationToken);
        if (memory is null)
        {
            return null;
        }

        var payload = await memoryRepository.GetPayloadByIdAsync(scope, memoryId, cancellationToken);
        if (payload is null)
        {
            throw new InvalidDataException("기억 본문을 찾을 수 없습니다.");
        }

        return ToDetails(memory, await DecryptAsync(payload.Value, scope, memoryId, nameof(MemoryPayload), nameof(MemoryPayload.Value), cancellationToken));
    }

    public async Task<MemoryPage> SearchAsync(
        TenantUserScope scope,
        MemorySearch search,
        PageRequest page,
        CancellationToken cancellationToken)
    {
        ValidateRange(search.CreatedFromUtc, search.CreatedToUtc, nameof(search.CreatedFromUtc));
        ValidateRange(search.UpdatedFromUtc, search.UpdatedToUtc, nameof(search.UpdatedFromUtc));
        var result = await memoryRepository.SearchAsync(
            scope,
            new MemorySearchCriteria(
                search.MemoryType,
                search.CreatedFromUtc,
                search.CreatedToUtc,
                search.UpdatedFromUtc,
                search.UpdatedToUtc,
                search.IncludeInactive,
                search.SortOrder),
            page,
            cancellationToken);
        var payloads = await memoryRepository.GetPayloadsByIdsAsync(
            scope,
            result.Items.Select(item => item.Id).ToArray(),
            cancellationToken);
        var details = new List<MemoryDetails>(result.Items.Count);
        foreach (var memory in result.Items)
        {
            if (!payloads.TryGetValue(memory.Id, out var payload))
            {
                throw new InvalidDataException("기억 본문을 찾을 수 없습니다.");
            }

            var content = await DecryptAsync(
                payload.Value,
                scope,
                memory.Id,
                nameof(MemoryPayload),
                nameof(MemoryPayload.Value),
                cancellationToken);
            details.Add(ToDetails(memory, content));
        }

        return new MemoryPage(details, result.PageNumber, result.PageSize, result.TotalCount);
    }

    public async Task<MemoryDetails?> UpdateAsync(
        TenantUserScope scope,
        Guid memoryId,
        UpdateMemoryCommand command,
        CancellationToken cancellationToken)
    {
        ValidateContent(command.Content);
        ValidateConfidence(command.ConfidenceScore);
        var memory = await memoryRepository.GetByIdAsync(scope, memoryId, cancellationToken);
        if (memory is null)
        {
            return null;
        }

        var current = await memoryRepository.GetPayloadByIdAsync(scope, memoryId, cancellationToken)
            ?? throw new InvalidDataException("기억 본문을 찾을 수 없습니다.");
        var revisionId = Guid.NewGuid();
        var previousPlaintext = await encryptionService.DecryptAsync(
            current.Value,
            new EncryptionContext(scope.TenantId, scope.UserId, memoryId, nameof(MemoryPayload), nameof(MemoryPayload.Value)),
            cancellationToken);
        EncryptedPayload priorValue;
        try
        {
            priorValue = await encryptionService.EncryptAsync(
                previousPlaintext,
                new EncryptionContext(
                    scope.TenantId,
                    scope.UserId,
                    revisionId,
                    nameof(MemoryRevision),
                    nameof(MemoryRevision.PreviousValue)),
                cancellationToken);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(previousPlaintext);
        }
        var replacement = await EncryptAsync(
            command.Content,
            scope,
            memoryId,
            nameof(MemoryPayload),
            nameof(MemoryPayload.Value),
            cancellationToken);
        var now = DateTime.UtcNow;
        var sourceId = Guid.NewGuid();
        var revision = new MemoryRevision(
            revisionId,
            scope.TenantId,
            scope.UserId,
            memoryId,
            memory.Version,
            MemoryChangeType.Updated,
            priorValue,
            now,
            scope.UserId);
        var source = new MemorySource(
            sourceId,
            scope.TenantId,
            scope.UserId,
            memoryId,
            MemorySourceType.UserCorrection,
            command.ConfidenceScore,
            now);
        memory.Update(
            command.MemoryType,
            command.ConfidenceScore,
            command.VerificationStatus,
            command.Sensitivity,
            now);
        var replacementPayload = new MemoryPayload(scope.TenantId, scope.UserId, memoryId, replacement);

        await unitOfWork.ExecuteInTransactionAsync(
            async token =>
            {
                await memoryRepository.UpdateAsync(scope, memory, replacementPayload, revision, source, [], token);
            },
            cancellationToken);
        return ToDetails(memory, command.Content);
    }

    public async Task<bool> DeleteAsync(
        TenantUserScope scope,
        Guid memoryId,
        CancellationToken cancellationToken)
    {
        if (await memoryRepository.GetByIdAsync(scope, memoryId, cancellationToken) is null)
        {
            return false;
        }

        await unitOfWork.ExecuteInTransactionAsync(
            token => memoryRepository.DeleteAsync(scope, memoryId, token),
            cancellationToken);
        return true;
    }

    private async Task<EncryptedPayload> EncryptAsync(
        string content,
        TenantUserScope scope,
        Guid recordId,
        string entityType,
        string fieldName,
        CancellationToken cancellationToken)
    {
        var plaintext = Encoding.UTF8.GetBytes(content);
        try
        {
            return await encryptionService.EncryptAsync(
                plaintext,
                new EncryptionContext(scope.TenantId, scope.UserId, recordId, entityType, fieldName),
                cancellationToken);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    private async Task<string> DecryptAsync(
        EncryptedPayload payload,
        TenantUserScope scope,
        Guid recordId,
        string entityType,
        string fieldName,
        CancellationToken cancellationToken)
    {
        var plaintext = await encryptionService.DecryptAsync(
            payload,
            new EncryptionContext(scope.TenantId, scope.UserId, recordId, entityType, fieldName),
            cancellationToken);
        try
        {
            return new UTF8Encoding(false, true).GetString(plaintext);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    private static MemoryDetails ToDetails(MemoryItem memory, string content) =>
        new(
            memory.Id,
            memory.MemoryType,
            content,
            memory.ConfidenceScore,
            memory.VerificationStatus,
            memory.Sensitivity,
            new DateTimeOffset(memory.CreatedAtUtc, TimeSpan.Zero),
            new DateTimeOffset(memory.UpdatedAtUtc, TimeSpan.Zero),
            memory.IsActive,
            memory.Version);

    private static void ValidateContent(string content)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(content);
        if (content.Length > MaximumContentLength)
        {
            throw new ArgumentOutOfRangeException(nameof(content), "기억 본문은 32,000자를 초과할 수 없습니다.");
        }
    }

    private static void ValidateConfidence(int confidenceScore)
    {
        if (confidenceScore is < 0 or > 10_000)
        {
            throw new ArgumentOutOfRangeException(nameof(confidenceScore));
        }
    }

    private static void ValidateRange(DateTime? from, DateTime? to, string parameterName)
    {
        if ((from.HasValue && from.Value.Kind != DateTimeKind.Utc) ||
            (to.HasValue && to.Value.Kind != DateTimeKind.Utc) ||
            (from.HasValue && to.HasValue && from >= to))
        {
            throw new ArgumentException("시간 범위는 UTC이며 시작은 종료보다 앞서야 합니다.", parameterName);
        }
    }
}
