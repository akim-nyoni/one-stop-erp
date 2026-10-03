using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using One4All.Core.Banking;

namespace One4All.Infrastructure.Persistence.Configurations;

/// <summary>Maps a bank rule (<c>cb.bank_rule</c>).</summary>
internal sealed class BankRuleConfiguration : IEntityTypeConfiguration<BankRule>
{
    public void Configure(EntityTypeBuilder<BankRule> b)
    {
        b.ToTable("bank_rule", Schemas.CashBook);
        b.HasKey(x => x.Id);
        b.Property(x => x.Name).HasMaxLength(120);
        b.Property(x => x.DescriptionContains).HasMaxLength(200);
        b.Property(x => x.AccountCode).HasMaxLength(20);
        b.Property(x => x.Narrative).HasMaxLength(200);
        b.Property(x => x.Direction).HasConversion<int>();
        b.Property(x => x.MinAmount).HasPrecision(19, 4);
        b.Property(x => x.MaxAmount).HasPrecision(19, 4);
        b.HasIndex(x => new { x.CompanyId, x.IsActive });
    }
}
