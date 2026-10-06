using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using One4All.Core.Crm;

namespace One4All.Infrastructure.Persistence.Configurations;

/// <summary>Maps leads (<c>crm.lead</c>).</summary>
internal sealed class LeadConfiguration : IEntityTypeConfiguration<Lead>
{
    public void Configure(EntityTypeBuilder<Lead> b)
    {
        b.ToTable("lead", Schemas.Crm);
        b.HasKey(x => x.Id);
        b.Property(x => x.Name).HasMaxLength(200);
        b.Property(x => x.Organisation).HasMaxLength(200);
        b.Property(x => x.Email).HasMaxLength(256);
        b.Property(x => x.Phone).HasMaxLength(30);
        b.Property(x => x.Source).HasMaxLength(60);
        b.Property(x => x.Status).HasConversion<int>();
        b.Property(x => x.DisqualifiedReason).HasMaxLength(300);
        b.HasIndex(x => new { x.CompanyId, x.Status });
        b.HasIndex(x => new { x.CompanyId, x.Email });
    }
}

/// <summary>Maps activities (<c>crm.activity</c>).</summary>
internal sealed class CrmActivityConfiguration : IEntityTypeConfiguration<CrmActivity>
{
    public void Configure(EntityTypeBuilder<CrmActivity> b)
    {
        b.ToTable("activity", Schemas.Crm);
        b.HasKey(x => x.Id);
        b.Property(x => x.Type).HasConversion<int>();
        b.Property(x => x.Subject).HasMaxLength(200);
        b.Property(x => x.Notes).HasMaxLength(2000);
        b.HasIndex(x => new { x.CompanyId, x.OwnerUserId, x.DueOnUtc });
        b.HasIndex(x => x.LeadId);
        b.HasIndex(x => x.OpportunityId);
        b.HasIndex(x => x.CustomerId);
    }
}
