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
