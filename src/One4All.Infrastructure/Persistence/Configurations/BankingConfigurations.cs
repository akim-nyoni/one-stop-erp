using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using One4All.Core.Banking;

namespace One4All.Infrastructure.Persistence.Configurations;

internal sealed class BankAccountConfiguration : IEntityTypeConfiguration<BankAccount>
{
    public void Configure(EntityTypeBuilder<BankAccount> b)
    {
        b.ToTable("bank_account", Schemas.CashBook);
        b.HasKey(x => x.Id);
        b.Property(x => x.Code).HasMaxLength(20);
        b.Property(x => x.Name).HasMaxLength(200);
        b.Property(x => x.Currency).HasMaxLength(3);
        b.Property(x => x.GlAccountCode).HasMaxLength(8);
        b.Property(x => x.BankName).HasMaxLength(120);
        b.Property(x => x.AccountNumber).HasMaxLength(40);
        b.HasIndex(x => new { x.CompanyId, x.Code }).IsUnique().HasFilter("is_deleted = false");
        b.HasQueryFilter(One4AllDbContext.SoftDeleteFilter, x => !x.IsDeleted);
    }
}

internal sealed class BankStatementLineConfiguration : IEntityTypeConfiguration<BankStatementLine>
{
    public void Configure(EntityTypeBuilder<BankStatementLine> b)
    {
        b.ToTable("bank_statement_line", Schemas.CashBook);
        b.HasKey(x => x.Id);
        b.Property(x => x.Description).HasMaxLength(300);
        b.Property(x => x.Reference).HasMaxLength(80);
        b.Property(x => x.Amount).HasPrecision(19, 4);
        b.HasIndex(x => new { x.CompanyId, x.BankAccountId, x.IsReconciled });
    }
}

internal sealed class ChequeConfiguration : IEntityTypeConfiguration<Cheque>
{
    public void Configure(EntityTypeBuilder<Cheque> b)
    {
        b.ToTable("cheque", Schemas.CashBook);
        b.HasKey(x => x.Id);
        b.Property(x => x.ChequeNumber).HasMaxLength(20);
        b.Property(x => x.Payee).HasMaxLength(160);
        b.Property(x => x.Amount).HasPrecision(19, 4);
        b.Property(x => x.DocumentNumber).HasMaxLength(40);
        b.Property(x => x.Status).HasConversion<int>();
        b.Property(x => x.Reason).HasMaxLength(200);
        b.HasIndex(x => new { x.BankAccountId, x.ChequeNumber }).IsUnique();
    }
}

internal sealed class SupplierStatementConfiguration : IEntityTypeConfiguration<SupplierStatement>
{
    public void Configure(EntityTypeBuilder<SupplierStatement> b)
    {
        b.ToTable("supplier_statement", Schemas.CashBook);
        b.HasKey(x => x.Id);
        b.Property(x => x.ClosingBalance).HasPrecision(19, 4);
        b.HasIndex(x => new { x.CompanyId, x.SupplierId, x.StatementDate });
        b.HasMany(x => x.Lines).WithOne().HasForeignKey(l => l.SupplierStatementId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class SupplierStatementLineConfiguration : IEntityTypeConfiguration<SupplierStatementLine>
{
    public void Configure(EntityTypeBuilder<SupplierStatementLine> b)
    {
        b.ToTable("supplier_statement_line", Schemas.CashBook);
        b.HasKey(x => x.Id);
        b.Property(x => x.DocumentNumber).HasMaxLength(60);
        b.Property(x => x.Amount).HasPrecision(19, 4);
    }
}

internal sealed class BankFileLayoutConfiguration : IEntityTypeConfiguration<BankFileLayout>
{
    public void Configure(EntityTypeBuilder<BankFileLayout> b)
    {
        b.ToTable("bank_file_layout", Schemas.CashBook);
        b.HasKey(x => x.Id);
        b.Property(x => x.Code).HasMaxLength(20);
        b.Property(x => x.Name).HasMaxLength(100);
        b.Property(x => x.HeaderTemplate).HasMaxLength(1000);
        b.Property(x => x.DetailTemplate).HasMaxLength(1000);
        b.Property(x => x.TrailerTemplate).HasMaxLength(1000);
        b.Property(x => x.LineEnding).HasMaxLength(4);
        b.HasIndex(x => new { x.CompanyId, x.Code }).IsUnique();
    }
}

internal sealed class CashForecastEntryConfiguration : IEntityTypeConfiguration<CashForecastEntry>
{
    public void Configure(EntityTypeBuilder<CashForecastEntry> b)
    {
        b.ToTable("cash_forecast_entry", Schemas.CashBook);
        b.HasKey(x => x.Id);
        b.Property(x => x.Amount).HasPrecision(19, 4);
        b.Property(x => x.Description).HasMaxLength(200);
        b.HasIndex(x => new { x.CompanyId, x.Date });
    }
}
