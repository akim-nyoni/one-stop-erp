using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using One4All.Core.Audit;

namespace One4All.Infrastructure.Persistence.Configurations;

/// <summary>Maps trigger-captured changes (<c>audit.db_change</c>); the application only reads them.</summary>
internal sealed class DatabaseChangeConfiguration : IEntityTypeConfiguration<DatabaseChange>
{
    public void Configure(EntityTypeBuilder<DatabaseChange> b)
    {
        b.ToTable("db_change", Schemas.Audit);
        b.HasKey(x => x.Id);
        b.Property(x => x.OccurredOnUtc).HasDefaultValueSql("now()");
        b.Property(x => x.TableName).HasMaxLength(120);
        b.Property(x => x.Operation).HasMaxLength(10);
        b.Property(x => x.RowId).HasMaxLength(40);
        b.Property(x => x.OldValues).HasColumnType("jsonb");
        b.Property(x => x.NewValues).HasColumnType("jsonb");
        b.Property(x => x.DbUser).HasMaxLength(120);
        b.Property(x => x.ApplicationName).HasMaxLength(120);
        b.Property(x => x.ClientAddress).HasMaxLength(60);
        b.HasIndex(x => x.OccurredOnUtc);
    }
}
