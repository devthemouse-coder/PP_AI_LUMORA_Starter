using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Lumora.Application.Abstractions.Security;
using Lumora.Domain.Entities;
using Lumora.Infrastructure.Security.KeyManagement;

namespace Lumora.Infrastructure.Security;

public sealed class AesGcmEncryptionService(IKeyMaterialProvider keyMaterialProvider) : IEncryptionService
{
    private const int CurrentFormatVersion = 1;
    private const int NonceLength = 12;
    private const int AuthenticationTagLength = 16;

    public async Task<EncryptedPayload> EncryptAsync(
        ReadOnlyMemory<byte> plaintext,
        EncryptionContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        using var keys = await keyMaterialProvider.LoadAsync(cancellationToken);

        var nonce = RandomNumberGenerator.GetBytes(NonceLength);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[AuthenticationTagLength];
        var associatedData = CreateAssociatedData(context, CurrentFormatVersion);

        using (var aes = new AesGcm(keys.EncryptionKey, AuthenticationTagLength))
        {
            aes.Encrypt(nonce, plaintext.Span, ciphertext, tag, associatedData);
        }

        return new EncryptedPayload(
            CurrentFormatVersion,
            keys.EncryptionKeyId,
            nonce,
            ciphertext,
            tag);
    }

    public async Task<byte[]> DecryptAsync(
        EncryptedPayload payload,
        EncryptionContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentNullException.ThrowIfNull(context);
        if (payload.FormatVersion != CurrentFormatVersion)
        {
            throw new CryptographicException($"지원하지 않는 암호문 포맷 버전입니다: {payload.FormatVersion}.");
        }

        using var keys = await keyMaterialProvider.LoadAsync(cancellationToken);
        if (!string.Equals(payload.KeyId, keys.EncryptionKeyId, StringComparison.Ordinal))
        {
            throw new CryptographicException("암호문에 필요한 키를 현재 키 저장소에서 찾을 수 없습니다.");
        }

        var plaintext = new byte[payload.Ciphertext.Length];
        var associatedData = CreateAssociatedData(context, payload.FormatVersion);
        try
        {
            using var aes = new AesGcm(keys.EncryptionKey, AuthenticationTagLength);
            aes.Decrypt(payload.Nonce, payload.Ciphertext, payload.AuthenticationTag, plaintext, associatedData);
            return plaintext;
        }
        catch
        {
            CryptographicOperations.ZeroMemory(plaintext);
            throw;
        }
    }

    private static byte[] CreateAssociatedData(EncryptionContext context, int formatVersion)
    {
        if (context.TenantId == Guid.Empty ||
            context.UserId == Guid.Empty ||
            context.RecordId == Guid.Empty ||
            string.IsNullOrWhiteSpace(context.EntityType) ||
            string.IsNullOrWhiteSpace(context.FieldName))
        {
            throw new ArgumentException("암호화 문맥에는 유효한 소유자, 레코드, 엔터티, 필드가 필요합니다.", nameof(context));
        }

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(Encoding.UTF8.GetBytes("P.P. AI - LUMORA|AEAD|v1"));
        writer.Write(formatVersion);
        writer.Write(context.TenantId.ToByteArray());
        writer.Write(context.UserId.ToByteArray());
        writer.Write(context.RecordId.ToByteArray());
        WriteLengthPrefixed(writer, context.EntityType);
        WriteLengthPrefixed(writer, context.FieldName);
        writer.Flush();
        return stream.ToArray();
    }

    private static void WriteLengthPrefixed(BinaryWriter writer, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        writer.Write(BinaryPrimitives.ReverseEndianness(bytes.Length));
        writer.Write(bytes);
    }
}
