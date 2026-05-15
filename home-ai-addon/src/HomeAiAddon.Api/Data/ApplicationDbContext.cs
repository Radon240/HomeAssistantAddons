using HomeAiAddon.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace HomeAiAddon.Api.Data;

public sealed class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
    : DbContext(options)
{
    public DbSet<AddonMetadata> AddonMetadata => Set<AddonMetadata>();

    public DbSet<StateChangeEventRecord> StateChangeEvents => Set<StateChangeEventRecord>();

    public DbSet<AnomalyAlertRecord> AnomalyAlerts => Set<AnomalyAlertRecord>();

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

        modelBuilder.Entity<AnomalyAlertRecord>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.DetectionId).HasMaxLength(64).IsRequired();
            entity.Property(e => e.EntityId).HasMaxLength(255).IsRequired();
            entity.Property(e => e.AnomalyType).HasMaxLength(64).IsRequired();
            entity.Property(e => e.Severity).HasMaxLength(16).IsRequired();
            entity.Property(e => e.Method).HasMaxLength(64).IsRequired();
            entity.Property(e => e.Title).HasMaxLength(512).IsRequired();
            entity.Property(e => e.Explanation).HasMaxLength(2048).IsRequired();
            entity.Property(e => e.RelatedEventIdsJson).IsRequired();
            entity.Property(e => e.MetricsJson).IsRequired();
            entity.HasIndex(e => e.DetectionId).IsUnique();
            entity.HasIndex(e => e.DetectedAtUtc);
            entity.HasIndex(e => e.EntityId);
            entity.HasIndex(e => e.Severity);
        });
    }
}
