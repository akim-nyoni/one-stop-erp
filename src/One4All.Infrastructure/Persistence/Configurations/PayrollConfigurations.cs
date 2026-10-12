using Microsoft.EntityFrameworkCore;
using One4All.Infrastructure.Security;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using One4All.Core.Payroll;

namespace One4All.Infrastructure.Persistence.Configurations;

internal sealed class EmployeeConfiguration : IEntityTypeConfiguration<Employee>
{
    public void Configure(EntityTypeBuilder<Employee> b)
    {
        b.ToTable("employee", Schemas.HumanResources);
        b.HasKey(x => x.Id);
        b.Property(x => x.EmployeeNumber).HasMaxLength(30);
        b.HasIndex(x => x.HousingTenancyId);
        b.Property(x => x.PayGroup).HasMaxLength(20);
        b.Property(x => x.EmploymentType).HasConversion<int>();
        b.Property(x => x.PhysicalAddress).HasMaxLength(300);
        b.Property(x => x.PostalAddress).HasMaxLength(300);
        b.Property(x => x.Phone).HasMaxLength(30);
        b.Property(x => x.JobTitle).HasMaxLength(100);
        b.Property(x => x.WorkersCompensationClass).HasMaxLength(20);
        b.Property(x => x.PhotoContentType).HasMaxLength(50);
        b.Property(x => x.FullName).HasMaxLength(200);
        b.Property(x => x.Department).HasMaxLength(30);
        b.Property(x => x.CostCentre).HasMaxLength(30);
        b.Property(x => x.Status).HasConversion<int>();
        b.Property(x => x.NationalIdNumber).Encrypted();
        b.Property(x => x.TaxNumber).Encrypted();
        b.Property(x => x.BasicSalary).Encrypted();
        b.Property(x => x.Gender).HasConversion<int>();
        b.Property(x => x.Grade).HasMaxLength(20);
        b.Property(x => x.PaymentMethod).HasConversion<int>();
        b.Property(x => x.BankName).HasMaxLength(100);
        b.Property(x => x.BankBranch).Encrypted();
        b.Property(x => x.BankAccountNumber).Encrypted();
        b.Property(x => x.MobileMoneyNumber).Encrypted();
        b.Property(x => x.TerminationReason).HasMaxLength(300);
        b.Property(x => x.HoldReason).HasMaxLength(300);
        b.HasMany(x => x.PayElements).WithOne().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.PayElements).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.HasIndex(x => new { x.CompanyId, x.EmployeeNumber }).IsUnique().HasFilter("is_deleted = false");
        b.HasQueryFilter(One4AllDbContext.SoftDeleteFilter, x => !x.IsDeleted);
    }
}

internal sealed class EmployeePayElementConfiguration : IEntityTypeConfiguration<EmployeePayElement>
{
    public void Configure(EntityTypeBuilder<EmployeePayElement> b)
    {
        b.ToTable("employee_pay_element", Schemas.HumanResources);
        b.HasKey(x => x.Id);
        b.Property(x => x.OverrideAmount).HasPrecision(19, 4);
        b.Property(x => x.OverrideRate).HasPrecision(19, 6);
        b.Property(x => x.Units).HasPrecision(19, 4);
        b.HasIndex(x => new { x.EmployeeId, x.PayElementId });
    }
}

internal sealed class PayElementConfiguration : IEntityTypeConfiguration<PayElement>
{
    public void Configure(EntityTypeBuilder<PayElement> b)
    {
        b.ToTable("element", Schemas.Payroll);
        b.Property(x => x.Recovers).HasConversion<int>();
        b.Property(x => x.TaxTableCode).HasMaxLength(20);
        b.HasKey(x => x.Id);
        b.Property(x => x.Code).HasMaxLength(30);
        b.Property(x => x.Name).HasMaxLength(120);
        b.Property(x => x.Type).HasConversion<int>();
        b.Property(x => x.Basis).HasConversion<int>();
        b.Property(x => x.Rate).HasPrecision(19, 6);
        b.Property(x => x.Formula).HasMaxLength(1000);
        b.Property(x => x.AccountCode).HasMaxLength(8);
        b.Property(x => x.ContributionExpenseAccountCode).HasMaxLength(8);
        b.HasIndex(x => new { x.CompanyId, x.Code }).IsUnique().HasFilter("is_deleted = false");
        b.HasQueryFilter(One4AllDbContext.SoftDeleteFilter, x => !x.IsDeleted);
    }
}

