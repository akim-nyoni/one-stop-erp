using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using One4All.Core.Municipal;

namespace One4All.Infrastructure.Persistence.Configurations;

internal sealed class ServiceOrderConfiguration : IEntityTypeConfiguration<ServiceOrder>
{
    public void Configure(EntityTypeBuilder<ServiceOrder> b)
    {
        b.ToTable("service_order", Schemas.Municipal);
        b.HasKey(x => x.Id);
        b.Property(x => x.OrderNumber).HasMaxLength(30);
        b.Property(x => x.ServiceType).HasConversion<int>();
        b.Property(x => x.Kind).HasConversion<int>();
        b.Property(x => x.Status).HasConversion<int>();
        b.Property(x => x.Arrears).HasPrecision(19, 4);
        b.Property(x => x.FeeInvoiceNumber).HasMaxLength(40);
        b.HasIndex(x => new { x.CompanyId, x.OrderNumber }).IsUnique();
        b.HasIndex(x => new { x.PropertyId, x.ServiceType }).IsUnique().HasFilter("status = 0").HasDatabaseName("ix_service_order_one_open");
    }
}

internal sealed class ClearanceCertificateConfiguration : IEntityTypeConfiguration<ClearanceCertificate>
{
    public void Configure(EntityTypeBuilder<ClearanceCertificate> b)
    {
        b.ToTable("clearance_certificate", Schemas.Municipal);
        b.HasKey(x => x.Id);
        b.Property(x => x.CertificateNumber).HasMaxLength(30);
        b.Property(x => x.AdvanceHeld).HasPrecision(19, 4);
        b.HasIndex(x => new { x.CompanyId, x.CertificateNumber }).IsUnique();
        b.HasIndex(x => x.PropertyId);
    }
}

internal sealed class CouncilLicenceConfiguration : IEntityTypeConfiguration<CouncilLicence>
{
    public void Configure(EntityTypeBuilder<CouncilLicence> b)
    {
        b.ToTable("council_licence", Schemas.Municipal, t => t.HasCheckConstraint("ck_council_licence_cycle", "cycle_months >= 1 AND fee >= 0"));
        b.HasKey(x => x.Id);
        b.Property(x => x.LicenceNumber).HasMaxLength(30);
        b.Property(x => x.Kind).HasConversion<int>();
        b.Property(x => x.Status).HasConversion<int>();
        b.Property(x => x.Description).HasMaxLength(200);
        b.Property(x => x.Fee).HasPrecision(19, 4);
        b.Property(x => x.RevenueAccountCode).HasMaxLength(20);
        b.Property(x => x.RevokedReason).HasMaxLength(500);
        b.HasIndex(x => new { x.CompanyId, x.LicenceNumber }).IsUnique();
        b.HasIndex(x => new { x.CompanyId, x.ExpiresOn });
        b.HasIndex(x => x.HolderCustomerId);
    }
}

