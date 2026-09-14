using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using One4All.Core.Audit;
using One4All.Infrastructure.Persistence.Converters;

namespace One4All.Infrastructure.Persistence.Configurations;

internal sealed class ChangeLogEntryConfiguration : IEntityTypeConfiguration<ChangeLogEntry>
{
    public void Configure(EntityTypeBuilder<ChangeLogEntry> b)
    {
        b.ToTable("change_log", Schemas.Audit);
        b.HasKey(x => x.Id);

        b.Property(x => x.UserName).HasMaxLength(200);
        b.Property(x => x.Terminal).HasMaxLength(200);
        b.Property(x => x.IpAddress).HasMaxLength(64);
        b.Property(x => x.SessionId).HasMaxLength(64);
        b.Property(x => x.Module).HasMaxLength(60);
        b.Property(x => x.EntityType).HasMaxLength(120);
        b.Property(x => x.EntityId).HasMaxLength(64);
        b.Property(x => x.Action).HasConversion<int>();
        b.Property(x => x.OldValues).HasColumnType("jsonb");
        b.Property(x => x.NewValues).HasColumnType("jsonb");
        b.Property(x => x.Reason).HasMaxLength(1024);
        b.Property(x => x.CorrelationId).HasMaxLength(64);
        b.Property(x => x.RowHash).HasMaxLength(64);

        b.Property(x => x.ChangedFields)
            .HasColumnName("changed_fields")
            .HasColumnType("text")
            .HasConversion(new StringListConverter())
            .Metadata.SetValueComparer(Comparers.StringList);

        b.HasIndex(x => new { x.CompanyId, x.OccurredOnUtc });
        b.HasIndex(x => new { x.EntityType, x.EntityId });
        b.HasIndex(x => x.UserId);
    }
}
