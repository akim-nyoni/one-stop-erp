using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using One4All.Core.Vouchers;

namespace One4All.Infrastructure.Persistence.Configurations;

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
