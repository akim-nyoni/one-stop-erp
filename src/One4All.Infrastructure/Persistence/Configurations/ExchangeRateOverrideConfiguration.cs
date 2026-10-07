using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using One4All.Core.System;

namespace One4All.Infrastructure.Persistence.Configurations;

internal sealed class ExchangeRateOverrideConfiguration : IEntityTypeConfiguration<ExchangeRateOverride>
{
    public void Configure(EntityTypeBuilder<ExchangeRateOverride> b)
    {
        b.ToTable("rate_override", Schemas.System);
        b.HasKey(x => x.Id);
        b.Property(x => x.DocumentNumber).HasMaxLength(40);
        b.Property(x => x.Currency).HasMaxLength(3);
        b.Property(x => x.KeyedRate).HasPrecision(19, 8);
        b.Property(x => x.TableRate).HasPrecision(19, 8);
        b.HasIndex(x => new { x.CompanyId, x.Date });
    }
}