internal sealed class PayslipConfiguration : IEntityTypeConfiguration<Payslip>
{
    public void Configure(EntityTypeBuilder<Payslip> b)
    {
        b.ToTable("payslip", Schemas.Payroll);
        b.HasKey(x => x.Id);
        b.Property(x => x.PeriodLabel).HasMaxLength(40);
        b.HasMany(x => x.Lines).WithOne().HasForeignKey(x => x.PayslipId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.Property(x => x.RunKind).HasConversion<int>();
        b.Property(x => x.RunReference).HasMaxLength(40).HasDefaultValue(string.Empty);
        b.Property(x => x.PayGroup).HasMaxLength(20).HasDefaultValue(string.Empty);
        // One payslip per employee per run; one live regular run per period is enforced on the run register.
        b.HasIndex(x => new { x.CompanyId, x.EmployeeId, x.PeriodLabel, x.RunReference }).IsUnique();
    }
}

internal sealed class PayslipLineConfiguration : IEntityTypeConfiguration<PayslipLine>
{
    public void Configure(EntityTypeBuilder<PayslipLine> b)
    {
        b.ToTable("payslip_line", Schemas.Payroll);
        b.HasKey(x => x.Id);
        b.Property(x => x.ElementCode).HasMaxLength(30);
        b.Property(x => x.Workings).HasMaxLength(1000);
        b.Property(x => x.Description).HasMaxLength(120);
        b.Property(x => x.Type).HasConversion<int>();
    }
}

internal sealed class EmployeeLoanConfiguration : IEntityTypeConfiguration<EmployeeLoan>
{
    public void Configure(EntityTypeBuilder<EmployeeLoan> b)
    {
        b.ToTable("employee_loan", Schemas.Payroll);
        b.HasKey(x => x.Id);
        b.Property(x => x.Reference).HasMaxLength(40);
        b.Property(x => x.Principal).HasPrecision(19, 4);
        b.Property(x => x.Installment).HasPrecision(19, 4);
        b.Property(x => x.Outstanding).HasPrecision(19, 4);
        b.Property(x => x.LoanReceivableAccountCode).HasMaxLength(8);
        b.Property(x => x.Status).HasConversion<int>();
        b.HasIndex(x => new { x.CompanyId, x.EmployeeId, x.Status });
    }
}

internal sealed class EmployeeGrievanceConfiguration : IEntityTypeConfiguration<EmployeeGrievance>
{
    public void Configure(EntityTypeBuilder<EmployeeGrievance> b)
    {
        b.ToTable("grievance", Schemas.HumanResources);
        b.HasKey(x => x.Id);
        b.Property(x => x.Subject).HasMaxLength(200);
        b.Property(x => x.Details).HasColumnType("text");
        b.Property(x => x.Against).HasMaxLength(200);
        b.Property(x => x.Status).HasConversion<int>();
        b.Property(x => x.Outcome).HasColumnType("text");
        b.HasIndex(x => new { x.CompanyId, x.Status });
    }
}

internal sealed class EmployeeAssetIssueConfiguration : IEntityTypeConfiguration<EmployeeAssetIssue>
{
    public void Configure(EntityTypeBuilder<EmployeeAssetIssue> b)
    {
        b.ToTable("asset_issue", Schemas.HumanResources);
        b.HasKey(x => x.Id);
        b.Property(x => x.Description).HasMaxLength(200);
        b.Property(x => x.SerialNumber).HasMaxLength(60);
        b.Property(x => x.Value).HasPrecision(18, 2);
        b.Property(x => x.ReturnCondition).HasMaxLength(200);
        b.HasIndex(x => new { x.CompanyId, x.EmployeeId });
    }
}

internal sealed class EmployeeMedicalRecordConfiguration : IEntityTypeConfiguration<EmployeeMedicalRecord>
{
    public void Configure(EntityTypeBuilder<EmployeeMedicalRecord> b)
    {
        b.ToTable("medical_record", Schemas.HumanResources);
        b.HasKey(x => x.Id);
        b.Property(x => x.Kind).HasConversion<int>();
        b.Property(x => x.Note).HasColumnType("text").Encrypted();
        b.HasIndex(x => new { x.CompanyId, x.EmployeeId });
    }
}

internal sealed class EmployeeDependantConfiguration : IEntityTypeConfiguration<EmployeeDependant>
{
    public void Configure(EntityTypeBuilder<EmployeeDependant> b)
    {
        b.ToTable("employee_dependant", Schemas.HumanResources);
        b.HasKey(x => x.Id);
        b.Property(x => x.Kind).HasConversion<int>();
        b.Property(x => x.Name).HasMaxLength(200);
        b.Property(x => x.Relationship).HasMaxLength(50);
        b.Property(x => x.IdNumber).HasMaxLength(30);
        b.Property(x => x.MedicalAidNumber).HasMaxLength(30);
        b.Property(x => x.SharePercent).HasPrecision(5, 2);
        b.HasIndex(x => new { x.CompanyId, x.EmployeeId });
    }
}

internal sealed class EmployeeLeaveBalanceConfiguration : IEntityTypeConfiguration<EmployeeLeaveBalance>
{
    public void Configure(EntityTypeBuilder<EmployeeLeaveBalance> b)
    {
        b.ToTable("leave_balance", Schemas.Payroll);
        b.HasKey(x => x.Id);
        b.Property(x => x.LeaveType).HasMaxLength(20);
        b.Property(x => x.AccruedDays).HasPrecision(9, 4);
        b.Property(x => x.TakenDays).HasPrecision(9, 4);
        b.Property(x => x.ForfeitedDays).HasPrecision(9, 4);
        b.Property(x => x.CarriedOverDays).HasPrecision(9, 4);
        b.Ignore(x => x.BalanceDays);
        b.HasIndex(x => new { x.CompanyId, x.EmployeeId, x.LeaveType }).IsUnique();
    }
}

internal sealed class StatutorySchemeConfiguration : IEntityTypeConfiguration<StatutoryScheme>
{
    public void Configure(EntityTypeBuilder<StatutoryScheme> b)
    {
        b.ToTable("statutory_scheme", Schemas.HumanResources);
        b.HasKey(x => x.Id);
        b.Property(x => x.Code).HasMaxLength(30);
        b.Property(x => x.Name).HasMaxLength(150);
        b.Property(x => x.Kind).HasConversion<int>();
        b.Property(x => x.Provider).HasMaxLength(150);
        b.Property(x => x.EmployeeElementCode).HasMaxLength(30);
        b.Property(x => x.EmployerElementCode).HasMaxLength(30);
        b.HasIndex(x => new { x.CompanyId, x.Code }).IsUnique();
    }
}

internal sealed class SchemeMembershipConfiguration : IEntityTypeConfiguration<SchemeMembership>
{
    public void Configure(EntityTypeBuilder<SchemeMembership> b)
    {
        b.ToTable("scheme_membership", Schemas.HumanResources);
        b.HasKey(x => x.Id);
        b.Property(x => x.MemberNumber).HasMaxLength(40);
        b.HasOne<StatutoryScheme>().WithMany().HasForeignKey(x => x.SchemeId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Employee>().WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Restrict);

        // One open membership per employee per scheme.
        b.HasIndex(x => new { x.SchemeId, x.EmployeeId }).IsUnique().HasFilter("left_on IS NULL");
    }
}

internal sealed class SchemeRemittanceConfiguration : IEntityTypeConfiguration<SchemeRemittance>
{
    public void Configure(EntityTypeBuilder<SchemeRemittance> b)
    {
        b.ToTable("scheme_remittance", Schemas.HumanResources);
        b.HasKey(x => x.Id);
        b.Property(x => x.PeriodLabel).HasMaxLength(20);
        b.Property(x => x.Amount).HasPrecision(19, 4);
        b.Property(x => x.Reference).HasMaxLength(60);
        b.HasOne<StatutoryScheme>().WithMany().HasForeignKey(x => x.SchemeId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.SchemeId, x.PeriodLabel });
    }
}

