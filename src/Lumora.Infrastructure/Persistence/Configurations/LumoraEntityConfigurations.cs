using Lumora.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lumora.Infrastructure.Persistence.Configurations;

internal static class EncryptedPayloadMapping
{
    public static void Configure<TOwner>(
        OwnedNavigationBuilder<TOwner, EncryptedPayload> payload,
        string prefix)
        where TOwner : class
    {
        payload.Property(value => value.FormatVersion).HasColumnName($"{prefix}FormatVersion").IsRequired();
        payload.Property(value => value.KeyId).HasColumnName($"{prefix}KeyId").HasMaxLength(64).IsRequired();
        payload.Property(value => value.Nonce).HasColumnName($"{prefix}Nonce").HasColumnType("BLOB").IsRequired();
        payload.Property(value => value.Ciphertext).HasColumnName($"{prefix}Ciphertext").HasColumnType("BLOB").IsRequired();
        payload.Property(value => value.AuthenticationTag)
            .HasColumnName($"{prefix}AuthenticationTag")
            .HasColumnType("BLOB")
            .IsRequired();
    }
}

internal sealed class TenantConfiguration : IEntityTypeConfiguration<Tenant>
{
    public void Configure(EntityTypeBuilder<Tenant> builder)
    {
        builder.ToTable("Tenants");
        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.Name).HasMaxLength(200).IsRequired();
        builder.Property(entity => entity.Status).HasConversion<short>().IsRequired();
        builder.Property(entity => entity.CreatedAtUtc).IsRequired();
        builder.Property(entity => entity.UpdatedAtUtc).IsRequired();
        builder.Property(entity => entity.Version).IsConcurrencyToken().IsRequired();
    }
}

internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("Users");
        builder.HasKey(entity => entity.Id);
        builder.HasAlternateKey(entity => new { entity.TenantId, entity.Id });
        builder.Property(entity => entity.Status).HasConversion<short>().IsRequired();
        builder.Property(entity => entity.Version).IsConcurrencyToken().IsRequired();
        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(entity => entity.TenantId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.OwnsOne(entity => entity.DisplayName, payload => EncryptedPayloadMapping.Configure(payload, "DisplayName"));
        builder.Navigation(entity => entity.DisplayName).IsRequired();
    }
}

internal sealed class ConversationConfiguration : IEntityTypeConfiguration<Conversation>
{
    public void Configure(EntityTypeBuilder<Conversation> builder)
    {
        builder.ToTable("Conversations");
        builder.HasKey(entity => entity.Id);
        builder.HasAlternateKey(entity => new { entity.TenantId, entity.UserId, entity.Id });
        builder.Property(entity => entity.Status).HasConversion<short>().IsRequired();
        builder.Property(entity => entity.Version).IsConcurrencyToken().IsRequired();
        builder.HasIndex(entity => new
        {
            entity.TenantId,
            entity.UserId,
            entity.Status,
            entity.UpdatedAtUtc,
            entity.Id
        });
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(entity => new { entity.TenantId, entity.UserId })
            .HasPrincipalKey(entity => new { entity.TenantId, entity.Id })
            .OnDelete(DeleteBehavior.Cascade);
        builder.OwnsOne(entity => entity.Summary, payload => EncryptedPayloadMapping.Configure(payload, "Summary"));
    }
}

