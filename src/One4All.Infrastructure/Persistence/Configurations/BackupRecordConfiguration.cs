using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using One4All.Core.System;

namespace One4All.Infrastructure.Persistence.Configurations;

/// <summary>Maps backups (<c>sys.backup</c>).</summary>
internal sealed class BackupRecordConfiguration : IEntityTypeConfiguration<BackupRecord>
{
    public void Configure(EntityTypeBuilder<BackupRecord> b)
    {
        b.ToTable("backup", Schemas.System);
        b.HasKey(x => x.Id);
        b.Property(x => x.FileName).HasMaxLength(500);
        b.Property(x => x.Reason).HasMaxLength(200);
        b.Property(x => x.Sha256).HasMaxLength(64);
        b.Property(x => x.Status).HasConversion<int>();
        b.Property(x => x.Message).HasMaxLength(600);
        b.Property(x => x.OffsiteCopy).HasMaxLength(500);
    }
}
