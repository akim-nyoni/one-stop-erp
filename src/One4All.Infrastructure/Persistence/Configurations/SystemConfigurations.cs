using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using One4All.Core.System;
using One4All.Infrastructure.Security;

namespace One4All.Infrastructure.Persistence.Configurations;

internal sealed class CompanyConfiguration : IEntityTypeConfiguration<Company>
{
    public void Configure(EntityTypeBuilder<Company> b)
    {
        b.Property(x => x.PosChangeCurrency).HasMaxLength(3);
        b.Property(x => x.MaxSalesDiscountPercent).HasPrecision(9, 4);
        b.Property(x => x.TaxYearStartMonth).HasDefaultValue(1).HasSentinel(0);
        b.ToTable("company", Schemas.System);
        b.HasKey(x => x.Id);
        b.Property(x => x.Code).HasMaxLength(20);
        b.HasIndex(x => x.Code).IsUnique().HasFilter("is_deleted = false");
        b.Property(x => x.Name).HasMaxLength(200);
        b.Property(x => x.Locale).HasMaxLength(20);
        b.Property(x => x.RegistrationNumber).HasMaxLength(60);
        b.Property(x => x.TaxNumber).HasMaxLength(60);
        b.Property(x => x.TimeZoneId).HasMaxLength(64);
        b.Property(x => x.BaseCurrency).HasMaxLength(3);
        b.Property(x => x.ReportingCurrency).HasMaxLength(3);
        b.HasQueryFilter(One4AllDbContext.SoftDeleteFilter, x => !x.IsDeleted);
    }
}

internal sealed class BranchConfiguration : IEntityTypeConfiguration<Branch>
{
    public void Configure(EntityTypeBuilder<Branch> b)
    {
        b.ToTable("branch", Schemas.System);
        b.HasKey(x => x.Id);
        b.Property(x => x.Code).HasMaxLength(20);
        b.Property(x => x.Name).HasMaxLength(200);
        b.Property(x => x.Address).HasMaxLength(512);
        b.HasIndex(x => new { x.CompanyId, x.Code }).IsUnique().HasFilter("is_deleted = false");
        b.HasQueryFilter(One4AllDbContext.SoftDeleteFilter, x => !x.IsDeleted);
    }
}

internal sealed class CurrencyConfiguration : IEntityTypeConfiguration<Currency>
{
    public void Configure(EntityTypeBuilder<Currency> b)
    {
        b.Ignore(x => x.SmallestCashUnit);
        b.ToTable("currency", Schemas.System);
        b.HasKey(x => x.Id);
        b.Property(x => x.Code).HasMaxLength(3);
        b.HasIndex(x => x.Code).IsUnique().HasFilter("is_deleted = false");
        b.Property(x => x.Name).HasMaxLength(80);
        b.Property(x => x.Symbol).HasMaxLength(8);
        b.Property(x => x.RoundingIncrement).HasPrecision(19, 6);
        b.Property(x => x.RoundingMode).HasConversion<int>();

        b.HasMany(x => x.Denominations).WithOne().HasForeignKey(x => x.CurrencyId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.Denominations).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.HasQueryFilter(One4AllDbContext.SoftDeleteFilter, x => !x.IsDeleted);
    }
}

internal sealed class CurrencyDenominationConfiguration : IEntityTypeConfiguration<CurrencyDenomination>
{
    public void Configure(EntityTypeBuilder<CurrencyDenomination> b)
    {
        b.ToTable("currency_denomination", Schemas.System);
        b.HasKey(x => x.Id);
        b.Property(x => x.FaceValue).HasPrecision(19, 4);
        b.HasIndex(x => new { x.CurrencyId, x.FaceValue }).IsUnique();
    }
}

