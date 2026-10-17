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
        b.Property(x => x.Locality).HasMaxLength(120);
        b.Property(x => x.Ward).HasMaxLength(40);
        b.Property(x => x.LandUse).HasMaxLength(80);
        b.Property(x => x.Improvements).HasMaxLength(500);
        b.Property(x => x.Tenure).HasConversion<int?>();
        b.Property(x => x.Occupation).HasConversion<int?>();
        b.Property(x => x.Description).HasMaxLength(300);
        b.Property(x => x.AreaSquareMetres).HasPrecision(19, 4);
        b.Property(x => x.Status).HasConversion<int>();
        b.OwnsOne(x => x.Boundary, o =>
        {
            o.Property(p => p.Wkt).HasColumnName("boundary_wkt").HasMaxLength(20000);
            o.Property(p => p.CentroidLongitude).HasColumnName("centroid_longitude");
            o.Property(p => p.CentroidLatitude).HasColumnName("centroid_latitude");
            o.Property(p => p.AreaSquareMetres).HasColumnName("mapped_area_m2").HasPrecision(19, 4);
        });
        b.HasIndex(x => new { x.CompanyId, x.ParcelNumber }).IsUnique().HasFilter("is_deleted = false");
        b.HasQueryFilter(One4AllDbContext.SoftDeleteFilter, x => !x.IsDeleted);
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
        b.Property(x => x.Ward).HasMaxLength(40);
        b.Property(x => x.FundingSource).HasConversion<int?>();
        b.Property(x => x.SchemeType).HasConversion<int>();
        b.HasIndex(x => new { x.CompanyId, x.Code }).IsUnique().HasFilter("is_deleted = false");
        b.HasQueryFilter(One4AllDbContext.SoftDeleteFilter, x => !x.IsDeleted);
    }
}

internal sealed class StandRemarkConfiguration : IEntityTypeConfiguration<StandRemark>
{
    public void Configure(EntityTypeBuilder<StandRemark> b)
    {
        b.ToTable("stand_remark", Schemas.Housing);
        b.HasKey(x => x.Id);
        b.Property(x => x.Text).HasColumnType("text");
        b.Property(x => x.RecordedBy).HasMaxLength(120);
        b.HasOne<HousingStand>().WithMany().HasForeignKey(x => x.StandId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => x.StandId);
    }
}

internal sealed class HousingStandConfiguration : IEntityTypeConfiguration<HousingStand>
{
    public void Configure(EntityTypeBuilder<HousingStand> b)
    {
        b.ToTable("stand", Schemas.Housing);
        b.HasKey(x => x.Id);
        b.Property(x => x.StandNumber).HasMaxLength(40);
        b.Property(x => x.StreetAddress).HasMaxLength(200);
        b.Property(x => x.Ward).HasMaxLength(40);
        b.Property(x => x.Suburb).HasMaxLength(80);
        b.Property(x => x.Zone).HasMaxLength(40);
        b.Property(x => x.IntendedUse).HasMaxLength(60);
        b.Property(x => x.Zoning).HasMaxLength(60);
        b.Property(x => x.ServicingPhase).HasMaxLength(40);
        b.Property(x => x.SizeSquareMetres).HasPrecision(19, 4);
        b.Property(x => x.Status).HasConversion<int>();
        b.HasMany(x => x.Ownership).WithOne().HasForeignKey(x => x.HousingStandId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.Ownership).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.Property(x => x.StatusBeforeHold).HasConversion<int?>();
        b.HasMany(x => x.StatusChanges).WithOne().HasForeignKey(x => x.HousingStandId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.StatusChanges).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.HasIndex(x => x.ParentStandId);
        b.HasIndex(x => x.ConsolidatedIntoStandId);
        b.Property(x => x.FrontageMetres).HasPrecision(19, 4);
        b.Property(x => x.ZoningWeight).HasPrecision(9, 4);
        b.OwnsOne(x => x.Boundary, o =>
        {
            o.Property(p => p.Wkt).HasColumnName("boundary_wkt").HasMaxLength(20000);
            o.Property(p => p.CentroidLongitude).HasColumnName("centroid_longitude");
            o.Property(p => p.CentroidLatitude).HasColumnName("centroid_latitude");
            o.Property(p => p.AreaSquareMetres).HasColumnName("mapped_area_m2").HasPrecision(19, 4);
        });
        b.HasIndex(x => new { x.CompanyId, x.StandNumber }).IsUnique().HasFilter("is_deleted = false");
        b.HasQueryFilter(One4AllDbContext.SoftDeleteFilter, x => !x.IsDeleted);
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
        b.Property(x => x.SpouseIdNumber).HasMaxLength(30);
        b.Property(x => x.SpouseName).HasMaxLength(200);
        b.Property(x => x.MaritalStatus).HasConversion<int>();
        b.Property(x => x.MaritalRegime).HasConversion<int?>();
        b.HasIndex(x => new { x.CompanyId, x.SpouseIdNumber });
        b.Property(x => x.IdNumber).HasMaxLength(40);
        b.Property(x => x.FullName).HasMaxLength(200);
        b.Property(x => x.WaitingListNumber).HasMaxLength(30);
        b.Property(x => x.Category).HasConversion<int>().HasDefaultValue(HousingCategory.HighDensity).HasSentinel((HousingCategory)0);
        b.Property(x => x.Tenure).HasConversion<int>().HasDefaultValue(CurrentTenure.Tenant).HasSentinel((CurrentTenure)0);
        b.Property(x => x.Status).HasConversion<int>();
        b.Property(x => x.MonthlyIncome).HasPrecision(19, 4);

        // One applicant record per national ID, and waiting-list numbers are unique (brief §4.10.3).
        b.HasIndex(x => new { x.CompanyId, x.IdNumber }).IsUnique().HasFilter("is_deleted = false");
        b.HasIndex(x => new { x.CompanyId, x.WaitingListNumber }).IsUnique().HasFilter("waiting_list_number IS NOT NULL");
        b.HasIndex(x => new { x.CompanyId, x.Category, x.Status });
        b.HasQueryFilter(One4AllDbContext.SoftDeleteFilter, x => !x.IsDeleted);
    }
}

internal sealed class AllocationRunConfiguration : IEntityTypeConfiguration<AllocationRun>
{
    public void Configure(EntityTypeBuilder<AllocationRun> b)
    {
        b.ToTable("allocation_run", Schemas.Housing, t => t.HasCheckConstraint("ck_allocation_run_approver", "approved_by_user_id IS NULL OR approved_by_user_id <> proposed_by_user_id"));
        b.HasKey(x => x.Id);
        b.Property(x => x.RunNumber).HasMaxLength(30);
        b.Property(x => x.Category).HasConversion<int>();
        b.Property(x => x.Method).HasConversion<int>();
        b.Property(x => x.Status).HasConversion<int>();
        b.Property(x => x.InputCandidates).HasColumnType("text"); // unbounded: a 100,000-row waiting list is kept in full for the draw to be re-verified
        b.Property(x => x.CommitteeResolution).HasMaxLength(120);
        b.Property(x => x.CommitteeMembers).HasMaxLength(400);
        b.Property(x => x.InputStands).HasColumnType("text");
        b.Property(x => x.InputHash).HasMaxLength(64);
        b.HasMany(x => x.Allocations).WithOne().HasForeignKey(x => x.AllocationRunId).OnDelete(DeleteBehavior.Restrict);
        b.Navigation(x => x.Allocations).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.HasMany(x => x.Exclusions).WithOne().HasForeignKey(x => x.AllocationRunId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.Exclusions).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.HasIndex(x => new { x.CompanyId, x.RunNumber }).IsUnique();
    }
}

internal sealed class StandAllocationConfiguration : IEntityTypeConfiguration<StandAllocation>
{
    public void Configure(EntityTypeBuilder<StandAllocation> b)
    {
        b.ToTable("stand_allocation", Schemas.Housing);
        b.HasKey(x => x.Id);
        b.Property(x => x.Status).HasConversion<int>();
        b.Property(x => x.CertificateNumber).HasMaxLength(30);
        b.Property(x => x.Reason).HasMaxLength(500);
        b.Ignore(x => x.IsLive);
        b.HasOne<HousingStand>().WithMany().HasForeignKey(x => x.StandId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<HousingApplicant>().WithMany().HasForeignKey(x => x.ApplicantId).OnDelete(DeleteBehavior.Restrict);

        // Hard controls (brief §4.10.4): one live allocation per stand, one per beneficiary, certificates never reused.
        b.HasIndex(x => x.StandId).IsUnique().HasFilter("status IN (0, 1, 2)").HasDatabaseName("ix_stand_allocation_one_live_per_stand");
        b.HasIndex(x => x.ApplicantId).IsUnique().HasFilter("status IN (0, 1, 2)").HasDatabaseName("ix_stand_allocation_one_live_per_applicant");
        b.HasIndex(x => new { x.CompanyId, x.CertificateNumber }).IsUnique().HasFilter("certificate_number IS NOT NULL");
    }
}

internal sealed class InstalmentNoticeConfiguration : IEntityTypeConfiguration<InstalmentNotice>
{
    public void Configure(EntityTypeBuilder<InstalmentNotice> b)
    {
        b.ToTable("instalment_notice", Schemas.Housing);
        b.HasKey(x => x.Id);
        b.Property(x => x.NoticeNumber).HasMaxLength(40);
        b.Property(x => x.Text).HasColumnType("text");
        b.Property(x => x.Stage).HasConversion<int>();
        b.Property(x => x.Arrears).HasPrecision(19, 4);
        b.Property(x => x.ServiceMethod).HasMaxLength(60);
        b.Property(x => x.ServedBy).HasMaxLength(120);
        b.HasOne<InstalmentAgreement>().WithMany().HasForeignKey(x => x.AgreementId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.CompanyId, x.NoticeNumber }).IsUnique();
        b.HasIndex(x => new { x.AgreementId, x.Stage });
    }
}

