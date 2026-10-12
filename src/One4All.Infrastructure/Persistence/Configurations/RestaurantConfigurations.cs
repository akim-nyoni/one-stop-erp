using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using One4All.Core.Restaurant;

namespace One4All.Infrastructure.Persistence.Configurations;

internal sealed class MenuItemConfiguration : IEntityTypeConfiguration<MenuItem>
{
    public void Configure(EntityTypeBuilder<MenuItem> b)
    {
        b.ToTable("menu_item", Schemas.Restaurant);
        b.Property(x => x.PrepStation).HasMaxLength(30);
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
        b.HasMany(x => x.Prices).WithOne().HasForeignKey(x => x.MenuItemId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.Prices).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.HasIndex(x => new { x.CompanyId, x.Code }).IsUnique().HasFilter("is_deleted = false");
        b.HasQueryFilter(One4AllDbContext.SoftDeleteFilter, x => !x.IsDeleted);
    }
}

internal sealed class MenuItemPriceConfiguration : IEntityTypeConfiguration<MenuItemPrice>
{
    public void Configure(EntityTypeBuilder<MenuItemPrice> b)
    {
        b.ToTable("menu_item_price", Schemas.Restaurant);
        b.HasKey(x => x.Id);
        b.Property(x => x.PriceLevel).HasMaxLength(30);
        b.Property(x => x.Price).HasPrecision(19, 4);
        b.HasIndex(x => new { x.MenuItemId, x.PriceLevel }).IsUnique();
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
        b.HasIndex(x => x.GroupId);
        b.HasIndex(x => x.SectionId);
        b.HasQueryFilter(One4AllDbContext.SoftDeleteFilter, x => !x.IsDeleted);
    }
}

internal sealed class RestaurantBillConfiguration : IEntityTypeConfiguration<RestaurantBill>
{
    public void Configure(EntityTypeBuilder<RestaurantBill> b)
    {
        b.ToTable("bill", Schemas.Restaurant);
        b.HasKey(x => x.Id);
        b.Property(x => x.Net).HasPrecision(19, 4);
        b.Property(x => x.Tax).HasPrecision(19, 4);
        b.Property(x => x.ServiceCharge).HasPrecision(19, 4);
        b.Property(x => x.Tip).HasPrecision(19, 4);
        b.Property(x => x.Discount).HasPrecision(19, 4);
        b.Property(x => x.Gross).HasPrecision(19, 4);
        b.Property(x => x.CostOfSales).HasPrecision(19, 4);
        b.Property(x => x.RevenueJournalNumber).HasMaxLength(30);
        b.Property(x => x.ReversalJournalNumber).HasMaxLength(30);
        b.HasMany(x => x.Postings).WithOne().HasForeignKey(x => x.RestaurantBillId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.Postings).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.HasMany(x => x.Issues).WithOne().HasForeignKey(x => x.RestaurantBillId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.Issues).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.HasIndex(x => new { x.CompanyId, x.Date });
        b.HasIndex(x => x.TableOrderId);
    }
}

internal sealed class RestaurantBillPostingConfiguration : IEntityTypeConfiguration<RestaurantBillPosting>
{
    public void Configure(EntityTypeBuilder<RestaurantBillPosting> b)
    {
        b.ToTable("bill_posting", Schemas.Restaurant);
        b.HasKey(x => x.Id);
        b.Property(x => x.AccountCode).HasMaxLength(8);
        b.Property(x => x.Amount).HasPrecision(19, 4);
        b.HasIndex(x => x.RestaurantBillId);
    }
}

internal sealed class RestaurantBillIssueConfiguration : IEntityTypeConfiguration<RestaurantBillIssue>
{
    public void Configure(EntityTypeBuilder<RestaurantBillIssue> b)
    {
        b.ToTable("bill_issue", Schemas.Restaurant);
        b.HasKey(x => x.Id);
        b.HasIndex(x => x.RestaurantBillId);
    }
}

internal sealed class WastageRecordConfiguration : IEntityTypeConfiguration<WastageRecord>
{
    public void Configure(EntityTypeBuilder<WastageRecord> b)
    {
        b.ToTable("wastage", Schemas.Restaurant);
        b.HasKey(x => x.Id);
        b.Property(x => x.Quantity).HasPrecision(19, 4);
        b.Property(x => x.Value).HasPrecision(19, 4);
        b.Property(x => x.Reason).HasMaxLength(200);
        b.Property(x => x.WastageAccountCode).HasMaxLength(8);
        b.HasIndex(x => new { x.CompanyId, x.Date });
    }
}

internal sealed class RestaurantPolicyConfiguration : IEntityTypeConfiguration<RestaurantPolicy>
{
    public void Configure(EntityTypeBuilder<RestaurantPolicy> b)
    {
        b.ToTable("policy", Schemas.Restaurant);
        b.HasKey(x => x.Id);
        b.Property(x => x.AutoGratuityPercent).HasPrecision(9, 4);
        b.Property(x => x.ServiceChargeAccountCode).HasMaxLength(8);
        b.HasIndex(x => x.CompanyId).IsUnique();
    }
}

