using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace CloudShell.Persistence;

public sealed class CloudShellDbContext(DbContextOptions<CloudShellDbContext> options) : DbContext(options)
{
    internal DbSet<ResourceGroupEntity> ResourceGroups => Set<ResourceGroupEntity>();

    internal DbSet<ResourceRegistrationEntity> ResourceRegistrations => Set<ResourceRegistrationEntity>();

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
            entity.HasOne<ResourceGroupEntity>()
                .WithMany()
                .HasForeignKey(registration => registration.ResourceGroupId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.Property(registration => registration.RegisteredAt)
                .HasConversion(new DateTimeOffsetToBinaryConverter());
        });
    }
}
