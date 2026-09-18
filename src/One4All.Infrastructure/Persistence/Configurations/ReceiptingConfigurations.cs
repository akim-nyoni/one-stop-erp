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
        b.HasQueryFilter(x => !x.IsDeleted);
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