internal sealed class InstalmentAgreementConfiguration : IEntityTypeConfiguration<InstalmentAgreement>
{
    public void Configure(EntityTypeBuilder<InstalmentAgreement> b)
    {
        b.ToTable("instalment_agreement", Schemas.Housing, t => t.HasCheckConstraint("ck_instalment_agreement_terms", "cash_price > 0 AND deposit >= 0 AND deposit < cash_price AND term_months >= 1 AND annual_rate_percent >= 0"));
        b.HasKey(x => x.Id);
        b.Property(x => x.Tenure).HasConversion<int>().HasDefaultValue(InstalmentTenure.InstalmentSale).HasSentinel((InstalmentTenure)0);
        b.Property(x => x.PenaltyRatePercent).HasPrecision(9, 4);
        b.Property(x => x.EscalationPercent).HasPrecision(7, 4);
        b.Property(x => x.AgreementNumber).HasMaxLength(30);
        b.Property(x => x.CashPrice).HasPrecision(19, 4);
        b.Property(x => x.Deposit).HasPrecision(19, 4);
        b.Property(x => x.AnnualRatePercent).HasPrecision(9, 4);
        b.Property(x => x.Method).HasConversion<int>();
        b.Property(x => x.Status).HasConversion<int>();
        b.Property(x => x.InterestAccountCode).HasMaxLength(20);
        b.Ignore(x => x.Financed);
        b.HasOne<HousingStand>().WithMany().HasForeignKey(x => x.StandId).OnDelete(DeleteBehavior.Restrict);
        b.HasMany(x => x.Lines).WithOne().HasForeignKey(x => x.InstalmentAgreementId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.HasIndex(x => new { x.CompanyId, x.AgreementNumber }).IsUnique();
        b.HasIndex(x => x.StandId).IsUnique().HasFilter("status = 0").HasDatabaseName("ix_instalment_agreement_one_active_per_stand");
    }
}

internal sealed class InstalmentScheduleLineConfiguration : IEntityTypeConfiguration<InstalmentScheduleLine>
{
    public void Configure(EntityTypeBuilder<InstalmentScheduleLine> b)
    {
        b.ToTable("instalment_schedule_line", Schemas.Housing);
        b.HasKey(x => x.Id);
        b.Property(x => x.Amount).HasPrecision(19, 4);
        b.Property(x => x.Interest).HasPrecision(19, 4);
        b.Property(x => x.Principal).HasPrecision(19, 4);
        b.Property(x => x.BalanceAfter).HasPrecision(19, 4);
        b.HasIndex(x => new { x.InstalmentAgreementId, x.Number }).IsUnique();
        b.HasIndex(x => x.DueDate);
    }
}

internal sealed class RentalUnitConfiguration : IEntityTypeConfiguration<RentalUnit>
{
    public void Configure(EntityTypeBuilder<RentalUnit> b)
    {
        b.ToTable("rental_unit", Schemas.Housing, t => t.HasCheckConstraint("ck_rental_unit_rent", "monthly_rent >= 0 AND rooms >= 0"));
        b.HasKey(x => x.Id);
        b.Property(x => x.Code).HasMaxLength(30);
        b.Property(x => x.Description).HasMaxLength(200);
        b.Property(x => x.UnitType).HasConversion<int>();
        b.Property(x => x.MonthlyRent).HasPrecision(19, 4);
        b.Property(x => x.RentAccountCode).HasMaxLength(20);
        b.Property(x => x.Suburb).HasMaxLength(80);
        b.Property(x => x.VoidReason).HasMaxLength(120);
        b.Property(x => x.Treatment).HasConversion<int>();
        b.Property(x => x.AssetAccountCode).HasMaxLength(20);
        b.Property(x => x.CarryingAmount).HasPrecision(19, 4);
        b.Property(x => x.RefurbishmentJobNumber).HasMaxLength(40);
        b.Ignore(x => x.IsLet);
        b.HasIndex(x => new { x.CompanyId, x.Code }).IsUnique();
    }
}

internal sealed class TenancyConfiguration : IEntityTypeConfiguration<Tenancy>
{
    public void Configure(EntityTypeBuilder<Tenancy> b)
    {
        b.ToTable("tenancy", Schemas.Housing, t => t.HasCheckConstraint("ck_tenancy_terms", "deposit >= 0 AND monthly_rent >= 0 AND (end_date IS NULL OR end_date >= start_date)"));
        b.HasKey(x => x.Id);
        b.Property(x => x.TenancyNumber).HasMaxLength(30);
        b.Property(x => x.MonthlyRent).HasPrecision(19, 4);
        b.Property(x => x.Deposit).HasPrecision(19, 4);
        b.Property(x => x.DepositAccountCode).HasMaxLength(20);
        b.Property(x => x.Status).HasConversion<int>();
        b.Property(x => x.IncomeCategory).HasConversion<int?>();
        b.HasOne<RentalUnit>().WithMany().HasForeignKey(x => x.UnitId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.CompanyId, x.TenancyNumber }).IsUnique();
        b.HasIndex(x => x.UnitId).IsUnique().HasFilter("status = 0").HasDatabaseName("ix_tenancy_one_active_per_unit");
        b.HasIndex(x => x.TenantCustomerId);
        b.HasMany(x => x.RentChanges).WithOne().HasForeignKey(x => x.TenancyId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.RentChanges).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.HasMany(x => x.Occupants).WithOne().HasForeignKey(x => x.TenancyId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.Occupants).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class RentTariffConfiguration : IEntityTypeConfiguration<RentTariff>
{
    public void Configure(EntityTypeBuilder<RentTariff> b)
    {
        b.ToTable("rent_tariff", Schemas.Housing, t => t.HasCheckConstraint("ck_rent_tariff_rent", "monthly_rent >= 0"));
        b.HasKey(x => x.Id);
        b.Property(x => x.UnitType).HasConversion<int>();
        b.Property(x => x.Suburb).HasMaxLength(80);
        b.Property(x => x.IncomeCategory).HasConversion<int?>();
        b.Property(x => x.MonthlyRent).HasPrecision(19, 4);
        b.HasIndex(x => new { x.CompanyId, x.UnitType, x.Suburb, x.IncomeCategory, x.EffectiveFrom }).IsUnique().AreNullsDistinct(false);
    }
}

internal sealed class TenancyRentChangeConfiguration : IEntityTypeConfiguration<TenancyRentChange>
{
    public void Configure(EntityTypeBuilder<TenancyRentChange> b)
    {
        b.ToTable("tenancy_rent_change", Schemas.Housing, t => t.HasCheckConstraint("ck_tenancy_rent_change_rent", "monthly_rent >= 0"));
        b.HasKey(x => x.Id);
        b.Property(x => x.MonthlyRent).HasPrecision(19, 4);
        b.HasIndex(x => new { x.TenancyId, x.EffectiveFrom }).IsUnique();
    }
}

internal sealed class EvictionCaseConfiguration : IEntityTypeConfiguration<EvictionCase>
{
    public void Configure(EntityTypeBuilder<EvictionCase> b)
    {
        b.ToTable("eviction_case", Schemas.Housing, t => t.HasCheckConstraint("ck_eviction_case_notice", "notice_period_days >= 1"));
        b.HasKey(x => x.Id);
        b.Property(x => x.CaseNumber).HasMaxLength(30);
        b.Property(x => x.ArrearsAtOpening).HasPrecision(19, 4);
        b.Property(x => x.Status).HasConversion<int>();
        b.Property(x => x.WithdrawnReason).HasMaxLength(500);
        b.Ignore(x => x.Current);
        b.HasOne<Tenancy>().WithMany().HasForeignKey(x => x.TenancyId).OnDelete(DeleteBehavior.Restrict);
        b.HasMany(x => x.Steps).WithOne().HasForeignKey(x => x.EvictionCaseId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.Steps).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.HasIndex(x => new { x.CompanyId, x.CaseNumber }).IsUnique();
        b.HasIndex(x => x.TenancyId).IsUnique().HasFilter("status = 0").HasDatabaseName("ix_eviction_case_one_open_per_tenancy");
    }
}

internal sealed class EvictionStepConfiguration : IEntityTypeConfiguration<EvictionStep>
{
    public void Configure(EntityTypeBuilder<EvictionStep> b)
    {
        b.ToTable("eviction_step", Schemas.Housing);
        b.HasKey(x => x.Id);
        b.Property(x => x.Stage).HasConversion<int>();
        b.Property(x => x.Reference).HasMaxLength(120);
        b.HasIndex(x => new { x.EvictionCaseId, x.Stage }).IsUnique();
    }
}

internal sealed class RepossessionConfiguration : IEntityTypeConfiguration<Repossession>
{
    public void Configure(EntityTypeBuilder<Repossession> b)
    {
        b.ToTable("repossession", Schemas.Housing, t => t.HasCheckConstraint("ck_repossession_approver", "approved_by_user_id <> requested_by_user_id"));
        b.HasKey(x => x.Id);
        b.Property(x => x.Reference).HasMaxLength(30);
        b.Property(x => x.ReceivableDerecognised).HasPrecision(19, 4);
        b.Property(x => x.PreviousCarryingAmount).HasPrecision(19, 4);
        b.Property(x => x.NetRealisableValue).HasPrecision(19, 4);
        b.Property(x => x.InventoryRecognised).HasPrecision(19, 4);
        b.Property(x => x.Reason).HasMaxLength(500);
        b.Ignore(x => x.GainOrLoss);
        b.HasOne<InstalmentAgreement>().WithMany().HasForeignKey(x => x.AgreementId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<HousingStand>().WithMany().HasForeignKey(x => x.StandId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.CompanyId, x.Reference }).IsUnique();
        b.HasIndex(x => x.AgreementId).IsUnique();
    }
}

internal sealed class UnitRepairConfiguration : IEntityTypeConfiguration<UnitRepair>
{
    public void Configure(EntityTypeBuilder<UnitRepair> b)
    {
        b.ToTable("unit_repair", Schemas.Housing, t => t.HasCheckConstraint("ck_unit_repair_cost", "cost >= 0"));
        b.HasKey(x => x.Id);
        b.Property(x => x.RepairNumber).HasMaxLength(30);
        b.Property(x => x.Description).HasMaxLength(500);
        b.Property(x => x.Status).HasConversion<int>();
        b.Property(x => x.Cost).HasPrecision(19, 4);
        b.Property(x => x.RechargeInvoiceNumber).HasMaxLength(40);
        b.HasOne<RentalUnit>().WithMany().HasForeignKey(x => x.UnitId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.CompanyId, x.RepairNumber }).IsUnique();
        b.HasIndex(x => new { x.UnitId, x.Status });
    }
}

internal sealed class UnitInspectionConfiguration : IEntityTypeConfiguration<UnitInspection>
{
    public void Configure(EntityTypeBuilder<UnitInspection> b)
    {
        b.ToTable("unit_inspection", Schemas.Housing, t => t.HasCheckConstraint("ck_unit_inspection_grade", "condition_grade BETWEEN 1 AND 5"));
        b.HasKey(x => x.Id);
        b.Property(x => x.Inspector).HasMaxLength(120);
        b.Property(x => x.Findings).HasMaxLength(1000);
        b.HasOne<RentalUnit>().WithMany().HasForeignKey(x => x.UnitId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.UnitId, x.InspectedOn });
    }
}

