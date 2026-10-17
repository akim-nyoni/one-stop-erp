using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using One4All.Core.Trading;

namespace One4All.Infrastructure.Persistence.Configurations;

internal sealed class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> b)
    {
        b.Property(x => x.DeliveryZone).HasMaxLength(20);
        b.Property(x => x.CreditLimitPolicy).HasConversion<int>();
        b.Property(x => x.ReceivableControlAccountCode).HasMaxLength(20);
        b.Property(x => x.CustomerType).HasMaxLength(30);
        b.Property(x => x.AnalysisCode1).HasMaxLength(30);
        b.Property(x => x.AnalysisCode2).HasMaxLength(30);
        b.Property(x => x.AnalysisCode3).HasMaxLength(30);
        b.Property(x => x.DunningGroupCode).HasMaxLength(20);
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
        b.Property(x => x.NationalIdNumber).HasMaxLength(40);
        b.Property(x => x.Email).HasMaxLength(200);
        b.Property(x => x.MobileNumber).HasMaxLength(30);
        b.HasIndex(x => new { x.CompanyId, x.Code }).IsUnique().HasFilter("is_deleted = false");
        b.HasIndex(x => new { x.CompanyId, x.NationalIdNumber });
        b.HasQueryFilter(One4AllDbContext.SoftDeleteFilter, x => !x.IsDeleted);
    }
}

internal sealed class SupplierConfiguration : IEntityTypeConfiguration<Supplier>
{
    public void Configure(EntityTypeBuilder<Supplier> b)
    {
        b.ToTable("supplier", Schemas.AccountsPayable);
        b.Property(x => x.DefaultTaxCode).HasMaxLength(20);
        b.HasKey(x => x.Id);
        b.Property(x => x.OverReceiptTolerancePercent).HasPrecision(9, 4);
        b.Property(x => x.Code).HasMaxLength(20);
        b.Property(x => x.Name).HasMaxLength(200);
        b.Property(x => x.AnalysisCode1).HasMaxLength(30);
        b.Property(x => x.AnalysisCode2).HasMaxLength(30);
        b.Property(x => x.AnalysisCode3).HasMaxLength(30);
        b.Property(x => x.Currency).HasMaxLength(3);
        b.Property(x => x.AgeingBasis).HasConversion<int>();
        b.Property(x => x.Category).HasMaxLength(60);
        b.Property(x => x.TaxRegistration).HasMaxLength(40);
        b.Property(x => x.BankAccountName).HasMaxLength(120);
        b.Property(x => x.BankAccountNumber).HasMaxLength(40);
        b.Property(x => x.BankName).HasMaxLength(120);
        b.Property(x => x.MobileMoneyNumber).HasMaxLength(30);
        b.Property(x => x.BlacklistReason).HasMaxLength(300);
        b.HasIndex(x => new { x.CompanyId, x.Code }).IsUnique().HasFilter("is_deleted = false");
        b.HasQueryFilter(One4AllDbContext.SoftDeleteFilter, x => !x.IsDeleted);
    }
}

internal sealed class DebtCaseConfiguration : IEntityTypeConfiguration<DebtCase>
{
    public void Configure(EntityTypeBuilder<DebtCase> b)
    {
        b.ToTable("debt_case", Schemas.AccountsReceivable, t =>
            t.HasCheckConstraint("ck_debt_case_write_off_approver", "write_off_approved_by_user_id IS NULL OR write_off_approved_by_user_id <> write_off_requested_by_user_id"));
        b.HasKey(x => x.Id);
        b.Property(x => x.CaseNumber).HasMaxLength(30);
        b.Property(x => x.HandedOverTo).HasMaxLength(120);
        b.Property(x => x.AmountHandedOver).HasPrecision(19, 4);
        b.Property(x => x.Stage).HasConversion<int>();
        b.Property(x => x.SummonsNumber).HasMaxLength(60);
        b.Property(x => x.Court).HasMaxLength(120);
        b.Property(x => x.WriteOffReason).HasMaxLength(500);
        b.Ignore(x => x.IsOpen);
        b.HasIndex(x => new { x.CompanyId, x.CaseNumber }).IsUnique();
        b.HasIndex(x => x.CustomerId).IsUnique().HasFilter("stage IN (0, 1)").HasDatabaseName("ix_debt_case_one_open");
    }
}

