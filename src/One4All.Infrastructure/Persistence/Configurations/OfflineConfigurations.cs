using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using One4All.Core.Offline;

namespace One4All.Infrastructure.Persistence.Configurations;

/// <summary>Maps the offline outbox (<c>sync.offline_operation</c>).</summary>
internal sealed class OfflineOperationConfiguration : IEntityTypeConfiguration<OfflineOperation>
{
    public void Configure(EntityTypeBuilder<OfflineOperation> b)
    {
        b.ToTable("offline_operation", Schemas.Sync);
        b.HasKey(x => x.Id);
        b.Property(x => x.Kind).HasMaxLength(60);
        b.Property(x => x.PayloadJson).HasColumnType("jsonb");
        b.Property(x => x.Status).HasConversion<int>();
        b.Property(x => x.LastError).HasMaxLength(2000);

        // The client-generated operation id is the idempotency key: enqueue-once is enforced here.
        b.HasIndex(x => x.OperationId).IsUnique();

        // The drain reads pending operations in capture order.
        b.HasIndex(x => new { x.Status, x.CreatedUtc });
    }
}
