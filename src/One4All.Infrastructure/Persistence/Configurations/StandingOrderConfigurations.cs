using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using One4All.Core.Banking;

namespace One4All.Infrastructure.Persistence.Configurations;

/// <summary>Maps standing orders (<c>cb.standing_order</c>).</summary>
internal sealed class StandingOrderConfiguration : IEntityTypeConfiguration<StandingOrder>
{
    public void Configure(EntityTypeBuilder<StandingOrder> b)
    {
        b.ToTable("standing_order", Schemas.CashBook);
        b.HasKey(x => x.Id);
        b.Property(x => x.Reference).HasMaxLength(30);
        b.Property(x => x.BankAccountCode).HasMaxLength(20);
        b.Property(x => x.DebitAccountCode).HasMaxLength(20);
        b.Property(x => x.Narrative).HasMaxLength(200);
        b.Property(x => x.Amount).HasPrecision(19, 4);
        b.Property(x => x.Frequency).HasConversion<int>();
        b.HasIndex(x => new { x.CompanyId, x.Reference }).IsUnique();
    }
}

/// <summary>Maps direct debit mandates (<c>cb.direct_debit_mandate</c>).</summary>
internal sealed class DirectDebitMandateConfiguration : IEntityTypeConfiguration<DirectDebitMandate>
{
    public void Configure(EntityTypeBuilder<DirectDebitMandate> b)
    {
        b.ToTable("direct_debit_mandate", Schemas.CashBook);
        b.HasKey(x => x.Id);
        b.Property(x => x.Reference).HasMaxLength(30);
        b.Property(x => x.BankAccountCode).HasMaxLength(20);
        b.Property(x => x.CustomerBankAccount).HasMaxLength(60);
        b.Property(x => x.FixedAmount).HasPrecision(19, 4);
        b.Property(x => x.MaximumAmount).HasPrecision(19, 4);
        b.Property(x => x.Frequency).HasConversion<int>();
        b.HasIndex(x => new { x.CompanyId, x.Reference }).IsUnique();
    }
}