internal sealed class RegistrationDateChangeConfiguration : IEntityTypeConfiguration<RegistrationDateChange>
{
    public void Configure(EntityTypeBuilder<RegistrationDateChange> b)
    {
        b.ToTable("registration_date_change", Schemas.Housing, t => t.HasCheckConstraint("ck_registration_date_change_decider", "decided_by_user_id IS NULL OR decided_by_user_id <> requested_by_user_id"));
        b.HasKey(x => x.Id);
        b.Property(x => x.Reason).HasMaxLength(500);
        b.Property(x => x.Status).HasConversion<int>();
        b.HasOne<HousingApplicant>().WithMany().HasForeignKey(x => x.ApplicantId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => x.ApplicantId).IsUnique().HasFilter("status = 0").HasDatabaseName("ix_registration_date_change_one_pending");
    }
}

internal sealed class ApplicantNoticeConfiguration : IEntityTypeConfiguration<ApplicantNotice>
{
    public void Configure(EntityTypeBuilder<ApplicantNotice> b)
    {
        b.ToTable("applicant_notice", Schemas.Housing);
        b.HasKey(x => x.Id);
        b.Property(x => x.NoticeType).HasConversion<int>();
        b.HasOne<HousingApplicant>().WithMany().HasForeignKey(x => x.ApplicantId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.ApplicantId, x.SentOn });
    }
}

internal sealed class AllocationAppealConfiguration : IEntityTypeConfiguration<AllocationAppeal>
{
    public void Configure(EntityTypeBuilder<AllocationAppeal> b)
    {
        b.ToTable("allocation_appeal", Schemas.Housing);
        b.HasKey(x => x.Id);
        b.Property(x => x.AppealNumber).HasMaxLength(30);
        b.Property(x => x.Grounds).HasMaxLength(1000);
        b.Property(x => x.Status).HasConversion<int>();
        b.Property(x => x.OutcomeReasons).HasMaxLength(1000);
        b.Property(x => x.ReallocationCertificate).HasMaxLength(30);
        b.HasOne<HousingApplicant>().WithMany().HasForeignKey(x => x.ApplicantId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<AllocationRun>().WithMany().HasForeignKey(x => x.AllocationRunId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.CompanyId, x.AppealNumber }).IsUnique();
        b.HasIndex(x => new { x.ApplicantId, x.AllocationRunId }).IsUnique();
    }
}

internal sealed class DevelopmentCovenantConfiguration : IEntityTypeConfiguration<DevelopmentCovenant>
{
    public void Configure(EntityTypeBuilder<DevelopmentCovenant> b)
    {
        b.ToTable("development_covenant", Schemas.Housing, t => t.HasCheckConstraint("ck_development_covenant_deadline", "deadline >= start_date"));
        b.HasKey(x => x.Id);
        b.Property(x => x.CovenantNumber).HasMaxLength(30);
        b.Property(x => x.Status).HasConversion<int>();
        b.Ignore(x => x.Latest);
        b.HasOne<HousingStand>().WithMany().HasForeignKey(x => x.StandId).OnDelete(DeleteBehavior.Restrict);
        b.HasMany(x => x.Steps).WithOne().HasForeignKey(x => x.DevelopmentCovenantId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.Steps).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.HasIndex(x => new { x.CompanyId, x.CovenantNumber }).IsUnique();
        b.HasIndex(x => x.StandId).IsUnique().HasFilter("status <> 1").HasDatabaseName("ix_development_covenant_one_live_per_stand");
        b.HasIndex(x => new { x.CompanyId, x.Status, x.Deadline });
    }
}

internal sealed class CovenantStepConfiguration : IEntityTypeConfiguration<CovenantStep>
{
    public void Configure(EntityTypeBuilder<CovenantStep> b)
    {
        b.ToTable("covenant_step", Schemas.Housing);
        b.HasKey(x => x.Id);
        b.Property(x => x.Milestone).HasConversion<int>();
        b.Property(x => x.Reference).HasMaxLength(120);
        b.HasIndex(x => new { x.DevelopmentCovenantId, x.Milestone }).IsUnique();
    }
}

internal sealed class StandLeaseConfiguration : IEntityTypeConfiguration<StandLease>
{
    public void Configure(EntityTypeBuilder<StandLease> b)
    {
        b.ToTable("stand_lease", Schemas.Housing, t => t.HasCheckConstraint("ck_stand_lease_terms", "term_years BETWEEN 1 AND 99 AND review_every_years >= 1 AND annual_rent > 0"));
        b.HasKey(x => x.Id);
        b.Property(x => x.LeaseNumber).HasMaxLength(30);
        b.Property(x => x.Use).HasConversion<int>();
        b.Property(x => x.AnnualRent).HasPrecision(19, 4);
        b.Property(x => x.EscalationPercent).HasPrecision(9, 4);
        b.Property(x => x.IncomeAccountCode).HasMaxLength(20);
        b.Property(x => x.AccruedIncomeAccountCode).HasMaxLength(20);
        b.Ignore(x => x.EndDate);
        b.HasOne<HousingStand>().WithMany().HasForeignKey(x => x.StandId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.CompanyId, x.LeaseNumber }).IsUnique();
        b.HasIndex(x => x.StandId);
    }
}

internal sealed class HousingGrantConfiguration : IEntityTypeConfiguration<HousingGrant>
{
    public void Configure(EntityTypeBuilder<HousingGrant> b)
    {
        b.ToTable("housing_grant", Schemas.Housing, t => t.HasCheckConstraint("ck_housing_grant_amounts", "amount > 0 AND budgeted_expenditure > 0 AND recognised_to_date >= 0 AND recognised_to_date <= amount"));
        b.HasKey(x => x.Id);
        b.Property(x => x.GrantNumber).HasMaxLength(30);
        b.Property(x => x.Grantor).HasMaxLength(200);
        b.Property(x => x.Conditions).HasMaxLength(2000);
        b.Property(x => x.Amount).HasPrecision(19, 4);
        b.Property(x => x.BudgetedExpenditure).HasPrecision(19, 4);
        b.Property(x => x.ExpenditureToDate).HasPrecision(19, 4);
        b.Property(x => x.RecognisedToDate).HasPrecision(19, 4);
        b.Property(x => x.DeferredAccountCode).HasMaxLength(20);
        b.Property(x => x.IncomeAccountCode).HasMaxLength(20);
        b.Ignore(x => x.Deferred);
        b.HasIndex(x => new { x.CompanyId, x.GrantNumber }).IsUnique();
    }
}

internal sealed class CompensationClaimConfiguration : IEntityTypeConfiguration<CompensationClaim>
{
    public void Configure(EntityTypeBuilder<CompensationClaim> b)
    {
        b.ToTable("compensation_claim", Schemas.Housing, t => t.HasCheckConstraint("ck_compensation_claim_provision", "provision >= 0"));
        b.HasKey(x => x.Id);
        b.Property(x => x.ClaimNumber).HasMaxLength(30);
        b.Property(x => x.Claimant).HasMaxLength(200);
        b.Property(x => x.Status).HasConversion<int>();
        b.Property(x => x.Provision).HasPrecision(19, 4);
        b.Property(x => x.SettledAmount).HasPrecision(19, 4);
        b.Property(x => x.PaidToDate).HasPrecision(19, 4);
        b.Property(x => x.AssessedAmount).HasPrecision(19, 4);
        b.Property(x => x.NoticeReference).HasMaxLength(60);
        b.Property(x => x.Assessor).HasMaxLength(120);
        b.HasMany(x => x.Steps).WithOne().HasForeignKey(x => x.CompensationClaimId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.Steps).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.Property(x => x.LandCostAccountCode).HasMaxLength(20);
        b.Property(x => x.ProvisionAccountCode).HasMaxLength(20);
        b.HasOne<LandParcel>().WithMany().HasForeignKey(x => x.LandParcelId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.CompanyId, x.ClaimNumber }).IsUnique();
    }
}

internal sealed class CooperativeContributionConfiguration : IEntityTypeConfiguration<CooperativeContribution>
{
    public void Configure(EntityTypeBuilder<CooperativeContribution> b)
    {
        b.ToTable("cooperative_contribution", Schemas.Housing, t => t.HasCheckConstraint("ck_cooperative_contribution_amount", "amount > 0"));
        b.HasKey(x => x.Id);
        b.Property(x => x.Amount).HasPrecision(19, 4);
        b.Property(x => x.Reference).HasMaxLength(40);
        b.HasOne<Cooperative>().WithMany().HasForeignKey(x => x.CooperativeId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<CooperativeMember>().WithMany().HasForeignKey(x => x.MemberId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.CooperativeId, x.Date });
    }
}

