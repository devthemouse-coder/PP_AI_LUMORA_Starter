namespace Lumora.Domain.Entities;

/// <summary>인증된 암호문과 이를 해석하는 포맷·키 버전을 보관합니다.</summary>
public sealed class EncryptedPayload
{
    internal EncryptedPayload()
    {
        KeyId = string.Empty;
        Nonce = [];
        Ciphertext = [];
        AuthenticationTag = [];
    }

    public EncryptedPayload(
        int formatVersion,
        string keyId,
        byte[] nonce,
        byte[] ciphertext,
        byte[] authenticationTag)
    {
        if (formatVersion < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(formatVersion));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(keyId);
        ArgumentNullException.ThrowIfNull(nonce);
        ArgumentNullException.ThrowIfNull(ciphertext);
        ArgumentNullException.ThrowIfNull(authenticationTag);
        if (nonce.Length != 12 || authenticationTag.Length != 16)
        {
            throw new ArgumentException("AES-256-GCM Payload의 Nonce는 12바이트, 인증 태그는 16바이트여야 합니다.");
        }

        if (keyId.Length > 64)
        {
            throw new ArgumentOutOfRangeException(nameof(keyId), "키 식별자는 64자를 초과할 수 없습니다.");
        }

        FormatVersion = formatVersion;
        KeyId = keyId;
        Nonce = nonce.ToArray();
        Ciphertext = ciphertext.ToArray();
        AuthenticationTag = authenticationTag.ToArray();
    }

    public int FormatVersion { get; private set; }

    public string KeyId { get; private set; }

    public byte[] Nonce { get; private set; }

    public byte[] Ciphertext { get; private set; }

    public byte[] AuthenticationTag { get; private set; }
}
