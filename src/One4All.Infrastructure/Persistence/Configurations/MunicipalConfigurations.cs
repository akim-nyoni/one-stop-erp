using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using One4All.Core.Municipal;

namespace One4All.Infrastructure.Persistence.Configurations;

internal sealed class IndigentRegistrationConfiguration : IEntityTypeConfiguration<IndigentRegistration>
{
    public void Configure(EntityTypeBuilder<IndigentRegistration> b)
    {
        b.ToTable("indigent_registration", Schemas.Municipal);
        b.HasKey(x => x.Id);
        b.Property(x => x.HouseholdIncome).HasPrecision(19, 4);
        b.Property(x => x.SubsidyPercent).HasPrecision(9, 4);
        b.Property(x => x.Status).HasConversion<int>();
        b.HasIndex(x => x.PropertyId).IsUnique().HasFilter("status = 0").HasDatabaseName("ix_indigent_registration_one_active");
    }
}

internal sealed class TariffConfiguration : IEntityTypeConfiguration<Tariff>
{
    public void Configure(EntityTypeBuilder<Tariff> b)
    {
        b.ToTable("tariff", Schemas.Municipal);
        b.HasKey(x => x.Id);
        b.Property(x => x.Code).HasMaxLength(30);
        b.Property(x => x.Name).HasMaxLength(120);
        b.Property(x => x.ServiceType).HasConversion<int>();
        b.Property(x => x.TariffType).HasConversion<int>();
        b.Property(x => x.RevenueAccountCode).HasMaxLength(8);
        b.Property(x => x.PerUnitRate).HasPrecision(19, 6);
        b.Property(x => x.RatePerValuationCents).HasPrecision(19, 6);
        b.HasMany(x => x.Blocks).WithOne().HasForeignKey(x => x.TariffId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.Blocks).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.HasIndex(x => new { x.CompanyId, x.Code }).IsUnique().HasFilter("is_deleted = false");
        b.HasQueryFilter(x => !x.IsDeleted);
    }
}

internal sealed class TariffBlockConfiguration : IEntityTypeConfiguration<TariffBlock>
{
    public void Configure(EntityTypeBuilder<TariffBlock> b)
    {
        b.ToTable("tariff_block", Schemas.Municipal);
        b.HasKey(x => x.Id);
        b.Property(x => x.FromUnit).HasPrecision(19, 4);
        b.Property(x => x.ToUnit).HasPrecision(19, 4);
        b.Property(x => x.Rate).HasPrecision(19, 6);
    }
}

internal sealed class PropertyConfiguration : IEntityTypeConfiguration<Property>
{
    public void Configure(EntityTypeBuilder<Property> b)
    {
        b.ToTable("property", Schemas.Municipal);
        b.HasKey(x => x.Id);
        b.Property(x => x.StandNumber).HasMaxLength(40);
        b.Property(x => x.Address).HasMaxLength(300);
        b.Property(x => x.Ward).HasMaxLength(60);
        b.Property(x => x.Zone).HasMaxLength(60);
        b.Property(x => x.LandUse).HasMaxLength(60);
        b.Property(x => x.SizeSquareMetres).HasPrecision(19, 4);
        b.HasMany(x => x.Services).WithOne().HasForeignKey(x => x.PropertyId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.Services).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.HasIndex(x => new { x.CompanyId, x.StandNumber }).IsUnique().HasFilter("is_deleted = false");
        b.HasQueryFilter(x => !x.IsDeleted);
    }
}

internal sealed class PropertyServiceLinkConfiguration : IEntityTypeConfiguration<PropertyServiceLink>
{
    public void Configure(EntityTypeBuilder<PropertyServiceLink> b)
    {
        b.ToTable("property_service", Schemas.Municipal);
        b.HasKey(x => x.Id);
        b.Property(x => x.ServiceType).HasConversion<int>();
        b.HasIndex(x => new { x.PropertyId, x.ServiceType });
    }
}

internal sealed class MeterConfiguration : IEntityTypeConfiguration<Meter>
{
    public void Configure(EntityTypeBuilder<Meter> b)
    {
        b.ToTable("meter", Schemas.Municipal);
        b.HasKey(x => x.Id);
        b.Property(x => x.Serial).HasMaxLength(60);
        b.Property(x => x.Multiplier).HasPrecision(19, 6);
        b.Property(x => x.LastReading).HasPrecision(19, 4);
        b.HasIndex(x => new { x.CompanyId, x.Serial }).IsUnique().HasFilter("is_deleted = false");
        b.HasQueryFilter(x => !x.IsDeleted);
    }
}

internal sealed class MeterReadingConfiguration : IEntityTypeConfiguration<MeterReading>
{
    public void Configure(EntityTypeBuilder<MeterReading> b)
    {
        b.ToTable("meter_reading", Schemas.Municipal);
        b.HasKey(x => x.Id);
        b.Property(x => x.Reading).HasPrecision(19, 4);
        b.Property(x => x.PreviousReading).HasPrecision(19, 4);
        b.Property(x => x.Consumption).HasPrecision(19, 4);
        b.HasIndex(x => new { x.CompanyId, x.MeterId, x.ReadingDate });
    }
}
