using Lumora.Application.Abstractions.Persistence;
using Lumora.Domain.Entities;

namespace Lumora.Application.Abstractions.Security;

public interface ISearchTokenService
{
    Task<(string KeyId, byte[] Token)> CreateBirthdayMonthTokenAsync(
        TenantUserScope scope,
        int month,
        CancellationToken cancellationToken);

    Task<(string KeyId, byte[] Token)> CreateBirthdayDateTokenAsync(
        TenantUserScope scope,
        int month,
        int day,
        CancellationToken cancellationToken);

    Task<(string KeyId, byte[] Token)> CreateBirthdaySubjectTokenAsync(
        TenantUserScope scope,
        string normalizedSubject,
        CancellationToken cancellationToken);

    Task<(string KeyId, byte[] Token)> CreateFactTypeTokenAsync(
        TenantUserScope scope,
        string factType,
        CancellationToken cancellationToken);

    Task<(string KeyId, byte[] Token)> CreateExactValueTokenAsync(
        TenantUserScope scope,
        string normalizedValue,
        CancellationToken cancellationToken);

    Task<(string KeyId, byte[] Token)> CreateCalendarDateTokenAsync(
        TenantUserScope scope,
        DateOnly date,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<(string KeyId, byte[] Token)>> CreateCalendarDateTokensAsync(
        TenantUserScope scope,
        IReadOnlyCollection<DateOnly> dates,
        CancellationToken cancellationToken);

    Task<(string KeyId, byte[] Token)> CreateFactSubjectTokenAsync(
        TenantUserScope scope,
        string normalizedSubject,
        CancellationToken cancellationToken);
}
