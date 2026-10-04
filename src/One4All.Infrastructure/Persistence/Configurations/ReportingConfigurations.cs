using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using One4All.Core.Reporting;

namespace One4All.Infrastructure.Persistence.Configurations;

internal sealed class ReportDefinitionRecordConfiguration : IEntityTypeConfiguration<ReportDefinitionRecord>
{
    public void Configure(EntityTypeBuilder<ReportDefinitionRecord> b)
    {
        b.ToTable("report_definition", Schemas.Reporting);
        b.HasKey(x => x.Id);
        b.Property(x => x.Code).HasMaxLength(60);
        b.Property(x => x.Name).HasMaxLength(160);
        b.Property(x => x.Module).HasMaxLength(60);
        b.Property(x => x.Scope).HasConversion<int>();
        b.Property(x => x.DefinitionJson).HasColumnType("jsonb");
        b.HasIndex(x => new { x.Code, x.Scope, x.CompanyId, x.OwnerUserId }).IsUnique();
    }
}

internal sealed class ReportDefinitionVersionConfiguration : IEntityTypeConfiguration<ReportDefinitionVersion>
{
    public void Configure(EntityTypeBuilder<ReportDefinitionVersion> b)
    {
        b.ToTable("report_definition_version", Schemas.Reporting);
        b.HasKey(x => x.Id);
        b.Property(x => x.DefinitionJson).HasColumnType("jsonb");
        b.Property(x => x.Note).HasMaxLength(300);
        b.HasOne<ReportDefinitionRecord>().WithMany().HasForeignKey(x => x.ReportDefinitionId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(x => new { x.ReportDefinitionId, x.Version }).IsUnique();
    }
}
