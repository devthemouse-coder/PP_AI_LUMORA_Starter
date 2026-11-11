namespace Lumora.Infrastructure.Security.KeyManagement;

public interface IKeyMaterialInitializer
{
    Task InitializeAsync(CancellationToken cancellationToken);
}
