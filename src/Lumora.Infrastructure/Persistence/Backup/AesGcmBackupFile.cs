using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Lumora.Infrastructure.Security.KeyManagement;

namespace Lumora.Infrastructure.Persistence.Backup;

internal static class AesGcmBackupFile
{
    private const int CurrentFormatVersion = 1;
    private const int ChunkSize = 1024 * 1024;
    private const int NonceLength = 12;
    private const int TagLength = 16;
    private const int FixedHeaderLength = 44;
    private const int MaximumHeaderLength = 512;
    private static readonly byte[] Magic = "LUMORAB1"u8.ToArray();
    private static readonly byte[] DerivationInfo = "P.P. AI - LUMORA|BACKUP-ENCRYPTION|v1"u8.ToArray();

    public static async Task<BackupFileMetadata> EncryptAsync(
        string sourcePath,
        string destinationPath,
        KeyMaterial keys,
        string schemaVersion,
        DateTimeOffset createdAtUtc,
        CancellationToken cancellationToken)
    {
        var header = CreateHeader(Guid.NewGuid(), createdAtUtc, schemaVersion, keys.EncryptionKeyId);
        var backupId = new Guid(header.AsSpan(9, 16));
        var derivedKey = DeriveKey(keys.EncryptionKey, backupId);
        var noncePrefix = header.AsSpan(40, 4).ToArray();
        var inputBuffer = new byte[ChunkSize];
        var ciphertext = new byte[ChunkSize];
        var tag = new byte[TagLength];
        var previousTag = new byte[TagLength];
        try
        {
            await using var source = new FileStream(
                sourcePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                ChunkSize,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            await using var destination = new FileStream(
                destinationPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                ChunkSize,
                FileOptions.Asynchronous | FileOptions.WriteThrough);
            await destination.WriteAsync(header, cancellationToken);

            ulong sequence = 0;
            using var aes = new AesGcm(derivedKey, TagLength);
            while (true)
            {
                var count = await source.ReadAsync(inputBuffer, cancellationToken);
                if (count == 0)
                {
                    break;
                }

                var recordHeader = CreateRecordHeader(sequence, checked((uint)count), isFinal: false);
                var nonce = CreateNonce(noncePrefix, sequence);
                var associatedData = CreateAssociatedData(header, recordHeader, previousTag);
                aes.Encrypt(nonce, inputBuffer.AsSpan(0, count), ciphertext.AsSpan(0, count), tag, associatedData);
                await destination.WriteAsync(recordHeader, cancellationToken);
                await destination.WriteAsync(ciphertext.AsMemory(0, count), cancellationToken);
                await destination.WriteAsync(tag, cancellationToken);
                tag.CopyTo(previousTag, 0);
                sequence = checked(sequence + 1);
                CryptographicOperations.ZeroMemory(nonce);
                CryptographicOperations.ZeroMemory(associatedData);
            }

            var finalHeader = CreateRecordHeader(sequence, 0, isFinal: true);
            var finalNonce = CreateNonce(noncePrefix, sequence);
            var finalAssociatedData = CreateAssociatedData(header, finalHeader, previousTag);
            aes.Encrypt(finalNonce, ReadOnlySpan<byte>.Empty, Span<byte>.Empty, tag, finalAssociatedData);
            await destination.WriteAsync(finalHeader, cancellationToken);
            await destination.WriteAsync(tag, cancellationToken);
            await destination.FlushAsync(cancellationToken);
            destination.Flush(flushToDisk: true);

            CryptographicOperations.ZeroMemory(finalNonce);
            CryptographicOperations.ZeroMemory(finalAssociatedData);
            return new BackupFileMetadata(
                backupId,
                createdAtUtc,
                schemaVersion,
                keys.EncryptionKeyId,
                new FileInfo(destinationPath).Length,
                ContainsEncryptedData: false);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(header);
            CryptographicOperations.ZeroMemory(derivedKey);
            CryptographicOperations.ZeroMemory(noncePrefix);
            CryptographicOperations.ZeroMemory(inputBuffer);
            CryptographicOperations.ZeroMemory(ciphertext);
            CryptographicOperations.ZeroMemory(tag);
            CryptographicOperations.ZeroMemory(previousTag);
        }
    }

    public static async Task<(BackupFileMetadata Metadata, long PlaintextLength)> DecryptAsync(
        string sourcePath,
        string destinationPath,
        KeyMaterial keys,
        CancellationToken cancellationToken)
    {
        await using var source = new FileStream(
            sourcePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            ChunkSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var header = await ReadHeaderAsync(source, cancellationToken);
        if (!string.Equals(header.Metadata.EncryptionKeyId, keys.EncryptionKeyId, StringComparison.Ordinal))
        {
            throw new CryptographicException("백업을 복호화하는 데 필요한 AES 키를 찾을 수 없습니다.");
        }

        var derivedKey = DeriveKey(keys.EncryptionKey, header.Metadata.BackupId);
        var inputBuffer = new byte[ChunkSize];
        var plaintextBuffer = new byte[ChunkSize];
        var previousTag = new byte[TagLength];
        var totalLength = 0L;
        try
        {
            await using var destination = new FileStream(
                destinationPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                ChunkSize,
                FileOptions.Asynchronous | FileOptions.WriteThrough);
            using var aes = new AesGcm(derivedKey, TagLength);
            ulong expectedSequence = 0;
            var sawFinalRecord = false;
            while (!sawFinalRecord)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var recordHeader = new byte[13];
                await source.ReadExactlyAsync(recordHeader, cancellationToken);
                var sequence = BinaryPrimitives.ReadUInt64BigEndian(recordHeader.AsSpan(0, 8));
                var plaintextLength = BinaryPrimitives.ReadUInt32BigEndian(recordHeader.AsSpan(8, 4));
                var flags = recordHeader[12];
                if (sequence != expectedSequence)
                {
                    throw new CryptographicException("백업 청크 순서가 올바르지 않습니다.");
                }

                var isFinal = flags == 1;
                if ((flags is not 0 and not 1) ||
                    (isFinal && plaintextLength != 0) ||
                    (!isFinal && (plaintextLength == 0 || plaintextLength > ChunkSize)))
                {
                    throw new CryptographicException("백업 청크 형식이 유효하지 않습니다.");
                }

                var count = checked((int)plaintextLength);
                if (count > 0)
                {
                    await source.ReadExactlyAsync(inputBuffer.AsMemory(0, count), cancellationToken);
                }

                var tag = new byte[TagLength];
                await source.ReadExactlyAsync(tag, cancellationToken);
                var nonce = CreateNonce(header.NoncePrefix, sequence);
                var associatedData = CreateAssociatedData(header.EncodedHeader, recordHeader, previousTag);
                try
                {
                    aes.Decrypt(
                        nonce,
                        inputBuffer.AsSpan(0, count),
                        tag,
                        plaintextBuffer.AsSpan(0, count),
                        associatedData);
                }
                catch
                {
                    CryptographicOperations.ZeroMemory(plaintextBuffer);
                    throw;
                }

                if (count > 0)
                {
                    await destination.WriteAsync(plaintextBuffer.AsMemory(0, count), cancellationToken);
                    totalLength = checked(totalLength + count);
                }

                tag.CopyTo(previousTag, 0);
                expectedSequence = checked(expectedSequence + 1);
                sawFinalRecord = isFinal;
                CryptographicOperations.ZeroMemory(plaintextBuffer.AsSpan(0, count));
                CryptographicOperations.ZeroMemory(tag);
                CryptographicOperations.ZeroMemory(nonce);
                CryptographicOperations.ZeroMemory(associatedData);
                CryptographicOperations.ZeroMemory(recordHeader);
            }

            if (source.ReadByte() != -1)
            {
                throw new CryptographicException("백업 파일 끝에 인증되지 않은 추가 데이터가 있습니다.");
            }

            await destination.FlushAsync(cancellationToken);
            destination.Flush(flushToDisk: true);
            return (
                header.Metadata with { FileSizeBytes = new FileInfo(sourcePath).Length },
                totalLength);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(header.EncodedHeader);
            CryptographicOperations.ZeroMemory(header.NoncePrefix);
            CryptographicOperations.ZeroMemory(derivedKey);
            CryptographicOperations.ZeroMemory(inputBuffer);
            CryptographicOperations.ZeroMemory(plaintextBuffer);
            CryptographicOperations.ZeroMemory(previousTag);
        }
    }

    private static byte[] CreateHeader(
        Guid backupId,
        DateTimeOffset createdAtUtc,
        string schemaVersion,
        string encryptionKeyId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(schemaVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(encryptionKeyId);
        if (encryptionKeyId.Length > 64)
        {
            throw new ArgumentOutOfRangeException(nameof(encryptionKeyId));
        }

        var keyId = Encoding.UTF8.GetBytes(encryptionKeyId);
        var schema = Encoding.UTF8.GetBytes(schemaVersion);
        if (keyId.Length is < 1 or > 64 || schema.Length is < 1 or > 256)
        {
            throw new ArgumentOutOfRangeException(nameof(schemaVersion), "백업 메타데이터 길이가 유효하지 않습니다.");
        }

        var header = new byte[FixedHeaderLength + keyId.Length + schema.Length];
        Magic.CopyTo(header, 0);
        header[8] = CurrentFormatVersion;
        backupId.TryWriteBytes(header.AsSpan(9, 16));
        BinaryPrimitives.WriteInt64BigEndian(header.AsSpan(25, 8), createdAtUtc.UtcDateTime.Ticks);
        header[33] = checked((byte)keyId.Length);
        BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(34, 2), checked((ushort)schema.Length));
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(36, 4), ChunkSize);
        RandomNumberGenerator.Fill(header.AsSpan(40, 4));
        keyId.CopyTo(header, FixedHeaderLength);
        schema.CopyTo(header, FixedHeaderLength + keyId.Length);
        CryptographicOperations.ZeroMemory(keyId);
        CryptographicOperations.ZeroMemory(schema);
        return header;
    }

