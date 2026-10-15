using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using One4All.Core.Receipting;

namespace One4All.Infrastructure.Persistence.Configurations;

internal sealed class CashierStationConfiguration : IEntityTypeConfiguration<CashierStation>
{
    public void Configure(EntityTypeBuilder<CashierStation> b)
    {
        b.ToTable("station", Schemas.Receipting);
        b.HasKey(x => x.Id);
        b.Property(x => x.Code).HasMaxLength(20);
        b.Property(x => x.Name).HasMaxLength(120);
        b.Property(x => x.ReceiptPrefix).HasMaxLength(10);
        b.HasIndex(x => new { x.CompanyId, x.Code }).IsUnique().HasFilter("is_deleted = false");
        b.HasQueryFilter(One4AllDbContext.SoftDeleteFilter, x => !x.IsDeleted);
    }
}

internal sealed class CashierSessionConfiguration : IEntityTypeConfiguration<CashierSession>
{
    public void Configure(EntityTypeBuilder<CashierSession> b)
    {
        b.ToTable("session", Schemas.Receipting);
        b.HasKey(x => x.Id);
        b.Property(x => x.Status).HasConversion<int>();
        b.HasMany(x => x.Floats).WithOne().HasForeignKey(x => x.CashierSessionId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.Floats).UsePropertyAccessMode(PropertyAccessMode.Field);
        // One open session per cashier per station (partial unique index).
        b.HasIndex(x => new { x.CompanyId, x.CashierUserId, x.Status }).HasFilter("status = 1");
    }
}

internal sealed class SessionFloatConfiguration : IEntityTypeConfiguration<SessionFloat>
{
    public void Configure(EntityTypeBuilder<SessionFloat> b)
    {
        b.ToTable("session_float", Schemas.Receipting);
        b.HasKey(x => x.Id);
        b.Property(x => x.Currency).HasMaxLength(3);
    }
}