internal sealed class TaxTableConfiguration : IEntityTypeConfiguration<TaxTable>
{
    public void Configure(EntityTypeBuilder<TaxTable> b)
    {
        b.ToTable("tax_table", Schemas.HumanResources);
        b.HasKey(x => x.Id);
        b.Property(x => x.Code).HasMaxLength(30);
        b.HasMany(x => x.Bands).WithOne().HasForeignKey(x => x.TaxTableId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.Bands).UsePropertyAccessMode(PropertyAccessMode.Field).HasField("_bands");
        b.HasIndex(x => new { x.CompanyId, x.Code, x.EffectiveFrom }).IsUnique();
    }
}

internal sealed class TaxBandConfiguration : IEntityTypeConfiguration<TaxBand>
{
    public void Configure(EntityTypeBuilder<TaxBand> b)
    {
        b.ToTable("tax_band", Schemas.HumanResources);
        b.HasKey(x => x.Id);
        b.Property(x => x.From).HasPrecision(19, 4);
        b.Property(x => x.To).HasPrecision(19, 4);
        b.Property(x => x.RatePercent).HasPrecision(7, 4);
    }
}

internal sealed class PayrollRunRecordConfiguration : IEntityTypeConfiguration<PayrollRunRecord>
{
    public void Configure(EntityTypeBuilder<PayrollRunRecord> b)
    {
        b.ToTable("payroll_run", Schemas.HumanResources);
        b.HasKey(x => x.Id);
        b.Property(x => x.NetPayAccountCode).HasMaxLength(20);
        b.Property(x => x.PaymentJournalNumber).HasMaxLength(40);
        b.Property(x => x.PeriodLabel).HasMaxLength(20);
        b.Property(x => x.JournalNumber).HasMaxLength(40);
        b.Property(x => x.Gross).HasPrecision(19, 4);
        b.Property(x => x.Deductions).HasPrecision(19, 4);
        b.Property(x => x.Net).HasPrecision(19, 4);
        b.Property(x => x.Kind).HasConversion<int>();
        b.Property(x => x.DocumentNumber).HasMaxLength(40).HasDefaultValue(string.Empty);
        b.Property(x => x.Reason).HasMaxLength(300);
        b.Property(x => x.ReversedBy).HasMaxLength(40);
        b.Ignore(x => x.IsReversed);
        b.Property(x => x.PayGroup).HasMaxLength(20).HasDefaultValue(string.Empty);
        // A period is paid once per pay group: one regular run that has not been reversed (the backstop to Payroll.PeriodAlreadyRun).
        b.HasIndex(x => new { x.CompanyId, x.PayGroup, x.PeriodLabel }).IsUnique().HasFilter("kind = 0 AND reversed_on IS NULL");
    }
}

