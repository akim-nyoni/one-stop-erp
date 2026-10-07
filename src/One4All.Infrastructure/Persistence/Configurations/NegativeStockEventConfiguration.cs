using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using One4All.Core.Inventory;

namespace One4All.Infrastructure.Persistence.Configurations;

internal sealed class NegativeStockEventConfiguration : IEntityTypeConfiguration<NegativeStockEvent>
{
    public void Configure(EntityTypeBuilder<NegativeStockEvent> b)
    {
        b.ToTable("negative_stock_event", Schemas.Inventory);
        b.HasKey(x => x.Id);
        b.Property(x => x.ShortQuantity).HasPrecision(19, 4);
        b.Property(x => x.UnitCostUsed).HasPrecision(19, 6);
        b.Property(x => x.Reference).HasMaxLength(60);
        b.Property(x => x.Reason).HasMaxLength(300);
        b.Property(x => x.CorrectionJournalNumber).HasMaxLength(40);
        b.HasIndex(x => new { x.CompanyId, x.ItemId, x.WarehouseId });
    }
}
