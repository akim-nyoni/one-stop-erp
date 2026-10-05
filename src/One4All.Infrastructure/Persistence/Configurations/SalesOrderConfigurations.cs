using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using One4All.Core.SalesOrders;

namespace One4All.Infrastructure.Persistence.Configurations;

internal sealed class SalesOrderConfiguration : IEntityTypeConfiguration<SalesOrder>
{
    public void Configure(EntityTypeBuilder<SalesOrder> b)
    {
        b.ToTable("sales_order", Schemas.SalesOrders);
        b.HasKey(x => x.Id);
        b.Property(x => x.OrderNumber).HasMaxLength(40);
        b.Property(x => x.Currency).HasMaxLength(3);
        b.Property(x => x.Status).HasConversion<int>();
        b.HasMany(x => x.Lines).WithOne().HasForeignKey(x => x.SalesOrderId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.HasIndex(x => new { x.CompanyId, x.OrderNumber }).IsUnique();
    }
}

internal sealed class SalesOrderLineConfiguration : IEntityTypeConfiguration<SalesOrderLine>
{
    public void Configure(EntityTypeBuilder<SalesOrderLine> b)
    {
        b.ToTable("sales_order_line", Schemas.SalesOrders);
        b.HasKey(x => x.Id);
        b.Property(x => x.Description).HasMaxLength(300);
        b.Property(x => x.RevenueAccountCode).HasMaxLength(8);
        b.Property(x => x.TaxCode).HasMaxLength(20);
        b.Property(x => x.OrderedQuantity).HasPrecision(19, 4);
        b.Property(x => x.UnitPrice).HasPrecision(19, 6);
        b.Property(x => x.DespatchedQuantity).HasPrecision(19, 4);
        b.Property(x => x.InvoicedQuantity).HasPrecision(19, 4);
    }
}

internal sealed class QuotationConfiguration : IEntityTypeConfiguration<Quotation>
{
    public void Configure(EntityTypeBuilder<Quotation> b)
    {
        b.ToTable("quotation", Schemas.SalesOrders);
        b.HasKey(x => x.Id);
        b.Property(x => x.QuoteNumber).HasMaxLength(40);
        b.Property(x => x.Currency).HasMaxLength(3);
        b.Property(x => x.Status).HasConversion<int>();
        b.Ignore(x => x.Total);
        b.HasMany(x => x.Lines).WithOne().HasForeignKey(x => x.QuotationId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.HasIndex(x => new { x.CompanyId, x.QuoteNumber }).IsUnique();
    }
}

internal sealed class QuotationLineConfiguration : IEntityTypeConfiguration<QuotationLine>
{
    public void Configure(EntityTypeBuilder<QuotationLine> b)
    {
        b.ToTable("quotation_line", Schemas.SalesOrders);
        b.HasKey(x => x.Id);
        b.Property(x => x.Description).HasMaxLength(300);
        b.Property(x => x.RevenueAccountCode).HasMaxLength(8);
        b.Property(x => x.TaxCode).HasMaxLength(20);
        b.Property(x => x.Quantity).HasPrecision(19, 4);
        b.Property(x => x.UnitPrice).HasPrecision(19, 6);
        b.HasIndex(x => x.QuotationId);
    }
}

internal sealed class SalesDeliveryConfiguration : IEntityTypeConfiguration<SalesDelivery>
{
    public void Configure(EntityTypeBuilder<SalesDelivery> b)
    {
        b.ToTable("delivery", Schemas.SalesOrders);
        b.HasKey(x => x.Id);
        b.Property(x => x.Reference).HasMaxLength(40);
        b.Property(x => x.Vehicle).HasMaxLength(20);
        b.Property(x => x.Route).HasMaxLength(60);
        b.Property(x => x.Driver).HasMaxLength(120);
        b.Property(x => x.ReceivedBy).HasMaxLength(120);
        b.Property(x => x.PodReference).HasMaxLength(60);
        b.HasIndex(x => new { x.CompanyId, x.Reference });
        b.HasMany(x => x.Lines).WithOne().HasForeignKey(l => l.SalesDeliveryId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class SalesDeliveryLineConfiguration : IEntityTypeConfiguration<SalesDeliveryLine>
{
    public void Configure(EntityTypeBuilder<SalesDeliveryLine> b)
    {
        b.ToTable("delivery_line", Schemas.SalesOrders);
        b.HasKey(x => x.Id);
        b.Property(x => x.Quantity).HasPrecision(19, 4);
    }
}
