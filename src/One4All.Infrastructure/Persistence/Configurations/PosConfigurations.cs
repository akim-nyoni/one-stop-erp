using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using One4All.Core.Pos;

namespace One4All.Infrastructure.Persistence.Configurations;

internal sealed class PosTerminalConfiguration : IEntityTypeConfiguration<PosTerminal>
{
    public void Configure(EntityTypeBuilder<PosTerminal> b)
    {
        b.ToTable("terminal", Schemas.PointOfSale);
        b.HasKey(x => x.Id);
        b.Property(x => x.Code).HasMaxLength(20);
        b.Property(x => x.Name).HasMaxLength(120);
        b.Property(x => x.Profile).HasConversion<int>();
        b.Property(x => x.FunctionKeys).HasMaxLength(400);
        b.HasIndex(x => new { x.CompanyId, x.Code }).IsUnique().HasFilter("is_deleted = false");
        b.HasQueryFilter(One4AllDbContext.SoftDeleteFilter, x => !x.IsDeleted);
    }
}

internal sealed class TillSessionConfiguration : IEntityTypeConfiguration<TillSession>
{
    public void Configure(EntityTypeBuilder<TillSession> b)
    {
        b.ToTable("till_session", Schemas.PointOfSale);
        b.HasKey(x => x.Id);
        b.Property(x => x.OpeningFloat).HasPrecision(19, 4);
        b.Property(x => x.OfflineKey).HasMaxLength(40);
        b.HasIndex(x => new { x.CompanyId, x.OfflineKey }).IsUnique().HasFilter("offline_key IS NOT NULL");
        b.Property(x => x.Status).HasConversion<int>();
        b.HasIndex(x => new { x.TerminalId, x.ZReadNumber });
    }
}