internal sealed class SalesRepConfiguration : IEntityTypeConfiguration<SalesRep>
{
    public void Configure(EntityTypeBuilder<SalesRep> b)
    {
        b.ToTable("sales_rep", Schemas.AccountsReceivable);
        b.HasKey(x => x.Id);
        b.Property(x => x.Code).HasMaxLength(20);
        b.Property(x => x.Name).HasMaxLength(120);
        b.Property(x => x.Basis).HasConversion<int>();
        b.Property(x => x.RatePercent).HasPrecision(9, 4);
        b.HasIndex(x => new { x.CompanyId, x.Code }).IsUnique();
    }
}

internal sealed class CommissionAccrualConfiguration : IEntityTypeConfiguration<CommissionAccrual>
{
    public void Configure(EntityTypeBuilder<CommissionAccrual> b)
    {
        b.ToTable("commission_accrual", Schemas.AccountsReceivable);
        b.HasKey(x => x.Id);
        b.Property(x => x.Accrued).HasPrecision(19, 4);
        b.HasIndex(x => new { x.CompanyId, x.SalesRepId, x.Month }).IsUnique();
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
        b.Property(x => x.MobileMoneyNumber).HasMaxLength(30);
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
        b.Property(x => x.ReasonCode).HasMaxLength(20);
        b.Property(x => x.FiscalCode).HasMaxLength(60);
        b.Property(x => x.FiscalVerification).HasMaxLength(400);
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
        b.Property(x => x.DebitTransactionAmount).HasPrecision(19, 4);
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

internal sealed class TraderAddressConfiguration : IEntityTypeConfiguration<TraderAddress>
{
    public void Configure(EntityTypeBuilder<TraderAddress> b)
    {
        b.ToTable("trader_address", Schemas.AccountsReceivable);
        b.HasKey(x => x.Id);
        b.Property(x => x.Ledger).HasConversion<int>();
        b.Property(x => x.Kind).HasConversion<int>();
        b.Property(x => x.Line1).HasMaxLength(120);
        b.Property(x => x.Line2).HasMaxLength(120);
        b.Property(x => x.City).HasMaxLength(80);
        b.Property(x => x.Region).HasMaxLength(80);
        b.Property(x => x.PostalCode).HasMaxLength(20);
        b.Property(x => x.Country).HasMaxLength(60);
        b.HasIndex(x => new { x.CompanyId, x.Ledger, x.TraderId });
    }
}

internal sealed class TraderContactConfiguration : IEntityTypeConfiguration<TraderContact>
{
    public void Configure(EntityTypeBuilder<TraderContact> b)
    {
        b.ToTable("trader_contact", Schemas.AccountsReceivable);
        b.HasKey(x => x.Id);
        b.Property(x => x.Ledger).HasConversion<int>();
        b.Property(x => x.Name).HasMaxLength(120);
        b.Property(x => x.Role).HasMaxLength(60);
        b.Property(x => x.Email).HasMaxLength(200);
        b.Property(x => x.Phone).HasMaxLength(40);
        b.HasIndex(x => new { x.CompanyId, x.Ledger, x.TraderId });
    }
}

internal sealed class PaymentRunBatchConfiguration : IEntityTypeConfiguration<PaymentRunBatch>
{
    public void Configure(EntityTypeBuilder<PaymentRunBatch> b)
    {
        b.Property(x => x.Method).HasConversion<int>();
        b.Property(x => x.FirstChequeNumber).HasMaxLength(20);
        b.ToTable("payment_run", Schemas.AccountsPayable, t => t.HasCheckConstraint("ck_payment_run_approver", "approved_by_user_id IS NULL OR approved_by_user_id <> proposed_by_user_id"));
        b.HasKey(x => x.Id);
        b.Property(x => x.Number).HasMaxLength(30);
        b.Property(x => x.BankAccountCode).HasMaxLength(20);
        b.Property(x => x.WithholdingTaxCode).HasMaxLength(20);
        b.Property(x => x.EftLayoutCode).HasMaxLength(20);
        b.Property(x => x.Currency).HasMaxLength(3);
        b.Property(x => x.Status).HasConversion<int>();
        b.HasIndex(x => new { x.CompanyId, x.Number }).IsUnique();
        b.HasMany(x => x.Lines).WithOne().HasForeignKey(x => x.PaymentRunBatchId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class PaymentRunBatchLineConfiguration : IEntityTypeConfiguration<PaymentRunBatchLine>
{
    public void Configure(EntityTypeBuilder<PaymentRunBatchLine> b)
    {
        b.ToTable("payment_run_line", Schemas.AccountsPayable);
        b.HasKey(x => x.Id);
        b.Property(x => x.SupplierCode).HasMaxLength(20);
        b.Property(x => x.Currency).HasMaxLength(3);
        b.Property(x => x.Due).HasPrecision(19, 4);
        b.Property(x => x.Amount).HasPrecision(19, 4);
        b.Property(x => x.Note).HasMaxLength(100);
        b.Property(x => x.PaymentNumber).HasMaxLength(30);
    }
}

internal sealed class DunningGroupConfiguration : IEntityTypeConfiguration<DunningGroup>
{
    public void Configure(EntityTypeBuilder<DunningGroup> b)
    {
        b.ToTable("dunning_group", Schemas.AccountsReceivable);
        b.HasKey(x => x.Id);
        b.Property(x => x.Code).HasMaxLength(20);
        b.Property(x => x.Name).HasMaxLength(120);
        b.HasIndex(x => new { x.CompanyId, x.Code }).IsUnique();
    }
}

internal sealed class DunningLogConfiguration : IEntityTypeConfiguration<DunningLog>
{
    public void Configure(EntityTypeBuilder<DunningLog> b)
    {
        b.ToTable("dunning_log", Schemas.AccountsReceivable);
        b.HasKey(x => x.Id);
        b.Property(x => x.Overdue).HasPrecision(19, 4);
        b.HasIndex(x => new { x.CustomerId, x.Level, x.SentOn });
    }
}

internal sealed class FinanceChargePolicyConfiguration : IEntityTypeConfiguration<FinanceChargePolicy>
{
    public void Configure(EntityTypeBuilder<FinanceChargePolicy> b)
    {
        b.ToTable("finance_charge_policy", Schemas.Trading);
        b.HasKey(x => x.Id);
        b.HasIndex(x => x.CompanyId).IsUnique();
        b.Property(x => x.AnnualRatePercent).HasPrecision(9, 4);
        b.Property(x => x.MinimumCharge).HasPrecision(18, 2);
        b.Property(x => x.InterestIncomeAccountCode).HasMaxLength(30).IsRequired();
        b.Property(x => x.DocumentPrefix).HasMaxLength(10).IsRequired();
    }
}

internal sealed class SupplierAdvanceConfiguration : IEntityTypeConfiguration<SupplierAdvance>
{
    public void Configure(EntityTypeBuilder<SupplierAdvance> b)
    {
        b.ToTable("supplier_advance", Schemas.Trading);
        b.HasKey(x => x.Id);
        b.Property(x => x.DocumentNumber).HasMaxLength(40);
        b.Property(x => x.Currency).HasMaxLength(3);
        b.Property(x => x.ExchangeRate).HasPrecision(19, 6);
        b.Property(x => x.AdvanceAccountCode).HasMaxLength(30);
        b.Property(x => x.BankAccountCode).HasMaxLength(30);
        b.Property(x => x.PurchaseOrderNumber).HasMaxLength(40);
        b.Property(x => x.Narrative).HasMaxLength(512);
        b.Property(x => x.Status).HasConversion<int>();
        b.Ignore(x => x.Outstanding);
        b.HasIndex(x => new { x.CompanyId, x.DocumentNumber }).IsUnique();
        b.HasIndex(x => new { x.CompanyId, x.SupplierId, x.Status });
    }
}