internal sealed class CouncilInspectionConfiguration : IEntityTypeConfiguration<CouncilInspection>
{
    public void Configure(EntityTypeBuilder<CouncilInspection> b)
    {
        b.ToTable("council_inspection", Schemas.Municipal);
        b.HasKey(x => x.Id);
        b.Property(x => x.Inspector).HasMaxLength(120);
        b.Property(x => x.Notes).HasMaxLength(1000);
        b.HasOne<CouncilLicence>().WithMany().HasForeignKey(x => x.LicenceId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class PropertyValuationConfiguration : IEntityTypeConfiguration<PropertyValuation>
{
    public void Configure(EntityTypeBuilder<PropertyValuation> b)
    {
        b.ToTable("property_valuation", Schemas.Municipal, t => t.HasCheckConstraint("ck_property_valuation_value", "value >= 0"));
        b.HasKey(x => x.Id);
        b.Property(x => x.Value).HasPrecision(19, 4);
        b.Property(x => x.Kind).HasConversion<int>();
        b.Property(x => x.Reference).HasMaxLength(40);
        b.Property(x => x.Reason).HasMaxLength(500);
        b.HasOne<Property>().WithMany().HasForeignKey(x => x.PropertyId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.PropertyId, x.EffectiveFrom });
    }
}

internal sealed class ValuationObjectionConfiguration : IEntityTypeConfiguration<ValuationObjection>
{
    public void Configure(EntityTypeBuilder<ValuationObjection> b)
    {
        b.ToTable("valuation_objection", Schemas.Municipal);
        b.HasKey(x => x.Id);
        b.Property(x => x.ObjectionNumber).HasMaxLength(30);
        b.Property(x => x.ValueObjectedTo).HasPrecision(19, 4);
        b.Property(x => x.ClaimedValue).HasPrecision(19, 4);
        b.Property(x => x.DecidedValue).HasPrecision(19, 4);
        b.Property(x => x.Grounds).HasMaxLength(1000);
        b.Property(x => x.DecisionNotes).HasMaxLength(1000);
        b.Property(x => x.Status).HasConversion<int>();
        b.HasOne<PropertyValuation>().WithMany().HasForeignKey(x => x.ObjectedValuationId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.CompanyId, x.ObjectionNumber }).IsUnique();
        b.HasIndex(x => x.PropertyId).IsUnique().HasFilter("status = 0").HasDatabaseName("ix_valuation_objection_one_open");
    }
}

internal sealed class PrepaidSaleConfiguration : IEntityTypeConfiguration<PrepaidSale>
{
    public void Configure(EntityTypeBuilder<PrepaidSale> b)
    {
        b.ToTable("prepaid_sale", Schemas.Municipal, t => t.HasCheckConstraint("ck_prepaid_sale_amounts", "amount > 0 AND to_arrears >= 0 AND to_arrears <= amount AND units >= 0"));
        b.HasKey(x => x.Id);
        b.Property(x => x.SaleNumber).HasMaxLength(30);
        b.Property(x => x.ServiceType).HasConversion<int>();
        b.Property(x => x.MeterSerial).HasMaxLength(60);
        b.Property(x => x.Amount).HasPrecision(19, 4);
        b.Property(x => x.ToArrears).HasPrecision(19, 4);
        b.Property(x => x.Units).HasPrecision(19, 4);
        b.Property(x => x.Token).HasMaxLength(60);
        b.Property(x => x.Channel).HasMaxLength(40);
        b.HasIndex(x => new { x.CompanyId, x.SaleNumber }).IsUnique();
        b.HasIndex(x => new { x.CompanyId, x.SoldOn });
    }
}

internal sealed class PropertyOwnerConfiguration : IEntityTypeConfiguration<PropertyOwner>
{
    public void Configure(EntityTypeBuilder<PropertyOwner> b)
    {
        b.ToTable("property_owner", Schemas.Municipal, t => t.HasCheckConstraint("ck_property_owner_share", "share_percent > 0 AND share_percent <= 100 AND (owned_to IS NULL OR owned_to >= owned_from)"));
        b.HasKey(x => x.Id);
        b.Property(x => x.SharePercent).HasPrecision(9, 4);
        b.Property(x => x.Reference).HasMaxLength(40);
        b.Property(x => x.DeedsReference).HasMaxLength(60);
        b.HasOne<Property>().WithMany().HasForeignKey(x => x.PropertyId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.PropertyId, x.OwnedFrom });
        b.HasIndex(x => new { x.CompanyId, x.Reference });
        b.HasIndex(x => new { x.PropertyId, x.CustomerId }).IsUnique().HasFilter("owned_to IS NULL").HasDatabaseName("ix_property_owner_one_current_share");
    }
}

