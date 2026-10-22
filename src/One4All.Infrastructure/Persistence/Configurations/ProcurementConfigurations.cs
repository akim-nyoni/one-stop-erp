using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using One4All.Core.Procurement;

namespace One4All.Infrastructure.Persistence.Configurations;

internal sealed class RebateAgreementConfiguration : IEntityTypeConfiguration<RebateAgreement>
{
    public void Configure(EntityTypeBuilder<RebateAgreement> b)
    {
        b.ToTable("rebate_agreement", Schemas.Purchasing);
        b.HasKey(x => x.Id);
        b.Property(x => x.Code).HasMaxLength(30);
        b.Property(x => x.Accrued).HasPrecision(19, 4);
        b.HasMany(x => x.Tiers).WithOne().HasForeignKey(x => x.RebateAgreementId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.Tiers).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.HasIndex(x => new { x.CompanyId, x.Code }).IsUnique();
    }
}

internal sealed class RebateAgreementTierConfiguration : IEntityTypeConfiguration<RebateAgreementTier>
{
    public void Configure(EntityTypeBuilder<RebateAgreementTier> b)
    {
        b.ToTable("rebate_agreement_tier", Schemas.Purchasing);
        b.HasKey(x => x.Id);
        b.Property(x => x.Threshold).HasPrecision(19, 4);
        b.Property(x => x.RatePercent).HasPrecision(9, 4);
    }
}

internal sealed class PurchaseOrderRevisionConfiguration : IEntityTypeConfiguration<PurchaseOrderRevision>
{
    public void Configure(EntityTypeBuilder<PurchaseOrderRevision> b)
    {
        b.ToTable("purchase_order_revision", Schemas.Purchasing);
        b.HasKey(x => x.Id);
        b.Property(x => x.TotalNet).HasPrecision(19, 4);
        b.Property(x => x.LinesSnapshot).HasColumnType("jsonb");
        b.Property(x => x.Reason).HasMaxLength(500);
        b.HasIndex(x => new { x.PurchaseOrderId, x.Version }).IsUnique();
    }
}

internal sealed class InvoiceDisputeConfiguration : IEntityTypeConfiguration<InvoiceDispute>
{
    public void Configure(EntityTypeBuilder<InvoiceDispute> b)
    {
        b.ToTable("invoice_dispute", Schemas.Purchasing);
        b.HasKey(x => x.Id);
        b.Property(x => x.InvoiceNumber).HasMaxLength(60);
        b.Property(x => x.ReasonCode).HasMaxLength(60);
        b.Property(x => x.Reason).HasMaxLength(500);
        b.Property(x => x.ResolutionNote).HasMaxLength(500);
        b.Property(x => x.JournalNumber).HasMaxLength(30);
        b.Property(x => x.Status).HasConversion<int>();
        b.HasMany(x => x.Lines).WithOne().HasForeignKey(x => x.InvoiceDisputeId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.HasIndex(x => new { x.CompanyId, x.Status });
    }
}

internal sealed class InvoiceDisputeLineConfiguration : IEntityTypeConfiguration<InvoiceDisputeLine>
{
    public void Configure(EntityTypeBuilder<InvoiceDisputeLine> b)
    {
        b.ToTable("invoice_dispute_line", Schemas.Purchasing);
        b.HasKey(x => x.Id);
        b.Property(x => x.Quantity).HasPrecision(19, 4);
        b.Property(x => x.UnitPrice).HasPrecision(19, 4);
    }
}

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
        b.Property(x => x.Department).HasMaxLength(30);
        b.Property(x => x.CostCentre).HasMaxLength(20);
        b.Property(x => x.Project).HasMaxLength(20);
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
        b.Property(x => x.ExpenseAccountCode).HasMaxLength(8);
        b.Property(x => x.UnitCode).HasMaxLength(10);
        b.Property(x => x.UnitFactor).HasPrecision(19, 6).HasDefaultValue(1m);
        b.Property(x => x.TaxCode).HasMaxLength(20);
        b.Property(x => x.ReceivedBaseValue).HasPrecision(19, 4);
        b.Property(x => x.InvoicedBaseValue).HasPrecision(19, 4);
        b.Ignore(x => x.UnitQuantity);
        b.Ignore(x => x.PricePerUnit);
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
        b.Property(x => x.BudgetOutcome).HasConversion<int>();
        b.Property(x => x.Justification).HasMaxLength(500);
        b.Property(x => x.Department).HasMaxLength(30);
        b.Property(x => x.CostCentre).HasMaxLength(20);
        b.Property(x => x.Project).HasMaxLength(20);
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
        b.Property(x => x.ExpenseAccountCode).HasMaxLength(20);
        b.Property(x => x.Description).HasMaxLength(300);
        b.Property(x => x.Quantity).HasPrecision(19, 4);
        b.Property(x => x.UnitPrice).HasPrecision(19, 6);
        b.HasIndex(x => x.PurchaseRequisitionId);
    }
}

internal sealed class FrameworkAgreementConfiguration : IEntityTypeConfiguration<FrameworkAgreement>
{
    public void Configure(EntityTypeBuilder<FrameworkAgreement> b)
    {
        b.ToTable("framework_agreement", Schemas.Purchasing);
        b.HasKey(x => x.Id);
        b.Property(x => x.Reference).HasMaxLength(40);
        b.Property(x => x.Description).HasMaxLength(200);
        b.Property(x => x.CeilingValue).HasPrecision(19, 4);
        b.HasIndex(x => new { x.CompanyId, x.Reference }).IsUnique();
    }
}

internal sealed class PurchaseOrderDeliveryConfiguration : IEntityTypeConfiguration<PurchaseOrderDelivery>
{
    public void Configure(EntityTypeBuilder<PurchaseOrderDelivery> b)
    {
        b.ToTable("po_delivery", Schemas.Purchasing);
        b.HasKey(x => x.Id);
        b.Property(x => x.Quantity).HasPrecision(19, 4);
        b.HasOne<PurchaseOrderLine>().WithMany().HasForeignKey(x => x.PurchaseOrderLineId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(x => new { x.PurchaseOrderLineId, x.DueDate });
    }
}

internal sealed class InvoicedOrderLineConfiguration : IEntityTypeConfiguration<InvoicedOrderLine>
{
    public void Configure(EntityTypeBuilder<InvoicedOrderLine> b)
    {
        b.ToTable("invoiced_order_line", Schemas.Purchasing);
        b.HasKey(x => x.Id);
        b.Property(x => x.InvoiceNumber).HasMaxLength(60);
        b.Property(x => x.Quantity).HasPrecision(19, 4);
        b.Property(x => x.OrderPrice).HasPrecision(19, 6);
        b.Property(x => x.InvoicePrice).HasPrecision(19, 6);
        b.HasIndex(x => new { x.CompanyId, x.SupplierId, x.Date });
    }
}
