using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using One4All.Core.Housing;

namespace One4All.Infrastructure.Persistence.Configurations;

internal sealed class LandParcelConfiguration : IEntityTypeConfiguration<LandParcel>
{
    public void Configure(EntityTypeBuilder<LandParcel> b)
    {
        b.ToTable("land_parcel", Schemas.Housing);
        b.HasKey(x => x.Id);
        b.Property(x => x.ParcelNumber).HasMaxLength(40);
        b.Property(x => x.Description).HasMaxLength(300);
        b.Property(x => x.AreaSquareMetres).HasPrecision(19, 4);
        b.Property(x => x.Status).HasConversion<int>();
        b.HasIndex(x => new { x.CompanyId, x.ParcelNumber }).IsUnique().HasFilter("is_deleted = false");
        b.HasQueryFilter(x => !x.IsDeleted);
    }
}

internal sealed class HousingSchemeConfiguration : IEntityTypeConfiguration<HousingScheme>
{
    public void Configure(EntityTypeBuilder<HousingScheme> b)
    {
        b.ToTable("scheme", Schemas.Housing);
        b.HasKey(x => x.Id);
        b.Property(x => x.Code).HasMaxLength(30);
        b.Property(x => x.Name).HasMaxLength(120);
        b.Property(x => x.SchemeType).HasConversion<int>();
        b.HasIndex(x => new { x.CompanyId, x.Code }).IsUnique().HasFilter("is_deleted = false");
        b.HasQueryFilter(x => !x.IsDeleted);
    }
}

internal sealed class HousingStandConfiguration : IEntityTypeConfiguration<HousingStand>
{
    public void Configure(EntityTypeBuilder<HousingStand> b)
    {
        b.ToTable("stand", Schemas.Housing);
        b.HasKey(x => x.Id);
        b.Property(x => x.StandNumber).HasMaxLength(40);
        b.Property(x => x.SizeSquareMetres).HasPrecision(19, 4);
        b.Property(x => x.Status).HasConversion<int>();
        b.HasMany(x => x.Ownership).WithOne().HasForeignKey(x => x.HousingStandId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.Ownership).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.HasIndex(x => new { x.CompanyId, x.StandNumber }).IsUnique().HasFilter("is_deleted = false");
        b.HasQueryFilter(x => !x.IsDeleted);
    }
}

internal sealed class OwnershipRecordConfiguration : IEntityTypeConfiguration<OwnershipRecord>
{
    public void Configure(EntityTypeBuilder<OwnershipRecord> b)
    {
        b.ToTable("ownership_record", Schemas.Housing);
        b.HasKey(x => x.Id);
        b.Property(x => x.ChangeType).HasConversion<int>();
        b.Property(x => x.HolderName).HasMaxLength(200);
        b.Property(x => x.PreviousHash).HasMaxLength(64);
        b.Property(x => x.Hash).HasMaxLength(64);
        b.HasIndex(x => new { x.HousingStandId, x.Sequence }).IsUnique();
    }
}

internal sealed class HousingApplicantConfiguration : IEntityTypeConfiguration<HousingApplicant>
{
    public void Configure(EntityTypeBuilder<HousingApplicant> b)
    {
        b.ToTable("applicant", Schemas.Housing);
        b.HasKey(x => x.Id);
        b.Property(x => x.IdNumber).HasMaxLength(40);
        b.Property(x => x.FullName).HasMaxLength(200);
        b.HasIndex(x => new { x.CompanyId, x.IdNumber });
        b.HasQueryFilter(x => !x.IsDeleted);
    }
}
