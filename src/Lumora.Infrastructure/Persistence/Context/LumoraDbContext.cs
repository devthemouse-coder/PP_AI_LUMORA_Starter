using Lumora.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Lumora.Infrastructure.Persistence.Context;

public sealed class LumoraDbContext(DbContextOptions<LumoraDbContext> options) : DbContext(options)
{
    private static readonly ValueConverter<DateTime, DateTime> UtcDateTimeConverter = new(
        value => value,
        value => DateTime.SpecifyKind(value, DateTimeKind.Utc));

    private static readonly ValueConverter<DateTime?, DateTime?> NullableUtcDateTimeConverter = new(
        value => value,
        value => value.HasValue
            ? DateTime.SpecifyKind(value.Value, DateTimeKind.Utc)
            : value);

    public DbSet<Tenant> Tenants => Set<Tenant>();

    public DbSet<User> Users => Set<User>();

    public DbSet<Conversation> Conversations => Set<Conversation>();

    public DbSet<Message> Messages => Set<Message>();

    public DbSet<MemoryItem> Memories => Set<MemoryItem>();

    public DbSet<MemoryPayload> MemoryPayloads => Set<MemoryPayload>();

    public DbSet<StructuredFact> StructuredFacts => Set<StructuredFact>();

    public DbSet<MemorySource> MemorySources => Set<MemorySource>();

    public DbSet<MemoryRevision> MemoryRevisions => Set<MemoryRevision>();

    public DbSet<MemoryLookupToken> MemoryLookupTokens => Set<MemoryLookupToken>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(LumoraDbContext).Assembly);

        foreach (var property in modelBuilder.Model.GetEntityTypes()
                     .SelectMany(entityType => entityType.GetProperties()))
        {
            if (property.ClrType == typeof(DateTime))
            {
                property.SetValueConverter(UtcDateTimeConverter);
            }
            else if (property.ClrType == typeof(DateTime?))
            {
                property.SetValueConverter(NullableUtcDateTimeConverter);
            }
        }

        base.OnModelCreating(modelBuilder);
    }

    public override int SaveChanges()
    {
        UpdateConcurrencyVersions();
        return base.SaveChanges();
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        UpdateConcurrencyVersions();
        return base.SaveChangesAsync(cancellationToken);
    }

    private void UpdateConcurrencyVersions()
    {
        ValidateTrackedTimes();
        foreach (var entry in ChangeTracker.Entries<IOptimisticConcurrencyEntity>()
                     .Where(entry => entry.State == EntityState.Modified))
        {
            var version = entry.Property(nameof(IOptimisticConcurrencyEntity.Version));
            version.CurrentValue = checked((long)version.OriginalValue! + 1);
        }
    }

    private void ValidateTrackedTimes()
    {
        foreach (var entry in ChangeTracker.Entries()
                     .Where(entry => entry.State is EntityState.Added or EntityState.Modified))
        {
            foreach (var property in entry.Properties)
            {
                if (property.CurrentValue is DateTime value && value.Kind != DateTimeKind.Utc)
                {
                    throw new InvalidOperationException("DB에 저장하는 DateTime은 UTC여야 합니다.");
                }

            }
        }
    }
}
