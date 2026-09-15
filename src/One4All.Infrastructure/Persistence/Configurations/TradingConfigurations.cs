using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using One4All.Core.Trading;

namespace One4All.Infrastructure.Persistence.Configurations;

internal sealed class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> b)
    {
        b.ToTable("customer", Schemas.AccountsReceivable);
        b.HasKey(x => x.Id);
        b.Property(x => x.Code).HasMaxLength(20);
        b.Property(x => x.Name).HasMaxLength(200);
        b.Property(x => x.Currency).HasMaxLength(3);
        b.Property(x => x.CreditLimit).HasPrecision(19, 4);
        b.Property(x => x.SettlementDiscountPercent).HasPrecision(9, 4);
        b.Property(x => x.AgeingBasis).HasConversion<int>();
        b.Property(x => x.StatementType).HasConversion<int>();
        b.Property(x => x.TaxRegistration).HasMaxLength(40);
        b.Property(x => x.DefaultTaxCode).HasMaxLength(20);
        b.Property(x => x.SalesAgent).HasMaxLength(40);
        b.HasIndex(x => new { x.CompanyId, x.Code }).IsUnique().HasFilter("is_deleted = false");
        b.HasQueryFilter(x => !x.IsDeleted);
    }
}

internal sealed class SupplierConfiguration : IEntityTypeConfiguration<Supplier>
{
    public void Configure(EntityTypeBuilder<Supplier> b)
    {
        b.ToTable("supplier", Schemas.AccountsPayable);
        b.HasKey(x => x.Id);
        b.Property(x => x.Code).HasMaxLength(20);
        b.Property(x => x.Name).HasMaxLength(200);
        b.Property(x => x.Currency).HasMaxLength(3);
        b.Property(x => x.AgeingBasis).HasConversion<int>();
        b.Property(x => x.Category).HasMaxLength(60);
        b.Property(x => x.TaxRegistration).HasMaxLength(40);
        b.Property(x => x.BankAccountName).HasMaxLength(120);
        b.Property(x => x.BankAccountNumber).HasMaxLength(40);
        b.Property(x => x.BankName).HasMaxLength(120);
        b.HasIndex(x => new { x.CompanyId, x.Code }).IsUnique().HasFilter("is_deleted = false");
        b.HasQueryFilter(x => !x.IsDeleted);
    }
}

internal sealed class OpenItemConfiguration : IEntityTypeConfiguration<OpenItem>
{
    public void Configure(EntityTypeBuilder<OpenItem> b)
    {
        b.ToTable("open_item", Schemas.Trading);
        b.HasKey(x => x.Id);
        b.Property(x => x.Ledger).HasConversion<int>();
        b.Property(x => x.ItemType).HasConversion<int>();
        b.Property(x => x.Sign).HasConversion<int>();
        b.Property(x => x.DocumentNumber).HasMaxLength(40);
        b.Property(x => x.Currency).HasMaxLength(3);
        b.Property(x => x.ExchangeRate).HasPrecision(19, 6);
        b.Property(x => x.OriginalTransaction).HasPrecision(19, 4);
        b.Property(x => x.OriginalBase).HasPrecision(19, 4);
        b.Property(x => x.OutstandingTransaction).HasPrecision(19, 4);
        b.Property(x => x.OutstandingBase).HasPrecision(19, 4);
        b.Property(x => x.Narrative).HasMaxLength(512);
        b.Ignore(x => x.IsSettled);
        b.HasIndex(x => new { x.CompanyId, x.Ledger, x.TraderId });
        b.HasIndex(x => new { x.CompanyId, x.Ledger, x.TraderId, x.OutstandingTransaction });
    }
}

internal sealed class AllocationConfiguration : IEntityTypeConfiguration<Allocation>
{
    public void Configure(EntityTypeBuilder<Allocation> b)
    {
        b.ToTable("allocation", Schemas.Trading);
        b.HasKey(x => x.Id);
        b.Property(x => x.Ledger).HasConversion<int>();
        b.Property(x => x.TransactionAmount).HasPrecision(19, 4);
        b.Property(x => x.DebitBase).HasPrecision(19, 4);
        b.Property(x => x.CreditBase).HasPrecision(19, 4);
        b.Property(x => x.RealisedFxBase).HasPrecision(19, 4);
        b.HasIndex(x => new { x.CompanyId, x.Ledger, x.TraderId });
        b.HasIndex(x => x.DebitItemId);
        b.HasIndex(x => x.CreditItemId);
    }
}
