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
