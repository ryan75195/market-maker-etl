using MarketMakerEtl.Core.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace MarketMakerEtl.Core.Data;

public sealed partial class EtlDbContext
{
    private static void ConfigureOpenAiUsageRecords(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<OpenAiUsageRecordEntity>(entity =>
        {
            entity.ToTable("OpenAiUsageRecords");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Model).IsRequired().HasMaxLength(64);
            entity.HasIndex(e => e.RecordedUtc);
        });
    }
}
