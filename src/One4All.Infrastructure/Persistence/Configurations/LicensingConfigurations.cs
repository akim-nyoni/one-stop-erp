using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using One4All.Core.Licensing;

namespace One4All.Infrastructure.Persistence.Configurations;

internal sealed class InstalledLicenceConfiguration : IEntityTypeConfiguration<InstalledLicence>
{
    public void Configure(EntityTypeBuilder<InstalledLicence> b)
    {
        b.ToTable("installed_licence", Schemas.Licensing);
        b.HasKey(x => x.Id);
        b.Property(x => x.Token).HasColumnType("text");
        b.HasIndex(x => x.IsActive).HasFilter("is_active = true");
    }
}

internal sealed class InstalledRevocationListConfiguration : IEntityTypeConfiguration<InstalledRevocationList>
{
    public void Configure(EntityTypeBuilder<InstalledRevocationList> b)
    {
        b.ToTable("installed_revocation_list", Schemas.Licensing);
        b.HasKey(x => x.Id);
        b.Property(x => x.Token).HasColumnType("text");
        b.HasIndex(x => x.Sequence).IsUnique();
    }
}

internal sealed class ClockMarkConfiguration : IEntityTypeConfiguration<One4All.Infrastructure.Licensing.ClockMark>
{
    public void Configure(EntityTypeBuilder<One4All.Infrastructure.Licensing.ClockMark> b)
    {
        b.ToTable("clock_mark", Schemas.Licensing);
        b.HasKey(x => x.Id);
        b.Property(x => x.Sealed).HasMaxLength(200);
    }
}
