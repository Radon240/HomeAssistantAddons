using HomeAiAddon.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace HomeAiAddon.Api.Data;

public sealed class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
    : DbContext(options)
{
    public DbSet<AddonMetadata> AddonMetadata => Set<AddonMetadata>();

    public DbSet<StateChangeEventRecord> StateChangeEvents => Set<StateChangeEventRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AddonMetadata>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.SchemaVersion).HasMaxLength(32).IsRequired();
        });

        modelBuilder.Entity<StateChangeEventRecord>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.EntityId).HasMaxLength(255).IsRequired();
            entity.Property(e => e.OldState).HasMaxLength(255);
            entity.Property(e => e.NewState).HasMaxLength(255);
            entity.Property(e => e.FriendlyName).HasMaxLength(255);
            entity.HasIndex(e => e.ReceivedAtUtc);
            entity.HasIndex(e => e.EntityId);
        });
    }
}