internal sealed class CooperativeConfiguration : IEntityTypeConfiguration<Cooperative>
{
    public void Configure(EntityTypeBuilder<Cooperative> b)
    {
        b.ToTable("cooperative", Schemas.Housing);
        b.HasKey(x => x.Id);
        b.Property(x => x.Code).HasMaxLength(30);
        b.Property(x => x.Name).HasMaxLength(200);
        b.Property(x => x.RegistrationNumber).HasMaxLength(60);
        b.HasIndex(x => new { x.CompanyId, x.AccountCustomerId }).IsUnique().HasFilter("account_customer_id IS NOT NULL");
        b.HasMany(x => x.Members).WithOne().HasForeignKey(x => x.CooperativeId).OnDelete(DeleteBehavior.Restrict);
        b.Navigation(x => x.Members).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.HasIndex(x => new { x.CompanyId, x.Code }).IsUnique();
    }
}

internal sealed class CooperativeMemberConfiguration : IEntityTypeConfiguration<CooperativeMember>
{
    public void Configure(EntityTypeBuilder<CooperativeMember> b)
    {
        b.ToTable("cooperative_member", Schemas.Housing);
        b.HasKey(x => x.Id);
        b.Property(x => x.IdNumber).HasMaxLength(40);
        b.Property(x => x.FullName).HasMaxLength(200);
        b.HasIndex(x => new { x.CooperativeId, x.IdNumber }).IsUnique();
    }
}

