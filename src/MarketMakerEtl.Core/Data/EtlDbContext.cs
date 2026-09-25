using MarketMakerEtl.Core.Data.Entities;
using MarketMakerEtl.Core.Models.Runs;
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

    public DbSet<ScrapeRunIssueEntity> ScrapeRunIssues => Set<ScrapeRunIssueEntity>();

    public DbSet<ListingEntity> Listings => Set<ListingEntity>();

    public DbSet<ListingStatusChangeEntity> ListingStatusChanges => Set<ListingStatusChangeEntity>();

    public DbSet<CategoryEntity> Categories => Set<CategoryEntity>();

    public DbSet<JobCategoryEntity> JobCategories => Set<JobCategoryEntity>();

    public DbSet<SchedulerStateEntity> SchedulerState => Set<SchedulerStateEntity>();

    public DbSet<SellerEntity> Sellers => Set<SellerEntity>();

    public DbSet<ListingRawDataEntity> ListingRawData => Set<ListingRawDataEntity>();

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
            entity.Property(e => e.TriggerType).IsRequired().HasMaxLength(32).HasDefaultValue(nameof(TriggerType.Manual));
            entity.Property(e => e.ErrorMessage).HasMaxLength(2000);
            entity.HasIndex(e => e.Status);
        });

        modelBuilder.Entity<ScrapeRunIssueEntity>(entity =>
        {
            entity.ToTable("ScrapeRunIssues");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.ListingId).HasMaxLength(64);
            entity.Property(e => e.IssueType).IsRequired().HasMaxLength(64);
            entity.Property(e => e.ErrorMessage).IsRequired().HasMaxLength(2000);
            entity.Property(e => e.Phase).IsRequired().HasMaxLength(32);
            entity.HasIndex(e => e.ScrapeRunId);
        });

        ConfigureListings(modelBuilder);
        ConfigureCategories(modelBuilder);

        modelBuilder.Entity<SchedulerStateEntity>(entity =>
        {
            entity.ToTable("SchedulerState");
            entity.HasKey(e => e.Id);
        });

        ConfigureSellersAndRawData(modelBuilder);
    }

    private static void ConfigureListings(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ListingEntity>(entity =>
        {
            entity.ToTable("Listings");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.ListingId).IsRequired().HasMaxLength(32);
            entity.HasIndex(e => e.ListingId).IsUnique();
            entity.HasIndex(e => e.ScrapeJobId);
            entity.HasOne(e => e.RawData)
                .WithOne()
                .HasForeignKey<ListingRawDataEntity>(r => r.ListingEntityId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }

    private static void ConfigureSellersAndRawData(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SellerEntity>(entity =>
        {
            entity.ToTable("Sellers");
            entity.HasKey(e => e.SellerId);
        });

        modelBuilder.Entity<ListingRawDataEntity>(entity =>
        {
            entity.ToTable("ListingRawData");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.ListingEntityId).IsUnique();
        });
    }

    private static void ConfigureCategories(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CategoryEntity>(entity =>
        {
            entity.ToTable("Categories");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(255);
        });

        modelBuilder.Entity<JobCategoryEntity>(entity =>
        {
            entity.ToTable("JobCategories");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.ScrapeJobId, e.CategoryId }).IsUnique();
            entity.HasOne(e => e.ScrapeJob)
                .WithMany(j => j.JobCategories)
                .HasForeignKey(e => e.ScrapeJobId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.Category)
                .WithMany()
                .HasForeignKey(e => e.CategoryId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
