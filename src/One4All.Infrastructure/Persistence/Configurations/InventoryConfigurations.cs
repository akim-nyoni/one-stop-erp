using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using One4All.Core.Inventory;

namespace One4All.Infrastructure.Persistence.Configurations;

internal sealed class StoreRequisitionConfiguration : IEntityTypeConfiguration<StoreRequisition>
{
    public void Configure(EntityTypeBuilder<StoreRequisition> b)
    {
        b.ToTable("store_requisition", Schemas.Inventory, t =>
            t.HasCheckConstraint("ck_store_requisition_approver_not_requester", "approved_by_user_id IS NULL OR approved_by_user_id <> requested_by_user_id"));
        b.HasKey(x => x.Id);
        b.Property(x => x.Number).HasMaxLength(30);
        b.Property(x => x.ChargeAccountCode).HasMaxLength(8);
        b.Property(x => x.CostCentre).HasMaxLength(3);
        b.Property(x => x.Status).HasConversion<int>();
        b.HasMany(x => x.Lines).WithOne().HasForeignKey(x => x.StoreRequisitionId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.HasIndex(x => new { x.CompanyId, x.Number }).IsUnique();
    }
}

internal sealed class StoreRequisitionLineConfiguration : IEntityTypeConfiguration<StoreRequisitionLine>
{
    public void Configure(EntityTypeBuilder<StoreRequisitionLine> b)
    {
        b.ToTable("store_requisition_line", Schemas.Inventory);
        b.HasKey(x => x.Id);
        b.Property(x => x.Requested).HasPrecision(19, 4);
        b.Property(x => x.Issued).HasPrecision(19, 4);
        b.Ignore(x => x.Outstanding);
    }
}

internal sealed class StockCostLayerConfiguration : IEntityTypeConfiguration<StockCostLayer>
{
    public void Configure(EntityTypeBuilder<StockCostLayer> b)
    {
        b.ToTable("stock_cost_layer", Schemas.Inventory);
        b.HasKey(x => x.Id);
        b.Property(x => x.Reference).HasMaxLength(60);
        b.Property(x => x.ReceivedQuantity).HasPrecision(19, 4);
        b.Property(x => x.RemainingQuantity).HasPrecision(19, 4);
        b.Property(x => x.RemainingValue).HasPrecision(19, 4);
        b.HasIndex(x => new { x.CompanyId, x.ItemId, x.WarehouseId, x.LayerDate });
    }
}

internal sealed class ItemConfiguration : IEntityTypeConfiguration<Item>
{
    public void Configure(EntityTypeBuilder<Item> b)
    {
        b.Property(x => x.CatchWeightUnit).HasMaxLength(10);
        b.Property(x => x.NominalWeight).HasPrecision(19, 4);
        b.Property(x => x.WeightTolerancePercent).HasPrecision(7, 4);
        b.Ignore(x => x.IsCatchWeight);
        b.Property(x => x.UsageVarianceAccountCode).HasMaxLength(20);
        b.ToTable("item", Schemas.Inventory);
        b.HasKey(x => x.Id);
        b.Property(x => x.Plu).HasMaxLength(20);
        b.Property(x => x.MaxQuantityPerSale).HasPrecision(19, 4);
        b.Property(x => x.Brand).HasMaxLength(60);
        b.Property(x => x.HsCode).HasMaxLength(20);
        b.Property(x => x.CountryOfOrigin).HasMaxLength(60);
        b.Property(x => x.Size).HasMaxLength(20);
        b.Property(x => x.Colour).HasMaxLength(30);
        b.Property(x => x.AbcClass).HasMaxLength(1);
        b.HasIndex(x => new { x.CompanyId, x.Plu });
        b.HasIndex(x => x.ParentItemId);
        b.Property(x => x.Tracking).HasConversion<int>();
        b.Property(x => x.Code).HasMaxLength(40);
        b.Property(x => x.Description).HasMaxLength(300);
        b.Property(x => x.ShortDescription).HasMaxLength(80);
        b.Property(x => x.ItemType).HasConversion<int>();
        b.Property(x => x.CostingMethod).HasConversion<int>();
        b.Property(x => x.StandardCost).HasPrecision(19, 6);
        b.Property(x => x.PriceVarianceAccountCode).HasMaxLength(8);
        b.Property(x => x.UnitWeightKg).HasPrecision(19, 6);
        b.Property(x => x.UnitVolumeM3).HasPrecision(19, 6);
        b.Property(x => x.UnitOfMeasure).HasMaxLength(10);
        b.Property(x => x.TaxCode).HasMaxLength(20);
        b.Property(x => x.Category).HasMaxLength(60);
        b.Property(x => x.Barcode).HasMaxLength(60);
        b.Property(x => x.CostOfSalesAccountCode).HasMaxLength(8);
        b.Property(x => x.RevenueAccountCode).HasMaxLength(8);
        b.Property(x => x.ReorderLevel).HasPrecision(19, 4);
        b.Property(x => x.ReorderQuantity).HasPrecision(19, 4);
        b.Property(x => x.MaximumStock).HasPrecision(19, 4);
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
        b.Property(x => x.WeightOnHand).HasPrecision(19, 4);
        b.Ignore(x => x.CostPerWeight);
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
        b.Property(x => x.Weight).HasPrecision(19, 4);
        b.ToTable("stock_movement", Schemas.Inventory);
        b.HasKey(x => x.Id);
        b.Property(x => x.ReasonCode).HasMaxLength(20);
        b.Property(x => x.Type).HasConversion<int>();
        b.Property(x => x.Quantity).HasPrecision(19, 4);
        b.Property(x => x.UnitCost).HasPrecision(19, 6);
        b.Property(x => x.Value).HasPrecision(19, 4);
        b.Property(x => x.Reference).HasMaxLength(60);
        b.HasIndex(x => new { x.CompanyId, x.ItemId, x.WarehouseId, x.Date });
    }
}

internal sealed class StockReasonCodeConfiguration : IEntityTypeConfiguration<StockReasonCode>
{
    public void Configure(EntityTypeBuilder<StockReasonCode> b)
    {
        b.ToTable("reason_code", Schemas.Inventory);
        b.HasKey(x => x.Id);
        b.Property(x => x.Code).HasMaxLength(20);
        b.Property(x => x.Name).HasMaxLength(120);
        b.HasIndex(x => new { x.CompanyId, x.Code }).IsUnique();
    }
}

internal sealed class StockLotMovementConfiguration : IEntityTypeConfiguration<StockLotMovement>
{
    public void Configure(EntityTypeBuilder<StockLotMovement> b)
    {
        b.Property(x => x.UnitCost).HasPrecision(19, 6);
        b.ToTable("lot_movement", Schemas.Inventory);
        b.HasKey(x => x.Id);
        b.Property(x => x.LotNumber).HasMaxLength(60);
        b.Property(x => x.Quantity).HasPrecision(19, 4);
        b.HasIndex(x => new { x.CompanyId, x.ItemId, x.LotNumber });
        b.HasIndex(x => x.StockMovementId);
    }
}

internal sealed class StockTransferConfiguration : IEntityTypeConfiguration<StockTransfer>
{
    public void Configure(EntityTypeBuilder<StockTransfer> b)
    {
        b.ToTable("stock_transfer", Schemas.Inventory);
        b.HasKey(x => x.Id);
        b.Property(x => x.TransferNumber).HasMaxLength(40);
        b.Property(x => x.Quantity).HasPrecision(19, 4);
        b.Property(x => x.Value).HasPrecision(19, 4);
        b.Property(x => x.ReceivedQuantity).HasPrecision(19, 4);
        b.Property(x => x.Status).HasConversion<int>();
        b.HasIndex(x => new { x.CompanyId, x.TransferNumber }).IsUnique();
    }
}

internal sealed class BillOfMaterialsConfiguration : IEntityTypeConfiguration<BillOfMaterials>
{
    public void Configure(EntityTypeBuilder<BillOfMaterials> b)
    {
        b.ToTable("bom", Schemas.Inventory);
        b.HasKey(x => x.Id);
        b.Property(x => x.Code).HasMaxLength(40);
        b.Property(x => x.OutputQuantity).HasPrecision(19, 4);
        b.HasIndex(x => new { x.CompanyId, x.Code }).IsUnique();
        b.HasMany(x => x.Lines).WithOne().HasForeignKey(l => l.BillOfMaterialsId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class BomLineConfiguration : IEntityTypeConfiguration<BomLine>
{
    public void Configure(EntityTypeBuilder<BomLine> b)
    {
        b.ToTable("bom_line", Schemas.Inventory);
        b.HasKey(x => x.Id);
        b.Property(x => x.Quantity).HasPrecision(19, 4);
        b.Property(x => x.ScrapPercent).HasPrecision(9, 4);
    }
}

internal sealed class AssemblyRunConfiguration : IEntityTypeConfiguration<AssemblyRun>
{
    public void Configure(EntityTypeBuilder<AssemblyRun> b)
    {
        b.Property(x => x.UsageVariance).HasPrecision(19, 4);
        b.ToTable("assembly_run", Schemas.Inventory);
        b.HasKey(x => x.Id);
        b.Property(x => x.RunNumber).HasMaxLength(40);
        b.Property(x => x.PlannedOutput).HasPrecision(19, 4);
        b.Property(x => x.ActualOutput).HasPrecision(19, 4);
        b.Property(x => x.TotalCost).HasPrecision(19, 4);
        b.HasIndex(x => new { x.CompanyId, x.RunNumber }).IsUnique();
        b.HasMany(x => x.Lines).WithOne().HasForeignKey(l => l.AssemblyRunId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class AssemblyRunLineConfiguration : IEntityTypeConfiguration<AssemblyRunLine>
{
    public void Configure(EntityTypeBuilder<AssemblyRunLine> b)
    {
        b.ToTable("assembly_run_line", Schemas.Inventory);
        b.HasKey(x => x.Id);
        b.Property(x => x.StandardQuantity).HasPrecision(19, 4);
        b.Property(x => x.ActualQuantity).HasPrecision(19, 4);
        b.Property(x => x.Cost).HasPrecision(19, 4);
    }
}

internal sealed class ItemAliasConfiguration : IEntityTypeConfiguration<ItemAlias>
{
    public void Configure(EntityTypeBuilder<ItemAlias> b)
    {
        b.ToTable("item_alias", Schemas.Inventory);
        b.HasKey(x => x.Id);
        b.Property(x => x.Kind).HasConversion<int>();
        b.Property(x => x.Alias).HasMaxLength(120);
        b.HasIndex(x => new { x.CompanyId, x.Alias });
        b.HasIndex(x => x.ItemId);
    }
}

internal sealed class ItemUnitConfiguration : IEntityTypeConfiguration<ItemUnit>
{
    public void Configure(EntityTypeBuilder<ItemUnit> b)
    {
        b.ToTable("item_unit", Schemas.Inventory);
        b.HasKey(x => x.Id);
        b.Property(x => x.UnitCode).HasMaxLength(20);
        b.Property(x => x.Factor).HasPrecision(19, 6);
        b.Property(x => x.Barcode).HasMaxLength(60);
        b.HasIndex(x => new { x.ItemId, x.UnitCode }).IsUnique();
        b.HasIndex(x => new { x.CompanyId, x.Barcode });
    }
}

internal sealed class ItemSupplierConfiguration : IEntityTypeConfiguration<ItemSupplier>
{
    public void Configure(EntityTypeBuilder<ItemSupplier> b)
    {
        b.ToTable("item_supplier", Schemas.Inventory);
        b.HasKey(x => x.Id);
        b.Property(x => x.SupplierItemCode).HasMaxLength(60);
        b.Property(x => x.MinimumOrderQuantity).HasPrecision(19, 4);
        b.Property(x => x.LastCost).HasPrecision(19, 6);
        b.HasIndex(x => new { x.ItemId, x.SupplierId }).IsUnique();
    }
}

internal sealed class WarehouseBinConfiguration : IEntityTypeConfiguration<WarehouseBin>
{
    public void Configure(EntityTypeBuilder<WarehouseBin> b)
    {
        b.ToTable("bin", Schemas.Inventory);
        b.HasKey(x => x.Id);
        b.Property(x => x.Code).HasMaxLength(30);
        b.Property(x => x.CapacityUnits).HasPrecision(19, 4);
        b.HasIndex(x => new { x.WarehouseId, x.Code }).IsUnique();
    }
}

internal sealed class BinStockConfiguration : IEntityTypeConfiguration<BinStock>
{
    public void Configure(EntityTypeBuilder<BinStock> b)
    {
        b.ToTable("bin_stock", Schemas.Inventory);
        b.HasKey(x => x.Id);
        b.Property(x => x.Quantity).HasPrecision(19, 4);
        b.HasIndex(x => new { x.BinId, x.ItemId }).IsUnique();
        b.HasIndex(x => x.ItemId);
    }
}

internal sealed class ItemWarehouseLevelConfiguration : IEntityTypeConfiguration<ItemWarehouseLevel>
{
    public void Configure(EntityTypeBuilder<ItemWarehouseLevel> b)
    {
        b.ToTable("item_warehouse_level", Schemas.Inventory);
        b.HasKey(x => x.Id);
        b.Property(x => x.ReorderLevel).HasPrecision(19, 4);
        b.Property(x => x.ReorderQuantity).HasPrecision(19, 4);
        b.HasIndex(x => new { x.ItemId, x.WarehouseId }).IsUnique();
    }
}