internal sealed class SalaryScaleConfiguration : IEntityTypeConfiguration<SalaryScale>
{
    public void Configure(EntityTypeBuilder<SalaryScale> b)
    {
        b.ToTable("salary_scale", Schemas.HumanResources);
        b.HasKey(x => x.Id);
        b.Property(x => x.Grade).HasMaxLength(40);
        b.Property(x => x.MonthlyAmount).HasPrecision(19, 4);
        b.HasIndex(x => new { x.CompanyId, x.Grade, x.Notch, x.EffectiveFrom }).IsUnique();
    }
}

internal sealed class ActingAppointmentConfiguration : IEntityTypeConfiguration<ActingAppointment>
{
    public void Configure(EntityTypeBuilder<ActingAppointment> b)
    {
        b.ToTable("acting_appointment", Schemas.HumanResources);
        b.HasKey(x => x.Id);
        b.Property(x => x.Grade).HasMaxLength(40);
        b.HasIndex(x => new { x.CompanyId, x.EmployeeId });
        b.HasOne<Employee>().WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class EstablishmentPostConfiguration : IEntityTypeConfiguration<EstablishmentPost>
{
    public void Configure(EntityTypeBuilder<EstablishmentPost> b)
    {
        b.ToTable("establishment_post", Schemas.HumanResources);
        b.HasKey(x => x.Id);
        b.Property(x => x.Department).HasMaxLength(80);
        b.Property(x => x.Grade).HasMaxLength(40);
        b.Property(x => x.AnnualCostPerPost).HasPrecision(19, 4);
        b.HasIndex(x => new { x.CompanyId, x.Department, x.Grade }).IsUnique();
    }
}

internal sealed class EmployeeDocumentConfiguration : IEntityTypeConfiguration<EmployeeDocument>
{
    public void Configure(EntityTypeBuilder<EmployeeDocument> b)
    {
        b.ToTable("employee_document", Schemas.HumanResources);
        b.HasKey(x => x.Id);
        b.Property(x => x.DocumentType).HasMaxLength(40);
        b.Property(x => x.Reference).HasMaxLength(80);
        b.HasIndex(x => new { x.CompanyId, x.EmployeeId });
    }
}

internal sealed class DisciplinaryRecordConfiguration : IEntityTypeConfiguration<DisciplinaryRecord>
{
    public void Configure(EntityTypeBuilder<DisciplinaryRecord> b)
    {
        b.ToTable("disciplinary", Schemas.HumanResources);
        b.HasKey(x => x.Id);
        b.Property(x => x.Offence).HasMaxLength(200);
        b.Property(x => x.Sanction).HasConversion<int>();
        b.HasIndex(x => new { x.CompanyId, x.EmployeeId });
    }
}

internal sealed class TrainingRecordConfiguration : IEntityTypeConfiguration<TrainingRecord>
{
    public void Configure(EntityTypeBuilder<TrainingRecord> b)
    {
        b.ToTable("training", Schemas.HumanResources);
        b.HasKey(x => x.Id);
        b.Property(x => x.Course).HasMaxLength(160);
        b.Property(x => x.Provider).HasMaxLength(120);
        b.Property(x => x.Cost).HasPrecision(19, 4);
        b.HasIndex(x => new { x.CompanyId, x.EmployeeId });
    }
}

internal sealed class EmployeeAppraisalConfiguration : IEntityTypeConfiguration<EmployeeAppraisal>
{
    public void Configure(EntityTypeBuilder<EmployeeAppraisal> b)
    {
        b.ToTable("appraisal", Schemas.HumanResources);
        b.HasKey(x => x.Id);
        b.HasIndex(x => new { x.CompanyId, x.EmployeeId });
    }
}

internal sealed class GarnisheeOrderConfiguration : IEntityTypeConfiguration<GarnisheeOrder>
{
    public void Configure(EntityTypeBuilder<GarnisheeOrder> b)
    {
        b.ToTable("garnishee_order", Schemas.HumanResources);
        b.HasKey(x => x.Id);
        b.Property(x => x.Reference).HasMaxLength(60);
        b.Property(x => x.Creditor).HasMaxLength(160);
        b.Property(x => x.ElementCode).HasMaxLength(20);
        b.Property(x => x.Instalment).HasPrecision(19, 4);
        b.Property(x => x.TotalAmount).HasPrecision(19, 4);
        b.HasIndex(x => new { x.CompanyId, x.Reference }).IsUnique();
    }
}

internal sealed class LeaveRecordConfiguration : IEntityTypeConfiguration<LeaveRecord>
{
    public void Configure(EntityTypeBuilder<LeaveRecord> b)
    {
        b.ToTable("leave_record", Schemas.HumanResources);
        b.HasKey(x => x.Id);
        b.Property(x => x.LeaveType).HasMaxLength(40);
        b.Property(x => x.Kind).HasConversion<int>();
        b.Property(x => x.Days).HasPrecision(9, 2);
        b.Property(x => x.Amount).HasPrecision(19, 4);
        b.HasIndex(x => new { x.EmployeeId, x.FromDate });
    }
}

internal sealed class AttendanceRecordConfiguration : IEntityTypeConfiguration<AttendanceRecord>
{
    public void Configure(EntityTypeBuilder<AttendanceRecord> b)
    {
        b.ToTable("attendance", Schemas.HumanResources);
        b.HasKey(x => x.Id);
        b.Property(x => x.Status).HasConversion<int>();
        b.Property(x => x.OvertimeHours).HasPrecision(9, 2);
        b.Property(x => x.OvertimeMultiplier).HasPrecision(5, 2);
        b.HasIndex(x => new { x.EmployeeId, x.Date }).IsUnique();
    }
}

internal sealed class EmployeePaymentSplitConfiguration : IEntityTypeConfiguration<EmployeePaymentSplit>
{
    public void Configure(EntityTypeBuilder<EmployeePaymentSplit> b)
    {
        b.ToTable("payment_split", Schemas.HumanResources);
        b.HasKey(x => x.Id);
        b.Property(x => x.Method).HasConversion<int>();
        b.Property(x => x.Destination).HasMaxLength(80);
        b.Property(x => x.Amount).HasPrecision(19, 4);
        b.Property(x => x.Percent).HasPrecision(9, 4);
        b.HasIndex(x => x.EmployeeId);
    }
}

internal sealed class PayslipPrintConfiguration : IEntityTypeConfiguration<PayslipPrint>
{
    public void Configure(EntityTypeBuilder<PayslipPrint> b)
    {
        b.ToTable("payslip_print", Schemas.HumanResources);
        b.HasKey(x => x.Id);
        b.Property(x => x.PeriodLabel).HasMaxLength(20);
        b.HasIndex(x => new { x.EmployeeId, x.PeriodLabel });
    }
}

internal sealed class LegacyPayslipLineConfiguration : IEntityTypeConfiguration<LegacyPayslipLine>
{
    public void Configure(EntityTypeBuilder<LegacyPayslipLine> b)
    {
        b.ToTable("legacy_payslip_line", Schemas.Payroll);
        b.HasKey(x => x.Id);
        b.Property(x => x.PeriodLabel).HasMaxLength(40);
        b.Property(x => x.EmployeeNumber).HasMaxLength(30);
        b.Property(x => x.ElementCode).HasMaxLength(30);
        b.Property(x => x.Amount).HasPrecision(19, 4);
        b.HasIndex(x => new { x.CompanyId, x.PeriodLabel });
    }
}

internal sealed class BackpayLineConfiguration : IEntityTypeConfiguration<BackpayLine>
{
    public void Configure(EntityTypeBuilder<BackpayLine> b)
    {
        b.ToTable("backpay_line", Schemas.Payroll);
        b.HasKey(x => x.Id);
        b.Property(x => x.ForPeriod).HasMaxLength(40);
        b.Property(x => x.ElementCode).HasMaxLength(30);
        b.Property(x => x.RunReference).HasMaxLength(40);
        b.Property(x => x.Reason).HasMaxLength(300);
        b.Property(x => x.Paid).HasPrecision(19, 4);
        b.Property(x => x.Due).HasPrecision(19, 4);
        b.Ignore(x => x.Difference);
        b.HasIndex(x => new { x.CompanyId, x.PaidOn });
    }
}

internal sealed class DeductionArrearConfiguration : IEntityTypeConfiguration<DeductionArrear>
{
    public void Configure(EntityTypeBuilder<DeductionArrear> b)
    {
        b.ToTable("deduction_arrear", Schemas.Payroll);
        b.HasKey(x => x.Id);
        b.Property(x => x.ElementCode).HasMaxLength(30);
        b.Property(x => x.PeriodLabel).HasMaxLength(40);
        b.Property(x => x.RunReference).HasMaxLength(40);
        b.Property(x => x.Reason).HasMaxLength(200);
        b.Property(x => x.Attempted).HasPrecision(19, 4);
        b.Property(x => x.Deducted).HasPrecision(19, 4);
        b.Property(x => x.Movement).HasPrecision(19, 4);
        b.HasIndex(x => new { x.CompanyId, x.EmployeeId, x.ElementCode });
    }
}

internal sealed class PayrollDraftConfiguration : IEntityTypeConfiguration<PayrollDraft>
{
    public void Configure(EntityTypeBuilder<PayrollDraft> b)
    {
        b.ToTable("payroll_draft", Schemas.Payroll);
        b.HasKey(x => x.Id);
        b.Property(x => x.PayGroup).HasMaxLength(20);
        b.Property(x => x.PeriodLabel).HasMaxLength(40);
        b.Property(x => x.NetPayAccountCode).HasMaxLength(8);
        b.Property(x => x.DocumentNumber).HasMaxLength(40);
        b.Property(x => x.JournalNumber).HasMaxLength(40);
        b.Property(x => x.Exceptions).HasMaxLength(8000);
        b.Property(x => x.Status).HasConversion<int>();
        b.Property(x => x.Gross).HasPrecision(19, 4);
        b.Property(x => x.Deductions).HasPrecision(19, 4);
        b.Property(x => x.Net).HasPrecision(19, 4);
        b.Property(x => x.PreviousNet).HasPrecision(19, 4);
        b.HasIndex(x => new { x.CompanyId, x.PayGroup, x.PeriodLabel });
    }
}

internal sealed class PayrollVariableConfiguration : IEntityTypeConfiguration<PayrollVariable>
{
    public void Configure(EntityTypeBuilder<PayrollVariable> b)
    {
        b.ToTable("payroll_variable", Schemas.Payroll);
        b.HasKey(x => x.Id);
        b.Property(x => x.ElementCode).HasMaxLength(30);
        b.Property(x => x.PeriodLabel).HasMaxLength(40);
        b.Property(x => x.Source).HasMaxLength(10);
        b.Property(x => x.Units).HasPrecision(19, 4);
        b.Property(x => x.Amount).HasPrecision(19, 4);
        b.HasIndex(x => new { x.CompanyId, x.PeriodLabel, x.EmployeeId });
    }
}

internal sealed class RemittanceInvoiceConfiguration : IEntityTypeConfiguration<RemittanceInvoice>
{
    public void Configure(EntityTypeBuilder<RemittanceInvoice> b)
    {
        b.ToTable("remittance_invoice", Schemas.Payroll);
        b.HasKey(x => x.Id);
        b.Property(x => x.RunReference).HasMaxLength(40);
        b.Property(x => x.PeriodLabel).HasMaxLength(40);
        b.Property(x => x.Payee).HasMaxLength(40);
        b.Property(x => x.AccountCode).HasMaxLength(8);
        b.Property(x => x.DocumentNumber).HasMaxLength(40);
        b.Property(x => x.Amount).HasPrecision(19, 4);
        b.HasIndex(x => new { x.CompanyId, x.RunReference });
    }
}

internal sealed class LeaveApplicationConfiguration : IEntityTypeConfiguration<LeaveApplication>
{
    public void Configure(EntityTypeBuilder<LeaveApplication> b)
    {
        b.ToTable("leave_application", Schemas.HumanResources);
        b.HasKey(x => x.Id);
        b.Property(x => x.LeaveType).HasMaxLength(20);
        b.Property(x => x.Days).HasPrecision(9, 2);
        b.Property(x => x.Status).HasConversion<int>();
        b.Property(x => x.Note).HasMaxLength(200);
        b.HasIndex(x => new { x.CompanyId, x.Status });
    }
}

internal sealed class DetailChangeRequestConfiguration : IEntityTypeConfiguration<DetailChangeRequest>
{
    public void Configure(EntityTypeBuilder<DetailChangeRequest> b)
    {
        b.ToTable("detail_change_request", Schemas.HumanResources);
        b.HasKey(x => x.Id);
        b.Property(x => x.Field).HasMaxLength(40);
        b.Property(x => x.OldValue).Encrypted();
        b.Property(x => x.NewValue).Encrypted();
        b.Property(x => x.Status).HasConversion<int>();
        b.Property(x => x.Note).HasMaxLength(200);
        b.HasIndex(x => new { x.CompanyId, x.Status });
    }
}

internal sealed class TerminationConfiguration : IEntityTypeConfiguration<Termination>
{
    public void Configure(EntityTypeBuilder<Termination> b)
    {
        b.ToTable("termination", Schemas.Payroll);
        b.HasKey(x => x.Id);
        b.Property(x => x.Reason).HasMaxLength(300);
        b.Property(x => x.JournalNumber).HasMaxLength(30);
        b.HasIndex(x => new { x.CompanyId, x.EmployeeId });
        b.HasMany(x => x.Clearance).WithOne().HasForeignKey(x => x.TerminationId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.Clearance).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class TerminationClearanceItemConfiguration : IEntityTypeConfiguration<TerminationClearanceItem>
{
    public void Configure(EntityTypeBuilder<TerminationClearanceItem> b)
    {
        b.ToTable("termination_clearance", Schemas.Payroll);
        b.HasKey(x => x.Id);
        b.Property(x => x.Description).HasMaxLength(200);
    }
}

internal sealed class LeavePolicyConfiguration : IEntityTypeConfiguration<LeavePolicy>
{
    public void Configure(EntityTypeBuilder<LeavePolicy> b)
    {
        b.ToTable("leave_policy", Schemas.Payroll);
        b.HasKey(x => x.Id);
        b.Property(x => x.LeaveType).HasMaxLength(20);
        b.Property(x => x.AnnualDays).HasPrecision(9, 4);
        b.Property(x => x.CarryOverCapDays).HasPrecision(9, 4);
        b.Property(x => x.MaxAccumulationDays).HasPrecision(9, 4);
        b.Property(x => x.NegativeBalanceDays).HasPrecision(9, 4);
        b.HasIndex(x => new { x.CompanyId, x.LeaveType }).IsUnique();
        b.HasMany(x => x.SickTiers).WithOne().HasForeignKey(x => x.LeavePolicyId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.SickTiers).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.HasMany(x => x.Bands).WithOne().HasForeignKey(x => x.LeavePolicyId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.Bands).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class LeaveServiceBandConfiguration : IEntityTypeConfiguration<LeaveServiceBand>
{
    public void Configure(EntityTypeBuilder<LeaveServiceBand> b)
    {
        b.ToTable("leave_service_band", Schemas.Payroll);
        b.HasKey(x => x.Id);
        b.Property(x => x.AnnualDays).HasPrecision(9, 4);
    }
}

internal sealed class LeaveSickTierConfiguration : IEntityTypeConfiguration<LeaveSickTier>
{
    public void Configure(EntityTypeBuilder<LeaveSickTier> b)
    {
        b.ToTable("leave_sick_tier", Schemas.Payroll);
        b.HasKey(x => x.Id);
        b.Property(x => x.PayPercent).HasPrecision(5, 2);
    }
}

internal sealed class ShiftConfiguration : IEntityTypeConfiguration<Shift>
{
    public void Configure(EntityTypeBuilder<Shift> b)
    {
        b.ToTable("shift", Schemas.HumanResources);
        b.HasKey(x => x.Id);
        b.Property(x => x.Code).HasMaxLength(10);
        b.Property(x => x.Name).HasMaxLength(60);
        b.Property(x => x.Allowance).HasPrecision(19, 4);
        b.HasIndex(x => new { x.CompanyId, x.Code }).IsUnique();
    }
}

internal sealed class ShiftPatternConfiguration : IEntityTypeConfiguration<ShiftPattern>
{
    public void Configure(EntityTypeBuilder<ShiftPattern> b)
    {
        b.ToTable("shift_pattern", Schemas.HumanResources);
        b.HasKey(x => x.Id);
        b.Property(x => x.Code).HasMaxLength(20);
        b.Property(x => x.Steps).HasMaxLength(400);
        b.Ignore(x => x.Cycle);
        b.HasIndex(x => new { x.CompanyId, x.Code }).IsUnique();
    }
}

internal sealed class RosterAssignmentConfiguration : IEntityTypeConfiguration<RosterAssignment>
{
    public void Configure(EntityTypeBuilder<RosterAssignment> b)
    {
        b.ToTable("roster_assignment", Schemas.HumanResources);
        b.HasKey(x => x.Id);
        b.HasIndex(x => x.EmployeeId).IsUnique();
        b.HasOne<ShiftPattern>().WithMany().HasForeignKey(x => x.ShiftPatternId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class RosterEntryConfiguration : IEntityTypeConfiguration<RosterEntry>
{
    public void Configure(EntityTypeBuilder<RosterEntry> b)
    {
        b.ToTable("roster_entry", Schemas.HumanResources);
        b.HasKey(x => x.Id);
        b.HasIndex(x => new { x.EmployeeId, x.Date }).IsUnique();
        b.HasOne<Shift>().WithMany().HasForeignKey(x => x.ShiftId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class PayrollYtdOpeningConfiguration : IEntityTypeConfiguration<PayrollYtdOpening>
{
    public void Configure(EntityTypeBuilder<PayrollYtdOpening> b)
    {
        b.ToTable("ytd_opening", Schemas.Payroll);
        b.HasKey(x => x.Id);
        b.Property(x => x.Code).HasMaxLength(20);
        b.Property(x => x.Amount).HasPrecision(19, 4);
        b.HasIndex(x => new { x.EmployeeId, x.TaxYear, x.Code }).IsUnique();
    }
}

internal sealed class EmployeeBenefitConfiguration : IEntityTypeConfiguration<EmployeeBenefit>
{
    public void Configure(EntityTypeBuilder<EmployeeBenefit> b)
    {
        b.ToTable("employee_benefit", Schemas.Payroll);
        b.Property(x => x.Description).HasMaxLength(200);
        b.HasIndex(x => new { x.CompanyId, x.EmployeeId });
    }
}

internal sealed class VehicleBenefitScaleConfiguration : IEntityTypeConfiguration<VehicleBenefitScale>
{
    public void Configure(EntityTypeBuilder<VehicleBenefitScale> b)
    {
        b.ToTable("vehicle_benefit_scale", Schemas.Payroll);
        b.HasIndex(x => new { x.CompanyId, x.Basis, x.EffectiveFrom });
    }
}

internal sealed class TaxCreditConfiguration : IEntityTypeConfiguration<TaxCredit>
{
    public void Configure(EntityTypeBuilder<TaxCredit> b)
    {
        b.ToTable("tax_credit", Schemas.HumanResources);
        b.HasKey(x => x.Id);
        b.Property(x => x.Code).HasMaxLength(20);
        b.Property(x => x.Name).HasMaxLength(120);
        b.Property(x => x.Basis).HasConversion<int>();
        b.Property(x => x.Amount).HasPrecision(19, 4);
        b.Property(x => x.Percent).HasPrecision(9, 4);
        b.Property(x => x.ElementCode).HasMaxLength(20);
        b.HasIndex(x => new { x.CompanyId, x.Code }).IsUnique();
    }
}

internal sealed class EmployeeTaxCreditConfiguration : IEntityTypeConfiguration<EmployeeTaxCredit>
{
    public void Configure(EntityTypeBuilder<EmployeeTaxCredit> b)
    {
        b.ToTable("employee_tax_credit", Schemas.HumanResources);
        b.HasKey(x => x.Id);
        b.HasIndex(x => new { x.EmployeeId, x.TaxCreditId }).IsUnique();
    }
}