internal sealed class BillingRunConfiguration : IEntityTypeConfiguration<BillingRun>
{
    public void Configure(EntityTypeBuilder<BillingRun> b)
    {
        b.ToTable("billing_run", Schemas.Municipal, t => t.HasCheckConstraint("ck_billing_run_approver", "approved_by_user_id IS NULL OR approved_by_user_id <> proposed_by_user_id"));
        b.HasKey(x => x.Id);
        b.Property(x => x.RunNumber).HasMaxLength(30);
        b.Property(x => x.ServiceType).HasConversion<int>();
        b.Property(x => x.Status).HasConversion<int>();
        b.Property(x => x.Ward).HasMaxLength(60);
        b.Property(x => x.InvoicePrefix).HasMaxLength(20);
        b.HasMany(x => x.Lines).WithOne().HasForeignKey(x => x.BillingRunId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.HasIndex(x => new { x.CompanyId, x.RunNumber }).IsUnique();
        b.HasIndex(x => new { x.CompanyId, x.ServiceType, x.Period }).IsUnique().HasFilter("status = 0").HasDatabaseName("ix_billing_run_one_proposed");
    }
}

internal sealed class BillingRunLineConfiguration : IEntityTypeConfiguration<BillingRunLine>
{
    public void Configure(EntityTypeBuilder<BillingRunLine> b)
    {
        b.ToTable("billing_run_line", Schemas.Municipal);
        b.HasKey(x => x.Id);
        b.Property(x => x.StandNumber).HasMaxLength(60);
        b.Property(x => x.InvoiceNumber).HasMaxLength(60);
        b.Property(x => x.Charge).HasPrecision(19, 4);
        b.Property(x => x.PreviousCharge).HasPrecision(19, 4);
        b.Property(x => x.RevenueAccountCode).HasMaxLength(20);
        b.Property(x => x.Narrative).HasMaxLength(256);
        b.Property(x => x.Exceptions).HasConversion<int>();
        b.Property(x => x.Message).HasMaxLength(500);
        b.Ignore(x => x.IsBlocked);
        b.HasIndex(x => new { x.BillingRunId, x.PropertyId }).IsUnique();
    }
}

internal sealed class ReadingRouteConfiguration : IEntityTypeConfiguration<ReadingRoute>
{
    public void Configure(EntityTypeBuilder<ReadingRoute> b)
    {
        b.ToTable("reading_route", Schemas.Municipal);
        b.HasKey(x => x.Id);
        b.Property(x => x.Code).HasMaxLength(30);
        b.Property(x => x.Name).HasMaxLength(120);
        b.Property(x => x.ReaderName).HasMaxLength(120);
        b.HasMany(x => x.Stops).WithOne().HasForeignKey(x => x.ReadingRouteId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.Stops).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.HasIndex(x => new { x.CompanyId, x.Code }).IsUnique();
    }
}

internal sealed class ReadingRouteStopConfiguration : IEntityTypeConfiguration<ReadingRouteStop>
{
    public void Configure(EntityTypeBuilder<ReadingRouteStop> b)
    {
        b.ToTable("reading_route_stop", Schemas.Municipal);
        b.HasKey(x => x.Id);
        b.HasOne<Meter>().WithMany().HasForeignKey(x => x.MeterId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => x.MeterId).IsUnique();
        b.HasIndex(x => new { x.ReadingRouteId, x.Sequence }).IsUnique();
    }
}

internal sealed class ServiceDepositConfiguration : IEntityTypeConfiguration<ServiceDeposit>
{
    public void Configure(EntityTypeBuilder<ServiceDeposit> b)
    {
        b.ToTable("service_deposit", Schemas.Municipal, t => t.HasCheckConstraint("ck_service_deposit_amount", "amount > 0"));
        b.HasKey(x => x.Id);
        b.Property(x => x.Reference).HasMaxLength(30);
        b.Property(x => x.ServiceType).HasConversion<int>();
        b.Property(x => x.Status).HasConversion<int>();
        b.Property(x => x.Amount).HasPrecision(19, 4);
        b.Property(x => x.LiabilityAccountCode).HasMaxLength(20);
        b.HasOne<Property>().WithMany().HasForeignKey(x => x.PropertyId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.CompanyId, x.Reference }).IsUnique();
        b.HasIndex(x => new { x.PropertyId, x.ServiceType }).IsUnique().HasFilter("status = 0").HasDatabaseName("ix_service_deposit_one_held");
    }
}

