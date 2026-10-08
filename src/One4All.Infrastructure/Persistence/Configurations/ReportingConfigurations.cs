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

internal sealed class ReportSubscriptionConfiguration : IEntityTypeConfiguration<ReportSubscription>
{
    public void Configure(EntityTypeBuilder<ReportSubscription> b)
    {
        b.Property(x => x.DeliveryFolder).HasMaxLength(200);
        b.ToTable("report_subscription", Schemas.Reporting);
        b.HasKey(x => x.Id);
        b.Property(x => x.Code).HasMaxLength(40);
        b.Property(x => x.ReportCode).HasMaxLength(60);
        b.Property(x => x.ParametersJson).HasColumnType("jsonb");
        b.HasIndex(x => new { x.CompanyId, x.Code }).IsUnique();
    }
}

internal sealed class ReportDeliveryConfiguration : IEntityTypeConfiguration<ReportDelivery>
{
    public void Configure(EntityTypeBuilder<ReportDelivery> b)
    {
        b.ToTable("report_delivery", Schemas.Reporting);
        b.HasKey(x => x.Id);
        b.Property(x => x.Recipient).HasMaxLength(200);
        b.Property(x => x.FileName).HasMaxLength(200);
        b.Property(x => x.Detail).HasMaxLength(1000);
        b.HasOne<ReportSubscription>().WithMany().HasForeignKey(x => x.SubscriptionId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => x.SubscriptionId);
    }
}

internal sealed class ReportRunConfiguration : IEntityTypeConfiguration<ReportRun>
{
    public void Configure(EntityTypeBuilder<ReportRun> b)
    {
        b.ToTable("report_run", Schemas.Reporting);
        b.HasKey(x => x.Id);
        b.Property(x => x.ReportCode).HasMaxLength(60);
        b.Property(x => x.Purpose).HasConversion<int>();
        b.Property(x => x.Format).HasMaxLength(10);
        b.Property(x => x.Parameters).HasMaxLength(2000);
        b.Property(x => x.ContentHash).HasMaxLength(64);
        b.HasIndex(x => new { x.CompanyId, x.ReportCode, x.RunAt });
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

internal sealed class FeaturedReportConfiguration : IEntityTypeConfiguration<Core.Reporting.FeaturedReport>
{
    public void Configure(EntityTypeBuilder<Core.Reporting.FeaturedReport> b)
    {
        b.ToTable("featured_report", Schemas.Reporting);
        b.Property(x => x.ReportCode).HasMaxLength(40);
        b.Property(x => x.Source).HasMaxLength(40);
        b.HasIndex(x => new { x.CompanyId, x.ReportCode }).IsUnique();
    }
}
