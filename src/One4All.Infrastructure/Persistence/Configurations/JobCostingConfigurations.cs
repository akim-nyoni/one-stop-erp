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
        b.Ignore(x => x.NetWeightKg);
        b.Ignore(x => x.NetTonnes);
        b.Ignore(x => x.NetCubicMetres);
        b.HasIndex(x => new { x.CompanyId, x.TicketNumber }).IsUnique();
    }
}
