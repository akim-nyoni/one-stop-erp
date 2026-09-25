using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using One4All.Core.Payroll;

namespace One4All.Infrastructure.Persistence.Configurations;

internal sealed class EmployeeConfiguration : IEntityTypeConfiguration<Employee>
{
    public void Configure(EntityTypeBuilder<Employee> b)
    {
        b.ToTable("employee", Schemas.HumanResources);
        b.HasKey(x => x.Id);
        b.Property(x => x.EmployeeNumber).HasMaxLength(30);
        b.Property(x => x.FullName).HasMaxLength(200);
        b.Property(x => x.Department).HasMaxLength(30);
        b.Property(x => x.CostCentre).HasMaxLength(30);
        b.Property(x => x.Status).HasConversion<int>();
        b.HasMany(x => x.PayElements).WithOne().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.PayElements).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.HasIndex(x => new { x.CompanyId, x.EmployeeNumber }).IsUnique().HasFilter("is_deleted = false");
        b.HasQueryFilter(x => !x.IsDeleted);
    }
}

internal sealed class EmployeePayElementConfiguration : IEntityTypeConfiguration<EmployeePayElement>
{
    public void Configure(EntityTypeBuilder<EmployeePayElement> b)
    {
        b.ToTable("employee_pay_element", Schemas.HumanResources);
        b.HasKey(x => x.Id);
        b.Property(x => x.OverrideAmount).HasPrecision(19, 4);
        b.Property(x => x.OverrideRate).HasPrecision(19, 6);
        b.Property(x => x.Units).HasPrecision(19, 4);
        b.HasIndex(x => new { x.EmployeeId, x.PayElementId });
    }
}

internal sealed class PayElementConfiguration : IEntityTypeConfiguration<PayElement>
{
    public void Configure(EntityTypeBuilder<PayElement> b)
    {
        b.ToTable("element", Schemas.Payroll);
        b.HasKey(x => x.Id);
        b.Property(x => x.Code).HasMaxLength(30);
        b.Property(x => x.Name).HasMaxLength(120);
        b.Property(x => x.Type).HasConversion<int>();
        b.Property(x => x.Basis).HasConversion<int>();
        b.Property(x => x.Rate).HasPrecision(19, 6);
        b.Property(x => x.Formula).HasMaxLength(1000);
        b.Property(x => x.AccountCode).HasMaxLength(8);
        b.HasIndex(x => new { x.CompanyId, x.Code }).IsUnique().HasFilter("is_deleted = false");
        b.HasQueryFilter(x => !x.IsDeleted);
    }
}

internal sealed class PayslipConfiguration : IEntityTypeConfiguration<Payslip>
{
    public void Configure(EntityTypeBuilder<Payslip> b)
    {
        b.ToTable("payslip", Schemas.Payroll);
        b.HasKey(x => x.Id);
        b.Property(x => x.PeriodLabel).HasMaxLength(40);
        b.HasMany(x => x.Lines).WithOne().HasForeignKey(x => x.PayslipId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.HasIndex(x => new { x.CompanyId, x.EmployeeId, x.PeriodLabel });
    }
}

internal sealed class PayslipLineConfiguration : IEntityTypeConfiguration<PayslipLine>
{
    public void Configure(EntityTypeBuilder<PayslipLine> b)
    {
        b.ToTable("payslip_line", Schemas.Payroll);
        b.HasKey(x => x.Id);
        b.Property(x => x.ElementCode).HasMaxLength(30);
        b.Property(x => x.Description).HasMaxLength(120);
        b.Property(x => x.Type).HasConversion<int>();
    }
}

internal sealed class EmployeeLoanConfiguration : IEntityTypeConfiguration<EmployeeLoan>
{
    public void Configure(EntityTypeBuilder<EmployeeLoan> b)
    {
        b.ToTable("employee_loan", Schemas.Payroll);
        b.HasKey(x => x.Id);
        b.Property(x => x.Reference).HasMaxLength(40);
        b.Property(x => x.Principal).HasPrecision(19, 4);
        b.Property(x => x.Installment).HasPrecision(19, 4);
        b.Property(x => x.Outstanding).HasPrecision(19, 4);
        b.Property(x => x.LoanReceivableAccountCode).HasMaxLength(8);
        b.Property(x => x.Status).HasConversion<int>();
        b.HasIndex(x => new { x.CompanyId, x.EmployeeId, x.Status });
    }
}
