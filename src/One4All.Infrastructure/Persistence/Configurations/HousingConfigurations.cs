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
        b.HasIndex(x => x.CertificateNumber).IsUnique().HasFilter("certificate_number IS NOT NULL");
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
