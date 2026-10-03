using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using One4All.Core.Accounting;

namespace One4All.Infrastructure.Persistence.Configurations;

internal sealed class LedgerAccountConfiguration : IEntityTypeConfiguration<LedgerAccount>
{
    public void Configure(EntityTypeBuilder<LedgerAccount> b)
    {
        b.ToTable("account", Schemas.GeneralLedger);
        b.HasKey(x => x.Id);
        b.Property(x => x.AccountCode).HasMaxLength(8);
        b.Property(x => x.Name).HasMaxLength(200);
        b.Property(x => x.ReportCategory).HasMaxLength(80);
        b.Property(x => x.Type).HasConversion<int>();
        b.Property(x => x.ControlAccountType).HasConversion<int>();
        b.Property(x => x.CashFlowCategory).HasConversion<int>();
        b.Property(x => x.CurrencyRestriction).HasMaxLength(3);
        b.Ignore(x => x.IsControlAccount);
        b.Ignore(x => x.NormalBalance);
        b.Ignore(x => x.IsPostableManually);
        b.HasIndex(x => new { x.CompanyId, x.AccountCode }).IsUnique().HasFilter("is_deleted = false");
        b.HasQueryFilter(x => !x.IsDeleted);
    }
}

internal sealed class CostCentreConfiguration : IEntityTypeConfiguration<CostCentre>
{
    public void Configure(EntityTypeBuilder<CostCentre> b)
    {
        b.ToTable("cost_centre", Schemas.GeneralLedger);
        b.HasKey(x => x.Id);
        b.Property(x => x.Code).HasMaxLength(3);
        b.Property(x => x.Name).HasMaxLength(120);
        b.HasIndex(x => new { x.CompanyId, x.Code }).IsUnique().HasFilter("is_deleted = false");
        b.HasQueryFilter(x => !x.IsDeleted);
    }
}

internal sealed class DepartmentConfiguration : IEntityTypeConfiguration<Department>
{
    public void Configure(EntityTypeBuilder<Department> b)
    {
        b.ToTable("department", Schemas.GeneralLedger);
        b.HasKey(x => x.Id);
        b.Property(x => x.Code).HasMaxLength(3);
        b.Property(x => x.Name).HasMaxLength(120);
        b.HasIndex(x => new { x.CompanyId, x.Code }).IsUnique().HasFilter("is_deleted = false");
        b.HasQueryFilter(x => !x.IsDeleted);
    }
}

internal sealed class JournalEntryConfiguration : IEntityTypeConfiguration<JournalEntry>
{
    public void Configure(EntityTypeBuilder<JournalEntry> b)
    {
        b.ToTable("journal", Schemas.GeneralLedger);
        b.HasKey(x => x.Id);
        b.Property(x => x.JournalNumber).HasMaxLength(30);
        b.Property(x => x.BatchNumber).HasMaxLength(30);
        b.Property(x => x.SourceModule).HasMaxLength(40);
        b.Property(x => x.DocumentType).HasMaxLength(40);
        b.Property(x => x.DocumentNumber).HasMaxLength(40);
        b.Property(x => x.Reference).HasMaxLength(80);
        b.Property(x => x.Narrative).HasMaxLength(512);
        b.Property(x => x.BaseCurrency).HasMaxLength(3);
        b.Property(x => x.TransactionCurrency).HasMaxLength(3);
        b.Property(x => x.Status).HasConversion<int>();
        b.Ignore(x => x.TotalBaseDebit);
        b.Ignore(x => x.TotalBaseCredit);
        b.Ignore(x => x.IsBalancedInBase);

        b.HasMany(x => x.Lines).WithOne().HasForeignKey(x => x.JournalEntryId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);

        b.HasIndex(x => new { x.CompanyId, x.JournalNumber }).IsUnique().HasFilter("journal_number <> ''");
        b.HasIndex(x => new { x.CompanyId, x.FiscalPeriodId });
        b.HasIndex(x => new { x.CompanyId, x.PostingDate });
    }
}

