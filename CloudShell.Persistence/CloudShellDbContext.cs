using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace CloudShell.Persistence;

public sealed class CloudShellDbContext(DbContextOptions<CloudShellDbContext> options) : DbContext(options)
{
    internal DbSet<ResourceGroupEntity> ResourceGroups => Set<ResourceGroupEntity>();

    internal DbSet<ResourceRegistrationEntity> ResourceRegistrations => Set<ResourceRegistrationEntity>();

    internal DbSet<ExtensionActivationEntity> ExtensionActivations => Set<ExtensionActivationEntity>();

    internal DbSet<ResourceEventEntity> ResourceEvents => Set<ResourceEventEntity>();

    internal DbSet<ResourceHealthSnapshotEntity> ResourceHealthSnapshots => Set<ResourceHealthSnapshotEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ResourceGroupEntity>(entity =>
        {
            entity.ToTable("ResourceGroups");
            entity.HasKey(group => group.Id);
            entity.Property(group => group.Name).HasMaxLength(200).IsRequired();
            entity.Property(group => group.Description).HasMaxLength(2000).IsRequired();
        });

        modelBuilder.Entity<ResourceRegistrationEntity>(entity =>
        {
            entity.ToTable("ResourceRegistrations");
            entity.HasKey(registration => registration.ResourceId);
            entity.Property(registration => registration.ResourceId).HasMaxLength(500);
            entity.Property(registration => registration.ProviderId).HasMaxLength(200).IsRequired();
            entity.Property(registration => registration.ResourceGroupId).HasMaxLength(100);
            entity.Property(registration => registration.DependsOnJson).IsRequired();
            entity.Property(registration => registration.IdentityJson);
            entity.HasOne<ResourceGroupEntity>()
                .WithMany()
                .HasForeignKey(registration => registration.ResourceGroupId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.Property(registration => registration.RegisteredAt)
                .HasConversion(new DateTimeOffsetToBinaryConverter());
        });

        modelBuilder.Entity<ExtensionActivationEntity>(entity =>
        {
            entity.ToTable("ExtensionActivations");
            entity.HasKey(activation => activation.ExtensionId);
            entity.Property(activation => activation.ExtensionId).HasMaxLength(200);
            entity.Property(activation => activation.State).HasMaxLength(50).IsRequired();
            entity.Property(activation => activation.UpdatedBy).HasMaxLength(200);
            entity.Property(activation => activation.UpdatedAt)
                .HasConversion(new DateTimeOffsetToBinaryConverter());
        });

        modelBuilder.Entity<ResourceEventEntity>(entity =>
        {
            entity.ToTable("ResourceEvents");
            entity.HasKey(resourceEvent => resourceEvent.Id);
            entity.Property(resourceEvent => resourceEvent.ResourceId).HasMaxLength(500).IsRequired();
            entity.Property(resourceEvent => resourceEvent.EventType).HasMaxLength(200).IsRequired();
            entity.Property(resourceEvent => resourceEvent.Message).HasMaxLength(4000).IsRequired();
            entity.Property(resourceEvent => resourceEvent.TriggeredBy).HasMaxLength(500);
            entity.Property(resourceEvent => resourceEvent.Level).HasMaxLength(50).IsRequired();
            entity.Property(resourceEvent => resourceEvent.TraceId).HasMaxLength(100);
            entity.Property(resourceEvent => resourceEvent.SpanId).HasMaxLength(100);
            entity.Property(resourceEvent => resourceEvent.Timestamp)
                .HasConversion(new DateTimeOffsetToBinaryConverter());
            entity.HasIndex(resourceEvent => resourceEvent.ResourceId);
            entity.HasIndex(resourceEvent => resourceEvent.EventType);
            entity.HasIndex(resourceEvent => resourceEvent.TraceId);
            entity.HasIndex(resourceEvent => resourceEvent.Timestamp);
        });

        modelBuilder.Entity<ResourceHealthSnapshotEntity>(entity =>
        {
            entity.ToTable("ResourceHealthSnapshots");
            entity.HasKey(snapshot => snapshot.Id);
            entity.Property(snapshot => snapshot.ResourceId).HasMaxLength(500).IsRequired();
            entity.Property(snapshot => snapshot.Status).HasMaxLength(50).IsRequired();
            entity.Property(snapshot => snapshot.CheckedAt)
                .HasConversion(new DateTimeOffsetToBinaryConverter());
            entity.Property(snapshot => snapshot.ChecksJson).IsRequired();
            entity.HasIndex(snapshot => snapshot.ResourceId);
            entity.HasIndex(snapshot => snapshot.CheckedAt);
        });
    }
}
