using MarketMakerEtl.Core.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace MarketMakerEtl.Core.Data;

public sealed class EtlDbContext : DbContext
{
    public EtlDbContext(DbContextOptions<EtlDbContext> options)
        : base(options)
    {
    }

    public DbSet<ScrapeJobEntity> ScrapeJobs => Set<ScrapeJobEntity>();

    public DbSet<ScrapeRunEntity> ScrapeRuns => Set<ScrapeRunEntity>();

    public DbSet<ListingEntity> Listings => Set<ListingEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ScrapeJobEntity>(entity =>
        {
            entity.ToTable("ScrapeJobs");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.SearchTerm).IsRequired().HasMaxLength(255);
            entity.HasIndex(e => e.SearchTerm);
        });

        modelBuilder.Entity<ScrapeRunEntity>(entity =>
        {
            entity.ToTable("ScrapeRuns");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Status).IsRequired().HasMaxLength(32);
            entity.Property(e => e.ErrorMessage).HasMaxLength(2000);
            entity.HasIndex(e => e.Status);
        });

        modelBuilder.Entity<ListingEntity>(entity =>
        {
            entity.ToTable("Listings");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.ListingId).IsRequired().HasMaxLength(32);
            entity.HasIndex(e => e.ListingId).IsUnique();
            entity.HasIndex(e => e.ScrapeJobId);
        });
    }
}