    private static async Task<ParsedHeader> ReadHeaderAsync(Stream source, CancellationToken cancellationToken)
    {
        var fixedHeader = new byte[FixedHeaderLength];
        await source.ReadExactlyAsync(fixedHeader, cancellationToken);
        if (!fixedHeader.AsSpan(0, Magic.Length).SequenceEqual(Magic) ||
            fixedHeader[8] != CurrentFormatVersion ||
            BinaryPrimitives.ReadInt32BigEndian(fixedHeader.AsSpan(36, 4)) != ChunkSize)
        {
            throw new CryptographicException("백업 파일 형식 또는 청크 설정을 지원하지 않습니다.");
        }

        var keyIdLength = fixedHeader[33];
        var schemaLength = BinaryPrimitives.ReadUInt16BigEndian(fixedHeader.AsSpan(34, 2));
        var headerLength = FixedHeaderLength + keyIdLength + schemaLength;
        if (keyIdLength is < 1 or > 64 || schemaLength is < 1 or > 256 || headerLength > MaximumHeaderLength)
        {
            throw new CryptographicException("백업 파일의 헤더 길이가 유효하지 않습니다.");
        }

        var header = new byte[headerLength];
        fixedHeader.CopyTo(header, 0);
        await source.ReadExactlyAsync(header.AsMemory(FixedHeaderLength), cancellationToken);
        var timestampTicks = BinaryPrimitives.ReadInt64BigEndian(header.AsSpan(25, 8));
        if (timestampTicks < DateTime.MinValue.Ticks || timestampTicks > DateTime.MaxValue.Ticks)
        {
            throw new CryptographicException("백업 파일의 시각 정보가 유효하지 않습니다.");
        }

        try
        {
            var strictUtf8 = new UTF8Encoding(false, true);
            var keyId = strictUtf8.GetString(header, FixedHeaderLength, keyIdLength);
            var schemaVersion = strictUtf8.GetString(header, FixedHeaderLength + keyIdLength, schemaLength);
            var backupId = new Guid(header.AsSpan(9, 16));
            if (backupId == Guid.Empty || string.IsNullOrWhiteSpace(keyId) || string.IsNullOrWhiteSpace(schemaVersion))
            {
                throw new CryptographicException("백업 파일의 메타데이터가 유효하지 않습니다.");
            }

            return new ParsedHeader(
                header,
                header.AsSpan(40, 4).ToArray(),
                new BackupFileMetadata(
                    backupId,
                    new DateTimeOffset(timestampTicks, TimeSpan.Zero),
                    schemaVersion,
                    keyId,
                    0,
                    ContainsEncryptedData: false));
        }
        catch (DecoderFallbackException exception)
        {
            throw new CryptographicException("백업 파일의 메타데이터 인코딩이 유효하지 않습니다.", exception);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(fixedHeader);
        }
    }

