using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using One4All.Core.Accounting;

namespace One4All.Infrastructure.Persistence.Configurations;

internal sealed class PettyCashFundConfiguration : IEntityTypeConfiguration<PettyCashFund>
{
    public void Configure(EntityTypeBuilder<PettyCashFund> b)
    {
        b.ToTable("petty_cash_fund", Schemas.CashBook);
        b.HasKey(x => x.Id);
        b.Property(x => x.Code).HasMaxLength(20);
        b.Property(x => x.Name).HasMaxLength(120);
        b.Property(x => x.AccountCode).HasMaxLength(30);
        b.Property(x => x.BankAccountCode).HasMaxLength(30);
        b.Property(x => x.OverShortAccountCode).HasMaxLength(30);
        b.HasIndex(x => new { x.CompanyId, x.Code }).IsUnique().HasFilter("is_deleted = false");
        b.HasQueryFilter(One4AllDbContext.SoftDeleteFilter, x => !x.IsDeleted);
    }
}

internal sealed class PettyCashCountConfiguration : IEntityTypeConfiguration<PettyCashCount>
{
    public void Configure(EntityTypeBuilder<PettyCashCount> b)
    {
        b.ToTable("petty_cash_count", Schemas.CashBook);
        b.HasKey(x => x.Id);
        b.Property(x => x.Denominations).HasColumnType("text");
        b.Property(x => x.Note).HasColumnType("text");
        b.Property(x => x.Status).HasConversion<int>();
        b.Property(x => x.VarianceJournalNumber).HasMaxLength(40);
        b.Property(x => x.ReimbursementJournalNumber).HasMaxLength(40);
        b.Ignore(x => x.Variance);
        b.HasOne<PettyCashFund>().WithMany().HasForeignKey(x => x.FundId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.CompanyId, x.FundId, x.CountDate });
    }
}