internal sealed class FloorSectionConfiguration : IEntityTypeConfiguration<FloorSection>
{
    public void Configure(EntityTypeBuilder<FloorSection> b)
    {
        b.ToTable("floor_section", Schemas.Restaurant);
        b.HasKey(x => x.Id);
        b.Property(x => x.Code).HasMaxLength(30);
        b.Property(x => x.Name).HasMaxLength(120);
        b.Property(x => x.Description).HasMaxLength(400);
        b.HasIndex(x => new { x.CompanyId, x.Code }).IsUnique().HasFilter("is_deleted = false");
        b.HasQueryFilter(One4AllDbContext.SoftDeleteFilter, x => !x.IsDeleted);
    }
}

internal sealed class TableGroupConfiguration : IEntityTypeConfiguration<TableGroup>
{
    public void Configure(EntityTypeBuilder<TableGroup> b)
    {
        b.ToTable("table_group", Schemas.Restaurant);
        b.HasKey(x => x.Id);
        b.Property(x => x.Label).HasMaxLength(120);
        b.Property(x => x.Status).HasConversion<int>();
        b.HasIndex(x => new { x.CompanyId, x.Status });
    }
}

internal sealed class WaitlistEntryConfiguration : IEntityTypeConfiguration<WaitlistEntry>
{
    public void Configure(EntityTypeBuilder<WaitlistEntry> b)
    {
        b.ToTable("waitlist", Schemas.Restaurant);
        b.HasKey(x => x.Id);
        b.Property(x => x.GuestName).HasMaxLength(120);
        b.Property(x => x.GuestPhone).HasMaxLength(40);
        b.Property(x => x.Notes).HasMaxLength(400);
        b.Property(x => x.Status).HasConversion<int>();
        b.HasIndex(x => new { x.CompanyId, x.Status, x.AddedOn });
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
        b.Property(x => x.OrderType).HasConversion<int>();
        b.Property(x => x.CustomerName).HasMaxLength(120);
        b.Property(x => x.CustomerPhone).HasMaxLength(40);
        b.Property(x => x.DeliveryAddress).HasMaxLength(400);
        b.Property(x => x.DeliveryStatus).HasConversion<int>();
        b.Property(x => x.JournalNumber).HasMaxLength(30);
        b.Property(x => x.SpendLimit).HasPrecision(19, 4);
        b.Property(x => x.PartySize);
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
        b.Property(x => x.Course).HasConversion<int>();
        b.Property(x => x.SeatNumber);
        b.Property(x => x.Quantity).HasPrecision(19, 4);
        b.Property(x => x.UnitPrice).HasPrecision(19, 4);
        b.Property(x => x.BilledQuantity).HasPrecision(19, 4);
        b.Property(x => x.RevenueAccountCode).HasMaxLength(8);
        b.Property(x => x.CostOfSalesAccountCode).HasMaxLength(8);
        b.Property(x => x.VoidReason).HasMaxLength(200);
        b.Property(x => x.CompReason).HasMaxLength(200);
        b.Property(x => x.AuthorizedByUserId);
        b.Ignore(x => x.LineNet);
        b.Ignore(x => x.RemainingQuantity);
        b.Ignore(x => x.ModifierDelta);
        b.Ignore(x => x.EffectiveUnitPrice);
        b.HasMany(x => x.Modifiers).WithOne().HasForeignKey(x => x.TableOrderLineId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.Modifiers).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class OrderLineModifierConfiguration : IEntityTypeConfiguration<OrderLineModifier>
{
    public void Configure(EntityTypeBuilder<OrderLineModifier> b)
    {
        b.ToTable("order_line_modifier", Schemas.Restaurant);
        b.HasKey(x => x.Id);
        b.Property(x => x.Description).HasMaxLength(120);
        b.Property(x => x.PriceDelta).HasPrecision(19, 4);
        b.Property(x => x.IngredientQuantity).HasPrecision(19, 4);
        b.HasIndex(x => x.TableOrderLineId);
    }
}

internal sealed class KitchenTicketConfiguration : IEntityTypeConfiguration<KitchenTicket>
{
    public void Configure(EntityTypeBuilder<KitchenTicket> b)
    {
        b.ToTable("kitchen_ticket", Schemas.Restaurant);
        b.Property(x => x.Station).HasMaxLength(30);
        b.HasKey(x => x.Id);
        b.Property(x => x.TicketNumber).HasMaxLength(40);
        b.Property(x => x.Course).HasConversion<int>();
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

internal sealed class KitchenStationPrinterConfiguration : IEntityTypeConfiguration<KitchenStationPrinter>
{
    public void Configure(EntityTypeBuilder<KitchenStationPrinter> b)
    {
        b.ToTable("station_printer", Schemas.Restaurant);
        b.HasKey(x => x.Id);
        b.Property(x => x.Station).HasMaxLength(30);
        b.Property(x => x.PrinterName).HasMaxLength(200);
        b.Property(x => x.Host).HasMaxLength(200);
        b.HasIndex(x => new { x.CompanyId, x.Station }).IsUnique();
    }
}
