using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using One4All.Core.JobCosting;

namespace One4All.Infrastructure.Persistence.Configurations;

internal sealed class JobContractConfiguration : IEntityTypeConfiguration<JobContract>
{
    public void Configure(EntityTypeBuilder<JobContract> b)
    {
        b.ToTable("contract", Schemas.JobCosting);
        b.HasKey(x => x.Id);
        b.Property(x => x.ContractNumber).HasMaxLength(40);
        b.Property(x => x.Name).HasMaxLength(200);
        b.Property(x => x.RevenueAccountCode).HasMaxLength(8);
        b.Property(x => x.RetentionPercent).HasPrecision(9, 4);
        b.Property(x => x.RevenueAdjustmentToDate).HasPrecision(19, 4);
        b.Property(x => x.LossProvisionToDate).HasPrecision(19, 4);
        b.Ignore(x => x.ExpectedLoss);
        b.Property(x => x.Status).HasConversion<int>();
        b.Ignore(x => x.ActualCost);
        b.Ignore(x => x.PercentComplete);
        b.HasMany(x => x.Costs).WithOne().HasForeignKey(x => x.JobContractId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.Costs).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.HasIndex(x => new { x.CompanyId, x.ContractNumber }).IsUnique().HasFilter("is_deleted = false");
        b.HasQueryFilter(x => !x.IsDeleted);
    }
}

internal sealed class JobCostEntryConfiguration : IEntityTypeConfiguration<JobCostEntry>
{
    public void Configure(EntityTypeBuilder<JobCostEntry> b)
    {
        b.ToTable("cost_entry", Schemas.JobCosting);
        b.HasKey(x => x.Id);
        b.Property(x => x.CostHead).HasMaxLength(80);
        b.Property(x => x.Source).HasConversion<int>();
        b.Property(x => x.Reference).HasMaxLength(60);
        b.HasIndex(x => new { x.CompanyId, x.JobContractId });
    }
}

internal sealed class ProgressCertificateConfiguration : IEntityTypeConfiguration<ProgressCertificate>
{
    public void Configure(EntityTypeBuilder<ProgressCertificate> b)
    {
        b.ToTable("progress_certificate", Schemas.JobCosting);
        b.HasKey(x => x.Id);
        b.Property(x => x.CertificateNumber).HasMaxLength(40);
        b.HasIndex(x => new { x.CompanyId, x.CertificateNumber }).IsUnique();
    }
}

internal sealed class WeighbridgeTicketConfiguration : IEntityTypeConfiguration<WeighbridgeTicket>
{
    public void Configure(EntityTypeBuilder<WeighbridgeTicket> b)
    {
        b.ToTable("weighbridge_ticket", Schemas.JobCosting);
        b.HasKey(x => x.Id);
        b.Property(x => x.TicketNumber).HasMaxLength(40);
        b.Property(x => x.VehicleRegistration).HasMaxLength(20);
        b.Property(x => x.Product).HasMaxLength(80);
        b.Property(x => x.GrossWeightKg).HasPrecision(19, 4);
        b.Property(x => x.TareWeightKg).HasPrecision(19, 4);
        b.Property(x => x.DeductionKg).HasPrecision(19, 4);
        b.Property(x => x.CubicMetresPerTonne).HasPrecision(19, 6);
        b.Property(x => x.Direction).HasConversion<int>();
        b.Property(x => x.DriverName).HasMaxLength(120);
        b.Property(x => x.SiteName).HasMaxLength(120);
        b.Property(x => x.ManualReason).HasMaxLength(200);
        b.Property(x => x.DistanceKm).HasPrecision(19, 2);
        b.Property(x => x.HaulageZoneCode).HasMaxLength(20);
        b.Property(x => x.HaulageCharge).HasPrecision(19, 4);
        b.Property(x => x.BatchRunNumber).HasMaxLength(40);
        b.Ignore(x => x.NetWeightKg);
        b.Ignore(x => x.NetTonnes);
        b.Ignore(x => x.NetCubicMetres);
        b.HasIndex(x => new { x.CompanyId, x.TicketNumber }).IsUnique();
    }
}

internal sealed class WeighbridgeVehicleConfiguration : IEntityTypeConfiguration<WeighbridgeVehicle>
{
    public void Configure(EntityTypeBuilder<WeighbridgeVehicle> b)
    {
        b.ToTable("weighbridge_vehicle", Schemas.JobCosting);
        b.HasKey(x => x.Id);
        b.Property(x => x.Registration).HasMaxLength(20);
        b.Property(x => x.Haulier).HasMaxLength(120);
        b.Property(x => x.StoredTareKg).HasPrecision(19, 4);
        b.Property(x => x.MaxGrossKg).HasPrecision(19, 4);
        b.HasIndex(x => new { x.CompanyId, x.Registration }).IsUnique();
    }
}

internal sealed class HaulageZoneConfiguration : IEntityTypeConfiguration<HaulageZone>
{
    public void Configure(EntityTypeBuilder<HaulageZone> b)
    {
        b.ToTable("haulage_zone", Schemas.JobCosting);
        b.HasKey(x => x.Id);
        b.Property(x => x.Code).HasMaxLength(20);
        b.Property(x => x.Name).HasMaxLength(120);
        b.Property(x => x.FromKm).HasPrecision(19, 2);
        b.Property(x => x.ToKm).HasPrecision(19, 2);
        b.Property(x => x.RatePerTonne).HasPrecision(19, 4);
        b.Property(x => x.MinimumCharge).HasPrecision(19, 4);
        b.HasIndex(x => new { x.CompanyId, x.Code }).IsUnique();
    }
}
