using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using One4All.Core.Expenses;

namespace One4All.Infrastructure.Persistence.Configurations;

internal sealed class ExpenseClaimConfiguration : IEntityTypeConfiguration<ExpenseClaim>
{
    public void Configure(EntityTypeBuilder<ExpenseClaim> b)
    {
        b.ToTable("claim", Schemas.Expenses);
        b.HasKey(x => x.Id);
        b.Property(x => x.Reference).HasMaxLength(40);
        b.Property(x => x.Status).HasConversion<int>();
        b.Property(x => x.JournalNumber).HasMaxLength(40);
        b.Ignore(x => x.Total);
        b.HasMany(x => x.Lines).WithOne().HasForeignKey(x => x.ExpenseClaimId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.HasIndex(x => new { x.CompanyId, x.EmployeeId, x.Status });
    }
}

internal sealed class ExpenseClaimLineConfiguration : IEntityTypeConfiguration<ExpenseClaimLine>
{
    public void Configure(EntityTypeBuilder<ExpenseClaimLine> b)
    {
        b.ToTable("claim_line", Schemas.Expenses);
        b.HasKey(x => x.Id);
        b.Property(x => x.Description).HasMaxLength(200);
        b.Property(x => x.ExpenseAccountCode).HasMaxLength(8);
        b.Property(x => x.Amount).HasPrecision(19, 4);
        b.HasIndex(x => x.ExpenseClaimId);
    }
}
