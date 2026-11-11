namespace Lumora.Infrastructure.Security.KeyManagement;

public interface IKeyMaterialProvider
{
    Task<KeyMaterial> LoadAsync(CancellationToken cancellationToken);
}
