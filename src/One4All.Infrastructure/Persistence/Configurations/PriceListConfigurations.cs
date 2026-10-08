using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using One4All.Core.Inventory;

namespace One4All.Infrastructure.Persistence.Configurations;

/// <summary>Maps a selling price list (<c>inv.price_list</c>).</summary>
internal sealed class PriceListConfiguration : IEntityTypeConfiguration<PriceList>
{
    public void Configure(EntityTypeBuilder<PriceList> b)
    {
        b.ToTable("price_list", Schemas.Inventory);
        b.HasKey(x => x.Id);
        b.Property(x => x.Code).HasMaxLength(20);
        b.Property(x => x.Name).HasMaxLength(120);
        b.Property(x => x.Currency).HasMaxLength(3);
        b.Property(x => x.CostPlusBasis).HasConversion<int>();
        b.Property(x => x.CostPlusPercent).HasPrecision(9, 4);
        b.Property(x => x.CostPlusRoundTo).HasPrecision(19, 4);
        b.HasMany(x => x.Lines).WithOne().HasForeignKey(x => x.PriceListId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(x => new { x.CompanyId, x.Code }).IsUnique().HasFilter("is_deleted = false");
        b.HasQueryFilter(One4AllDbContext.SoftDeleteFilter, x => !x.IsDeleted);
    }
}

/// <summary>Maps a price-list line (<c>inv.price_list_line</c>).</summary>
internal sealed class PriceListLineConfiguration : IEntityTypeConfiguration<PriceListLine>
{
    public void Configure(EntityTypeBuilder<PriceListLine> b)
    {
        b.ToTable("price_list_line", Schemas.Inventory);
        b.HasKey(x => x.Id);
        b.Property(x => x.MinimumQuantity).HasPrecision(19, 4);
        b.Property(x => x.UnitPrice).HasPrecision(19, 4);
        b.HasIndex(x => new { x.PriceListId, x.ItemId, x.MinimumQuantity, x.EffectiveFrom }).IsUnique().AreNullsDistinct(false);
    }
}
