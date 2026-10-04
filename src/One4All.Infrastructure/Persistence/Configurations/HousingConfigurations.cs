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
        b.Property(x => x.WaitingListNumber).HasMaxLength(30);
        b.Property(x => x.Category).HasConversion<int>().HasDefaultValue(HousingCategory.HighDensity).HasSentinel((HousingCategory)0);
        b.Property(x => x.Tenure).HasConversion<int>().HasDefaultValue(CurrentTenure.Tenant).HasSentinel((CurrentTenure)0);
        b.Property(x => x.Status).HasConversion<int>();
        b.Property(x => x.MonthlyIncome).HasPrecision(19, 4);

        // One applicant record per national ID, and waiting-list numbers are unique (brief §4.10.3).
        b.HasIndex(x => new { x.CompanyId, x.IdNumber }).IsUnique().HasFilter("is_deleted = false");
        b.HasIndex(x => new { x.CompanyId, x.WaitingListNumber }).IsUnique().HasFilter("waiting_list_number IS NOT NULL");
        b.HasIndex(x => new { x.CompanyId, x.Category, x.Status });
        b.HasQueryFilter(x => !x.IsDeleted);
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
        b.Property(x => x.InputCandidates).HasMaxLength(100_000);
        b.Property(x => x.InputStands).HasMaxLength(100_000);
        b.Property(x => x.InputHash).HasMaxLength(64);
        b.HasMany(x => x.Allocations).WithOne().HasForeignKey(x => x.AllocationRunId).OnDelete(DeleteBehavior.Restrict);
        b.Navigation(x => x.Allocations).UsePropertyAccessMode(PropertyAccessMode.Field);
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

internal sealed class InstalmentAgreementConfiguration : IEntityTypeConfiguration<InstalmentAgreement>
{
    public void Configure(EntityTypeBuilder<InstalmentAgreement> b)
    {
        b.ToTable("instalment_agreement", Schemas.Housing, t => t.HasCheckConstraint("ck_instalment_agreement_terms", "cash_price > 0 AND deposit >= 0 AND deposit < cash_price AND term_months >= 1 AND annual_rate_percent >= 0"));
        b.HasKey(x => x.Id);
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
        b.HasOne<RentalUnit>().WithMany().HasForeignKey(x => x.UnitId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.CompanyId, x.TenancyNumber }).IsUnique();
        b.HasIndex(x => x.UnitId).IsUnique().HasFilter("status = 0").HasDatabaseName("ix_tenancy_one_active_per_unit");
        b.HasIndex(x => x.TenantCustomerId);
        b.HasMany(x => x.RentChanges).WithOne().HasForeignKey(x => x.TenancyId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.RentChanges).UsePropertyAccessMode(PropertyAccessMode.Field);
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
        b.Property(x => x.LandCostAccountCode).HasMaxLength(20);
        b.Property(x => x.ProvisionAccountCode).HasMaxLength(20);
        b.HasOne<LandParcel>().WithMany().HasForeignKey(x => x.LandParcelId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.CompanyId, x.ClaimNumber }).IsUnique();
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
