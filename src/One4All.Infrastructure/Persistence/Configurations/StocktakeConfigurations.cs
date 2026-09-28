using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using One4All.Core.Inventory;

namespace One4All.Infrastructure.Persistence.Configurations;

/// <summary>Maps a stocktake session (<c>inv.stocktake</c>).</summary>
internal sealed class StocktakeSessionConfiguration : IEntityTypeConfiguration<StocktakeSession>
{
    public void Configure(EntityTypeBuilder<StocktakeSession> b)
    {
        b.ToTable("stocktake", Schemas.Inventory);
        b.HasKey(x => x.Id);
        b.Property(x => x.Reference).HasMaxLength(60);
        b.Property(x => x.Status).HasConversion<int>();
        b.HasMany(x => x.Lines).WithOne().HasForeignKey(x => x.StocktakeSessionId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(x => new { x.CompanyId, x.WarehouseId });
    }
}

/// <summary>Maps a stocktake line (<c>inv.stocktake_line</c>).</summary>
internal sealed class StocktakeLineConfiguration : IEntityTypeConfiguration<StocktakeLine>
{
    public void Configure(EntityTypeBuilder<StocktakeLine> b)
    {
        b.ToTable("stocktake_line", Schemas.Inventory);
        b.HasKey(x => x.Id);
        b.Property(x => x.SystemQuantity).HasPrecision(19, 4);
        b.Property(x => x.AverageCost).HasPrecision(19, 6);
        b.Property(x => x.CountedQuantity).HasPrecision(19, 4);
    }
}
