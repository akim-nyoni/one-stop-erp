using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using One4All.Core.Accounting;

namespace One4All.Infrastructure.Persistence.Configurations;

internal sealed class TaxCodeConfiguration : IEntityTypeConfiguration<TaxCode>
{
    public void Configure(EntityTypeBuilder<TaxCode> b)
    {
        b.ToTable("tax_code", Schemas.System);
        b.HasKey(x => x.Id);
        b.Property(x => x.Code).HasMaxLength(20);
        b.Property(x => x.Name).HasMaxLength(120);
        b.Property(x => x.Type).HasConversion<int>();
        b.Property(x => x.AppliesTo).HasConversion<int>();
        b.Property(x => x.OutputAccountCode).HasMaxLength(8);
        b.Property(x => x.InputAccountCode).HasMaxLength(8);
        b.Property(x => x.RecoverablePercent).HasPrecision(9, 4);
        b.Property(x => x.Jurisdiction).HasMaxLength(60);
        b.Property(x => x.ReturnBox).HasMaxLength(40);
        b.HasIndex(x => new { x.CompanyId, x.Code }).IsUnique().HasFilter("is_deleted = false");

        b.HasMany(x => x.Rates).WithOne().HasForeignKey(x => x.TaxCodeId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.Rates).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.HasQueryFilter(x => !x.IsDeleted);
    }
}

internal sealed class TaxRateConfiguration : IEntityTypeConfiguration<TaxRate>
{
    public void Configure(EntityTypeBuilder<TaxRate> b)
    {
        b.ToTable("tax_rate", Schemas.System);
        b.HasKey(x => x.Id);
        b.Property(x => x.RatePercent).HasPrecision(9, 4);
        b.HasIndex(x => new { x.TaxCodeId, x.EffectiveFrom }).IsUnique();
    }
}
