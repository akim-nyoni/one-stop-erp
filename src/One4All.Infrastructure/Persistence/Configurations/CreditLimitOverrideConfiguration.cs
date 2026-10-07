using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using One4All.Core.Trading;

namespace One4All.Infrastructure.Persistence.Configurations;

internal sealed class CreditLimitOverrideConfiguration : IEntityTypeConfiguration<CreditLimitOverride>
{
    public void Configure(EntityTypeBuilder<CreditLimitOverride> b)
    {
        b.ToTable("credit_limit_override", Schemas.AccountsReceivable);
        b.HasKey(x => x.Id);
        b.Property(x => x.DocumentNumber).HasMaxLength(40);
        b.Property(x => x.CreditLimit).HasPrecision(19, 4);
        b.Property(x => x.BalanceAfter).HasPrecision(19, 4);
        b.HasIndex(x => new { x.CompanyId, x.CustomerId });
    }
}