internal sealed class MessageConfiguration : IEntityTypeConfiguration<Message>
{
    public void Configure(EntityTypeBuilder<Message> builder)
    {
        builder.ToTable("Messages");
        builder.HasKey(entity => entity.Id);
        builder.HasAlternateKey(entity => new
        {
            entity.TenantId,
            entity.UserId,
            entity.ConversationId,
            entity.Id
        });
        builder.Property(entity => entity.Role).HasConversion<short>().IsRequired();
        builder.Property(entity => entity.SequenceNumber).IsRequired();
        builder.HasIndex(entity => new
        {
            entity.TenantId,
            entity.UserId,
            entity.ConversationId,
            entity.SequenceNumber
        }).IsUnique();
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(entity => new { entity.TenantId, entity.UserId })
            .HasPrincipalKey(entity => new { entity.TenantId, entity.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Conversation>()
            .WithMany()
            .HasForeignKey(entity => new { entity.TenantId, entity.UserId, entity.ConversationId })
            .HasPrincipalKey(nameof(MemoryItem.TenantId), nameof(MemoryItem.UserId), nameof(MemoryItem.Id))
            .OnDelete(DeleteBehavior.Cascade);
        builder.OwnsOne(entity => entity.Body, payload => EncryptedPayloadMapping.Configure(payload, "Body"));
        builder.Navigation(entity => entity.Body).IsRequired();
    }
}

internal sealed class MemoryItemConfiguration : IEntityTypeConfiguration<MemoryItem>
{
    public void Configure(EntityTypeBuilder<MemoryItem> builder)
    {
        builder.ToTable("Memories");
        builder.HasKey(entity => entity.Id);
        builder.HasAlternateKey(entity => new { entity.TenantId, entity.UserId, entity.Id });
        builder.Property(entity => entity.MemoryType).HasConversion<short>().IsRequired();
        builder.Property(entity => entity.VerificationStatus).HasConversion<short>().IsRequired();
        builder.Property(entity => entity.Sensitivity).HasConversion<short>().IsRequired();
        builder.Property(entity => entity.ConfidenceScore).IsRequired();
        builder.Property(entity => entity.IsActive).IsRequired();
        builder.Property(entity => entity.Version).IsConcurrencyToken().IsRequired();
        builder.HasIndex(entity => new
        {
            entity.TenantId,
            entity.UserId,
            entity.IsActive,
            entity.VerificationStatus,
            entity.MemoryType,
            entity.UpdatedAtUtc,
            entity.Id
        });
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(entity => new { entity.TenantId, entity.UserId })
            .HasPrincipalKey(entity => new { entity.TenantId, entity.Id })
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class MemoryPayloadConfiguration : IEntityTypeConfiguration<MemoryPayload>
{
    public void Configure(EntityTypeBuilder<MemoryPayload> builder)
    {
        builder.ToTable("MemoryPayloads");
        builder.HasKey(entity => new { entity.TenantId, entity.UserId, entity.MemoryId });
        builder.HasOne<MemoryItem>()
            .WithMany()
            .HasForeignKey(entity => new { entity.TenantId, entity.UserId, entity.MemoryId })
            .HasPrincipalKey(entity => new { entity.TenantId, entity.UserId, entity.Id })
            .OnDelete(DeleteBehavior.Cascade);
        builder.OwnsOne(entity => entity.Value, payload => EncryptedPayloadMapping.Configure(payload, "Value"));
        builder.Navigation(entity => entity.Value).IsRequired();
    }
}

internal sealed class StructuredFactConfiguration : IEntityTypeConfiguration<StructuredFact>
{
    public void Configure(EntityTypeBuilder<StructuredFact> builder)
    {
        builder.ToTable("StructuredFacts");
        builder.HasKey(entity => new { entity.TenantId, entity.UserId, entity.MemoryId });
        builder.Property(entity => entity.FactType).HasMaxLength(160).IsRequired();
        builder.HasIndex(entity => new { entity.TenantId, entity.UserId, entity.FactType });
        builder.HasOne<MemoryItem>()
            .WithMany()
            .HasForeignKey(entity => new { entity.TenantId, entity.UserId, entity.MemoryId })
            .HasPrincipalKey(entity => new { entity.TenantId, entity.UserId, entity.Id })
            .OnDelete(DeleteBehavior.Cascade);
        builder.OwnsOne(entity => entity.Value, payload => EncryptedPayloadMapping.Configure(payload, "Value"));
        builder.Navigation(entity => entity.Value).IsRequired();
    }
}

internal sealed class MemorySourceConfiguration : IEntityTypeConfiguration<MemorySource>
{
    public void Configure(EntityTypeBuilder<MemorySource> builder)
    {
        builder.ToTable("MemorySources");
        builder.HasKey(entity => entity.Id);
        builder.HasAlternateKey(entity => new { entity.TenantId, entity.UserId, entity.MemoryId, entity.Id });
        builder.Property(entity => entity.SourceType).HasConversion<short>().IsRequired();
        builder.Property(entity => entity.ConfidenceScore).IsRequired();
        builder.HasOne<MemoryItem>()
            .WithMany()
            .HasForeignKey(entity => new { entity.TenantId, entity.UserId, entity.MemoryId })
            .HasPrincipalKey(nameof(MemoryItem.TenantId), nameof(MemoryItem.UserId), nameof(MemoryItem.Id))
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Message>()
            .WithMany()
            .HasForeignKey(entity => new
            {
                entity.TenantId,
                entity.UserId,
                entity.ConversationId,
                entity.MessageId
            })
            .HasPrincipalKey(entity => new
            {
                entity.TenantId,
                entity.UserId,
                entity.ConversationId,
                entity.Id
            })
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class MemoryRevisionConfiguration : IEntityTypeConfiguration<MemoryRevision>
{
    public void Configure(EntityTypeBuilder<MemoryRevision> builder)
    {
        builder.ToTable("MemoryRevisions");
        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.ChangeType).HasConversion<short>().IsRequired();
        builder.HasIndex(entity => new { entity.TenantId, entity.UserId, entity.MemoryId, entity.RevisionNumber })
            .IsUnique();
        builder.HasOne<MemoryItem>()
            .WithMany()
            .HasForeignKey(entity => new { entity.TenantId, entity.UserId, entity.MemoryId })
            .HasPrincipalKey(nameof(MemoryItem.TenantId), nameof(MemoryItem.UserId), nameof(MemoryItem.Id))
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(entity => new { entity.TenantId, ChangedByUserId = entity.ChangedByUserId })
            .HasPrincipalKey(nameof(User.TenantId), nameof(User.Id))
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<MemorySource>()
            .WithMany()
            .HasForeignKey(entity => new
            {
                entity.TenantId,
                entity.UserId,
                entity.MemoryId,
                entity.SourceId
            })
            .HasPrincipalKey(
                nameof(MemorySource.TenantId),
                nameof(MemorySource.UserId),
                nameof(MemorySource.MemoryId),
                nameof(MemorySource.Id))
            .OnDelete(DeleteBehavior.Restrict);
        builder.OwnsOne(entity => entity.PreviousValue, payload => EncryptedPayloadMapping.Configure(payload, "Previous"));
        builder.Navigation(entity => entity.PreviousValue).IsRequired();
        builder.OwnsOne(entity => entity.Reason, payload => EncryptedPayloadMapping.Configure(payload, "Reason"));
    }
}

internal sealed class MemoryLookupTokenConfiguration : IEntityTypeConfiguration<MemoryLookupToken>
{
    public void Configure(EntityTypeBuilder<MemoryLookupToken> builder)
    {
        builder.ToTable("MemoryLookupTokens");
        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.TokenType).HasConversion<short>().IsRequired();
        builder.Property(entity => entity.KeyId).HasMaxLength(64).IsRequired();
        builder.Property(entity => entity.Token).HasColumnType("BLOB").IsRequired();
        builder.HasIndex(entity => new
        {
            entity.TenantId,
            entity.UserId,
            entity.TokenType,
            entity.KeyId,
            entity.Token,
            entity.MemoryId
        });
        builder.HasOne<MemoryItem>()
            .WithMany()
            .HasForeignKey(entity => new { entity.TenantId, entity.UserId, entity.MemoryId })
            .HasPrincipalKey(nameof(MemoryItem.TenantId), nameof(MemoryItem.UserId), nameof(MemoryItem.Id))
            .OnDelete(DeleteBehavior.Cascade);
    }
}