internal sealed class ExchangeRateConfiguration : IEntityTypeConfiguration<ExchangeRate>
{
    public void Configure(EntityTypeBuilder<ExchangeRate> b)
    {
        b.ToTable("exchange_rate", Schemas.System);
        b.HasKey(x => x.Id);
        b.Property(x => x.FromCurrency).HasMaxLength(3);
        b.Property(x => x.ToCurrency).HasMaxLength(3);
        b.Property(x => x.RateType).HasConversion<int>();
        b.Property(x => x.Quotation).HasConversion<int>();
        b.Property(x => x.Rate).HasPrecision(19, 6);
        b.HasIndex(x => new { x.FromCurrency, x.ToCurrency, x.RateType, x.EffectiveFrom }).IsUnique();
    }
}

internal sealed class FiscalYearConfiguration : IEntityTypeConfiguration<FiscalYear>
{
    public void Configure(EntityTypeBuilder<FiscalYear> b)
    {
        b.ToTable("fiscal_year", Schemas.System);
        b.HasKey(x => x.Id);
        b.Property(x => x.Code).HasMaxLength(20);
        b.HasIndex(x => new { x.CompanyId, x.Code }).IsUnique();

        b.HasMany(x => x.Periods).WithOne().HasForeignKey(x => x.FiscalYearId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.Periods).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.Ignore(x => x.OrderedPeriods);
    }
}

