using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Lumora.Application.Abstractions.Persistence;
using Lumora.Application.Abstractions.Security;
using Lumora.Domain.Entities;

namespace Lumora.Application.UseCases.StructuredFacts;

public sealed class StructuredFactUseCases(
    IMemoryRepository memoryRepository,
    IStructuredFactRepository structuredFactRepository,
    IUnitOfWork unitOfWork,
    IEncryptionService encryptionService,
    ISearchTokenService searchTokenService) : IStructuredFactUseCases
{
    private const int MaximumSubjectLength = 512;
    private const int MaximumTextValueLength = 8_000;
    private const int MaximumDateRangeDays = 366;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<StructuredFactDetails> CreateAsync(
        TenantUserScope scope,
        CreateStructuredFactCommand command,
        CancellationToken cancellationToken)
    {
        var factType = ValidateFactType(command.FactType);
        ValidateValue(command.Value);
        var serializedValue = SerializeValue(command.Value);
        try
        {
            var tokens = await CreateTokensAsync(scope, factType, command.Value, cancellationToken);

            var memoryId = Guid.NewGuid();
            var now = DateTime.UtcNow;
            var memoryPayload = await EncryptAsync(
                serializedValue,
                scope,
                memoryId,
                nameof(MemoryPayload),
                nameof(MemoryPayload.Value),
                cancellationToken);
            var factPayload = await EncryptAsync(
                serializedValue,
                scope,
                memoryId,
                nameof(StructuredFact),
                nameof(StructuredFact.Value),
                cancellationToken);
            var memory = new MemoryItem(memoryId, scope.TenantId, scope.UserId, MemoryType.Fact, now);
            memory.Update(
                MemoryType.Fact,
                10_000,
                MemoryVerificationStatus.Verified,
                MemorySensitivity.Personal,
                now);
            var payload = new MemoryPayload(scope.TenantId, scope.UserId, memoryId, memoryPayload);
            var fact = new StructuredFact(scope.TenantId, scope.UserId, memoryId, factType, factPayload);
            var source = new MemorySource(
                Guid.NewGuid(),
                scope.TenantId,
                scope.UserId,
                memoryId,
                MemorySourceType.DirectInput,
                10_000,
                now);
            var lookupTokens = CreateEntities(scope, memoryId, tokens);

            await unitOfWork.ExecuteInTransactionAsync(
                async token =>
                {
                    await EnsureNoIdentityConflictAsync(scope, tokens, null, token);
                    await memoryRepository.AddAsync(scope, memory, payload, source, lookupTokens, token);
                    await structuredFactRepository.AddAsync(scope, fact, token);
                },
                cancellationToken);
            return ToDetails(memory, factType, command.Value);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(serializedValue);
        }
    }

    public async Task<StructuredFactDetails?> GetAsync(
        TenantUserScope scope,
        Guid memoryId,
        CancellationToken cancellationToken)
    {
        var memory = await memoryRepository.GetByIdAsync(scope, memoryId, cancellationToken);
        var fact = await structuredFactRepository.GetByMemoryIdAsync(scope, memoryId, cancellationToken);
        if (memory is null || fact is null)
        {
            return null;
        }

        var value = await DecryptValueAsync(fact.Value, scope, memoryId, cancellationToken);
        return ToDetails(memory, fact.FactType, value);
    }

    public async Task<StructuredFactPage> SearchAsync(
        TenantUserScope scope,
        StructuredFactSearch search,
        PageRequest page,
        CancellationToken cancellationToken)
    {
        var factType = string.IsNullOrWhiteSpace(search.FactType) ? null : ValidateFactType(search.FactType);
        var tokenGroups = new List<IReadOnlyCollection<LookupTokenQuery>>();
        if (!string.IsNullOrWhiteSpace(search.Subject))
        {
            var subject = await searchTokenService.CreateFactSubjectTokenAsync(scope, search.Subject, cancellationToken);
            tokenGroups.Add([new LookupTokenQuery(LookupTokenType.FactSubject, subject.KeyId, subject.Token)]);
        }

        if (!string.IsNullOrWhiteSpace(search.ExactValue))
        {
            var exactValue = CanonicalExactValue(search.ExactValue);
            var token = await searchTokenService.CreateExactValueTokenAsync(scope, exactValue, cancellationToken);
            tokenGroups.Add([new LookupTokenQuery(LookupTokenType.ExactValue, token.KeyId, token.Token)]);
        }

        if (search.BirthdayMonth.HasValue)
        {
            if (search.BirthdayMonth is < 1 or > 12)
            {
                throw new ArgumentOutOfRangeException(nameof(search.BirthdayMonth), "월은 1부터 12 사이여야 합니다.");
            }

            var token = await searchTokenService.CreateBirthdayMonthTokenAsync(
                scope,
                search.BirthdayMonth.Value,
                cancellationToken);
            tokenGroups.Add([new LookupTokenQuery(LookupTokenType.BirthdayMonth, token.KeyId, token.Token)]);
        }

        IReadOnlyCollection<LookupTokenQuery>? dateTokens = null;
        if (search.DateFrom.HasValue != search.DateTo.HasValue)
        {
            throw new ArgumentException("날짜 범위에는 시작과 종료 날짜가 모두 필요합니다.");
        }

        if (search.DateFrom is { } dateFrom && search.DateTo is { } dateTo)
        {
            if (dateTo < dateFrom || dateTo.DayNumber - dateFrom.DayNumber > MaximumDateRangeDays)
            {
                throw new ArgumentOutOfRangeException(nameof(search.DateTo), "날짜 범위는 역전될 수 없고 최대 367일입니다.");
            }

            var dates = Enumerable.Range(0, dateTo.DayNumber - dateFrom.DayNumber + 1)
                .Select(offset => dateFrom.AddDays(offset))
                .ToArray();
            var rangeTokens = await searchTokenService.CreateCalendarDateTokensAsync(scope, dates, cancellationToken);
            dateTokens = rangeTokens
                .Select(token => new LookupTokenQuery(LookupTokenType.CalendarDate, token.KeyId, token.Token))
                .ToArray();
        }

        var result = await structuredFactRepository.SearchAsync(
            scope,
            new StructuredFactSearchCriteria(factType, tokenGroups, dateTokens, search.SortOrder),
            page,
            cancellationToken);
        var items = new List<StructuredFactDetails>(result.Items.Count);
        foreach (var item in result.Items)
        {
            var value = await DecryptValueAsync(item.Fact.Value, scope, item.Fact.MemoryId, cancellationToken);
            items.Add(ToDetails(item.Memory, item.Fact.FactType, value));
        }

        return new StructuredFactPage(items, result.PageNumber, result.PageSize, result.TotalCount);
    }

    public async Task<StructuredFactDetails?> UpdateAsync(
        TenantUserScope scope,
        Guid memoryId,
        UpdateStructuredFactCommand command,
        CancellationToken cancellationToken)
    {
        var factType = ValidateFactType(command.FactType);
        ValidateValue(command.Value);
        var memory = await memoryRepository.GetByIdAsync(scope, memoryId, cancellationToken);
        var fact = await structuredFactRepository.GetByMemoryIdAsync(scope, memoryId, cancellationToken);
        var oldMemoryPayload = await memoryRepository.GetPayloadByIdAsync(scope, memoryId, cancellationToken);
        if (memory is null || fact is null || oldMemoryPayload is null)
        {
            return null;
        }

        var tokens = await CreateTokensAsync(scope, factType, command.Value, cancellationToken);

        var previousPlaintext = await encryptionService.DecryptAsync(
            oldMemoryPayload.Value,
            new EncryptionContext(scope.TenantId, scope.UserId, memoryId, nameof(MemoryPayload), nameof(MemoryPayload.Value)),
            cancellationToken);
        var revisionId = Guid.NewGuid();
        EncryptedPayload priorValue;
        try
        {
            priorValue = await EncryptAsync(
                previousPlaintext,
                scope,
                revisionId,
                nameof(MemoryRevision),
                nameof(MemoryRevision.PreviousValue),
                cancellationToken);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(previousPlaintext);
        }

        var newValue = SerializeValue(command.Value);
        EncryptedPayload newMemoryPayload;
        EncryptedPayload newFactPayload;
        try
        {
            newMemoryPayload = await EncryptAsync(
                newValue,
                scope,
                memoryId,
                nameof(MemoryPayload),
                nameof(MemoryPayload.Value),
                cancellationToken);
            newFactPayload = await EncryptAsync(
                newValue,
                scope,
                memoryId,
                nameof(StructuredFact),
                nameof(StructuredFact.Value),
                cancellationToken);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(newValue);
        }

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
            memory.ConfidenceScore,
            now);
        memory.Update(MemoryType.Fact, memory.ConfidenceScore, memory.VerificationStatus, memory.Sensitivity, now);
        fact.ChangeFactType(factType);
        fact.ReplaceValue(newFactPayload);
        var replacementPayload = new MemoryPayload(scope.TenantId, scope.UserId, memoryId, newMemoryPayload);
        var lookupTokens = CreateEntities(scope, memoryId, tokens);

        await unitOfWork.ExecuteInTransactionAsync(
            async token =>
            {
                await EnsureNoIdentityConflictAsync(scope, tokens, memoryId, token);
                await memoryRepository.UpdateAsync(
                    scope,
                    memory,
                    replacementPayload,
                    revision,
                    source,
                    lookupTokens,
                    token);
                await structuredFactRepository.UpdateAsync(scope, fact, token);
            },
            cancellationToken);
        return ToDetails(memory, factType, command.Value);
    }

    private async Task EnsureNoIdentityConflictAsync(
        TenantUserScope scope,
        FactTokens tokens,
        Guid? exceptMemoryId,
        CancellationToken cancellationToken)
    {
        var identityTokens = new[] { tokens.FactType, tokens.Subject };
        if (!await structuredFactRepository.ExistsMatchingAsync(
                scope,
                identityTokens,
                exceptMemoryId,
                cancellationToken))
        {
            return;
        }

        if (await structuredFactRepository.ExistsMatchingAsync(
                scope,
                [.. identityTokens, tokens.ExactValue],
                exceptMemoryId,
                cancellationToken))
        {
            throw new DuplicateStructuredFactException();
        }

        throw new ConflictingStructuredFactException();
    }

    public async Task<bool> DeleteAsync(
        TenantUserScope scope,
        Guid memoryId,
        CancellationToken cancellationToken)
    {
        if (await structuredFactRepository.GetByMemoryIdAsync(scope, memoryId, cancellationToken) is null)
        {
            return false;
        }

        await unitOfWork.ExecuteInTransactionAsync(
            token => memoryRepository.DeleteAsync(scope, memoryId, token),
            cancellationToken);
        return true;
    }

    private async Task<StructuredFactValue> DecryptValueAsync(
        EncryptedPayload payload,
        TenantUserScope scope,
        Guid memoryId,
        CancellationToken cancellationToken)
    {
        var plaintext = await encryptionService.DecryptAsync(
            payload,
            new EncryptionContext(scope.TenantId, scope.UserId, memoryId, nameof(StructuredFact), nameof(StructuredFact.Value)),
            cancellationToken);
        try
        {
            return JsonSerializer.Deserialize<StructuredFactValue>(plaintext, JsonOptions)
                ?? throw new InvalidDataException("구조화 사실 데이터가 유효하지 않습니다.");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    private async Task<EncryptedPayload> EncryptAsync(
        ReadOnlyMemory<byte> plaintext,
        TenantUserScope scope,
        Guid recordId,
        string entityType,
        string fieldName,
        CancellationToken cancellationToken) =>
        await encryptionService.EncryptAsync(
            plaintext,
            new EncryptionContext(scope.TenantId, scope.UserId, recordId, entityType, fieldName),
            cancellationToken);

    private async Task<FactTokens> CreateTokensAsync(
        TenantUserScope scope,
        string factType,
        StructuredFactValue value,
        CancellationToken cancellationToken)
    {
        var type = await searchTokenService.CreateFactTypeTokenAsync(scope, factType, cancellationToken);
        var subject = await searchTokenService.CreateFactSubjectTokenAsync(scope, value.Subject, cancellationToken);
        var exact = await searchTokenService.CreateExactValueTokenAsync(
            scope,
            CanonicalValue(value),
            cancellationToken);
        var isBirthday = string.Equals(factType, "Person.Birthday", StringComparison.OrdinalIgnoreCase);
        var birthdayMonth = isBirthday && value.Month.HasValue
            ? await searchTokenService.CreateBirthdayMonthTokenAsync(scope, value.Month.Value, cancellationToken)
            : ((string KeyId, byte[] Token)?)null;
        var birthdayDate = isBirthday && value.Month.HasValue
            ? await searchTokenService.CreateBirthdayDateTokenAsync(
                scope,
                value.Month.Value,
                value.Day!.Value,
                cancellationToken)
            : ((string KeyId, byte[] Token)?)null;
        var calendarDate = value.Year.HasValue
            ? await searchTokenService.CreateCalendarDateTokenAsync(
                scope,
                new DateOnly(value.Year.Value, value.Month!.Value, value.Day!.Value),
                cancellationToken)
            : ((string KeyId, byte[] Token)?)null;

        return new FactTokens(
            new LookupTokenQuery(LookupTokenType.FactType, type.KeyId, type.Token),
            new LookupTokenQuery(LookupTokenType.FactSubject, subject.KeyId, subject.Token),
            new LookupTokenQuery(LookupTokenType.ExactValue, exact.KeyId, exact.Token),
            birthdayMonth is { } month
                ? new LookupTokenQuery(LookupTokenType.BirthdayMonth, month.KeyId, month.Token)
                : null,
            birthdayDate is { } date
                ? new LookupTokenQuery(LookupTokenType.BirthdayDate, date.KeyId, date.Token)
                : null,
            calendarDate is { } calendar
                ? new LookupTokenQuery(LookupTokenType.CalendarDate, calendar.KeyId, calendar.Token)
                : null);
    }

    private static IReadOnlyCollection<MemoryLookupToken> CreateEntities(
        TenantUserScope scope,
        Guid memoryId,
        FactTokens tokens)
    {
        var queries = new[]
        {
            tokens.FactType,
            tokens.Subject,
            tokens.ExactValue,
            tokens.BirthdayMonth,
            tokens.BirthdayDate,
            tokens.CalendarDate
        }.Where(query => query is not null).Select(query => query!).ToArray();
        return queries.Select(query => new MemoryLookupToken(
            Guid.NewGuid(),
            scope.TenantId,
            scope.UserId,
            memoryId,
            query.TokenType,
            query.KeyId,
            query.Token)).ToArray();
    }

    private static string ValidateFactType(string factType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(factType);
        var normalized = factType.Trim();
        if (normalized.Length > 160 || normalized.Any(char.IsControl))
        {
            throw new ArgumentOutOfRangeException(nameof(factType), "정보 유형은 160자 이내의 제어 문자가 아닌 값이어야 합니다.");
        }

        return normalized;
    }

    private static void ValidateValue(StructuredFactValue value)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentException.ThrowIfNullOrWhiteSpace(value.Subject);
        if (value.Subject.Trim().Length > MaximumSubjectLength)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "대상은 512자를 초과할 수 없습니다.");
        }

        var hasText = !string.IsNullOrWhiteSpace(value.TextValue);
        var hasPartialDate = value.Month.HasValue || value.Day.HasValue || value.Year.HasValue;
        if (hasText == hasPartialDate)
        {
            throw new ArgumentException("사실 값은 텍스트 또는 날짜 중 하나로 지정해야 합니다.", nameof(value));
        }

        if (hasText)
        {
            if (value.TextValue!.Length > MaximumTextValueLength)
            {
                throw new ArgumentOutOfRangeException(nameof(value), "사실 값은 8,000자를 초과할 수 없습니다.");
            }

            return;
        }

        if ((value.Year.HasValue && value.Year.Value is < 1 or > 9999) ||
            !value.Month.HasValue || !value.Day.HasValue ||
            value.Month is < 1 or > 12 ||
            value.Day < 1 ||
            value.Day > DateTime.DaysInMonth(value.Year ?? 2000, value.Month.Value))
        {
            throw new ArgumentException("날짜는 유효한 월·일과 선택적 연도로 지정해야 합니다. 연도가 없으면 임의로 만들지 않습니다.", nameof(value));
        }
    }

    private static byte[] SerializeValue(StructuredFactValue value) =>
        JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions);

    private static string CanonicalValue(StructuredFactValue value) =>
        value.TextValue is not null
            ? value.TextValue.Trim().Normalize(NormalizationForm.FormKC).ToUpperInvariant()
            : value.Year.HasValue
                ? new DateOnly(value.Year.Value, value.Month!.Value, value.Day!.Value)
                    .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                : $"{value.Month!.Value:D2}-{value.Day!.Value:D2}";

    private static string CanonicalExactValue(string value) =>
        value.Trim().Normalize(NormalizationForm.FormKC).ToUpperInvariant();

    private static StructuredFactDetails ToDetails(MemoryItem memory, string factType, StructuredFactValue value) =>
        new(
            memory.Id,
            factType,
            value,
            new DateTimeOffset(memory.CreatedAtUtc, TimeSpan.Zero),
            new DateTimeOffset(memory.UpdatedAtUtc, TimeSpan.Zero),
            memory.ConfidenceScore,
            memory.Version);

    private sealed record FactTokens(
        LookupTokenQuery FactType,
        LookupTokenQuery Subject,
        LookupTokenQuery ExactValue,
        LookupTokenQuery? BirthdayMonth,
        LookupTokenQuery? BirthdayDate,
        LookupTokenQuery? CalendarDate);
}
