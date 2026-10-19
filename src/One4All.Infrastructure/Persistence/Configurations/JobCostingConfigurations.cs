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
        b.HasQueryFilter(One4AllDbContext.SoftDeleteFilter, x => !x.IsDeleted);
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
        b.HasIndex(x => x.SalesOrderId);
        b.HasIndex(x => x.PurchaseOrderId);
        b.Property(x => x.GrnNumber).HasMaxLength(40);
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
        b.Property(x => x.InvoiceNumber).HasMaxLength(40);
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

internal sealed class VariationOrderConfiguration : IEntityTypeConfiguration<VariationOrder>
{
    public void Configure(EntityTypeBuilder<VariationOrder> b)
    {
        b.ToTable("variation_order", Schemas.JobCosting);
        b.HasKey(x => x.Id);
        b.Property(x => x.Number).HasMaxLength(40);
        b.Property(x => x.Description).HasMaxLength(300);
        b.Property(x => x.ValueImpact).HasPrecision(19, 4);
        b.Property(x => x.CostImpact).HasPrecision(19, 4);
        b.Property(x => x.Status).HasConversion<int>();
        b.Property(x => x.DecisionNote).HasMaxLength(300);
        b.HasIndex(x => new { x.JobContractId, x.Number }).IsUnique();
    }
}

internal sealed class BoqItemConfiguration : IEntityTypeConfiguration<BoqItem>
{
    public void Configure(EntityTypeBuilder<BoqItem> b)
    {
        b.ToTable("boq_item", Schemas.JobCosting);
        b.HasKey(x => x.Id);
        b.Property(x => x.ItemCode).HasMaxLength(40);
        b.Property(x => x.Description).HasMaxLength(300);
        b.Property(x => x.Unit).HasMaxLength(20);
        b.Property(x => x.Quantity).HasPrecision(19, 4);
        b.Property(x => x.Rate).HasPrecision(19, 4);
        b.Property(x => x.MeasuredQuantity).HasPrecision(19, 4);
        b.Property(x => x.CostHead).HasMaxLength(60);
        b.Ignore(x => x.Value);
        b.HasIndex(x => new { x.JobContractId, x.ItemCode }).IsUnique();
    }
}

internal sealed class JobTimesheetConfiguration : IEntityTypeConfiguration<JobTimesheet>
{
    public void Configure(EntityTypeBuilder<JobTimesheet> b)
    {
        b.ToTable("timesheet", Schemas.JobCosting);
        b.HasKey(x => x.Id);
        b.Property(x => x.Hours).HasPrecision(9, 2);
        b.Property(x => x.HourlyCost).HasPrecision(19, 4);
        b.Property(x => x.CostHead).HasMaxLength(60);
        b.Property(x => x.CapitalisedJournal).HasMaxLength(40);
        b.Ignore(x => x.Cost);
        b.HasIndex(x => new { x.EmployeeId, x.Date });
        b.HasIndex(x => x.JobContractId);
    }
}

