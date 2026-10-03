using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using One4All.Core.Procurement;

namespace One4All.Infrastructure.Persistence.Configurations;

internal sealed class GrnInspectionConfiguration : IEntityTypeConfiguration<GrnInspection>
{
    public void Configure(EntityTypeBuilder<GrnInspection> b)
    {
        b.ToTable("grn_inspection", Schemas.Purchasing);
        b.HasKey(x => x.Id);
        b.HasMany(x => x.Lines).WithOne().HasForeignKey(x => x.GrnInspectionId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.HasIndex(x => x.GoodsReceivedNoteId);
    }
}

internal sealed class GrnInspectionLineConfiguration : IEntityTypeConfiguration<GrnInspectionLine>
{
    public void Configure(EntityTypeBuilder<GrnInspectionLine> b)
    {
        b.ToTable("grn_inspection_line", Schemas.Purchasing);
        b.HasKey(x => x.Id);
        b.Property(x => x.Passed).HasPrecision(19, 4);
        b.Property(x => x.Rejected).HasPrecision(19, 4);
        b.Property(x => x.RejectionReason).HasMaxLength(300);
    }
}

internal sealed class RfqConfiguration : IEntityTypeConfiguration<Rfq>
{
    public void Configure(EntityTypeBuilder<Rfq> b)
    {
        b.ToTable("rfq", Schemas.Purchasing);
        b.HasKey(x => x.Id);
        b.Property(x => x.RfqNumber).HasMaxLength(40);
        b.Property(x => x.Status).HasConversion<int>();
        b.Property(x => x.AwardReason).HasMaxLength(500);
        b.HasMany(x => x.Lines).WithOne().HasForeignKey(x => x.RfqId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.HasMany(x => x.Quotes).WithOne().HasForeignKey(x => x.RfqId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.Quotes).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.HasIndex(x => new { x.CompanyId, x.RfqNumber }).IsUnique();
    }
}

internal sealed class RfqLineConfiguration : IEntityTypeConfiguration<RfqLine>
{
    public void Configure(EntityTypeBuilder<RfqLine> b)
    {
        b.ToTable("rfq_line", Schemas.Purchasing);
        b.HasKey(x => x.Id);
        b.Property(x => x.Description).HasMaxLength(200);
        b.Property(x => x.Quantity).HasPrecision(19, 4);
    }
}

internal sealed class RfqQuoteConfiguration : IEntityTypeConfiguration<RfqQuote>
{
    public void Configure(EntityTypeBuilder<RfqQuote> b)
    {
        b.ToTable("rfq_quote", Schemas.Purchasing);
        b.HasKey(x => x.Id);
        b.Property(x => x.Terms).HasMaxLength(300);
        b.HasMany(x => x.Prices).WithOne().HasForeignKey(x => x.RfqQuoteId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.Prices).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.HasIndex(x => new { x.RfqId, x.SupplierId }).IsUnique();
    }
}

internal sealed class RfqQuoteLineConfiguration : IEntityTypeConfiguration<RfqQuoteLine>
{
    public void Configure(EntityTypeBuilder<RfqQuoteLine> b)
    {
        b.ToTable("rfq_quote_line", Schemas.Purchasing);
        b.HasKey(x => x.Id);
        b.Property(x => x.UnitPrice).HasPrecision(19, 4);
    }
}

internal sealed class PurchaseOrderConfiguration : IEntityTypeConfiguration<PurchaseOrder>
{
    public void Configure(EntityTypeBuilder<PurchaseOrder> b)
    {
        b.ToTable("purchase_order", Schemas.Purchasing);
        b.HasKey(x => x.Id);
        b.Property(x => x.OrderNumber).HasMaxLength(40);
        b.Property(x => x.Currency).HasMaxLength(3);
        b.Property(x => x.Status).HasConversion<int>();
        b.Ignore(x => x.TotalNet);
        b.HasMany(x => x.Lines).WithOne().HasForeignKey(x => x.PurchaseOrderId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.HasIndex(x => new { x.CompanyId, x.OrderNumber }).IsUnique();
    }
}

internal sealed class PurchaseOrderLineConfiguration : IEntityTypeConfiguration<PurchaseOrderLine>
{
    public void Configure(EntityTypeBuilder<PurchaseOrderLine> b)
    {
        b.ToTable("purchase_order_line", Schemas.Purchasing);
        b.HasKey(x => x.Id);
        b.Property(x => x.Description).HasMaxLength(300);
        b.Property(x => x.OrderedQuantity).HasPrecision(19, 4);
        b.Property(x => x.UnitPrice).HasPrecision(19, 6);
        b.Property(x => x.ReceivedQuantity).HasPrecision(19, 4);
        b.Property(x => x.InvoicedQuantity).HasPrecision(19, 4);
    }
}

internal sealed class GoodsReceivedNoteConfiguration : IEntityTypeConfiguration<GoodsReceivedNote>
{
    public void Configure(EntityTypeBuilder<GoodsReceivedNote> b)
    {
        b.ToTable("grn", Schemas.Purchasing);
        b.HasKey(x => x.Id);
        b.Property(x => x.GrnNumber).HasMaxLength(40);
        b.HasMany(x => x.Lines).WithOne().HasForeignKey(x => x.GoodsReceivedNoteId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.HasIndex(x => new { x.CompanyId, x.GrnNumber }).IsUnique();
    }
}

internal sealed class GrnLineConfiguration : IEntityTypeConfiguration<GrnLine>
{
    public void Configure(EntityTypeBuilder<GrnLine> b)
    {
        b.ToTable("grn_line", Schemas.Purchasing);
        b.HasKey(x => x.Id);
        b.Property(x => x.Quantity).HasPrecision(19, 4);
        b.Property(x => x.UnitPrice).HasPrecision(19, 6);
    }
}

internal sealed class PurchaseRequisitionConfiguration : IEntityTypeConfiguration<PurchaseRequisition>
{
    public void Configure(EntityTypeBuilder<PurchaseRequisition> b)
    {
        b.ToTable("requisition", Schemas.Purchasing);
        b.HasKey(x => x.Id);
        b.Property(x => x.RequisitionNumber).HasMaxLength(40);
        b.Property(x => x.Currency).HasMaxLength(3);
        b.Property(x => x.Status).HasConversion<int>();
        b.Ignore(x => x.Total);
        b.HasMany(x => x.Lines).WithOne().HasForeignKey(x => x.PurchaseRequisitionId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.HasIndex(x => new { x.CompanyId, x.RequisitionNumber }).IsUnique();
    }
}

internal sealed class PurchaseRequisitionLineConfiguration : IEntityTypeConfiguration<PurchaseRequisitionLine>
{
    public void Configure(EntityTypeBuilder<PurchaseRequisitionLine> b)
    {
        b.ToTable("requisition_line", Schemas.Purchasing);
        b.HasKey(x => x.Id);
        b.Property(x => x.Description).HasMaxLength(300);
        b.Property(x => x.Quantity).HasPrecision(19, 4);
        b.Property(x => x.UnitPrice).HasPrecision(19, 6);
        b.HasIndex(x => x.PurchaseRequisitionId);
    }
}
