using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Lumora.Infrastructure.Security.KeyManagement;

public sealed class PasswordProtectedKeyRecoveryService(IKeyMaterialProvider keyMaterialProvider)
{
    private const int CurrentFormatVersion = 1;
    private const int IterationCount = 600_000;
    private const int SaltLength = 16;
    private const int NonceLength = 12;
    private const int KeyLength = 32;
    private const int TagLength = 16;
    private const int MaximumFileLength = 4096;
    private static readonly byte[] Magic = "LUMKEY01"u8.ToArray();

    public async Task ExportAsync(
        string recoveryFilePath,
        ReadOnlyMemory<char> password,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(recoveryFilePath);
        ValidatePassword(password.Span);
        var fullPath = Path.GetFullPath(recoveryFilePath);
        var directory = Path.GetDirectoryName(fullPath)
            ?? throw new InvalidOperationException("복구 파일 디렉터리를 확인할 수 없습니다.");
        Directory.CreateDirectory(directory);

        using var keys = await keyMaterialProvider.LoadAsync(cancellationToken);
        var plaintext = Serialize(keys);
        var salt = RandomNumberGenerator.GetBytes(SaltLength);
        var nonce = RandomNumberGenerator.GetBytes(NonceLength);
        var derivedKey = new byte[KeyLength];
        var header = CreateHeader(salt);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[TagLength];
        var temporaryPath = $"{fullPath}.{Guid.NewGuid():N}.partial";
        try
        {
            Rfc2898DeriveBytes.Pbkdf2(
                password.Span,
                salt,
                derivedKey,
                IterationCount,
                HashAlgorithmName.SHA256);
            using (var aes = new AesGcm(derivedKey, TagLength))
            {
                aes.Encrypt(nonce, plaintext, ciphertext, tag, header);
            }

            await using (var stream = new FileStream(
                             temporaryPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             4096,
                             FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await stream.WriteAsync(header, cancellationToken);
                await stream.WriteAsync(nonce, cancellationToken);
                await stream.WriteAsync(ciphertext, cancellationToken);
                await stream.WriteAsync(tag, cancellationToken);
                await stream.FlushAsync(cancellationToken);
                stream.Flush(flushToDisk: true);
            }

            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporaryPath, fullPath);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
            CryptographicOperations.ZeroMemory(derivedKey);
            CryptographicOperations.ZeroMemory(ciphertext);
            CryptographicOperations.ZeroMemory(tag);
            CryptographicOperations.ZeroMemory(salt);
            CryptographicOperations.ZeroMemory(nonce);
            CryptographicOperations.ZeroMemory(header);
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    public async Task ImportAsync(
        string recoveryFilePath,
        ReadOnlyMemory<char> password,
        IKeyMaterialRecoveryTarget recoveryTarget,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(recoveryFilePath);
        ArgumentNullException.ThrowIfNull(recoveryTarget);
        ValidatePassword(password.Span);
        var fullPath = Path.GetFullPath(recoveryFilePath);
        await using var recoveryStream = new FileStream(
            fullPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            4096,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (recoveryStream.Length > MaximumFileLength ||
            recoveryStream.Length < Magic.Length + 1 + 4 + SaltLength + NonceLength + TagLength)
        {
            throw new CryptographicException("키 복구 파일의 길이가 유효하지 않습니다.");
        }

        var contents = new byte[checked((int)recoveryStream.Length)];
        await recoveryStream.ReadExactlyAsync(contents, cancellationToken);
        if (recoveryStream.ReadByte() != -1)
        {
            CryptographicOperations.ZeroMemory(contents);
            throw new CryptographicException("키 복구 파일의 크기가 읽는 중 변경되었습니다.");
        }

        var salt = contents.AsSpan(Magic.Length + 1 + 4, SaltLength).ToArray();
        var headerLength = Magic.Length + 1 + 4 + SaltLength;
        var header = contents.AsSpan(0, headerLength).ToArray();
        var nonce = contents.AsSpan(headerLength, NonceLength).ToArray();
        var ciphertextLength = contents.Length - headerLength - NonceLength - TagLength;
        var ciphertext = contents.AsSpan(headerLength + NonceLength, ciphertextLength).ToArray();
        var tag = contents.AsSpan(contents.Length - TagLength, TagLength).ToArray();
        var derivedKey = new byte[KeyLength];
        var plaintext = new byte[ciphertextLength];

        try
        {
            ValidateHeader(header);
            Rfc2898DeriveBytes.Pbkdf2(
                password.Span,
                salt,
                derivedKey,
                IterationCount,
                HashAlgorithmName.SHA256);
            using (var aes = new AesGcm(derivedKey, TagLength))
            {
                aes.Decrypt(nonce, ciphertext, tag, plaintext, header);
            }

            using var keys = Deserialize(plaintext);
            await recoveryTarget.ImportAsync(keys, cancellationToken);
        }
        catch (CryptographicException exception)
        {
            throw new CryptographicException("키 복구 파일을 인증하거나 복호화할 수 없습니다.", exception);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(contents);
            CryptographicOperations.ZeroMemory(salt);
            CryptographicOperations.ZeroMemory(header);
            CryptographicOperations.ZeroMemory(nonce);
            CryptographicOperations.ZeroMemory(ciphertext);
            CryptographicOperations.ZeroMemory(tag);
            CryptographicOperations.ZeroMemory(derivedKey);
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    private static byte[] Serialize(KeyMaterial keys)
    {
        var encryptionId = Encoding.UTF8.GetBytes(keys.EncryptionKeyId);
        var searchId = Encoding.UTF8.GetBytes(keys.SearchKeyId);
        if (encryptionId.Length is < 1 or > 64 || searchId.Length is < 1 or > 64 ||
            keys.EncryptionKey.Length != KeyLength || keys.SearchKey.Length != KeyLength)
        {
            throw new CryptographicException("키 자료의 길이 또는 식별자가 유효하지 않습니다.");
        }

        var plaintext = new byte[2 + encryptionId.Length + searchId.Length + 2 * KeyLength];
        var offset = 0;
        plaintext[offset++] = checked((byte)encryptionId.Length);
        encryptionId.CopyTo(plaintext, offset);
        offset += encryptionId.Length;
        keys.EncryptionKey.CopyTo(plaintext, offset);
        offset += KeyLength;
        plaintext[offset++] = checked((byte)searchId.Length);
        searchId.CopyTo(plaintext, offset);
        offset += searchId.Length;
        keys.SearchKey.CopyTo(plaintext, offset);
        CryptographicOperations.ZeroMemory(encryptionId);
        CryptographicOperations.ZeroMemory(searchId);
        return plaintext;
    }

    private static KeyMaterial Deserialize(ReadOnlySpan<byte> plaintext)
    {
        var offset = 0;
        var encryptionIdLength = ReadKeyIdLength(plaintext, ref offset);
        var encryptionId = new UTF8Encoding(false, true).GetString(plaintext.Slice(offset, encryptionIdLength));
        offset += encryptionIdLength;
        var encryptionKey = plaintext.Slice(offset, KeyLength).ToArray();
        offset += KeyLength;
        var searchIdLength = ReadKeyIdLength(plaintext, ref offset);
        var searchId = new UTF8Encoding(false, true).GetString(plaintext.Slice(offset, searchIdLength));
        offset += searchIdLength;
        var searchKey = plaintext.Slice(offset, KeyLength).ToArray();
        offset += KeyLength;

        if (offset != plaintext.Length ||
            string.IsNullOrWhiteSpace(encryptionId) ||
            string.IsNullOrWhiteSpace(searchId) ||
            string.Equals(encryptionId, searchId, StringComparison.Ordinal) ||
            CryptographicOperations.FixedTimeEquals(encryptionKey, searchKey))
        {
            CryptographicOperations.ZeroMemory(encryptionKey);
            CryptographicOperations.ZeroMemory(searchKey);
            throw new CryptographicException("키 복구 파일의 키 자료가 유효하지 않습니다.");
        }

        return new KeyMaterial(encryptionId, encryptionKey, searchId, searchKey);
    }

    private static int ReadKeyIdLength(ReadOnlySpan<byte> plaintext, ref int offset)
    {
        if (offset >= plaintext.Length)
        {
            throw new CryptographicException("키 복구 파일의 키 식별자가 잘렸습니다.");
        }

        var length = plaintext[offset++];
        if (length is < 1 or > 64 || plaintext.Length - offset < length + KeyLength)
        {
            throw new CryptographicException("키 복구 파일의 키 식별자 길이가 유효하지 않습니다.");
        }

        return length;
    }

    private static byte[] CreateHeader(byte[] salt)
    {
        var header = new byte[Magic.Length + 1 + 4 + SaltLength];
        Magic.CopyTo(header, 0);
        header[Magic.Length] = CurrentFormatVersion;
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(Magic.Length + 1, 4), IterationCount);
        salt.CopyTo(header, Magic.Length + 1 + 4);
        return header;
    }

    private static void ValidateHeader(ReadOnlySpan<byte> header)
    {
        if (header.Length != Magic.Length + 1 + 4 + SaltLength ||
            !header[..Magic.Length].SequenceEqual(Magic) ||
            header[Magic.Length] != CurrentFormatVersion ||
            BinaryPrimitives.ReadInt32LittleEndian(header.Slice(Magic.Length + 1, 4)) != IterationCount)
        {
            throw new CryptographicException("키 복구 파일 형식 또는 KDF 설정을 지원하지 않습니다.");
        }
    }

    private static void ValidatePassword(ReadOnlySpan<char> password)
    {
        if (password.Length is < 12 or > 1024)
        {
            throw new ArgumentException("복구 비밀번호는 12자 이상 1024자 이하여야 합니다.", nameof(password));
        }
    }
}
