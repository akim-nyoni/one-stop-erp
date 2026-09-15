using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using One4All.Core.Inventory;

namespace One4All.Infrastructure.Persistence.Configurations;

internal sealed class ItemConfiguration : IEntityTypeConfiguration<Item>
{
    public void Configure(EntityTypeBuilder<Item> b)
    {
        b.ToTable("item", Schemas.Inventory);
        b.HasKey(x => x.Id);
        b.Property(x => x.Code).HasMaxLength(40);
        b.Property(x => x.Description).HasMaxLength(300);
        b.Property(x => x.ShortDescription).HasMaxLength(80);
        b.Property(x => x.ItemType).HasConversion<int>();
        b.Property(x => x.CostingMethod).HasConversion<int>();
        b.Property(x => x.UnitOfMeasure).HasMaxLength(10);
        b.Property(x => x.TaxCode).HasMaxLength(20);
        b.Property(x => x.Category).HasMaxLength(60);
        b.Property(x => x.Barcode).HasMaxLength(60);
        b.Property(x => x.CostOfSalesAccountCode).HasMaxLength(8);
        b.Property(x => x.RevenueAccountCode).HasMaxLength(8);
        b.Property(x => x.ReorderLevel).HasPrecision(19, 4);
        b.Property(x => x.ReorderQuantity).HasPrecision(19, 4);
        b.Ignore(x => x.IsStockTracked);
        b.HasIndex(x => new { x.CompanyId, x.Code }).IsUnique().HasFilter("is_deleted = false");
        b.HasQueryFilter(x => !x.IsDeleted);
    }
}

internal sealed class WarehouseConfiguration : IEntityTypeConfiguration<Warehouse>
{
    public void Configure(EntityTypeBuilder<Warehouse> b)
    {
        b.ToTable("warehouse", Schemas.Inventory);
        b.HasKey(x => x.Id);
        b.Property(x => x.Code).HasMaxLength(20);
        b.Property(x => x.Name).HasMaxLength(120);
        b.HasIndex(x => new { x.CompanyId, x.Code }).IsUnique().HasFilter("is_deleted = false");
        b.HasQueryFilter(x => !x.IsDeleted);
    }
}

internal sealed class StockBalanceConfiguration : IEntityTypeConfiguration<StockBalance>
{
    public void Configure(EntityTypeBuilder<StockBalance> b)
    {
        b.ToTable("stock_balance", Schemas.Inventory);
        b.HasKey(x => x.Id);
        b.Property(x => x.QuantityOnHand).HasPrecision(19, 4);
        b.Property(x => x.AverageCost).HasPrecision(19, 6);
        b.Property(x => x.TotalValue).HasPrecision(19, 4);
        b.HasIndex(x => new { x.CompanyId, x.ItemId, x.WarehouseId }).IsUnique();
    }
}

internal sealed class StockMovementConfiguration : IEntityTypeConfiguration<StockMovement>
{
    public void Configure(EntityTypeBuilder<StockMovement> b)
    {
        b.ToTable("stock_movement", Schemas.Inventory);
        b.HasKey(x => x.Id);
        b.Property(x => x.Type).HasConversion<int>();
        b.Property(x => x.Quantity).HasPrecision(19, 4);
        b.Property(x => x.UnitCost).HasPrecision(19, 6);
        b.Property(x => x.Value).HasPrecision(19, 4);
        b.Property(x => x.Reference).HasMaxLength(60);
        b.HasIndex(x => new { x.CompanyId, x.ItemId, x.WarehouseId, x.Date });
    }
}
