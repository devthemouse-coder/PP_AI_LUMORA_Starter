using System.Security.Cryptography;
using Lumora.Infrastructure.Security.KeyManagement;

namespace Lumora.Infrastructure.IntegrationTests;

public sealed class KeyRecoveryTests
{
    [Fact]
    public async Task Password_protected_recovery_file_imports_both_keys_and_rejects_bad_password_or_tampering()
    {
        var root = Path.Combine(Path.GetTempPath(), $"lumora-key-recovery-{Guid.NewGuid():N}");
        var sourceDirectory = Path.Combine(root, "source");
        var restoredDirectory = Path.Combine(root, "restored");
        var wrongPasswordDirectory = Path.Combine(root, "wrong-password");
        var damagedDirectory = Path.Combine(root, "damaged");
        var recoveryFile = Path.Combine(root, "offline", "keys.lumora-keys");
        var damagedFile = Path.Combine(root, "offline", "damaged.lumora-keys");
        var password = "synthetic recovery phrase 2026";
        try
        {
            Directory.CreateDirectory(root);
            var sourceStore = new DpapiKeyMaterialStore(sourceDirectory);
            await sourceStore.InitializeAsync(CancellationToken.None);
            using var sourceKeys = await sourceStore.LoadAsync(CancellationToken.None);
            var recovery = new PasswordProtectedKeyRecoveryService(sourceStore);
            await recovery.ExportAsync(recoveryFile, password.AsMemory(), CancellationToken.None);

            var contents = await File.ReadAllBytesAsync(recoveryFile);
            contents[^1] ^= 0x01;
            await File.WriteAllBytesAsync(damagedFile, contents);
            CryptographicOperations.ZeroMemory(contents);

            await Assert.ThrowsAnyAsync<CryptographicException>(
                () => recovery.ImportAsync(
                    recoveryFile,
                    "incorrect synthetic password".AsMemory(),
                    new DpapiKeyMaterialStore(wrongPasswordDirectory),
                    CancellationToken.None));
            await Assert.ThrowsAnyAsync<CryptographicException>(
                () => recovery.ImportAsync(
                    damagedFile,
                    password.AsMemory(),
                    new DpapiKeyMaterialStore(damagedDirectory),
                    CancellationToken.None));
            Assert.False(Directory.Exists(wrongPasswordDirectory));
            Assert.False(Directory.Exists(damagedDirectory));

            await recovery.ImportAsync(
                recoveryFile,
                password.AsMemory(),
                new DpapiKeyMaterialStore(restoredDirectory),
                CancellationToken.None);
            using var restoredKeys = await new DpapiKeyMaterialStore(restoredDirectory).LoadAsync(CancellationToken.None);
            Assert.Equal(sourceKeys.EncryptionKeyId, restoredKeys.EncryptionKeyId);
            Assert.Equal(sourceKeys.SearchKeyId, restoredKeys.SearchKeyId);
            Assert.True(CryptographicOperations.FixedTimeEquals(sourceKeys.EncryptionKey, restoredKeys.EncryptionKey));
            Assert.True(CryptographicOperations.FixedTimeEquals(sourceKeys.SearchKey, restoredKeys.SearchKey));
            Assert.False(CryptographicOperations.FixedTimeEquals(restoredKeys.EncryptionKey, restoredKeys.SearchKey));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Key_recovery_refuses_to_overwrite_existing_dpapi_keys()
    {
        var root = Path.Combine(Path.GetTempPath(), $"lumora-key-recovery-existing-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(root);
            var store = new DpapiKeyMaterialStore(Path.Combine(root, "keys"));
            await store.InitializeAsync(CancellationToken.None);
            using var keys = await store.LoadAsync(CancellationToken.None);
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => store.ImportAsync(keys, CancellationToken.None));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