internal sealed class CooperativeStandConfiguration : IEntityTypeConfiguration<CooperativeStand>
{
    public void Configure(EntityTypeBuilder<CooperativeStand> b)
    {
        b.ToTable("cooperative_stand", Schemas.Housing);
        b.HasKey(x => x.Id);
        b.HasOne<Cooperative>().WithMany().HasForeignKey(x => x.CooperativeId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<HousingStand>().WithMany().HasForeignKey(x => x.StandId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<CooperativeMember>().WithMany().HasForeignKey(x => x.MemberId).OnDelete(DeleteBehavior.Restrict);

        // Both levels are controlled in the database: a stand belongs to one cooperative, a member holds one stand.
        b.HasIndex(x => x.StandId).IsUnique();
        b.HasIndex(x => x.MemberId).IsUnique().HasFilter("member_id IS NOT NULL");
    }
}

internal sealed class HousingAgreementConfiguration : IEntityTypeConfiguration<HousingAgreement>
{
    public void Configure(EntityTypeBuilder<HousingAgreement> b)
    {
        b.ToTable("housing_agreement", Schemas.Housing);
        b.HasKey(x => x.Id);
        b.Property(x => x.AgreementNumber).HasMaxLength(30);
        b.Property(x => x.AgreementType).HasConversion<int>();
        b.Ignore(x => x.Latest);
        b.Ignore(x => x.InForce);
        b.HasMany(x => x.Versions).WithOne().HasForeignKey(x => x.HousingAgreementId).OnDelete(DeleteBehavior.Restrict);
        b.Navigation(x => x.Versions).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.HasIndex(x => new { x.CompanyId, x.AgreementNumber }).IsUnique();
        b.HasIndex(x => x.StandId);
    }
}

internal sealed class AgreementVersionConfiguration : IEntityTypeConfiguration<AgreementVersion>
{
    public void Configure(EntityTypeBuilder<AgreementVersion> b)
    {
        b.ToTable("agreement_version", Schemas.Housing);
        b.HasKey(x => x.Id);
        b.Property(x => x.Price).HasPrecision(19, 4);
        b.Property(x => x.Terms).HasMaxLength(2000);
        b.Property(x => x.SpecialConditions).HasMaxLength(2000);
        b.Property(x => x.AmendmentReason).HasMaxLength(500);
        b.Property(x => x.Document).HasMaxLength(20000);
        b.Property(x => x.DocumentHash).HasMaxLength(64);
        b.Property(x => x.Witnesses).HasMaxLength(500);
        b.HasIndex(x => new { x.HousingAgreementId, x.Version }).IsUnique();
    }
}

internal sealed class InformalSettlementConfiguration : IEntityTypeConfiguration<InformalSettlement>
{
    public void Configure(EntityTypeBuilder<InformalSettlement> b)
    {
        b.ToTable("informal_settlement", Schemas.Housing);
        b.HasKey(x => x.Id);
        b.Property(x => x.Code).HasMaxLength(30);
        b.Property(x => x.Name).HasMaxLength(200);
        b.Property(x => x.Ward).HasMaxLength(60);
        b.Property(x => x.UpgradingPlan).HasMaxLength(4000);
        b.HasOne<LandParcel>().WithMany().HasForeignKey(x => x.LandParcelId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.CompanyId, x.Code }).IsUnique();
    }
}

internal sealed class EnumerationExerciseConfiguration : IEntityTypeConfiguration<EnumerationExercise>
{
    public void Configure(EntityTypeBuilder<EnumerationExercise> b)
    {
        b.ToTable("enumeration_exercise", Schemas.Housing);
        b.HasKey(x => x.Id);
        b.Property(x => x.Reference).HasMaxLength(60);
        b.HasOne<InformalSettlement>().WithMany().HasForeignKey(x => x.SettlementId).OnDelete(DeleteBehavior.Restrict);

        // One open exercise per settlement.
        b.HasIndex(x => x.SettlementId).IsUnique().HasFilter("closed_on IS NULL");
    }
}

internal sealed class EnumeratedHouseholdConfiguration : IEntityTypeConfiguration<EnumeratedHousehold>
{
    public void Configure(EntityTypeBuilder<EnumeratedHousehold> b)
    {
        b.ToTable("enumerated_household", Schemas.Housing);
        b.HasKey(x => x.Id);
        b.Property(x => x.HouseholdNumber).HasMaxLength(30);
        b.Property(x => x.HeadIdNumber).HasMaxLength(40);
        b.Property(x => x.HeadName).HasMaxLength(200);
        b.Property(x => x.MonthlyIncome).HasPrecision(19, 4);
        b.Property(x => x.StructureType).HasConversion<int>();
        b.Property(x => x.StructureReference).HasMaxLength(60);
        b.Property(x => x.IneligibleReason).HasMaxLength(1000);
        b.Property(x => x.Outcome).HasConversion<int>();
        b.Property(x => x.WaitingListNumber).HasMaxLength(30);
        b.HasOne<InformalSettlement>().WithMany().HasForeignKey(x => x.SettlementId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<EnumerationExercise>().WithMany().HasForeignKey(x => x.ExerciseId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<HousingStand>().WithMany().HasForeignKey(x => x.StandId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.CompanyId, x.HouseholdNumber }).IsUnique();

        // A person is enumerated once per council, and a stand is regularised to one household.
        b.HasIndex(x => new { x.CompanyId, x.HeadIdNumber }).IsUnique();
        b.HasIndex(x => x.StandId).IsUnique().HasFilter("stand_id IS NOT NULL");
    }
}

internal sealed class DeveloperAgreementConfiguration : IEntityTypeConfiguration<DeveloperAgreement>
{
    public void Configure(EntityTypeBuilder<DeveloperAgreement> b)
    {
        b.ToTable("developer_agreement", Schemas.Housing);
        b.HasKey(x => x.Id);
        b.Property(x => x.AgreementNumber).HasMaxLength(30);
        b.Property(x => x.PenaltyPerStandPerMonth).HasPrecision(19, 4);
        b.Property(x => x.PenaltiesCharged).HasPrecision(19, 4);
        b.HasOne<HousingScheme>().WithMany().HasForeignKey(x => x.SchemeId).OnDelete(DeleteBehavior.Restrict);
        b.HasMany(x => x.Stands).WithOne().HasForeignKey(x => x.DeveloperAgreementId).OnDelete(DeleteBehavior.Restrict);
        b.Navigation(x => x.Stands).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.HasMany(x => x.Milestones).WithOne().HasForeignKey(x => x.DeveloperAgreementId).OnDelete(DeleteBehavior.Restrict);
        b.Navigation(x => x.Milestones).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.HasIndex(x => new { x.CompanyId, x.AgreementNumber }).IsUnique();
    }
}

internal sealed class DeveloperAgreementStandConfiguration : IEntityTypeConfiguration<DeveloperAgreementStand>
{
    public void Configure(EntityTypeBuilder<DeveloperAgreementStand> b)
    {
        b.ToTable("developer_agreement_stand", Schemas.Housing);
        b.HasKey(x => x.Id);
        b.Property(x => x.Quota).HasConversion<int>();
        b.HasOne<HousingStand>().WithMany().HasForeignKey(x => x.StandId).OnDelete(DeleteBehavior.Restrict);

        // A stand is on one developer agreement.
        b.HasIndex(x => x.StandId).IsUnique();
    }
}

internal sealed class DeliveryMilestoneConfiguration : IEntityTypeConfiguration<DeliveryMilestone>
{
    public void Configure(EntityTypeBuilder<DeliveryMilestone> b)
    {
        b.ToTable("delivery_milestone", Schemas.Housing);
        b.HasKey(x => x.Id);
        b.Property(x => x.Description).HasMaxLength(300);
        b.HasIndex(x => x.DeveloperAgreementId);
    }
}

internal sealed class StandBlockConfiguration : IEntityTypeConfiguration<StandBlock>
{
    public void Configure(EntityTypeBuilder<StandBlock> b)
    {
        b.ToTable("stand_block", Schemas.Housing);
        b.HasKey(x => x.Id);
        b.Property(x => x.Reason).HasConversion<int>();
        b.Property(x => x.Detail).HasMaxLength(1000);
        b.Property(x => x.DocumentReference).HasMaxLength(100);
        b.Property(x => x.LiftReason).HasMaxLength(1000);
        b.Property(x => x.LiftDocumentReference).HasMaxLength(100);
        b.HasOne<HousingStand>().WithMany().HasForeignKey(x => x.StandId).OnDelete(DeleteBehavior.Restrict);

        // One block in force per stand.
        b.HasIndex(x => x.StandId).IsUnique().HasFilter("lifted_on IS NULL");
    }
}

internal sealed class TitleDeedConfiguration : IEntityTypeConfiguration<TitleDeed>
{
    public void Configure(EntityTypeBuilder<TitleDeed> b)
    {
        b.ToTable("title_deed", Schemas.Housing);
        b.HasKey(x => x.Id);
        b.Property(x => x.ApplicationNumber).HasMaxLength(30);
        b.Property(x => x.Stage).HasConversion<int>();
        b.Property(x => x.Conveyancer).HasMaxLength(200);
        b.Property(x => x.DeedNumber).HasMaxLength(60);
        b.Property(x => x.ReceivedBy).HasMaxLength(200);
        b.Ignore(x => x.IsClosed);
        b.HasOne<HousingStand>().WithMany().HasForeignKey(x => x.StandId).OnDelete(DeleteBehavior.Restrict);
        b.HasMany(x => x.Steps).WithOne().HasForeignKey(x => x.TitleDeedId).OnDelete(DeleteBehavior.Restrict);
        b.Navigation(x => x.Steps).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.HasIndex(x => new { x.CompanyId, x.ApplicationNumber }).IsUnique();

        // Title is applied for once per stand.
        b.HasIndex(x => x.StandId).IsUnique();
    }
}

internal sealed class TitleDeedStepConfiguration : IEntityTypeConfiguration<TitleDeedStep>
{
    public void Configure(EntityTypeBuilder<TitleDeedStep> b)
    {
        b.ToTable("title_deed_step", Schemas.Housing);
        b.HasKey(x => x.Id);
        b.Property(x => x.Stage).HasConversion<int>();
        b.Property(x => x.Note).HasMaxLength(1000);
        b.HasIndex(x => x.TitleDeedId);
    }
}

internal sealed class TransferApplicationConfiguration : IEntityTypeConfiguration<TransferApplication>
{
    public void Configure(EntityTypeBuilder<TransferApplication> b)
    {
        b.ToTable("transfer_application", Schemas.Housing);
        b.HasKey(x => x.Id);
        b.Property(x => x.ApplicationNumber).HasMaxLength(30);
        b.Property(x => x.TransferType).HasConversion<int>();
        b.Property(x => x.Status).HasConversion<int>();
        b.Property(x => x.Consideration).HasPrecision(19, 4);
        b.Property(x => x.BalanceAssumedAmount).HasPrecision(19, 4);
        b.Property(x => x.AssumptionClearingAccountCode).HasMaxLength(20);
        b.Property(x => x.RejectionReason).HasMaxLength(1000);
        b.Property(x => x.ClearanceNumber).HasMaxLength(30);
        b.HasOne<HousingStand>().WithMany().HasForeignKey(x => x.StandId).OnDelete(DeleteBehavior.Restrict);
        b.HasMany(x => x.Checks).WithOne().HasForeignKey(x => x.TransferApplicationId).OnDelete(DeleteBehavior.Restrict);
        b.Navigation(x => x.Checks).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.HasMany(x => x.Approvals).WithOne().HasForeignKey(x => x.TransferApplicationId).OnDelete(DeleteBehavior.Restrict);
        b.Navigation(x => x.Approvals).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.HasIndex(x => new { x.CompanyId, x.ApplicationNumber }).IsUnique();

        // One open application per stand (lodged, submitted or approved).
        b.HasIndex(x => x.StandId).IsUnique().HasFilter("status IN (1, 2, 3)");
    }
}

internal sealed class TransferCheckConfiguration : IEntityTypeConfiguration<TransferCheck>
{
    public void Configure(EntityTypeBuilder<TransferCheck> b)
    {
        b.ToTable("transfer_check", Schemas.Housing);
        b.HasKey(x => x.Id);
        b.Property(x => x.Requirement).HasConversion<int>();
        b.Property(x => x.DocumentReference).HasMaxLength(100);
        b.HasIndex(x => new { x.TransferApplicationId, x.Requirement }).IsUnique();
    }
}

internal sealed class TransferApprovalConfiguration : IEntityTypeConfiguration<TransferApproval>
{
    public void Configure(EntityTypeBuilder<TransferApproval> b)
    {
        b.ToTable("transfer_approval", Schemas.Housing);
        b.HasKey(x => x.Id);
        b.HasIndex(x => new { x.TransferApplicationId, x.UserId }).IsUnique();
    }
}

internal sealed class LandAcquisitionConfiguration : IEntityTypeConfiguration<LandAcquisition>
{
    public void Configure(EntityTypeBuilder<LandAcquisition> b)
    {
        b.ToTable("land_acquisition", Schemas.Housing);
        b.HasKey(x => x.Id);
        b.Property(x => x.AcquisitionNumber).HasMaxLength(30);
        b.Property(x => x.ParcelNumber).HasMaxLength(60);
        b.Property(x => x.Description).HasMaxLength(500);
        b.Property(x => x.AreaSquareMetres).HasPrecision(19, 4);
        b.Property(x => x.Method).HasConversion<int>();
        b.Property(x => x.Counterparty).HasMaxLength(200);
        b.Property(x => x.ResolutionReference).HasMaxLength(100);
        b.Property(x => x.Valuer).HasMaxLength(200);
        b.Property(x => x.ValuedAt).HasPrecision(19, 4);
        b.Property(x => x.Status).HasConversion<int>();
        b.Property(x => x.AbandonReason).HasMaxLength(1000);
        b.Ignore(x => x.TotalCost);
        b.HasOne<LandParcel>().WithMany().HasForeignKey(x => x.LandParcelId).OnDelete(DeleteBehavior.Restrict);
        b.HasMany(x => x.Checks).WithOne().HasForeignKey(x => x.LandAcquisitionId).OnDelete(DeleteBehavior.Restrict);
        b.Navigation(x => x.Checks).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.HasMany(x => x.Costs).WithOne().HasForeignKey(x => x.LandAcquisitionId).OnDelete(DeleteBehavior.Restrict);
        b.Navigation(x => x.Costs).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.HasMany(x => x.Offers).WithOne().HasForeignKey(x => x.LandAcquisitionId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.Offers).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.Property(x => x.Negotiation).HasConversion<int>();
        b.Property(x => x.AgreedPrice).HasPrecision(19, 4);
        b.Property(x => x.BreakOffReason).HasMaxLength(300);
        b.HasIndex(x => new { x.CompanyId, x.AcquisitionNumber }).IsUnique();
    }
}

internal sealed class DueDiligenceCheckConfiguration : IEntityTypeConfiguration<DueDiligenceCheck>
{
    public void Configure(EntityTypeBuilder<DueDiligenceCheck> b)
    {
        b.ToTable("due_diligence_check", Schemas.Housing);
        b.HasKey(x => x.Id);
        b.Property(x => x.Item).HasConversion<int>();
        b.Property(x => x.Finding).HasMaxLength(1000);
        b.HasIndex(x => new { x.LandAcquisitionId, x.Item }).IsUnique();
    }
}

internal sealed class NegotiationOfferConfiguration : IEntityTypeConfiguration<NegotiationOffer>
{
    public void Configure(EntityTypeBuilder<NegotiationOffer> b)
    {
        b.ToTable("acquisition_offer", Schemas.Housing);
        b.HasKey(x => x.Id);
        b.Property(x => x.Party).HasConversion<int>();
        b.Property(x => x.Amount).HasPrecision(19, 4);
        b.Property(x => x.Note).HasMaxLength(300);
        b.HasIndex(x => x.LandAcquisitionId);
    }
}

internal sealed class AcquisitionCostConfiguration : IEntityTypeConfiguration<AcquisitionCost>
{
    public void Configure(EntityTypeBuilder<AcquisitionCost> b)
    {
        b.ToTable("acquisition_cost", Schemas.Housing);
        b.HasKey(x => x.Id);
        b.Property(x => x.Component).HasConversion<int>();
        b.Property(x => x.Amount).HasPrecision(19, 4);
        b.Property(x => x.Reference).HasMaxLength(100);
        b.Property(x => x.JournalNumber).HasMaxLength(30);
        b.HasIndex(x => x.LandAcquisitionId);
    }
}

internal sealed class LayoutPlanConfiguration : IEntityTypeConfiguration<LayoutPlan>
{
    public void Configure(EntityTypeBuilder<LayoutPlan> b)
    {
        b.ToTable("layout_plan", Schemas.Housing);
        b.HasKey(x => x.Id);
        b.Property(x => x.PlanNumber).HasMaxLength(60);
        b.Ignore(x => x.Latest);
        b.Ignore(x => x.InForce);
        b.HasOne<LandParcel>().WithMany().HasForeignKey(x => x.LandParcelId).OnDelete(DeleteBehavior.Restrict);
        b.HasMany(x => x.Versions).WithOne().HasForeignKey(x => x.LayoutPlanId).OnDelete(DeleteBehavior.Restrict);
        b.Navigation(x => x.Versions).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.HasIndex(x => new { x.CompanyId, x.PlanNumber }).IsUnique();

        // One layout per parcel; changes are versions of it.
        b.HasIndex(x => x.LandParcelId).IsUnique();
    }
}

internal sealed class LayoutPlanVersionConfiguration : IEntityTypeConfiguration<LayoutPlanVersion>
{
    public void Configure(EntityTypeBuilder<LayoutPlanVersion> b)
    {
        b.ToTable("layout_plan_version", Schemas.Housing);
        b.HasKey(x => x.Id);
        b.Property(x => x.Planner).HasMaxLength(200);
        b.Property(x => x.Density).HasConversion<int>();
        b.Property(x => x.AmendmentReason).HasMaxLength(1000);
        b.Property(x => x.ApprovalAuthority).HasMaxLength(200);
        b.HasMany(x => x.Uses).WithOne().HasForeignKey(x => x.LayoutPlanVersionId).OnDelete(DeleteBehavior.Restrict);
        b.Navigation(x => x.Uses).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.HasIndex(x => new { x.LayoutPlanId, x.Version }).IsUnique();
    }
}

internal sealed class LayoutUseCountConfiguration : IEntityTypeConfiguration<LayoutUseCount>
{
    public void Configure(EntityTypeBuilder<LayoutUseCount> b)
    {
        b.ToTable("layout_use_count", Schemas.Housing);
        b.HasKey(x => x.Id);
        b.Property(x => x.Use).HasConversion<int>();
        b.HasIndex(x => new { x.LayoutPlanVersionId, x.Use }).IsUnique();
    }
}

internal sealed class StandPricingRuleConfiguration : IEntityTypeConfiguration<StandPricingRule>
{
    public void Configure(EntityTypeBuilder<StandPricingRule> b)
    {
        b.ToTable("stand_pricing_rule", Schemas.Housing);
        b.HasKey(x => x.Id);
        b.Property(x => x.Scope).HasConversion<int>();
        b.Property(x => x.MinSize).HasPrecision(19, 4);
        b.Property(x => x.MaxSize).HasPrecision(19, 4);
        b.Property(x => x.OverheadPercent).HasPrecision(9, 4);
        b.Property(x => x.LevyPerStand).HasPrecision(19, 4);
        b.Property(x => x.LevyPerSquareMetre).HasPrecision(19, 4);
        b.Property(x => x.MarginPercent).HasPrecision(9, 4);
        b.Property(x => x.RoundTo).HasPrecision(19, 4);
        b.HasOne<HousingScheme>().WithMany().HasForeignKey(x => x.SchemeId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<HousingStand>().WithMany().HasForeignKey(x => x.StandId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.CompanyId, x.EffectiveFrom });
    }
}

internal sealed class DevelopmentLevyConfiguration : IEntityTypeConfiguration<DevelopmentLevy>
{
    public void Configure(EntityTypeBuilder<DevelopmentLevy> b)
    {
        b.ToTable("development_levy", Schemas.Housing);
        b.HasKey(x => x.Id);
        b.Property(x => x.Code).HasMaxLength(30);
        b.Property(x => x.Name).HasMaxLength(200);
        b.Property(x => x.Trigger).HasConversion<int>();
        b.Property(x => x.Basis).HasConversion<int>();
        b.Property(x => x.Rate).HasPrecision(19, 4);
        b.Property(x => x.IncomeAccountCode).HasMaxLength(20);
        b.HasIndex(x => new { x.CompanyId, x.Code }).IsUnique();
    }
}

internal sealed class LevyChargeConfiguration : IEntityTypeConfiguration<LevyCharge>
{
    public void Configure(EntityTypeBuilder<LevyCharge> b)
    {
        b.ToTable("levy_charge", Schemas.Housing);
        b.HasKey(x => x.Id);
        b.Property(x => x.EventReference).HasMaxLength(30);
        b.Property(x => x.Amount).HasPrecision(19, 4);
        b.Property(x => x.DocumentNumber).HasMaxLength(30);
        b.HasOne<DevelopmentLevy>().WithMany().HasForeignKey(x => x.LevyId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<HousingStand>().WithMany().HasForeignKey(x => x.StandId).OnDelete(DeleteBehavior.Restrict);

        // Each levy is raised once per allocation or transfer.
        b.HasIndex(x => new { x.LevyId, x.EventReference }).IsUnique();
    }
}

internal sealed class StandCoOwnershipConfiguration : IEntityTypeConfiguration<StandCoOwnership>
{
    public void Configure(EntityTypeBuilder<StandCoOwnership> b)
    {
        b.ToTable("stand_co_ownership", Schemas.Housing);
        b.HasKey(x => x.Id);
        b.Property(x => x.Regime).HasConversion<int>();
        b.HasOne<HousingStand>().WithMany().HasForeignKey(x => x.StandId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => x.StandId).IsUnique();
    }
}

internal sealed class StandHoldingConfiguration : IEntityTypeConfiguration<StandHolding>
{
    public void Configure(EntityTypeBuilder<StandHolding> b)
    {
        b.ToTable("stand_holding", Schemas.Housing);
        b.HasKey(x => x.Id);
        b.Property(x => x.SharePercent).HasPrecision(9, 4);
        b.Property(x => x.Reason).HasMaxLength(200);
        b.HasOne<HousingStand>().WithMany().HasForeignKey(x => x.StandId).OnDelete(DeleteBehavior.Restrict);

        // One current share per holder per stand.
        b.HasIndex(x => new { x.StandId, x.CustomerId }).IsUnique().HasFilter("held_to IS NULL");
    }
}

internal sealed class SectionalSchemeConfiguration : IEntityTypeConfiguration<SectionalScheme>
{
    public void Configure(EntityTypeBuilder<SectionalScheme> b)
    {
        b.ToTable("sectional_scheme", Schemas.Housing);
        b.HasKey(x => x.Id);
        b.Property(x => x.Code).HasMaxLength(30);
        b.Property(x => x.Name).HasMaxLength(200);
        b.HasOne<HousingStand>().WithMany().HasForeignKey(x => x.StandId).OnDelete(DeleteBehavior.Restrict);
        b.HasMany(x => x.Units).WithOne().HasForeignKey(x => x.SectionalSchemeId).OnDelete(DeleteBehavior.Restrict);
        b.Navigation(x => x.Units).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.HasIndex(x => new { x.CompanyId, x.Code }).IsUnique();
        b.HasIndex(x => x.StandId).IsUnique();
    }
}

internal sealed class SectionalUnitConfiguration : IEntityTypeConfiguration<SectionalUnit>
{
    public void Configure(EntityTypeBuilder<SectionalUnit> b)
    {
        b.ToTable("sectional_unit", Schemas.Housing);
        b.HasKey(x => x.Id);
        b.Property(x => x.UnitNumber).HasMaxLength(20);
        b.Property(x => x.FloorArea).HasPrecision(19, 4);
        b.Property(x => x.ParticipationQuota).HasPrecision(9, 4);
        b.Ignore(x => x.CurrentOwnerCustomerId);
        b.HasMany(x => x.Owners).WithOne().HasForeignKey(x => x.SectionalUnitId).OnDelete(DeleteBehavior.Restrict);
        b.Navigation(x => x.Owners).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.HasIndex(x => new { x.SectionalSchemeId, x.UnitNumber }).IsUnique();
    }
}

internal sealed class UnitOwnershipConfiguration : IEntityTypeConfiguration<UnitOwnership>
{
    public void Configure(EntityTypeBuilder<UnitOwnership> b)
    {
        b.ToTable("unit_ownership", Schemas.Housing);
        b.HasKey(x => x.Id);
        b.Property(x => x.Consideration).HasPrecision(19, 4);

        // One current owner per unit.
        b.HasIndex(x => x.SectionalUnitId).IsUnique().HasFilter("owned_to IS NULL");
    }
}

internal sealed class SectionalLevyRunConfiguration : IEntityTypeConfiguration<SectionalLevyRun>
{
    public void Configure(EntityTypeBuilder<SectionalLevyRun> b)
    {
        b.ToTable("sectional_levy_run", Schemas.Housing);
        b.HasKey(x => x.Id);
        b.Property(x => x.Levy).HasPrecision(19, 4);
        b.Property(x => x.Billed).HasPrecision(19, 4);
        b.HasOne<SectionalScheme>().WithMany().HasForeignKey(x => x.SectionalSchemeId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.SectionalSchemeId, x.Period }).IsUnique();
    }
}

internal sealed class MunicipalLinkSettingsConfiguration : IEntityTypeConfiguration<MunicipalLinkSettings>
{
    public void Configure(EntityTypeBuilder<MunicipalLinkSettings> b)
    {
        b.ToTable("municipal_link_settings", Schemas.Housing);
        b.HasKey(x => x.Id);
        b.Property(x => x.Trigger).HasConversion<int>();
        b.Property(x => x.Ward).HasMaxLength(60);
        b.HasIndex(x => x.CompanyId).IsUnique();
    }
}

internal sealed class ServicingLinkConfiguration : IEntityTypeConfiguration<ServicingLink>
{
    public void Configure(EntityTypeBuilder<ServicingLink> b)
    {
        b.ToTable("servicing_link", Schemas.Housing);
        b.HasKey(x => x.Id);
        b.Property(x => x.Basis).HasConversion<int>();
        b.Property(x => x.ApportionedToDate).HasPrecision(19, 4);
        b.HasOne<HousingScheme>().WithMany().HasForeignKey(x => x.SchemeId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<One4All.Core.JobCosting.JobContract>().WithMany().HasForeignKey(x => x.JobContractId).OnDelete(DeleteBehavior.Restrict);

        // A contract services one scheme.
        b.HasIndex(x => x.JobContractId).IsUnique();
    }
}

internal sealed class ServicingChargeConfiguration : IEntityTypeConfiguration<ServicingCharge>
{
    public void Configure(EntityTypeBuilder<ServicingCharge> b)
    {
        b.ToTable("servicing_charge", Schemas.Housing);
        b.HasKey(x => x.Id);
        b.Property(x => x.Amount).HasPrecision(19, 4);
        b.Property(x => x.JournalNumber).HasMaxLength(30);
        b.HasOne<ServicingLink>().WithMany().HasForeignKey(x => x.ServicingLinkId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<HousingStand>().WithMany().HasForeignKey(x => x.StandId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => x.StandId);
    }
}

internal sealed class WaitingListIncomeCeilingConfiguration : IEntityTypeConfiguration<WaitingListIncomeCeiling>
{
    public void Configure(EntityTypeBuilder<WaitingListIncomeCeiling> b)
    {
        b.ToTable("waiting_list_income_ceiling", Schemas.Housing, t => t.HasCheckConstraint("ck_waiting_list_income_ceiling", "maximum_monthly_income >= 0"));
        b.HasKey(x => x.Id);
        b.Property(x => x.Category).HasConversion<int>();
        b.Property(x => x.MaximumMonthlyIncome).HasPrecision(19, 4);
        b.HasIndex(x => new { x.CompanyId, x.Category }).IsUnique();
    }
}

internal sealed class WaitingListPolicyConfiguration : IEntityTypeConfiguration<WaitingListPolicy>
{
    public void Configure(EntityTypeBuilder<WaitingListPolicy> b)
    {
        b.ToTable("waiting_list_policy", Schemas.Housing);
        b.HasKey(x => x.Id);
        b.Property(x => x.MaximumMonthlyIncome).HasPrecision(19, 4);
        b.Property(x => x.PointsPerYearOnList).HasPrecision(9, 2);
        b.Property(x => x.PointsPerYearResident).HasPrecision(9, 2);
        b.Property(x => x.PointsPerDependant).HasPrecision(9, 2);
        b.Property(x => x.DisabilityPoints).HasPrecision(9, 2);
        b.Property(x => x.HomelessPoints).HasPrecision(9, 2);
        b.Property(x => x.LodgerPoints).HasPrecision(9, 2);
        b.HasIndex(x => x.CompanyId).IsUnique();
    }
}

internal sealed class LandEncumbranceConfiguration : IEntityTypeConfiguration<LandEncumbrance>
{
    public void Configure(EntityTypeBuilder<LandEncumbrance> b)
    {
        b.ToTable("land_encumbrance", Schemas.Housing);
        b.HasKey(x => x.Id);
        b.Property(x => x.Type).HasConversion<int>();
        b.Property(x => x.Reference).HasMaxLength(100);
        b.Property(x => x.InFavourOf).HasMaxLength(200);
        b.Property(x => x.Description).HasMaxLength(1000);
        b.Property(x => x.LiftReference).HasMaxLength(100);
        b.HasOne<LandParcel>().WithMany().HasForeignKey(x => x.LandParcelId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.CompanyId, x.LandParcelId });
    }
}

internal sealed class LandDisputeConfiguration : IEntityTypeConfiguration<LandDispute>
{
    public void Configure(EntityTypeBuilder<LandDispute> b)
    {
        b.ToTable("land_dispute", Schemas.Housing);
        b.HasKey(x => x.Id);
        b.Property(x => x.DisputeNumber).HasMaxLength(40);
        b.Property(x => x.Type).HasConversion<int>();
        b.Property(x => x.Stage).HasConversion<int>();
        b.Property(x => x.Claimant).HasMaxLength(200);
        b.Property(x => x.Particulars).HasMaxLength(2000);
        b.Property(x => x.Outcome).HasMaxLength(2000);
        b.HasOne<LandParcel>().WithMany().HasForeignKey(x => x.LandParcelId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<HousingStand>().WithMany().HasForeignKey(x => x.StandId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.CompanyId, x.DisputeNumber }).IsUnique();
        b.HasMany(x => x.Steps).WithOne().HasForeignKey(x => x.LandDisputeId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.Steps).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class LandDisputeStepConfiguration : IEntityTypeConfiguration<LandDisputeStep>
{
    public void Configure(EntityTypeBuilder<LandDisputeStep> b)
    {
        b.ToTable("land_dispute_step", Schemas.Housing);
        b.HasKey(x => x.Id);
        b.Property(x => x.Stage).HasConversion<int>();
        b.Property(x => x.Note).HasMaxLength(2000);
    }
}

internal sealed class LandDisposalConfiguration : IEntityTypeConfiguration<LandDisposal>
{
    public void Configure(EntityTypeBuilder<LandDisposal> b)
    {
        b.ToTable("land_disposal", Schemas.Housing);
        b.HasKey(x => x.Id);
        b.Property(x => x.DisposalNumber).HasMaxLength(40);
        b.Property(x => x.Type).HasConversion<int>();
        b.Property(x => x.Counterparty).HasMaxLength(200);
        b.Property(x => x.ResolutionReference).HasMaxLength(100);
        b.Property(x => x.JournalNumber).HasMaxLength(40);
        b.Property(x => x.AreaSquareMetres).HasPrecision(19, 4);
        b.Property(x => x.CostDerecognised).HasPrecision(19, 4);
        b.Property(x => x.Consideration).HasPrecision(19, 4);
        b.Ignore(x => x.GainOrLoss);
        b.HasOne<LandParcel>().WithMany().HasForeignKey(x => x.LandParcelId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<LandParcel>().WithMany().HasForeignKey(x => x.ExchangeParcelId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.CompanyId, x.DisposalNumber }).IsUnique();
    }
}

internal sealed class StandSurveyStepConfiguration : IEntityTypeConfiguration<StandSurveyStep>
{
    public void Configure(EntityTypeBuilder<StandSurveyStep> b)
    {
        b.ToTable("stand_survey_step", Schemas.Housing);
        b.HasKey(x => x.Id);
        b.Property(x => x.Stage).HasConversion<int>();
        b.Property(x => x.Reference).HasMaxLength(100);
        b.HasOne<HousingStand>().WithMany().HasForeignKey(x => x.StandId).OnDelete(DeleteBehavior.Restrict);

        // A stand reaches each stage once.
        b.HasIndex(x => new { x.StandId, x.Stage }).IsUnique();
    }
}

internal sealed class SchemeServicingProgressConfiguration : IEntityTypeConfiguration<SchemeServicingProgress>
{
    public void Configure(EntityTypeBuilder<SchemeServicingProgress> b)
    {
        b.ToTable("scheme_servicing_progress", Schemas.Housing);
        b.HasKey(x => x.Id);
        b.Property(x => x.Service).HasConversion<int>();
        b.Property(x => x.PercentComplete).HasPrecision(5, 2);
        b.Property(x => x.CertificateReference).HasMaxLength(100);
        b.HasOne<HousingScheme>().WithMany().HasForeignKey(x => x.SchemeId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.SchemeId, x.Service, x.Date });
    }
}

internal sealed class LandValuationConfiguration : IEntityTypeConfiguration<LandValuation>
{
    public void Configure(EntityTypeBuilder<LandValuation> b)
    {
        b.ToTable("land_valuation", Schemas.Housing);
        b.HasKey(x => x.Id);
        b.Property(x => x.Value).HasPrecision(19, 4);
        b.Property(x => x.Valuer).HasMaxLength(200);
        b.HasOne<LandParcel>().WithMany().HasForeignKey(x => x.LandParcelId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.LandParcelId, x.Date });
    }
}

internal sealed class AllocationExclusionConfiguration : IEntityTypeConfiguration<AllocationExclusion>
{
    public void Configure(EntityTypeBuilder<AllocationExclusion> b)
    {
        b.ToTable("allocation_exclusion", Schemas.Housing);
        b.HasKey(x => x.Id);
        b.Property(x => x.Reason).HasConversion<int>();
        b.Property(x => x.Note).HasMaxLength(200);
        b.HasOne<HousingApplicant>().WithMany().HasForeignKey(x => x.ApplicantId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class AllocationQuotaConfiguration : IEntityTypeConfiguration<AllocationQuota>
{
    public void Configure(EntityTypeBuilder<AllocationQuota> b)
    {
        b.ToTable("allocation_quota", Schemas.Housing);
        b.HasKey(x => x.Id);
        b.Property(x => x.Group).HasConversion<int>();
        b.Property(x => x.TargetPercent).HasPrecision(5, 2);
        b.HasOne<HousingScheme>().WithMany().HasForeignKey(x => x.SchemeId).OnDelete(DeleteBehavior.Restrict);

        // One quota per group per scheme, and one council-wide.
        b.HasIndex(x => new { x.CompanyId, x.SchemeId, x.Group }).IsUnique().HasFilter("scheme_id IS NOT NULL");
        b.HasIndex(x => new { x.CompanyId, x.Group }).IsUnique().HasFilter("scheme_id IS NULL");
    }
}

internal sealed class ApplicantFeeConfiguration : IEntityTypeConfiguration<ApplicantFee>
{
    public void Configure(EntityTypeBuilder<ApplicantFee> b)
    {
        b.ToTable("applicant_fee", Schemas.Housing);
        b.HasKey(x => x.Id);
        b.Property(x => x.FeeType).HasConversion<int>();
        b.Property(x => x.Amount).HasPrecision(19, 4);
        b.Property(x => x.ReceiptReference).HasMaxLength(60);
        b.Property(x => x.JournalNumber).HasMaxLength(40);
        b.HasOne<HousingApplicant>().WithMany().HasForeignKey(x => x.ApplicantId).OnDelete(DeleteBehavior.Restrict);

        // The registration fee is paid once.
        b.HasIndex(x => new { x.ApplicantId, x.FeeType }).IsUnique().HasFilter("fee_type = 1");
    }
}

internal sealed class TransferDutyConfiguration : IEntityTypeConfiguration<TransferDuty>
{
    public void Configure(EntityTypeBuilder<TransferDuty> b)
    {
        b.ToTable("transfer_duty", Schemas.Housing);
        b.HasKey(x => x.Id);
        b.Property(x => x.Consideration).HasPrecision(19, 4);
        b.Property(x => x.RatePercent).HasPrecision(7, 4);
        b.Property(x => x.Amount).HasPrecision(19, 4);
        b.Property(x => x.ReceiptReference).HasMaxLength(60);
        b.Property(x => x.LiabilityAccountCode).HasMaxLength(20);
        b.Property(x => x.JournalNumber).HasMaxLength(40);
        b.Property(x => x.RemittanceReference).HasMaxLength(60);
        b.Property(x => x.RemittanceJournalNumber).HasMaxLength(40);
        b.HasOne<TransferApplication>().WithMany().HasForeignKey(x => x.TransferApplicationId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => x.TransferApplicationId).IsUnique();
    }
}

internal sealed class StandDepositConfiguration : IEntityTypeConfiguration<StandDeposit>
{
    public void Configure(EntityTypeBuilder<StandDeposit> b)
    {
        b.ToTable("stand_deposit", Schemas.Housing);
        b.HasKey(x => x.Id);
        b.Property(x => x.Amount).HasPrecision(19, 4);
        b.Property(x => x.ReceiptReference).HasMaxLength(60);
        b.Property(x => x.LiabilityAccountCode).HasMaxLength(20);
        b.Property(x => x.JournalNumber).HasMaxLength(40);
        b.Property(x => x.ClearingJournalNumber).HasMaxLength(40);
        b.HasOne<StandAllocation>().WithMany().HasForeignKey(x => x.AllocationId).OnDelete(DeleteBehavior.Restrict);

        // One deposit held per allocation.
        b.HasIndex(x => x.AllocationId).IsUnique().HasFilter("cleared_on IS NULL");
    }
}

internal sealed class AgreementCancellationConfiguration : IEntityTypeConfiguration<AgreementCancellation>
{
    public void Configure(EntityTypeBuilder<AgreementCancellation> b)
    {
        b.ToTable("agreement_cancellation", Schemas.Housing);
        b.HasKey(x => x.Id);
        b.Property(x => x.AgreementNumber).HasMaxLength(40);
        b.Property(x => x.Reason).HasMaxLength(500);
        b.Property(x => x.RepossessionReference).HasMaxLength(40);
        b.Property(x => x.RefundJournalNumber).HasMaxLength(40);
        b.Property(x => x.Paid).HasPrecision(19, 4);
        b.Property(x => x.OccupationRental).HasPrecision(19, 4);
        b.Property(x => x.AdminCharge).HasPrecision(19, 4);
        b.Property(x => x.Arrears).HasPrecision(19, 4);
        b.Property(x => x.Refund).HasPrecision(19, 4);
        b.HasIndex(x => new { x.CompanyId, x.AgreementNumber }).IsUnique();
    }
}

internal sealed class SchemeBudgetConfiguration : IEntityTypeConfiguration<SchemeBudget>
{
    public void Configure(EntityTypeBuilder<SchemeBudget> b)
    {
        b.ToTable("scheme_budget", Schemas.Housing);
        b.HasKey(x => x.Id);
        b.Property(x => x.Element).HasConversion<int>();
        b.Property(x => x.Amount).HasPrecision(19, 4);
        b.HasOne<HousingScheme>().WithMany().HasForeignKey(x => x.SchemeId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.SchemeId, x.Element }).IsUnique();
    }
}

internal sealed class SchemeCostConfiguration : IEntityTypeConfiguration<SchemeCost>
{
    public void Configure(EntityTypeBuilder<SchemeCost> b)
    {
        b.ToTable("scheme_cost", Schemas.Housing);
        b.HasKey(x => x.Id);
        b.Property(x => x.Element).HasConversion<int>();
        b.Property(x => x.Amount).HasPrecision(19, 4);
        b.Property(x => x.Reference).HasMaxLength(60);
        b.Property(x => x.Description).HasMaxLength(300);
        b.Property(x => x.JournalNumber).HasMaxLength(40);
        b.HasOne<HousingScheme>().WithMany().HasForeignKey(x => x.SchemeId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.SchemeId, x.Date });
    }
}

internal sealed class StandStatusChangeConfiguration : IEntityTypeConfiguration<StandStatusChange>
{
    public void Configure(EntityTypeBuilder<StandStatusChange> b)
    {
        b.ToTable("stand_status_change", Schemas.Housing);
        b.HasKey(x => x.Id);
        b.Property(x => x.From).HasConversion<int>();
        b.Property(x => x.To).HasConversion<int>();
        b.Property(x => x.Authority).HasMaxLength(120);
        b.Property(x => x.Reason).HasMaxLength(300);
    }
}

internal sealed class CompensationStepConfiguration : IEntityTypeConfiguration<CompensationStep>
{
    public void Configure(EntityTypeBuilder<CompensationStep> b)
    {
        b.ToTable("compensation_step", Schemas.Housing);
        b.HasKey(x => x.Id);
        b.Property(x => x.Kind).HasConversion<int>();
        b.Property(x => x.Status).HasConversion<int>();
        b.Property(x => x.Party).HasMaxLength(200);
        b.Property(x => x.Amount).HasPrecision(19, 4);
        b.Property(x => x.Note).HasMaxLength(1000);
    }
}

internal sealed class TenancyOccupantConfiguration : IEntityTypeConfiguration<TenancyOccupant>
{
    public void Configure(EntityTypeBuilder<TenancyOccupant> b)
    {
        b.ToTable("tenancy_occupant", Schemas.Housing);
        b.HasKey(x => x.Id);
        b.Property(x => x.FullName).HasMaxLength(200);
        b.Property(x => x.IdNumber).HasMaxLength(40);
        b.Property(x => x.Relationship).HasMaxLength(60);
    }
}

internal sealed class LodgerPermitConfiguration : IEntityTypeConfiguration<LodgerPermit>
{
    public void Configure(EntityTypeBuilder<LodgerPermit> b)
    {
        b.ToTable("lodger_permit", Schemas.Housing, t => t.HasCheckConstraint("ck_lodger_permit_terms", "rooms >= 1 AND monthly_fee >= 0 AND expires_on >= issued_on"));
        b.HasKey(x => x.Id);
        b.Property(x => x.PermitNumber).HasMaxLength(50);
        b.Property(x => x.LodgerName).HasMaxLength(200);
        b.Property(x => x.IdNumber).HasMaxLength(40);
        b.Property(x => x.MonthlyFee).HasPrecision(19, 4);
        b.Property(x => x.FeeAccountCode).HasMaxLength(20);
        b.Property(x => x.Status).HasConversion<int>();
        b.Property(x => x.RevokedReason).HasMaxLength(300);
        b.Ignore(x => x.LastDay);
        b.HasOne<Tenancy>().WithMany().HasForeignKey(x => x.TenancyId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.CompanyId, x.PermitNumber }).IsUnique();
    }
}

internal sealed class StandSwapConfiguration : IEntityTypeConfiguration<StandSwap>
{
    public void Configure(EntityTypeBuilder<StandSwap> b)
    {
        b.ToTable("stand_swap", Schemas.Housing);
        b.HasKey(x => x.Id);
        b.Property(x => x.FirstCertificate).HasMaxLength(40);
        b.Property(x => x.SecondCertificate).HasMaxLength(40);
        b.Property(x => x.Reference).HasMaxLength(80);
        b.Property(x => x.Reason).HasMaxLength(300);
    }
}

internal sealed class ApplicantDocumentConfiguration : IEntityTypeConfiguration<ApplicantDocument>
{
    public void Configure(EntityTypeBuilder<ApplicantDocument> b)
    {
        b.ToTable("applicant_document", Schemas.Housing);
        b.HasKey(x => x.Id);
        b.Property(x => x.Type).HasConversion<int>();
        b.Property(x => x.Description).HasMaxLength(200);
        b.Property(x => x.VerifiedBy).HasMaxLength(120);
        b.Ignore(x => x.IsVerified);
        b.HasOne<HousingApplicant>().WithMany().HasForeignKey(x => x.ApplicantId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => x.ApplicantId);
    }
}

internal sealed class DeceasedPersonConfiguration : IEntityTypeConfiguration<DeceasedPerson>
{
    public void Configure(EntityTypeBuilder<DeceasedPerson> b)
    {
        b.ToTable("deceased_person", Schemas.Housing);
        b.HasKey(x => x.Id);
        b.Property(x => x.IdNumber).HasMaxLength(40);
        b.Property(x => x.FullName).HasMaxLength(200);
        b.HasIndex(x => new { x.CompanyId, x.IdNumber }).IsUnique();
    }
}

internal sealed class DuplicateCaseConfiguration : IEntityTypeConfiguration<DuplicateCase>
{
    public void Configure(EntityTypeBuilder<DuplicateCase> b)
    {
        b.ToTable("duplicate_case", Schemas.Housing);
        b.HasKey(x => x.Id);
        b.Property(x => x.FirstSource).HasMaxLength(40);
        b.Property(x => x.SecondSource).HasMaxLength(40);
        b.Property(x => x.FirstReference).HasMaxLength(60);
        b.Property(x => x.SecondReference).HasMaxLength(60);
        b.Property(x => x.Reason).HasMaxLength(200);
        b.Property(x => x.DecisionReason).HasMaxLength(500);
        b.Property(x => x.Status).HasConversion<int>();
        b.HasIndex(x => new { x.CompanyId, x.Status });
    }
}

internal sealed class PlannedMaintenanceConfiguration : IEntityTypeConfiguration<PlannedMaintenance>
{
    public void Configure(EntityTypeBuilder<PlannedMaintenance> b)
    {
        b.ToTable("planned_maintenance", Schemas.Housing, t => t.HasCheckConstraint("ck_planned_maintenance_every", "every_months >= 0"));
        b.HasKey(x => x.Id);
        b.Property(x => x.Description).HasMaxLength(200);
        b.HasIndex(x => new { x.CompanyId, x.NextDue });
    }
}

internal sealed class TenureControlAccountConfiguration : IEntityTypeConfiguration<TenureControlAccount>
{
    public void Configure(EntityTypeBuilder<TenureControlAccount> b)
    {
        b.ToTable("tenure_control_account", Schemas.Housing);
        b.HasKey(x => x.Id);
        b.Property(x => x.Tenure).HasConversion<int>();
        b.Property(x => x.AccountCode).HasMaxLength(20);
        b.HasIndex(x => new { x.CompanyId, x.Tenure }).IsUnique();
    }
}

internal sealed class AllocationBlockLogConfiguration : IEntityTypeConfiguration<AllocationBlockLog>
{
    public void Configure(EntityTypeBuilder<AllocationBlockLog> b)
    {
        b.ToTable("allocation_block_log", Schemas.Housing);
        b.HasKey(x => x.Id);
        b.Property(x => x.RunNumber).HasMaxLength(40);
        b.Property(x => x.StandNumber).HasMaxLength(40);
        b.Property(x => x.Reason).HasMaxLength(300);
        b.HasIndex(x => new { x.CompanyId, x.StandId });
    }
}
