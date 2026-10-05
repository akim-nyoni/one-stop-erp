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
        b.Property(x => x.SealHash).HasMaxLength(64);
        b.Ignore(x => x.IsSealed);
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
        b.Property(x => x.Basis).HasConversion<int>();
        b.Property(x => x.DriverCode).HasMaxLength(20);
        b.HasIndex(x => new { x.BudgetId, x.AccountCode, x.Period });
    }
}

internal sealed class BudgetVirementConfiguration : IEntityTypeConfiguration<BudgetVirement>
{
    public void Configure(EntityTypeBuilder<BudgetVirement> b)
    {
        b.ToTable("budget_virement", Schemas.Budget, t =>
            t.HasCheckConstraint("ck_budget_virement_reviewer_not_requester", "status <> 1 OR reviewed_by_user_id <> requested_by_user_id"));
        b.HasKey(x => x.Id);
        b.Property(x => x.FromAccountCode).HasMaxLength(8);
        b.Property(x => x.ToAccountCode).HasMaxLength(8);
        b.Property(x => x.Amount).HasPrecision(19, 4);
        b.Property(x => x.Justification).HasMaxLength(500);
        b.Property(x => x.RejectionReason).HasMaxLength(256);
        b.Property(x => x.Status).HasConversion<int>();
        b.HasIndex(x => new { x.CompanyId, x.BudgetId, x.Status });
    }
}

internal sealed class VarianceCommentConfiguration : IEntityTypeConfiguration<VarianceComment>
{
    public void Configure(EntityTypeBuilder<VarianceComment> b)
    {
        b.ToTable("variance_comment", Schemas.Budget);
        b.HasKey(x => x.Id);
        b.Property(x => x.AccountCode).HasMaxLength(8);
        b.Property(x => x.Comment).HasMaxLength(2000);
        b.HasIndex(x => new { x.CompanyId, x.FiscalYearId, x.AsAtPeriod, x.AccountCode }).IsUnique();
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

internal sealed class BudgetDriverConfiguration : IEntityTypeConfiguration<BudgetDriver>
{
    public void Configure(EntityTypeBuilder<BudgetDriver> b)
    {
        b.ToTable("driver", Schemas.Budget);
        b.HasKey(x => x.Id);
        b.Property(x => x.Code).HasMaxLength(20);
        b.Property(x => x.Name).HasMaxLength(120);
        b.Property(x => x.Unit).HasMaxLength(20);
        b.Property(x => x.Value).HasPrecision(19, 6);
        b.HasIndex(x => new { x.CompanyId, x.FiscalYearId, x.Code }).IsUnique();
    }
}

internal sealed class BudgetCeilingConfiguration : IEntityTypeConfiguration<BudgetCeiling>
{
    public void Configure(EntityTypeBuilder<BudgetCeiling> b)
    {
        b.ToTable("ceiling", Schemas.Budget);
        b.HasKey(x => x.Id);
        b.Property(x => x.Department).HasMaxLength(80);
        b.Property(x => x.Amount).HasPrecision(19, 4);
        b.HasIndex(x => new { x.CompanyId, x.FiscalYearId, x.Department }).IsUnique();
    }
}

internal sealed class DepartmentUnitConfiguration : IEntityTypeConfiguration<DepartmentUnit>
{
    public void Configure(EntityTypeBuilder<DepartmentUnit> b)
    {
        b.ToTable("department_unit", Schemas.Budget);
        b.HasKey(x => x.Id);
        b.Property(x => x.Department).HasMaxLength(80);
        b.Property(x => x.Division).HasMaxLength(80);
        b.Property(x => x.Directorate).HasMaxLength(80);
        b.Property(x => x.BudgetHolder).HasMaxLength(120);
        b.Property(x => x.BudgetHolderEmail).HasMaxLength(200);
        b.HasIndex(x => new { x.CompanyId, x.Department }).IsUnique();
    }
}
