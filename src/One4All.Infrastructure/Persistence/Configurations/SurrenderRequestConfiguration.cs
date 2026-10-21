using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using One4All.Core.Housing;

namespace One4All.Infrastructure.Persistence.Configurations;

internal sealed class SurrenderRequestConfiguration : IEntityTypeConfiguration<SurrenderRequest>
{
    public void Configure(EntityTypeBuilder<SurrenderRequest> b)
    {
        b.ToTable("stand_surrender_request", Schemas.Housing);
        b.HasKey(x => x.Id);
        b.Property(x => x.StandNumber).HasMaxLength(40);
        b.Property(x => x.Reason).HasMaxLength(300);
        b.Property(x => x.GainLossAccountCode).HasMaxLength(20);
        b.Property(x => x.StandsAvailableAccountCode).HasMaxLength(20);
        b.Property(x => x.RefundAccountCode).HasMaxLength(20);
        b.Property(x => x.NetRealisableValue).HasPrecision(19, 4);
        b.Property(x => x.Refund).HasPrecision(19, 4);
        b.Property(x => x.SurrenderReference).HasMaxLength(40);
        b.Property(x => x.RejectionReason).HasMaxLength(300);
        b.Property(x => x.Status).HasConversion<int>();

        // One open request per stand: a second is refused until the first is decided.
        b.HasIndex(x => new { x.CompanyId, x.StandNumber }).IsUnique().HasFilter("status = 0").HasDatabaseName("ix_stand_surrender_request_one_pending_per_stand");
    }
}
