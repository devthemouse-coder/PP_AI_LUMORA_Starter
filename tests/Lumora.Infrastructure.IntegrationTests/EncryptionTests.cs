using System.Security.Cryptography;
using System.Text;
using Lumora.Application.Abstractions.Persistence;
using Lumora.Application.Abstractions.Security;
using Lumora.Infrastructure.Security;
using Lumora.Infrastructure.Security.KeyManagement;
using Microsoft.Extensions.Configuration;

namespace Lumora.Infrastructure.IntegrationTests;

public sealed class EncryptionTests
{
    [Fact]
    public async Task Dpapi_key_store_encrypts_payloads_and_rejects_modified_authentication_data()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var store = new DpapiKeyMaterialStore(directory);
            await store.InitializeAsync(CancellationToken.None);
            using var keys = await store.LoadAsync(CancellationToken.None);
            Assert.Equal(32, keys.EncryptionKey.Length);
            Assert.Equal(32, keys.SearchKey.Length);
            Assert.False(CryptographicOperations.FixedTimeEquals(keys.EncryptionKey, keys.SearchKey));
            Assert.NotEqual(keys.EncryptionKeyId, keys.SearchKeyId);

            var encryption = new AesGcmEncryptionService(store);
            var context = new EncryptionContext(
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                "MemoryPayload",
                "Value");
            var plaintext = Encoding.UTF8.GetBytes("synthetic-test-payload");
            var first = await encryption.EncryptAsync(plaintext, context, CancellationToken.None);
            var second = await encryption.EncryptAsync(plaintext, context, CancellationToken.None);
            Assert.NotEqual(first.Nonce, second.Nonce);
            Assert.NotEqual(first.Ciphertext, second.Ciphertext);
            Assert.Equal(plaintext, await encryption.DecryptAsync(first, context, CancellationToken.None));

            var wrongContext = context with { UserId = Guid.NewGuid() };
            await Assert.ThrowsAnyAsync<CryptographicException>(
                () => encryption.DecryptAsync(first, wrongContext, CancellationToken.None));

            var tampered = new Lumora.Domain.Entities.EncryptedPayload(
                first.FormatVersion,
                first.KeyId,
                first.Nonce,
                first.Ciphertext,
                first.AuthenticationTag.Select((value, index) => index == 0 ? (byte)(value ^ 0xFF) : value).ToArray());
            await Assert.ThrowsAnyAsync<CryptographicException>(
                () => encryption.DecryptAsync(tampered, context, CancellationToken.None));

            var otherDirectory = CreateTemporaryDirectory();
            try
            {
                var otherStore = new DpapiKeyMaterialStore(otherDirectory);
                await otherStore.InitializeAsync(CancellationToken.None);
                await Assert.ThrowsAsync<CryptographicException>(
                    () => new AesGcmEncryptionService(otherStore)
                        .DecryptAsync(first, context, CancellationToken.None));
            }
            finally
            {
                DeleteTemporaryDirectory(otherDirectory);
            }
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [Fact]
    public async Task Hmac_tokens_are_deterministic_scoped_and_domain_separated()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var store = new DpapiKeyMaterialStore(directory);
            await store.InitializeAsync(CancellationToken.None);
            var tokenService = new HmacSearchTokenService(store);
            var scope = new TenantUserScope(Guid.NewGuid(), Guid.NewGuid());

            var first = await tokenService.CreateBirthdayMonthTokenAsync(scope, 7, CancellationToken.None);
            var repeated = await tokenService.CreateBirthdayMonthTokenAsync(scope, 7, CancellationToken.None);
            var differentMonth = await tokenService.CreateBirthdayMonthTokenAsync(scope, 8, CancellationToken.None);
            var differentScope = await tokenService.CreateBirthdayMonthTokenAsync(
                new TenantUserScope(scope.TenantId, Guid.NewGuid()),
                7,
                CancellationToken.None);
            var date = await tokenService.CreateBirthdayDateTokenAsync(scope, 7, 15, CancellationToken.None);
            var calendarDate = await tokenService.CreateCalendarDateTokenAsync(
                scope,
                new DateOnly(2000, 2, 29),
                CancellationToken.None);
            var calendarDates = await tokenService.CreateCalendarDateTokensAsync(
                scope,
                [new DateOnly(2000, 2, 29)],
                CancellationToken.None);

            Assert.Equal(first.KeyId, repeated.KeyId);
            Assert.Equal(first.Token, repeated.Token);
            Assert.NotEqual(first.Token, differentMonth.Token);
            Assert.NotEqual(first.Token, differentScope.Token);
            Assert.NotEqual(first.Token, date.Token);
            Assert.Equal(calendarDate.KeyId, Assert.Single(calendarDates).KeyId);
            Assert.Equal(calendarDate.Token, calendarDates[0].Token);
            Assert.Equal(32, first.Token.Length);
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
                () => tokenService.CreateBirthdayDateTokenAsync(scope, 2, 30, CancellationToken.None));
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [Fact]
    public async Task Missing_or_damaged_key_material_fails_closed_without_creating_replacement_keys()
    {
        var missingDirectory = CreateTemporaryDirectory();
        var damagedDirectory = CreateTemporaryDirectory();
        try
        {
            var missingStore = new DpapiKeyMaterialStore(missingDirectory);
            await Assert.ThrowsAsync<CryptographicException>(
                () => missingStore.LoadAsync(CancellationToken.None));
            Assert.False(Directory.Exists(missingDirectory));

            var damagedStore = new DpapiKeyMaterialStore(damagedDirectory);
            await damagedStore.InitializeAsync(CancellationToken.None);
            await File.WriteAllBytesAsync(Path.Combine(damagedDirectory, "search.dpapi"), [1, 2, 3]);
            await Assert.ThrowsAsync<CryptographicException>(
                () => damagedStore.LoadAsync(CancellationToken.None));
            Assert.True(File.Exists(Path.Combine(damagedDirectory, "encryption.dpapi")));
            Assert.True(File.Exists(Path.Combine(damagedDirectory, "search.dpapi")));
        }
        finally
        {
            DeleteTemporaryDirectory(missingDirectory);
            DeleteTemporaryDirectory(damagedDirectory);
        }
    }

    private static string CreateTemporaryDirectory() =>
        Path.Combine(Path.GetTempPath(), $"lumora-keys-{Guid.NewGuid():N}");

    private static void DeleteTemporaryDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }
}
