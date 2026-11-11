using Lumora.Domain.Entities;

namespace Lumora.Application.Abstractions.Security;

public interface IEncryptionService
{
    Task<EncryptedPayload> EncryptAsync(
        ReadOnlyMemory<byte> plaintext,
        EncryptionContext context,
        CancellationToken cancellationToken);

    Task<byte[]> DecryptAsync(
        EncryptedPayload payload,
        EncryptionContext context,
        CancellationToken cancellationToken);
}