internal sealed class FacilityConfiguration : IEntityTypeConfiguration<Facility>
{
    public void Configure(EntityTypeBuilder<Facility> b)
    {
        b.ToTable("facility", Schemas.Municipal, t => t.HasCheckConstraint("ck_facility_rate", "daily_rate >= 0"));
        b.HasKey(x => x.Id);
        b.Property(x => x.Code).HasMaxLength(30);
        b.Property(x => x.Name).HasMaxLength(120);
        b.Property(x => x.DailyRate).HasPrecision(19, 4);
        b.Property(x => x.RevenueAccountCode).HasMaxLength(20);
        b.HasIndex(x => new { x.CompanyId, x.Code }).IsUnique();
    }
}

internal sealed class FacilityBookingConfiguration : IEntityTypeConfiguration<FacilityBooking>
{
    public void Configure(EntityTypeBuilder<FacilityBooking> b)
    {
        b.ToTable("facility_booking", Schemas.Municipal, t => t.HasCheckConstraint("ck_facility_booking_dates", "to_date >= from_date AND fee >= 0"));
        b.HasKey(x => x.Id);
        b.Property(x => x.BookingNumber).HasMaxLength(30);
        b.Property(x => x.Fee).HasPrecision(19, 4);
        b.Property(x => x.Refunded).HasPrecision(19, 4);
        b.Property(x => x.Purpose).HasMaxLength(200);
        b.Property(x => x.Status).HasConversion<int>();
        b.HasOne<Facility>().WithMany().HasForeignKey(x => x.FacilityId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.CompanyId, x.BookingNumber }).IsUnique();
        b.HasIndex(x => new { x.FacilityId, x.FromDate });
    }
}

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
        b.Property(x => x.RatePerSquareMetre).HasPrecision(19, 6);
        b.Property(x => x.Category).HasConversion<int>();
        b.Property(x => x.Ward).HasMaxLength(60);
        b.HasMany(x => x.Blocks).WithOne().HasForeignKey(x => x.TariffId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.Blocks).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.HasMany(x => x.Seasons).WithOne().HasForeignKey(x => x.TariffId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.Seasons).UsePropertyAccessMode(PropertyAccessMode.Field);
        // A tariff code has effective-dated versions: one per code and effective date.
        b.HasIndex(x => new { x.CompanyId, x.Code, x.EffectiveFrom }).IsUnique().HasFilter("is_deleted = false");
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

internal sealed class TariffSeasonConfiguration : IEntityTypeConfiguration<TariffSeason>
{
    public void Configure(EntityTypeBuilder<TariffSeason> b)
    {
        b.ToTable("tariff_season", Schemas.Municipal, t => t.HasCheckConstraint("ck_tariff_season_months", "from_month BETWEEN 1 AND 12 AND to_month BETWEEN 1 AND 12"));
        b.HasKey(x => x.Id);
        b.Property(x => x.PerUnitRate).HasPrecision(19, 6);
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
        b.Property(x => x.LastActualReading).HasPrecision(19, 4);
        b.Property(x => x.EstimatedSinceActual).HasPrecision(19, 4);
        b.Property(x => x.UnitsCredit).HasPrecision(19, 4);
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
        b.Property(x => x.BilledConsumption).HasPrecision(19, 4);
        b.Property(x => x.Kind).HasConversion<int>();
        b.Property(x => x.Flags).HasConversion<int>();
        b.HasIndex(x => new { x.CompanyId, x.MeterId, x.ReadingDate });
    }
}