internal sealed class JournalLineConfiguration : IEntityTypeConfiguration<JournalLine>
{
    public void Configure(EntityTypeBuilder<JournalLine> b)
    {
        b.ToTable("journal_line", Schemas.GeneralLedger);
        b.HasKey(x => x.Id);
        b.Property(x => x.AccountCode).HasMaxLength(8);
        b.Property(x => x.CostCentre).HasMaxLength(3);
        b.Property(x => x.Department).HasMaxLength(3);
        b.Property(x => x.TransactionCurrency).HasMaxLength(3);
        b.Property(x => x.ExchangeRate).HasPrecision(19, 6);
        b.Property(x => x.Narrative).HasMaxLength(512);
        b.Property(x => x.TaxCode).HasMaxLength(20);
        b.Ignore(x => x.Key);
        b.HasIndex(x => new { x.AccountCode, x.CostCentre, x.Department });
    }
}

internal sealed class GlPeriodBalanceConfiguration : IEntityTypeConfiguration<GlPeriodBalance>
{
    public void Configure(EntityTypeBuilder<GlPeriodBalance> b)
    {
        b.ToTable("period_balance", Schemas.GeneralLedger);
        b.HasKey(x => x.Id);
        b.Property(x => x.AccountCode).HasMaxLength(8);
        b.Property(x => x.CostCentre).HasMaxLength(3);
        b.Property(x => x.Department).HasMaxLength(3);
        b.Ignore(x => x.NetBase);
        b.Ignore(x => x.Key);
        b.HasIndex(x => new { x.CompanyId, x.FiscalPeriodId, x.AccountCode, x.CostCentre, x.Department }).IsUnique();
    }
}

internal sealed class RecurringJournalConfiguration : IEntityTypeConfiguration<RecurringJournal>
{
    public void Configure(EntityTypeBuilder<RecurringJournal> b)
    {
        b.ToTable("recurring_journal", Schemas.GeneralLedger);
        b.HasKey(x => x.Id);
        b.Property(x => x.Code).HasMaxLength(30);
        b.Property(x => x.Description).HasMaxLength(200);
        b.Property(x => x.SourceModule).HasMaxLength(10);
        b.Property(x => x.Narrative).HasMaxLength(400);
        b.Ignore(x => x.TotalDebit);
        b.Ignore(x => x.TotalCredit);
        b.Ignore(x => x.IsBalanced);
        b.HasMany(x => x.Lines).WithOne().HasForeignKey(x => x.RecurringJournalId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.HasIndex(x => new { x.CompanyId, x.Code }).IsUnique();
    }
}

internal sealed class RecurringJournalLineConfiguration : IEntityTypeConfiguration<RecurringJournalLine>
{
    public void Configure(EntityTypeBuilder<RecurringJournalLine> b)
    {
        b.ToTable("recurring_journal_line", Schemas.GeneralLedger);
        b.HasKey(x => x.Id);
        b.Property(x => x.AccountCode).HasMaxLength(8);
        b.Property(x => x.Amount).HasPrecision(19, 4);
        b.Property(x => x.CostCentre).HasMaxLength(3);
        b.Property(x => x.Department).HasMaxLength(3);
        b.HasIndex(x => x.RecurringJournalId);
    }
}

internal sealed class DeferredIncomeScheduleConfiguration : IEntityTypeConfiguration<DeferredIncomeSchedule>
{
    public void Configure(EntityTypeBuilder<DeferredIncomeSchedule> b)
    {
        b.ToTable("deferred_income_schedule", Schemas.GeneralLedger);
        b.HasKey(x => x.Id);
        b.Property(x => x.Reference).HasMaxLength(60);
        b.Property(x => x.Description).HasMaxLength(200);
        b.Property(x => x.DeferredAccountCode).HasMaxLength(8);
        b.Property(x => x.RevenueAccountCode).HasMaxLength(8);
        b.Property(x => x.Amount).HasPrecision(19, 4);
        b.Property(x => x.RecognisedAmount).HasPrecision(19, 4);
        b.HasIndex(x => new { x.CompanyId, x.StartDate });
    }
}
