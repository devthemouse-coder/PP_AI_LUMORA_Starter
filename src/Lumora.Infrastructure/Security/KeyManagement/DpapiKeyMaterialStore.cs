using System.Buffers.Binary;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;

namespace Lumora.Infrastructure.Security.KeyManagement;

public sealed class DpapiKeyMaterialStore :
    IKeyMaterialProvider,
    IKeyMaterialInitializer,
    IKeyMaterialRecoveryTarget
{
    private const int KeyLength = 32;
    private const int MaximumProtectedKeyLength = 16_384;
    private static readonly byte[] EncryptionEntropy = Encoding.UTF8.GetBytes("P.P. AI - LUMORA|AES-256-GCM|v1");
    private static readonly byte[] SearchEntropy = Encoding.UTF8.GetBytes("P.P. AI - LUMORA|HMAC-SHA-256|v1");
    private readonly string _directory;

    public DpapiKeyMaterialStore(IConfiguration configuration)
        : this(ResolveKeyDirectory(configuration))
    {
    }

    public DpapiKeyMaterialStore(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        _directory = Path.GetFullPath(directory);
    }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("현재 키 저장소는 Windows DPAPI에서만 사용할 수 있습니다.");
        }

        if (Directory.Exists(_directory) || File.Exists(_directory))
        {
            throw new InvalidOperationException(
                "키 저장 경로가 이미 존재합니다. 기존 키를 덮어쓰거나 자동 교체하지 않습니다.");
        }

        var parent = Path.GetDirectoryName(_directory)
            ?? throw new InvalidOperationException("키 저장 경로의 상위 디렉터리를 확인할 수 없습니다.");
        Directory.CreateDirectory(parent);
        var stagingDirectory = $"{_directory}.initializing-{Guid.NewGuid():N}";
        var encryptionKey = RandomNumberGenerator.GetBytes(KeyLength);
        var searchKey = RandomNumberGenerator.GetBytes(KeyLength);

        try
        {
            Directory.CreateDirectory(stagingDirectory);
            await WriteProtectedKeyAsync(
                Path.Combine(stagingDirectory, "encryption.dpapi"),
                $"enc-v1-{Guid.NewGuid():N}",
                encryptionKey,
                EncryptionEntropy,
                cancellationToken);
            await WriteProtectedKeyAsync(
                Path.Combine(stagingDirectory, "search.dpapi"),
                $"hmac-v1-{Guid.NewGuid():N}",
                searchKey,
                SearchEntropy,
                cancellationToken);
            Directory.Move(stagingDirectory, _directory);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(encryptionKey);
            CryptographicOperations.ZeroMemory(searchKey);
            if (Directory.Exists(stagingDirectory))
            {
                Directory.Delete(stagingDirectory, recursive: true);
            }
        }
    }

    public async Task ImportAsync(KeyMaterial keyMaterial, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(keyMaterial);
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("현재 키 저장소는 Windows DPAPI에서만 사용할 수 있습니다.");
        }

        ValidateKeyMaterial(keyMaterial);
        if (Directory.Exists(_directory) || File.Exists(_directory))
        {
            throw new InvalidOperationException(
                "키 저장 경로가 이미 존재합니다. 기존 키를 덮어쓰거나 자동 교체하지 않습니다.");
        }

        var parent = Path.GetDirectoryName(_directory)
            ?? throw new InvalidOperationException("키 저장 경로의 상위 디렉터리를 확인할 수 없습니다.");
        Directory.CreateDirectory(parent);
        var stagingDirectory = $"{_directory}.importing-{Guid.NewGuid():N}";

        try
        {
            Directory.CreateDirectory(stagingDirectory);
            await WriteProtectedKeyAsync(
                Path.Combine(stagingDirectory, "encryption.dpapi"),
                keyMaterial.EncryptionKeyId,
                keyMaterial.EncryptionKey,
                EncryptionEntropy,
                cancellationToken);
            await WriteProtectedKeyAsync(
                Path.Combine(stagingDirectory, "search.dpapi"),
                keyMaterial.SearchKeyId,
                keyMaterial.SearchKey,
                SearchEntropy,
                cancellationToken);

            using (var imported = await new DpapiKeyMaterialStore(stagingDirectory).LoadAsync(cancellationToken))
            {
                if (!CryptographicOperations.FixedTimeEquals(imported.EncryptionKey, keyMaterial.EncryptionKey) ||
                    !CryptographicOperations.FixedTimeEquals(imported.SearchKey, keyMaterial.SearchKey) ||
                    !string.Equals(imported.EncryptionKeyId, keyMaterial.EncryptionKeyId, StringComparison.Ordinal) ||
                    !string.Equals(imported.SearchKeyId, keyMaterial.SearchKeyId, StringComparison.Ordinal))
                {
                    throw new CryptographicException("가져온 키를 DPAPI로 다시 읽어 검증할 수 없습니다.");
                }
            }

            Directory.Move(stagingDirectory, _directory);
        }
        finally
        {
            if (Directory.Exists(stagingDirectory))
            {
                Directory.Delete(stagingDirectory, recursive: true);
            }
        }
    }

    public async Task<KeyMaterial> LoadAsync(CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("현재 키 저장소는 Windows DPAPI에서만 사용할 수 있습니다.");
        }

        var encryptionPath = Path.Combine(_directory, "encryption.dpapi");
        var searchPath = Path.Combine(_directory, "search.dpapi");
        if (!File.Exists(encryptionPath) || !File.Exists(searchPath))
        {
            throw new CryptographicException(
                "DPAPI 키가 없거나 일부만 있습니다. 기존 데이터를 보호하기 위해 새 키를 자동 생성하지 않습니다.");
        }

        byte[]? encryptionKey = null;
        byte[]? searchKey = null;
        try
        {
            var encryption = await ReadProtectedKeyAsync(
                encryptionPath,
                EncryptionEntropy,
                cancellationToken);
            encryptionKey = encryption.Key;
            var search = await ReadProtectedKeyAsync(searchPath, SearchEntropy, cancellationToken);
            searchKey = search.Key;

            if (CryptographicOperations.FixedTimeEquals(encryptionKey, searchKey))
            {
                throw new CryptographicException("AES 암호화 키와 HMAC 검색 키는 서로 달라야 합니다.");
            }

            var material = new KeyMaterial(encryption.Id, encryptionKey, search.Id, searchKey);
            encryptionKey = null;
            searchKey = null;
            return material;
        }
        finally
        {
            if (encryptionKey is not null)
            {
                CryptographicOperations.ZeroMemory(encryptionKey);
            }

            if (searchKey is not null)
            {
                CryptographicOperations.ZeroMemory(searchKey);
            }
        }
    }

    [SupportedOSPlatform("windows")]
    private static async Task WriteProtectedKeyAsync(
        string path,
        string keyId,
        byte[] key,
        byte[] entropy,
        CancellationToken cancellationToken)
    {
        var protectedKey = ProtectedData.Protect(key, entropy, DataProtectionScope.CurrentUser);
        var keyIdBytes = Encoding.UTF8.GetBytes(keyId);
        var contents = new byte[2 + keyIdBytes.Length + 4 + protectedKey.Length];
        try
        {
            BinaryPrimitives.WriteUInt16LittleEndian(contents.AsSpan(0, 2), checked((ushort)keyIdBytes.Length));
            keyIdBytes.CopyTo(contents.AsSpan(2));
            BinaryPrimitives.WriteInt32LittleEndian(contents.AsSpan(2 + keyIdBytes.Length, 4), protectedKey.Length);
            protectedKey.CopyTo(contents.AsSpan(2 + keyIdBytes.Length + 4));
            await File.WriteAllBytesAsync(path, contents, cancellationToken);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(protectedKey);
            CryptographicOperations.ZeroMemory(contents);
        }
    }

    [SupportedOSPlatform("windows")]
    private static async Task<(string Id, byte[] Key)> ReadProtectedKeyAsync(
        string path,
        byte[] entropy,
        CancellationToken cancellationToken)
    {
        var contents = await File.ReadAllBytesAsync(path, cancellationToken);
        try
        {
            if (contents.Length < 7)
            {
                throw new CryptographicException("DPAPI 키 파일이 잘렸거나 손상되었습니다.");
            }

            var keyIdLength = BinaryPrimitives.ReadUInt16LittleEndian(contents.AsSpan(0, 2));
            if (keyIdLength is < 1 or > 64 || contents.Length < 2 + keyIdLength + 4)
            {
                throw new CryptographicException("DPAPI 키 파일의 키 식별자가 유효하지 않습니다.");
            }

            var keyId = Encoding.UTF8.GetString(contents, 2, keyIdLength);
            var lengthOffset = 2 + keyIdLength;
            var protectedLength = BinaryPrimitives.ReadInt32LittleEndian(contents.AsSpan(lengthOffset, 4));
            if (protectedLength is < 1 or > MaximumProtectedKeyLength ||
                contents.Length != lengthOffset + 4 + protectedLength)
            {
                throw new CryptographicException("DPAPI 키 파일의 데이터 길이가 유효하지 않습니다.");
            }

            var protectedKey = contents.AsSpan(lengthOffset + 4, protectedLength).ToArray();
            try
            {
                var key = ProtectedData.Unprotect(protectedKey, entropy, DataProtectionScope.CurrentUser);
                if (key.Length != KeyLength)
                {
                    CryptographicOperations.ZeroMemory(key);
                    throw new CryptographicException("복호화된 키의 길이가 유효하지 않습니다.");
                }

                return (keyId, key);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(protectedKey);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(contents);
        }
    }

    private static string ResolveKeyDirectory(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var configuredDirectory = configuration["Security:KeyDirectory"];
        if (string.IsNullOrWhiteSpace(configuredDirectory))
        {
            configuredDirectory = Environment.GetEnvironmentVariable("LUMORA_KEY_DIRECTORY");
        }

        if (!string.IsNullOrWhiteSpace(configuredDirectory))
        {
            return configuredDirectory;
        }

        var localApplicationData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localApplicationData))
        {
            throw new InvalidOperationException(
                "키 저장 경로가 없습니다. Security:KeyDirectory 또는 LUMORA_KEY_DIRECTORY를 설정하십시오.");
        }

        return Path.Combine(localApplicationData, "P.P. AI - LUMORA", "keys");
    }

    private static void ValidateKeyMaterial(KeyMaterial keyMaterial)
    {
        if (keyMaterial.EncryptionKey.Length != KeyLength ||
            keyMaterial.SearchKey.Length != KeyLength ||
            CryptographicOperations.FixedTimeEquals(keyMaterial.EncryptionKey, keyMaterial.SearchKey) ||
            string.IsNullOrWhiteSpace(keyMaterial.EncryptionKeyId) ||
            string.IsNullOrWhiteSpace(keyMaterial.SearchKeyId) ||
            keyMaterial.EncryptionKeyId.Length > 64 ||
            keyMaterial.SearchKeyId.Length > 64 ||
            Encoding.UTF8.GetByteCount(keyMaterial.EncryptionKeyId) > 64 ||
            Encoding.UTF8.GetByteCount(keyMaterial.SearchKeyId) > 64 ||
            string.Equals(keyMaterial.EncryptionKeyId, keyMaterial.SearchKeyId, StringComparison.Ordinal))
        {
            throw new CryptographicException("복구 키 자료의 길이 또는 식별자가 유효하지 않습니다.");
        }
    }
}