internal sealed class SubcontractConfiguration : IEntityTypeConfiguration<Subcontract>
{
    public void Configure(EntityTypeBuilder<Subcontract> b)
    {
        b.ToTable("subcontract", Schemas.JobCosting);
        b.HasKey(x => x.Id);
        b.Property(x => x.CostHead).HasMaxLength(60).HasDefaultValue("Subcontract");
        b.Property(x => x.WithholdingTaxCode).HasMaxLength(20);
        b.Property(x => x.AdvancePaid).HasPrecision(19, 4);
        b.Property(x => x.AdvanceRecovered).HasPrecision(19, 4);
        b.Property(x => x.AdvanceRecoveryPercent).HasPrecision(9, 4);
        b.Property(x => x.AdvanceAccountCode).HasMaxLength(20);
        b.Ignore(x => x.AdvanceOutstanding);
        b.Property(x => x.Reference).HasMaxLength(40);
        b.Property(x => x.Description).HasMaxLength(200);
        b.Property(x => x.Value).HasPrecision(19, 4);
        b.Property(x => x.RetentionPercent).HasPrecision(9, 4);
        b.Property(x => x.RetentionReleased).HasPrecision(19, 4);
        b.Ignore(x => x.CertifiedToDate);
        b.Ignore(x => x.RetentionHeld);
        b.HasIndex(x => new { x.CompanyId, x.Reference }).IsUnique();
        b.HasMany(x => x.Certificates).WithOne().HasForeignKey(l => l.SubcontractId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.Certificates).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class SubcontractCertificateConfiguration : IEntityTypeConfiguration<SubcontractCertificate>
{
    public void Configure(EntityTypeBuilder<SubcontractCertificate> b)
    {
        b.ToTable("subcontract_certificate", Schemas.JobCosting);
        b.HasKey(x => x.Id);
        b.Property(x => x.Number).HasMaxLength(40);
        b.Property(x => x.InvoiceNumber).HasMaxLength(80);
        b.Property(x => x.WorkDoneToDate).HasPrecision(19, 4);
        b.Property(x => x.Gross).HasPrecision(19, 4);
        b.Property(x => x.Retention).HasPrecision(19, 4);
        b.Property(x => x.AdvanceRecovered).HasPrecision(19, 4);
        b.Property(x => x.Withheld).HasPrecision(19, 4);
        b.Ignore(x => x.Payable);
    }
}

internal sealed class PlantItemConfiguration : IEntityTypeConfiguration<PlantItem>
{
    public void Configure(EntityTypeBuilder<PlantItem> b)
    {
        b.ToTable("plant_item", Schemas.JobCosting);
        b.HasKey(x => x.Id);
        b.Property(x => x.Code).HasMaxLength(20);
        b.Property(x => x.Name).HasMaxLength(120);
        b.Property(x => x.HireRatePerHour).HasPrecision(19, 4);
        b.HasIndex(x => new { x.CompanyId, x.Code }).IsUnique();
    }
}

internal sealed class PlantLogConfiguration : IEntityTypeConfiguration<PlantLog>
{
    public void Configure(EntityTypeBuilder<PlantLog> b)
    {
        b.ToTable("plant_log", Schemas.JobCosting);
        b.HasKey(x => x.Id);
        b.Property(x => x.Hours).HasPrecision(9, 2);
        b.Property(x => x.HireCharge).HasPrecision(19, 4);
        b.Property(x => x.RunningCost).HasPrecision(19, 4);
        b.Property(x => x.Description).HasMaxLength(200);
        b.HasIndex(x => new { x.PlantItemId, x.Date });
    }
}

internal sealed class JobWbsElementConfiguration : IEntityTypeConfiguration<JobWbsElement>
{
    public void Configure(EntityTypeBuilder<JobWbsElement> b)
    {
        b.ToTable("wbs_element", Schemas.JobCosting);
        b.HasKey(x => x.Id);
        b.Property(x => x.Code).HasMaxLength(70);
        b.Property(x => x.Name).HasMaxLength(200);
        b.Property(x => x.CostHead).HasMaxLength(60);
        b.Property(x => x.BudgetCost).HasPrecision(19, 4);
        b.Property(x => x.EstimateToComplete).HasPrecision(19, 4);
        b.Ignore(x => x.ParentCode);
        b.HasIndex(x => new { x.JobContractId, x.Code }).IsUnique();
    }
}

internal sealed class JobOverheadRateConfiguration : IEntityTypeConfiguration<JobOverheadRate>
{
    public void Configure(EntityTypeBuilder<JobOverheadRate> b)
    {
        b.ToTable("overhead_rate", Schemas.JobCosting);
        b.HasKey(x => x.Id);
        b.Property(x => x.AppliesTo).HasConversion<int>();
        b.Property(x => x.Percent).HasPrecision(9, 4);
        b.Property(x => x.CostHead).HasMaxLength(60);
        b.HasIndex(x => new { x.CompanyId, x.AppliesTo }).IsUnique();
    }
}