internal sealed class FiscalPeriodConfiguration : IEntityTypeConfiguration<FiscalPeriod>
{
    public void Configure(EntityTypeBuilder<FiscalPeriod> b)
    {
        b.ToTable("fiscal_period", Schemas.System);
        b.HasKey(x => x.Id);
        b.Property(x => x.Name).HasMaxLength(60);
        b.HasIndex(x => new { x.FiscalYearId, x.PeriodNumber }).IsUnique();

        b.HasMany(x => x.ModuleStatuses).WithOne().HasForeignKey(x => x.FiscalPeriodId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.ModuleStatuses).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class PeriodModuleStatusConfiguration : IEntityTypeConfiguration<PeriodModuleStatus>
{
    public void Configure(EntityTypeBuilder<PeriodModuleStatus> b)
    {
        b.ToTable("period_module_status", Schemas.System);
        b.HasKey(x => x.Id);
        b.Property(x => x.ModuleCode).HasMaxLength(40);
        b.Property(x => x.Status).HasConversion<int>();
        b.HasIndex(x => new { x.FiscalPeriodId, x.ModuleCode }).IsUnique();
    }
}

internal sealed class NumberSequenceConfiguration : IEntityTypeConfiguration<NumberSequence>
{
    public void Configure(EntityTypeBuilder<NumberSequence> b)
    {
        b.ToTable("number_sequence", Schemas.System);
        b.HasKey(x => x.Id);
        b.Property(x => x.DocumentType).HasMaxLength(60);
        b.Property(x => x.Prefix).HasMaxLength(20);
        b.HasIndex(x => new { x.CompanyId, x.BranchId, x.DocumentType }).IsUnique();
        b.OwnsMany(x => x.Years, y =>
        {
            y.ToTable("number_sequence_year", Schemas.System);
            y.WithOwner().HasForeignKey("NumberSequenceId");
            y.HasKey("NumberSequenceId", nameof(NumberSequenceYear.Year));
            y.Property(x => x.Year).ValueGeneratedNever();
        });
        b.Navigation(x => x.Years).HasField("_years").UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class ScheduledJobConfiguration : IEntityTypeConfiguration<ScheduledJob>
{
    public void Configure(EntityTypeBuilder<ScheduledJob> b)
    {
        b.ToTable("scheduled_job", Schemas.System);
        b.HasKey(x => x.Id);
        b.Property(x => x.Code).HasMaxLength(40);
        b.Property(x => x.TaskKey).HasMaxLength(100);
        b.Property(x => x.Frequency).HasConversion<int>();
        b.Property(x => x.DayOfWeek).HasConversion<int?>();
        b.HasIndex(x => new { x.CompanyId, x.Code }).IsUnique();
        b.HasIndex(x => new { x.IsEnabled, x.NextRunAt });
    }
}

internal sealed class JobRunConfiguration : IEntityTypeConfiguration<JobRun>
{
    public void Configure(EntityTypeBuilder<JobRun> b)
    {
        b.ToTable("job_run", Schemas.System);
        b.HasKey(x => x.Id);
        b.Property(x => x.Message).HasMaxLength(2000);
        b.HasOne<ScheduledJob>().WithMany().HasForeignKey(x => x.ScheduledJobId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => x.ScheduledJobId);
    }
}

internal sealed class TerminologyOverrideConfiguration : IEntityTypeConfiguration<TerminologyOverride>
{
    public void Configure(EntityTypeBuilder<TerminologyOverride> b)
    {
        b.ToTable("terminology_override", Schemas.System);
        b.HasKey(x => x.Id);
        b.Property(x => x.Term).HasMaxLength(60);
        b.Property(x => x.Replacement).HasMaxLength(60);
        b.HasIndex(x => new { x.CompanyId, x.Term }).IsUnique();
    }
}

internal sealed class IdempotencyRecordConfiguration : IEntityTypeConfiguration<IdempotencyRecord>
{
    public void Configure(EntityTypeBuilder<IdempotencyRecord> b)
    {
        b.ToTable("idempotency_record", Schemas.System);
        b.HasKey(x => x.Id);
        b.Property(x => x.Key).HasMaxLength(100);
        b.Property(x => x.Method).HasMaxLength(10);
        b.Property(x => x.Path).HasMaxLength(400);
        b.Property(x => x.RequestHash).HasMaxLength(64);
        b.Property(x => x.ResponseContentType).HasMaxLength(200);
        b.Property(x => x.State).HasConversion<int>();
        b.HasIndex(x => new { x.UserId, x.Key }).IsUnique();
        b.HasIndex(x => x.StartedOnUtc);
    }
}

internal sealed class WorkstationPrinterConfiguration : IEntityTypeConfiguration<WorkstationPrinter>
{
    public void Configure(EntityTypeBuilder<WorkstationPrinter> b)
    {
        b.ToTable("workstation_printer", Schemas.System, t => t.HasCheckConstraint("ck_workstation_printer_copies", "copies BETWEEN 1 AND 99"));
        b.HasKey(x => x.Id);
        b.Property(x => x.Workstation).HasMaxLength(64);
        b.Property(x => x.Purpose).HasConversion<int>();
        b.Property(x => x.PrinterName).HasMaxLength(200);
        b.Property(x => x.Tray).HasMaxLength(40);
        b.HasIndex(x => new { x.CompanyId, x.Workstation, x.Purpose }).IsUnique();
    }
}

internal sealed class CompanyMessagingSettingsConfiguration : IEntityTypeConfiguration<Core.Communications.CompanyMessagingSettings>
{
    public void Configure(EntityTypeBuilder<Core.Communications.CompanyMessagingSettings> b)
    {
        b.ToTable("company_messaging", Schemas.System);
        b.HasKey(x => x.Id);
        b.HasIndex(x => x.CompanyId).IsUnique();
        b.Property(x => x.EmailProvider).HasConversion<int>();
        b.Property(x => x.SmtpHost).HasMaxLength(200);
        b.Property(x => x.SmtpUsername).HasMaxLength(200);
        b.Property(x => x.SmtpPassword).Encrypted();
        b.Property(x => x.FromAddress).HasMaxLength(200);
        b.Property(x => x.FromName).HasMaxLength(120);
        b.Property(x => x.GraphTenantId).HasMaxLength(100);
        b.Property(x => x.GraphClientId).HasMaxLength(100);
        b.Property(x => x.GraphClientSecret).Encrypted();
        b.Property(x => x.SmsUrl).HasMaxLength(400);
        b.Property(x => x.SmsBodyTemplate).HasMaxLength(1000);
        b.Property(x => x.SmsContentType).HasMaxLength(100);
        b.Property(x => x.SmsAuthHeader).HasMaxLength(100);
        b.Property(x => x.SmsAuthValue).Encrypted();
        b.Property(x => x.SmsFrom).HasMaxLength(40);
    }
}
