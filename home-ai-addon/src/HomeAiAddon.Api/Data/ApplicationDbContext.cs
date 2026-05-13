using HomeAiAddon.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace HomeAiAddon.Api.Data;

public sealed class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
    : DbContext(options)
{
    public DbSet<AddonMetadata> AddonMetadata => Set<AddonMetadata>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AddonMetadata>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.SchemaVersion).HasMaxLength(32).IsRequired();
        });
    }
}