internal sealed class SaleConfiguration : IEntityTypeConfiguration<Sale>
{
    public void Configure(EntityTypeBuilder<Sale> b)
    {
        b.ToTable("sale", Schemas.PointOfSale);
        b.Property(x => x.FiscalStatus).HasConversion<int>();
        b.Property(x => x.Tip).HasPrecision(19, 4);
        b.Property(x => x.FiscalCode).HasMaxLength(60);
        b.Property(x => x.FiscalVerification).HasMaxLength(500);
        b.Property(x => x.FiscalError).HasMaxLength(300);
        b.HasIndex(x => new { x.CompanyId, x.FiscalStatus });
        b.HasKey(x => x.Id);
        b.Property(x => x.SaleNumber).HasMaxLength(40);
        b.Property(x => x.PrescriptionReference).HasMaxLength(60);
        b.Property(x => x.BaseCurrency).HasMaxLength(3);
        b.Property(x => x.Status).HasConversion<int>();
        b.Ignore(x => x.TotalNet);
        b.Ignore(x => x.TotalTax);
        b.Ignore(x => x.TotalGross);
        b.Ignore(x => x.NetTenderedBase);
        b.HasMany(x => x.Lines).WithOne().HasForeignKey(x => x.SaleId).OnDelete(DeleteBehavior.Cascade);
        b.HasMany(x => x.Tenders).WithOne().HasForeignKey(x => x.SaleId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.Navigation(x => x.Tenders).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.HasIndex(x => new { x.CompanyId, x.SaleNumber }).IsUnique();
        b.HasIndex(x => x.TillSessionId);
    }
}

internal sealed class SaleLineConfiguration : IEntityTypeConfiguration<SaleLine>
{
    public void Configure(EntityTypeBuilder<SaleLine> b)
    {
        b.ToTable("sale_line", Schemas.PointOfSale);
        b.HasKey(x => x.Id);
        b.Property(x => x.PromotionDiscount).HasPrecision(19, 4);
        b.Property(x => x.DiscountAccountCode).HasMaxLength(8);
        b.Property(x => x.PromotionCode).HasMaxLength(60);
        b.Property(x => x.GiftCardNumber).HasMaxLength(40);
        b.Property(x => x.Note).HasMaxLength(200);
        b.Property(x => x.SerialNumber).HasMaxLength(60);
        b.Property(x => x.Description).HasMaxLength(200);
        b.Property(x => x.RevenueAccountCode).HasMaxLength(8);
        b.Property(x => x.Quantity).HasPrecision(19, 4);
        b.Property(x => x.UnitPrice).HasPrecision(19, 6);
        b.Property(x => x.LineNet).HasPrecision(19, 4);
        b.Property(x => x.TaxAmount).HasPrecision(19, 4);
    }
}

internal sealed class SaleTenderConfiguration : IEntityTypeConfiguration<SaleTender>
{
    public void Configure(EntityTypeBuilder<SaleTender> b)
    {
        b.ToTable("sale_tender", Schemas.PointOfSale);
        b.HasIndex(x => x.StaffEmployeeId);
        b.HasIndex(x => x.VoucherNumber);
        b.Property(x => x.VoucherNumber).HasMaxLength(40);
        b.HasKey(x => x.Id);
        b.Property(x => x.TenderType).HasConversion<int>();
        b.Property(x => x.Currency).HasMaxLength(3);
        b.Property(x => x.Amount).HasPrecision(19, 4);
        b.Property(x => x.ExchangeRate).HasPrecision(19, 6);
        b.Property(x => x.BaseAmount).HasPrecision(19, 4);
        b.Property(x => x.AccountCode).HasMaxLength(8);
    }
}

internal sealed class PosEventConfiguration : IEntityTypeConfiguration<PosEvent>
{
    public void Configure(EntityTypeBuilder<PosEvent> b)
    {
        b.ToTable("event", Schemas.PointOfSale);
        b.HasKey(x => x.Id);
        b.Property(x => x.Kind).HasConversion<int>();
        b.Property(x => x.SaleNumber).HasMaxLength(40);
        b.Property(x => x.Description).HasMaxLength(200);
        b.Property(x => x.Amount).HasPrecision(19, 4);
        b.Property(x => x.OriginalAmount).HasPrecision(19, 4);
        b.Property(x => x.Quantity).HasPrecision(19, 4);
        b.Property(x => x.Reason).HasMaxLength(200);
        b.Property(x => x.CustomerReference).HasMaxLength(60);
        b.Property(x => x.Reference).HasMaxLength(60);
        b.Property(x => x.Outcome).HasMaxLength(200);
        b.Property(x => x.FiscalCode).HasMaxLength(60);
        b.Property(x => x.FiscalVerification).HasMaxLength(400);
        b.HasIndex(x => new { x.CompanyId, x.Kind, x.EventDate });
        b.HasIndex(x => new { x.CompanyId, x.SaleNumber });
    }
}

internal sealed class PromotionConfiguration : IEntityTypeConfiguration<Promotion>
{
    public void Configure(EntityTypeBuilder<Promotion> b)
    {
        b.Property(x => x.CustomerTypes).HasMaxLength(200);
        b.ToTable("promotion", Schemas.PointOfSale);
        b.HasKey(x => x.Id);
        b.Property(x => x.Code).HasMaxLength(30);
        b.Property(x => x.Name).HasMaxLength(100);
        b.Property(x => x.Kind).HasConversion<int>();
        b.Property(x => x.DiscountAccountCode).HasMaxLength(8);
        b.Property(x => x.Price).HasPrecision(19, 4);
        b.Property(x => x.Percent).HasPrecision(9, 4);
        b.Property(x => x.Category).HasMaxLength(60);
        b.Property(x => x.CouponCode).HasMaxLength(40);
        b.HasMany(x => x.Items).WithOne().HasForeignKey(x => x.PromotionId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.Items).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.HasIndex(x => new { x.CompanyId, x.Code }).IsUnique();
    }
}

internal sealed class PromotionItemConfiguration : IEntityTypeConfiguration<PromotionItem>
{
    public void Configure(EntityTypeBuilder<PromotionItem> b)
    {
        b.ToTable("promotion_item", Schemas.PointOfSale);
        b.HasKey(x => x.Id);
    }
}

internal sealed class LoyaltySchemeConfiguration : IEntityTypeConfiguration<LoyaltyScheme>
{
    public void Configure(EntityTypeBuilder<LoyaltyScheme> b)
    {
        b.ToTable("loyalty_scheme", Schemas.PointOfSale);
        b.HasKey(x => x.Id);
        b.Property(x => x.PointsPerUnit).HasPrecision(19, 6);
        b.Property(x => x.ValuePerPoint).HasPrecision(19, 6);
        b.Property(x => x.LiabilityAccountCode).HasMaxLength(8);
        b.Property(x => x.ExpenseAccountCode).HasMaxLength(8);
        b.HasMany(x => x.Tiers).WithOne().HasForeignKey(x => x.LoyaltySchemeId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.Tiers).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.HasIndex(x => x.CompanyId).IsUnique();
    }
}

internal sealed class LoyaltyTierConfiguration : IEntityTypeConfiguration<LoyaltyTier>
{
    public void Configure(EntityTypeBuilder<LoyaltyTier> b)
    {
        b.ToTable("loyalty_tier", Schemas.PointOfSale);
        b.HasKey(x => x.Id);
        b.Property(x => x.Name).HasMaxLength(40);
        b.Property(x => x.MinPoints).HasPrecision(19, 2);
        b.Property(x => x.Multiplier).HasPrecision(9, 4);
    }
}

internal sealed class LoyaltyMemberConfiguration : IEntityTypeConfiguration<LoyaltyMember>
{
    public void Configure(EntityTypeBuilder<LoyaltyMember> b)
    {
        b.ToTable("loyalty_member", Schemas.PointOfSale);
        b.HasKey(x => x.Id);
        b.Property(x => x.MemberNumber).HasMaxLength(30);
        b.Property(x => x.Name).HasMaxLength(120);
        b.HasIndex(x => new { x.CompanyId, x.MemberNumber }).IsUnique();
    }
}

internal sealed class LoyaltyMovementConfiguration : IEntityTypeConfiguration<LoyaltyMovement>
{
    public void Configure(EntityTypeBuilder<LoyaltyMovement> b)
    {
        b.ToTable("loyalty_movement", Schemas.PointOfSale);
        b.HasKey(x => x.Id);
        b.Property(x => x.Kind).HasConversion<int>();
        b.Property(x => x.Points).HasPrecision(19, 2);
        b.Property(x => x.Value).HasPrecision(19, 4);
        b.Property(x => x.Reference).HasMaxLength(60);
        b.HasIndex(x => new { x.MemberId, x.Date });
    }
}

internal sealed class TillCashMovementConfiguration : IEntityTypeConfiguration<TillCashMovement>
{
    public void Configure(EntityTypeBuilder<TillCashMovement> b)
    {
        b.ToTable("till_cash_movement", Schemas.PointOfSale);
        b.HasKey(x => x.Id);
        b.Property(x => x.Kind).HasConversion<int>();
        b.Property(x => x.Amount).HasPrecision(19, 4);
        b.Property(x => x.AccountCode).HasMaxLength(8);
        b.Property(x => x.Reason).HasMaxLength(200);
        b.Property(x => x.JournalNumber).HasMaxLength(40);
        b.Ignore(x => x.DrawerEffect);
        b.HasIndex(x => x.TillSessionId);
    }
}

internal sealed class ParkedSaleConfiguration : IEntityTypeConfiguration<ParkedSale>
{
    public void Configure(EntityTypeBuilder<ParkedSale> b)
    {
        b.ToTable("parked_sale", Schemas.PointOfSale);
        b.HasKey(x => x.Id);
        b.Property(x => x.Tag).HasMaxLength(80);
        b.Property(x => x.LinesJson).HasColumnType("jsonb");
        b.Property(x => x.Total).HasPrecision(19, 4);
        b.Property(x => x.Status).HasConversion<int>();
        b.Property(x => x.DiscardReason).HasMaxLength(200);
        b.HasIndex(x => new { x.CompanyId, x.Status });
    }
}

internal sealed class TillCashCountConfiguration : IEntityTypeConfiguration<TillCashCount>
{
    public void Configure(EntityTypeBuilder<TillCashCount> b)
    {
        b.ToTable("till_cash_count", Schemas.PointOfSale);
        b.HasKey(x => x.Id);
        b.Property(x => x.Currency).HasMaxLength(3);
        b.Property(x => x.Amount).HasPrecision(19, 4);
        b.HasIndex(x => new { x.TillSessionId, x.Currency }).IsUnique();
    }
}
