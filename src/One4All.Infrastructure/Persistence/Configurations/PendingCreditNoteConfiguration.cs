using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using One4All.Core.Trading;

namespace One4All.Infrastructure.Persistence.Configurations;

internal sealed class PendingCreditNoteConfiguration : IEntityTypeConfiguration<PendingCreditNote>
{
    public void Configure(EntityTypeBuilder<PendingCreditNote> b)
    {
        b.ToTable("pending_credit_note", Schemas.AccountsReceivable);
        b.HasKey(x => x.Id);
        b.Property(x => x.DocumentNumber).HasMaxLength(40);
        b.Property(x => x.Currency).HasMaxLength(3);
        b.Property(x => x.NetAmount).HasPrecision(19, 4);
        b.Property(x => x.TaxAmount).HasPrecision(19, 4);
        b.Property(x => x.SalesAccountCode).HasMaxLength(20);
        b.Property(x => x.TaxAccountCode).HasMaxLength(20);
        b.Property(x => x.Narrative).HasMaxLength(500);
        b.Property(x => x.ReasonCode).HasMaxLength(30);
        b.Property(x => x.JournalNumber).HasMaxLength(40);
        b.Property(x => x.Status).HasConversion<int>();
        b.HasIndex(x => new { x.CompanyId, x.DocumentNumber });
    }
}