    private static byte[] CreateRecordHeader(ulong sequence, uint length, bool isFinal)
    {
        var header = new byte[13];
        BinaryPrimitives.WriteUInt64BigEndian(header.AsSpan(0, 8), sequence);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(8, 4), length);
        header[12] = isFinal ? (byte)1 : (byte)0;
        return header;
    }

    private static byte[] CreateNonce(byte[] prefix, ulong sequence)
    {
        var nonce = new byte[NonceLength];
        prefix.CopyTo(nonce, 0);
        BinaryPrimitives.WriteUInt64BigEndian(nonce.AsSpan(4, 8), sequence);
        return nonce;
    }

    private static byte[] CreateAssociatedData(byte[] header, byte[] recordHeader, byte[] previousTag)
    {
        var associatedData = new byte[header.Length + recordHeader.Length + previousTag.Length];
        header.CopyTo(associatedData, 0);
        recordHeader.CopyTo(associatedData, header.Length);
        previousTag.CopyTo(associatedData, header.Length + recordHeader.Length);
        return associatedData;
    }

    private static byte[] DeriveKey(byte[] encryptionKey, Guid backupId) =>
        HKDF.DeriveKey(
            HashAlgorithmName.SHA256,
            encryptionKey,
            32,
            backupId.ToByteArray(),
            DerivationInfo);

    private sealed record ParsedHeader(
        byte[] EncodedHeader,
        byte[] NoncePrefix,
        BackupFileMetadata Metadata);
}
