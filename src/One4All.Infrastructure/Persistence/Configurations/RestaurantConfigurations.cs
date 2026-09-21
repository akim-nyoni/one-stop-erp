using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using One4All.Core.Restaurant;

namespace One4All.Infrastructure.Persistence.Configurations;

internal sealed class MenuItemConfiguration : IEntityTypeConfiguration<MenuItem>
{
    public void Configure(EntityTypeBuilder<MenuItem> b)
    {
        b.ToTable("menu_item", Schemas.Restaurant);
        b.HasKey(x => x.Id);
        b.Property(x => x.Code).HasMaxLength(30);
        b.Property(x => x.Name).HasMaxLength(120);
        b.Property(x => x.Category).HasMaxLength(60);
        b.Property(x => x.RevenueAccountCode).HasMaxLength(8);
        b.Property(x => x.CostOfSalesAccountCode).HasMaxLength(8);
        b.Property(x => x.TaxCode).HasMaxLength(20);
        b.Property(x => x.Price).HasPrecision(19, 4);
        b.HasMany(x => x.Recipe).WithOne().HasForeignKey(x => x.MenuItemId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.Recipe).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.HasIndex(x => new { x.CompanyId, x.Code }).IsUnique().HasFilter("is_deleted = false");
        b.HasQueryFilter(x => !x.IsDeleted);
    }
}

internal sealed class RecipeLineConfiguration : IEntityTypeConfiguration<RecipeLine>
{
    public void Configure(EntityTypeBuilder<RecipeLine> b)
    {
        b.ToTable("recipe_line", Schemas.Restaurant);
        b.HasKey(x => x.Id);
        b.Property(x => x.Quantity).HasPrecision(19, 4);
        b.HasIndex(x => x.MenuItemId);
    }
}

internal sealed class RestaurantTableConfiguration : IEntityTypeConfiguration<RestaurantTable>
{
    public void Configure(EntityTypeBuilder<RestaurantTable> b)
    {
        b.ToTable("table", Schemas.Restaurant);
        b.HasKey(x => x.Id);
        b.Property(x => x.TableNumber).HasMaxLength(20);
        b.Property(x => x.Status).HasConversion<int>();
        b.HasIndex(x => new { x.CompanyId, x.TableNumber }).IsUnique().HasFilter("is_deleted = false");
        b.HasQueryFilter(x => !x.IsDeleted);
    }
}

internal sealed class TableOrderConfiguration : IEntityTypeConfiguration<TableOrder>
{
    public void Configure(EntityTypeBuilder<TableOrder> b)
    {
        b.ToTable("order", Schemas.Restaurant);
        b.HasKey(x => x.Id);
        b.Property(x => x.OrderNumber).HasMaxLength(40);
        b.Property(x => x.Status).HasConversion<int>();
        b.Property(x => x.JournalNumber).HasMaxLength(30);
        b.Ignore(x => x.NetTotal);
        b.Ignore(x => x.TaxTotal);
        b.Ignore(x => x.GrossTotal);
        b.HasMany(x => x.Lines).WithOne().HasForeignKey(x => x.TableOrderId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.HasIndex(x => new { x.CompanyId, x.OrderNumber }).IsUnique();
    }
}

internal sealed class TableOrderLineConfiguration : IEntityTypeConfiguration<TableOrderLine>
{
    public void Configure(EntityTypeBuilder<TableOrderLine> b)
    {
        b.ToTable("order_line", Schemas.Restaurant);
        b.HasKey(x => x.Id);
        b.Property(x => x.Description).HasMaxLength(120);
        b.Property(x => x.Quantity).HasPrecision(19, 4);
        b.Property(x => x.UnitPrice).HasPrecision(19, 4);
        b.Property(x => x.BilledQuantity).HasPrecision(19, 4);
        b.Property(x => x.RevenueAccountCode).HasMaxLength(8);
        b.Property(x => x.CostOfSalesAccountCode).HasMaxLength(8);
        b.Ignore(x => x.LineNet);
        b.Ignore(x => x.RemainingQuantity);
    }
}

internal sealed class KitchenTicketConfiguration : IEntityTypeConfiguration<KitchenTicket>
{
    public void Configure(EntityTypeBuilder<KitchenTicket> b)
    {
        b.ToTable("kitchen_ticket", Schemas.Restaurant);
        b.HasKey(x => x.Id);
        b.Property(x => x.TicketNumber).HasMaxLength(40);
        b.Property(x => x.Status).HasConversion<int>();
        b.HasMany(x => x.Lines).WithOne().HasForeignKey(x => x.KitchenTicketId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.HasIndex(x => new { x.CompanyId, x.Status });
    }
}

internal sealed class TipAllocationConfiguration : IEntityTypeConfiguration<TipAllocation>
{
    public void Configure(EntityTypeBuilder<TipAllocation> b)
    {
        b.ToTable("tip_allocation", Schemas.Restaurant);
        b.HasKey(x => x.Id);
        b.Property(x => x.TipsPayableAccountCode).HasMaxLength(8);
        b.Property(x => x.Status).HasConversion<int>();
        b.HasIndex(x => new { x.CompanyId, x.Status });
    }
}

internal sealed class TableReservationConfiguration : IEntityTypeConfiguration<TableReservation>
{
    public void Configure(EntityTypeBuilder<TableReservation> b)
    {
        b.ToTable("reservation", Schemas.Restaurant);
        b.HasKey(x => x.Id);
        b.Property(x => x.GuestName).HasMaxLength(120);
        b.Property(x => x.GuestPhone).HasMaxLength(40);
        b.Property(x => x.Notes).HasMaxLength(400);
        b.Property(x => x.CancellationReason).HasMaxLength(400);
        b.Property(x => x.Status).HasConversion<int>();
        b.HasIndex(x => new { x.CompanyId, x.TableId, x.ReservedFor });
        b.HasIndex(x => new { x.CompanyId, x.Status });
    }
}

internal sealed class KitchenTicketLineConfiguration : IEntityTypeConfiguration<KitchenTicketLine>
{
    public void Configure(EntityTypeBuilder<KitchenTicketLine> b)
    {
        b.ToTable("kitchen_ticket_line", Schemas.Restaurant);
        b.HasKey(x => x.Id);
        b.Property(x => x.Description).HasMaxLength(120);
        b.Property(x => x.Quantity).HasPrecision(19, 4);
    }
}
