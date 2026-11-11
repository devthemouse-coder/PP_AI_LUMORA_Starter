using System.Security.Cryptography;

namespace Lumora.Infrastructure.Security.KeyManagement;

public sealed class KeyMaterial(string encryptionKeyId, byte[] encryptionKey, string searchKeyId, byte[] searchKey)
    : IDisposable
{
    public string EncryptionKeyId { get; } = encryptionKeyId;

    public byte[] EncryptionKey { get; } = encryptionKey;

    public string SearchKeyId { get; } = searchKeyId;

    public byte[] SearchKey { get; } = searchKey;

    public void Dispose()
    {
        CryptographicOperations.ZeroMemory(EncryptionKey);
        CryptographicOperations.ZeroMemory(SearchKey);
    }
}
