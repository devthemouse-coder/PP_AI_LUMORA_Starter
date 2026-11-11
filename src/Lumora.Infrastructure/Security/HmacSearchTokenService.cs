using System.Security.Cryptography;
using System.Text;
using Lumora.Application.Abstractions.Persistence;
using Lumora.Application.Abstractions.Security;
using Lumora.Domain.Entities;
using Lumora.Infrastructure.Security.KeyManagement;

namespace Lumora.Infrastructure.Security;

public sealed class HmacSearchTokenService(IKeyMaterialProvider keyMaterialProvider) : ISearchTokenService
{
    public Task<(string KeyId, byte[] Token)> CreateBirthdayMonthTokenAsync(
        TenantUserScope scope,
        int month,
        CancellationToken cancellationToken)
    {
        if (month is < 1 or > 12)
        {
            throw new ArgumentOutOfRangeException(nameof(month));
        }

        return CreateTokenAsync(scope, "birthday-month", [checked((byte)month)], cancellationToken);
    }

    public Task<(string KeyId, byte[] Token)> CreateBirthdayDateTokenAsync(
        TenantUserScope scope,
        int month,
        int day,
        CancellationToken cancellationToken)
    {
        if (month is < 1 or > 12 || day < 1 || day > DateTime.DaysInMonth(2000, month))
        {
            throw new ArgumentOutOfRangeException(nameof(day), "생일 월·일이 올바르지 않습니다.");
        }

        return CreateTokenAsync(
            scope,
            "birthday-date",
            [checked((byte)month), checked((byte)day)],
            cancellationToken);
    }

    public Task<(string KeyId, byte[] Token)> CreateBirthdaySubjectTokenAsync(
        TenantUserScope scope,
        string normalizedSubject,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(normalizedSubject);
        var canonicalSubject = normalizedSubject.Normalize(NormalizationForm.FormKC).Trim().ToUpperInvariant();
        if (canonicalSubject.Length > 512)
        {
            throw new ArgumentOutOfRangeException(nameof(normalizedSubject), "검색 대상은 512자를 초과할 수 없습니다.");
        }

        return CreateTokenAsync(
            scope,
            "birthday-subject",
            Encoding.UTF8.GetBytes(canonicalSubject),
            cancellationToken);
    }

    public Task<(string KeyId, byte[] Token)> CreateFactTypeTokenAsync(
        TenantUserScope scope,
        string factType,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(factType);
        var canonicalFactType = factType.Trim().Normalize(NormalizationForm.FormKC);
        return CreateTokenAsync(
            scope,
            "fact-type",
            Encoding.UTF8.GetBytes(canonicalFactType),
            cancellationToken);
    }

    public Task<(string KeyId, byte[] Token)> CreateExactValueTokenAsync(
        TenantUserScope scope,
        string normalizedValue,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(normalizedValue);
        var canonicalValue = normalizedValue.Trim().Normalize(NormalizationForm.FormKC).ToUpperInvariant();
        return CreateTokenAsync(
            scope,
            "exact-value",
            Encoding.UTF8.GetBytes(canonicalValue),
            cancellationToken);
    }

    public Task<(string KeyId, byte[] Token)> CreateCalendarDateTokenAsync(
        TenantUserScope scope,
        DateOnly date,
        CancellationToken cancellationToken) =>
        CreateTokenAsync(
            scope,
            "calendar-date",
            Encoding.ASCII.GetBytes(date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)),
            cancellationToken);

    public async Task<IReadOnlyList<(string KeyId, byte[] Token)>> CreateCalendarDateTokensAsync(
        TenantUserScope scope,
        IReadOnlyCollection<DateOnly> dates,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dates);
        using var keys = await keyMaterialProvider.LoadAsync(cancellationToken);
        return dates.Select(date => CreateToken(
            keys,
            scope,
            "calendar-date",
            Encoding.ASCII.GetBytes(date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture))))
            .ToArray();
    }

    public Task<(string KeyId, byte[] Token)> CreateFactSubjectTokenAsync(
        TenantUserScope scope,
        string normalizedSubject,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(normalizedSubject);
        var canonicalSubject = normalizedSubject.Normalize(NormalizationForm.FormKC).Trim().ToUpperInvariant();
        if (canonicalSubject.Length > 512)
        {
            throw new ArgumentOutOfRangeException(nameof(normalizedSubject), "검색 대상은 512자를 초과할 수 없습니다.");
        }

        return CreateTokenAsync(
            scope,
            "fact-subject",
            Encoding.UTF8.GetBytes(canonicalSubject),
            cancellationToken);
    }

    private async Task<(string KeyId, byte[] Token)> CreateTokenAsync(
        TenantUserScope scope,
        string purpose,
        byte[] value,
        CancellationToken cancellationToken)
    {
        using var keys = await keyMaterialProvider.LoadAsync(cancellationToken);
        return CreateToken(keys, scope, purpose, value);
    }

    private static (string KeyId, byte[] Token) CreateToken(
        KeyMaterial keys,
        TenantUserScope scope,
        string purpose,
        byte[] value)
    {
        using var input = new MemoryStream();
        using var writer = new BinaryWriter(input, Encoding.UTF8, leaveOpen: true);
        writer.Write(Encoding.UTF8.GetBytes($"P.P. AI - LUMORA|HMAC-SHA-256|v1|{purpose}"));
        writer.Write(scope.TenantId.ToByteArray());
        writer.Write(scope.UserId.ToByteArray());
        writer.Write(value.Length);
        writer.Write(value);
        writer.Flush();

        var inputBytes = input.GetBuffer();
        try
        {
            var token = HMACSHA256.HashData(keys.SearchKey, inputBytes.AsSpan(0, checked((int)input.Length)));
            return (keys.SearchKeyId, token);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(inputBytes);
            CryptographicOperations.ZeroMemory(value);
        }
    }
}