internal sealed class ReceiptConfiguration : IEntityTypeConfiguration<Receipt>
{
    public void Configure(EntityTypeBuilder<Receipt> b)
    {
        b.ToTable("receipt", Schemas.Receipting);
        b.HasKey(x => x.Id);
        b.Property(x => x.ReceiptNumber).HasMaxLength(30);
        b.Property(x => x.PayerKind).HasConversion<int>();
        b.Property(x => x.Currency).HasMaxLength(3);
        b.Property(x => x.Status).HasConversion<int>();
        b.Property(x => x.JournalNumber).HasMaxLength(30);
        b.Property(x => x.CashAccountCode).HasMaxLength(8);
        b.Property(x => x.CancellationReason).HasMaxLength(300);
        b.Property(x => x.AllocationOverride).HasMaxLength(30);
        b.Property(x => x.AllocationOverrideReason).HasMaxLength(300);
        b.Ignore(x => x.LineTotal);
        b.Ignore(x => x.TenderTotal);
        b.HasMany(x => x.Lines).WithOne().HasForeignKey(x => x.ReceiptId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.HasMany(x => x.Tenders).WithOne().HasForeignKey(x => x.ReceiptId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.Tenders).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.HasIndex(x => new { x.CompanyId, x.StationId, x.ReceiptNumber }).IsUnique();
        b.HasIndex(x => x.CashierSessionId);
    }
}

internal sealed class ReceiptLineConfiguration : IEntityTypeConfiguration<ReceiptLine>
{
    public void Configure(EntityTypeBuilder<ReceiptLine> b)
    {
        b.ToTable("receipt_line", Schemas.Receipting);
        b.HasKey(x => x.Id);
        b.Property(x => x.Type).HasConversion<int>();
        b.Property(x => x.AccountCode).HasMaxLength(8);
        b.Property(x => x.Narrative).HasMaxLength(300);
        b.Property(x => x.Service).HasMaxLength(60);
    }
}

internal sealed class AgentConfiguration : IEntityTypeConfiguration<Agent>
{
    public void Configure(EntityTypeBuilder<Agent> b)
    {
        b.ToTable("agent", Schemas.Receipting);
        b.HasKey(x => x.Id);
        b.Property(x => x.Code).HasMaxLength(20);
        b.Property(x => x.Name).HasMaxLength(200);
        b.Property(x => x.CommissionPercent).HasPrecision(9, 4);
        b.Ignore(x => x.Outstanding);
        b.HasIndex(x => new { x.CompanyId, x.Code }).IsUnique().HasFilter("is_deleted = false");
        b.HasQueryFilter(One4AllDbContext.SoftDeleteFilter, x => !x.IsDeleted);
    }
}

internal sealed class StationeryBookConfiguration : IEntityTypeConfiguration<StationeryBook>
{
    public void Configure(EntityTypeBuilder<StationeryBook> b)
    {
        b.ToTable("stationery_book", Schemas.Receipting);
        b.HasKey(x => x.Id);
        b.Property(x => x.BookNumber).HasMaxLength(30);
        b.Property(x => x.Status).HasConversion<int>();
        b.Ignore(x => x.SerialCount);
        b.HasMany(x => x.Captures).WithOne().HasForeignKey(x => x.StationeryBookId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.Captures).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.HasIndex(x => new { x.CompanyId, x.BookNumber }).IsUnique().HasFilter("is_deleted = false");
        b.HasQueryFilter(One4AllDbContext.SoftDeleteFilter, x => !x.IsDeleted);
    }
}

internal sealed class ManualReceiptCaptureConfiguration : IEntityTypeConfiguration<ManualReceiptCapture>
{
    public void Configure(EntityTypeBuilder<ManualReceiptCapture> b)
    {
        b.ToTable("manual_receipt_capture", Schemas.Receipting);
        b.HasKey(x => x.Id);
        b.HasIndex(x => new { x.StationeryBookId, x.Serial }).IsUnique();
    }
}

internal sealed class SegregationViolationConfiguration : IEntityTypeConfiguration<SegregationViolation>
{
    public void Configure(EntityTypeBuilder<SegregationViolation> b)
    {
        b.ToTable("sod_violation", Schemas.Receipting);
        b.HasKey(x => x.Id);
        b.Property(x => x.Conflict).HasMaxLength(300);
        b.HasIndex(x => new { x.CompanyId, x.OccurredOn });
    }
}

internal sealed class PendingTenderConfiguration : IEntityTypeConfiguration<PendingTender>
{
    public void Configure(EntityTypeBuilder<PendingTender> b)
    {
        b.ToTable("pending_tender", Schemas.Receipting);
        b.HasKey(x => x.Id);
        b.Property(x => x.ReceiptNumber).HasMaxLength(30);
        b.Property(x => x.TenderType).HasConversion<int>();
        b.Property(x => x.Reference).HasMaxLength(60);
        b.Property(x => x.PendingAccountCode).HasMaxLength(8);
        b.Property(x => x.Status).HasConversion<int>();
        b.HasIndex(x => new { x.CompanyId, x.Status });
    }
}

internal sealed class DishonouredPaymentConfiguration : IEntityTypeConfiguration<DishonouredPayment>
{
    public void Configure(EntityTypeBuilder<DishonouredPayment> b)
    {
        b.ToTable("dishonoured_payment", Schemas.Receipting);
        b.HasKey(x => x.Id);
        b.Property(x => x.ReceiptNumber).HasMaxLength(30);
        b.Property(x => x.Reference).HasMaxLength(60);
        b.Property(x => x.Drawer).HasMaxLength(200);
        b.HasIndex(x => new { x.CompanyId, x.Date });
    }
}

internal sealed class SuspenseAllocationConfiguration : IEntityTypeConfiguration<SuspenseAllocation>
{
    public void Configure(EntityTypeBuilder<SuspenseAllocation> b)
    {
        b.ToTable("suspense_allocation", Schemas.Receipting);
        b.HasKey(x => x.Id);
        b.Property(x => x.Reason).HasMaxLength(300);
        b.Property(x => x.JournalNumber).HasMaxLength(30);
        b.HasIndex(x => new { x.CompanyId, x.Date });
    }
}

internal sealed class PaymentArrangementConfiguration : IEntityTypeConfiguration<PaymentArrangement>
{
    public void Configure(EntityTypeBuilder<PaymentArrangement> b)
    {
        b.ToTable("payment_arrangement", Schemas.Receipting);
        b.HasKey(x => x.Id);
        b.Property(x => x.Status).HasConversion<int>();
        b.HasIndex(x => new { x.CompanyId, x.CustomerId });
    }
}

internal sealed class ReceiptReprintConfiguration : IEntityTypeConfiguration<ReceiptReprint>
{
    public void Configure(EntityTypeBuilder<ReceiptReprint> b)
    {
        b.ToTable("receipt_reprint", Schemas.Receipting);
        b.HasKey(x => x.Id);
        b.Property(x => x.ReceiptNumber).HasMaxLength(30);
        b.HasIndex(x => new { x.CompanyId, x.ReceiptId });
    }
}

internal sealed class RefundRecordConfiguration : IEntityTypeConfiguration<RefundRecord>
{
    public void Configure(EntityTypeBuilder<RefundRecord> b)
    {
        b.ToTable("refund", Schemas.Receipting);
        b.HasKey(x => x.Id);
        b.Property(x => x.Type).HasConversion<int>();
        b.Property(x => x.PayerName).HasMaxLength(200);
        b.Property(x => x.Reason).HasMaxLength(300);
        b.Property(x => x.JournalNumber).HasMaxLength(30);
        b.HasIndex(x => new { x.CompanyId, x.Date });
    }
}

internal sealed class SessionCashMovementConfiguration : IEntityTypeConfiguration<SessionCashMovement>
{
    public void Configure(EntityTypeBuilder<SessionCashMovement> b)
    {
        b.ToTable("session_cash_movement", Schemas.Receipting);
        b.HasKey(x => x.Id);
        b.Property(x => x.Type).HasConversion<int>();
        b.Property(x => x.Currency).HasMaxLength(3);
        b.Property(x => x.Reason).HasMaxLength(300);
        b.HasIndex(x => x.CashierSessionId);
    }
}

internal sealed class ImportBatchConfiguration : IEntityTypeConfiguration<ImportBatch>
{
    public void Configure(EntityTypeBuilder<ImportBatch> b)
    {
        b.ToTable("import_batch", Schemas.Receipting);
        b.HasKey(x => x.Id);
        b.Property(x => x.FileReference).HasMaxLength(200);
        b.Property(x => x.FileHash).HasMaxLength(128);
        b.Property(x => x.Source).HasConversion<int>();
        b.Property(x => x.BankAccountCode).HasMaxLength(8);
        b.Ignore(x => x.MatchedCount);
        b.Ignore(x => x.UnmatchedCount);
        b.Ignore(x => x.TotalAmount);
        b.HasMany(x => x.Lines).WithOne().HasForeignKey(x => x.ImportBatchId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.HasIndex(x => new { x.CompanyId, x.FileHash }).IsUnique();
    }
}

internal sealed class ImportLineConfiguration : IEntityTypeConfiguration<ImportLine>
{
    public void Configure(EntityTypeBuilder<ImportLine> b)
    {
        b.ToTable("import_line", Schemas.Receipting);
        b.HasKey(x => x.Id);
        b.Property(x => x.PayerReference).HasMaxLength(120);
        b.Property(x => x.PayerName).HasMaxLength(200);
        b.Property(x => x.Status).HasConversion<int>();
        b.HasIndex(x => new { x.ImportBatchId, x.Status });
    }
}

internal sealed class BankDepositConfiguration : IEntityTypeConfiguration<BankDeposit>
{
    public void Configure(EntityTypeBuilder<BankDeposit> b)
    {
        b.ToTable("bank_deposit", Schemas.Receipting);
        b.HasKey(x => x.Id);
        b.Property(x => x.Reference).HasMaxLength(40);
        b.Property(x => x.Currency).HasMaxLength(3);
        b.Property(x => x.Status).HasConversion<int>();
        b.HasIndex(x => new { x.CompanyId, x.Status });
    }
}

internal sealed class ReceiptTenderConfiguration : IEntityTypeConfiguration<ReceiptTender>
{
    public void Configure(EntityTypeBuilder<ReceiptTender> b)
    {
        b.ToTable("receipt_tender", Schemas.Receipting);
        b.HasKey(x => x.Id);
        b.Property(x => x.Type).HasConversion<int>();
        b.Property(x => x.Currency).HasMaxLength(3);
        b.Property(x => x.Reference).HasMaxLength(60);
    }
}

internal sealed class CashierRelatedAccountConfiguration : IEntityTypeConfiguration<CashierRelatedAccount>
{
    public void Configure(EntityTypeBuilder<CashierRelatedAccount> b)
    {
        b.ToTable("cashier_related_account", "rcp");
        b.HasKey(x => x.Id);
        b.Property(x => x.Relationship).HasMaxLength(40);
        b.HasIndex(x => new { x.CompanyId, x.UserId, x.CustomerId }).IsUnique();
    }
}

internal sealed class ReceiptMatchRuleConfiguration : IEntityTypeConfiguration<ReceiptMatchRule>
{
    public void Configure(EntityTypeBuilder<ReceiptMatchRule> b)
    {
        b.ToTable("receipt_match_rule", Schemas.Receipting);
        b.HasKey(x => x.Id);
        b.HasIndex(x => new { x.CompanyId, x.Priority });
        b.Property(x => x.Field).HasConversion<int>();
        b.Property(x => x.Kind).HasConversion<int>();
        b.Property(x => x.Pattern).HasMaxLength(200).IsRequired();
    }
}
