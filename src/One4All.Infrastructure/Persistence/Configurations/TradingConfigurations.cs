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

internal sealed class CreditLimitRequestConfiguration : IEntityTypeConfiguration<CreditLimitRequest>
{
    public void Configure(EntityTypeBuilder<CreditLimitRequest> b)
    {
        b.ToTable("credit_limit_request", Schemas.AccountsReceivable, t =>
            t.HasCheckConstraint("ck_credit_limit_request_reviewer_not_requester", "status <> 1 OR reviewed_by_user_id <> requested_by_user_id"));
        b.HasKey(x => x.Id);
        b.Property(x => x.CurrentLimit).HasPrecision(19, 4);
        b.Property(x => x.RequestedLimit).HasPrecision(19, 4);
        b.Property(x => x.Reason).HasMaxLength(500);
        b.Property(x => x.RejectionReason).HasMaxLength(256);
        b.Property(x => x.Status).HasConversion<int>();
        b.HasIndex(x => x.CustomerId).IsUnique().HasFilter("status = 0").HasDatabaseName("ix_credit_limit_request_one_pending");
    }
}

internal sealed class LaybyPlanConfiguration : IEntityTypeConfiguration<LaybyPlan>
{
    public void Configure(EntityTypeBuilder<LaybyPlan> b)
    {
        b.ToTable("layby_plan", Schemas.AccountsReceivable);
        b.HasKey(x => x.Id);
        b.Property(x => x.PlanNumber).HasMaxLength(40);
        b.Property(x => x.ForfeitPercent).HasPrecision(9, 4);
        b.Property(x => x.DepositAccountCode).HasMaxLength(8);
        b.Property(x => x.RevenueAccountCode).HasMaxLength(8);
        b.Property(x => x.ForfeitureAccountCode).HasMaxLength(8);
        b.Property(x => x.Paid).HasPrecision(19, 4);
        b.Property(x => x.Forfeited).HasPrecision(19, 4);
        b.Property(x => x.Status).HasConversion<int>();
        b.Ignore(x => x.Total);
        b.Ignore(x => x.Balance);
        b.HasMany(x => x.Lines).WithOne().HasForeignKey(x => x.LaybyPlanId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.HasIndex(x => new { x.CompanyId, x.PlanNumber }).IsUnique();
        b.HasIndex(x => new { x.CompanyId, x.Status, x.FinalDate });
    }
}

internal sealed class LaybyLineConfiguration : IEntityTypeConfiguration<LaybyLine>
{
    public void Configure(EntityTypeBuilder<LaybyLine> b)
    {
        b.ToTable("layby_line", Schemas.AccountsReceivable);
        b.HasKey(x => x.Id);
        b.Property(x => x.Quantity).HasPrecision(19, 4);
        b.Property(x => x.UnitPrice).HasPrecision(19, 4);
        b.HasIndex(x => x.ItemId);
    }
}

internal sealed class PaymentPromiseConfiguration : IEntityTypeConfiguration<PaymentPromise>
{
    public void Configure(EntityTypeBuilder<PaymentPromise> b)
    {
        b.ToTable("payment_promise", Schemas.AccountsReceivable);
        b.HasKey(x => x.Id);
        b.Property(x => x.Amount).HasPrecision(19, 4);
        b.Property(x => x.Note).HasMaxLength(500);
        b.Property(x => x.Status).HasConversion<int>();
        b.HasIndex(x => x.CustomerId).IsUnique().HasFilter("status = 0").HasDatabaseName("ix_payment_promise_one_open");
        b.HasIndex(x => new { x.CompanyId, x.Status, x.PayBy });
    }
}

internal sealed class SupplierBankChangeConfiguration : IEntityTypeConfiguration<SupplierBankChange>
{
    public void Configure(EntityTypeBuilder<SupplierBankChange> b)
    {
        b.ToTable("supplier_bank_change", Schemas.AccountsPayable, t =>
            t.HasCheckConstraint("ck_supplier_bank_change_reviewer_not_requester", "status <> 1 OR reviewed_by_user_id <> requested_by_user_id"));
        b.HasKey(x => x.Id);
        b.Property(x => x.AccountName).HasMaxLength(120);
        b.Property(x => x.AccountNumber).HasMaxLength(40);
        b.Property(x => x.BankName).HasMaxLength(120);
        b.Property(x => x.Status).HasConversion<int>();
        b.Property(x => x.RejectionReason).HasMaxLength(256);
        b.HasIndex(x => x.SupplierId).IsUnique().HasFilter("status = 0").HasDatabaseName("ix_supplier_bank_change_one_pending");
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

internal sealed class RecurringSalesInvoiceConfiguration : IEntityTypeConfiguration<RecurringSalesInvoice>
{
    public void Configure(EntityTypeBuilder<RecurringSalesInvoice> b)
    {
        b.ToTable("recurring_invoice", Schemas.AccountsReceivable);
        b.HasKey(x => x.Id);
        b.Property(x => x.Reference).HasMaxLength(30);
        b.Property(x => x.Currency).HasMaxLength(3);
        b.Property(x => x.NetAmount).HasPrecision(19, 4);
        b.Property(x => x.TaxAmount).HasPrecision(19, 4);
        b.Property(x => x.SalesAccountCode).HasMaxLength(8);
        b.Property(x => x.TaxAccountCode).HasMaxLength(8);
        b.Property(x => x.Narrative).HasMaxLength(200);
        b.HasIndex(x => new { x.CompanyId, x.IsActive, x.NextRunDate });
    }
}

internal sealed class RecurringPurchaseInvoiceConfiguration : IEntityTypeConfiguration<RecurringPurchaseInvoice>
{
    public void Configure(EntityTypeBuilder<RecurringPurchaseInvoice> b)
    {
        b.ToTable("recurring_invoice", Schemas.AccountsPayable);
        b.HasKey(x => x.Id);
        b.Property(x => x.Reference).HasMaxLength(30);
        b.Property(x => x.Currency).HasMaxLength(3);
        b.Property(x => x.NetAmount).HasPrecision(19, 4);
        b.Property(x => x.TaxAmount).HasPrecision(19, 4);
        b.Property(x => x.ExpenseAccountCode).HasMaxLength(8);
        b.Property(x => x.TaxAccountCode).HasMaxLength(8);
        b.Property(x => x.Narrative).HasMaxLength(200);
        b.HasIndex(x => new { x.CompanyId, x.IsActive, x.NextRunDate });
    }
}

internal sealed class WithholdingCertificateConfiguration : IEntityTypeConfiguration<WithholdingCertificate>
{
    public void Configure(EntityTypeBuilder<WithholdingCertificate> b)
    {
        b.ToTable("withholding_certificate", Schemas.Trading);
        b.HasKey(x => x.Id);
        b.Property(x => x.CertificateNumber).HasMaxLength(60);
        b.Property(x => x.PaymentDocumentNumber).HasMaxLength(60);
        b.Property(x => x.TaxCode).HasMaxLength(20);
        b.Property(x => x.RemittanceJournalNumber).HasMaxLength(30);
        b.Property(x => x.RatePercent).HasPrecision(9, 4);
        b.Property(x => x.GrossAmount).HasPrecision(19, 4);
        b.Property(x => x.WithheldAmount).HasPrecision(19, 4);
        b.HasIndex(x => new { x.CompanyId, x.PaymentDate });
    }
}
