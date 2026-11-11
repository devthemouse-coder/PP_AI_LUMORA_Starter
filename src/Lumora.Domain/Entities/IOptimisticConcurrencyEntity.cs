namespace Lumora.Domain.Entities;

public interface IOptimisticConcurrencyEntity
{
    long Version { get; }
}
