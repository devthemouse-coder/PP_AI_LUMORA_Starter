namespace Lumora.Infrastructure.Security.KeyManagement;

public interface IKeyMaterialRecoveryTarget
{
    Task ImportAsync(KeyMaterial keyMaterial, CancellationToken cancellationToken);
}
