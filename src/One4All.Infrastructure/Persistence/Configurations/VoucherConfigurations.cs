using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using One4All.Core.Vouchers;

namespace One4All.Infrastructure.Persistence.Configurations;

internal sealed class GiftCardTransactionConfiguration : IEntityTypeConfiguration<GiftCardTransaction>
{
    public void Configure(EntityTypeBuilder<GiftCardTransaction> b)
    {
        b.ToTable("gift_card_transaction", Schemas.Vouchers);
        b.HasKey(x => x.Id);
        b.Property(x => x.Event).HasConversion<int>();
        b.Property(x => x.Amount).HasPrecision(19, 4);
        b.Property(x => x.BalanceAfter).HasPrecision(19, 4);
        b.Property(x => x.Note).HasMaxLength(200);
        b.HasIndex(x => x.GiftCardId);
    }
}

internal sealed class AllowanceVoucherConfiguration : IEntityTypeConfiguration<AllowanceVoucher>
{
    public void Configure(EntityTypeBuilder<AllowanceVoucher> b)
    {
        b.ToTable("allowance_voucher", Schemas.Vouchers);
        b.HasKey(x => x.Id);
        b.Property(x => x.Serial).HasMaxLength(30);
        b.Property(x => x.Kind).HasConversion<int>();
        b.Property(x => x.FaceValue).HasPrecision(19, 4);
        b.Property(x => x.IssuedTo).HasMaxLength(120);
        b.Property(x => x.Status).HasConversion<int>();
        b.Property(x => x.SupplierInvoiceNumber).HasMaxLength(60);
        b.HasIndex(x => new { x.CompanyId, x.Serial }).IsUnique();
    }
}

internal sealed class PromoCampaignConfiguration : IEntityTypeConfiguration<PromoCampaign>
{
    public void Configure(EntityTypeBuilder<PromoCampaign> b)
    {
        b.ToTable("promo_campaign", Schemas.Vouchers);
        b.HasKey(x => x.Id);
        b.Property(x => x.Code).HasMaxLength(30);
        b.Property(x => x.Name).HasMaxLength(120);
        b.Property(x => x.DiscountType).HasConversion<int>();
        b.Property(x => x.DiscountValue).HasPrecision(19, 4);
        b.Property(x => x.MinimumSpend).HasPrecision(19, 4);
        b.HasMany(x => x.Codes).WithOne().HasForeignKey(x => x.PromoCampaignId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.Codes).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.HasIndex(x => new { x.CompanyId, x.Code }).IsUnique();
    }
}

internal sealed class PromoCodeConfiguration : IEntityTypeConfiguration<PromoCode>
{
    public void Configure(EntityTypeBuilder<PromoCode> b)
    {
        b.ToTable("promo_code", Schemas.Vouchers);
        b.HasKey(x => x.Id);
        b.Property(x => x.Code).HasMaxLength(40);
        b.HasIndex(x => x.Code);
    }
}

internal sealed class PromoRedemptionConfiguration : IEntityTypeConfiguration<PromoRedemption>
{
    public void Configure(EntityTypeBuilder<PromoRedemption> b)
    {
        b.ToTable("promo_redemption", Schemas.Vouchers);
        b.HasKey(x => x.Id);
        b.Property(x => x.Spend).HasPrecision(19, 4);
        b.Property(x => x.Discount).HasPrecision(19, 4);
        b.Property(x => x.Reference).HasMaxLength(60);
        b.HasIndex(x => new { x.PromoCampaignId, x.Date });
    }
}

internal sealed class GiftCardConfiguration : IEntityTypeConfiguration<GiftCard>
{
    public void Configure(EntityTypeBuilder<GiftCard> b)
    {
        b.ToTable("gift_card", Schemas.Vouchers);
        b.HasKey(x => x.Id);
        b.Property(x => x.CardNumber).HasMaxLength(40);
        b.Property(x => x.Currency).HasMaxLength(3);
        b.Property(x => x.OriginalValue).HasPrecision(19, 4);
        b.Property(x => x.Balance).HasPrecision(19, 4);
        b.Property(x => x.Status).HasConversion<int>();
        b.HasIndex(x => new { x.CompanyId, x.CardNumber }).IsUnique();
    }
}
