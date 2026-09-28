using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using One4All.Core.Budgeting;

namespace One4All.Infrastructure.Persistence.Configurations;

internal sealed class BudgetConfiguration : IEntityTypeConfiguration<Budget>
{
    public void Configure(EntityTypeBuilder<Budget> b)
    {
        b.ToTable("budget", Schemas.Budget);
        b.HasKey(x => x.Id);
        b.Property(x => x.Label).HasMaxLength(120);
        b.Property(x => x.Type).HasConversion<int>();
        b.Property(x => x.Department).HasMaxLength(30);
        b.Property(x => x.Status).HasConversion<int>();
        b.Property(x => x.Reason).HasMaxLength(300);
        b.Ignore(x => x.AnnualTotal);
        b.HasMany(x => x.Lines).WithOne().HasForeignKey(x => x.BudgetId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.HasIndex(x => new { x.CompanyId, x.FiscalYearId, x.Type, x.Department, x.VersionNumber }).IsUnique();
        b.HasQueryFilter(x => !x.IsDeleted);
    }
}

internal sealed class BudgetLineConfiguration : IEntityTypeConfiguration<BudgetLine>
{
    public void Configure(EntityTypeBuilder<BudgetLine> b)
    {
        b.ToTable("budget_line", Schemas.Budget);
        b.HasKey(x => x.Id);
        b.Property(x => x.AccountCode).HasMaxLength(8);
        b.Property(x => x.CostCentre).HasMaxLength(20);
        b.HasIndex(x => new { x.BudgetId, x.AccountCode, x.Period });
    }
}

internal sealed class BudgetCommitmentConfiguration : IEntityTypeConfiguration<BudgetCommitment>
{
    public void Configure(EntityTypeBuilder<BudgetCommitment> b)
    {
        b.ToTable("commitment", Schemas.Budget);
        b.HasKey(x => x.Id);
        b.Property(x => x.Type).HasConversion<int>();
        b.Property(x => x.Department).HasMaxLength(30);
        b.Property(x => x.AccountCode).HasMaxLength(8);
        b.Property(x => x.Amount).HasPrecision(19, 4);
        b.Property(x => x.Reference).HasMaxLength(60);
        b.Property(x => x.Status).HasConversion<int>();
        b.HasIndex(x => new { x.CompanyId, x.FiscalYearId, x.Type, x.Department, x.AccountCode, x.Status });
    }
}
