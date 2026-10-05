using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Payroll;
using One4All.Application.Payroll;
using One4All.Core.Common;
using One4All.Core.Payroll;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Payroll;

/// <summary>Keeps employee HR records: establishment, documents, discipline, training, appraisals and court orders.</summary>
public sealed class HrRecordsService : IHrRecordsService
{
    private readonly One4AllDbContext _db;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    public HrRecordsService(One4AllDbContext db) => _db = db;

    private Task<bool> EmployeeExistsAsync(long companyId, long employeeId, CancellationToken ct)
        => _db.Employees.AnyAsync(e => e.Id == employeeId && e.CompanyId == companyId, ct);

    private static Error NoEmployee(long employeeId) => Error.NotFound("Hr.EmployeeNotFound", $"Employee {employeeId} not found.");

    /// <inheritdoc />
    public async Task<Result> SetEstablishmentAsync(long companyId, string department, string grade, int approvedPosts, decimal annualCostPerPost, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(department) || string.IsNullOrWhiteSpace(grade) || approvedPosts < 0 || annualCostPerPost < 0m)
        {
            return Result.Failure(Error.Validation("Hr.EstablishmentInvalid", "An establishment line needs a department, a grade and non-negative posts and cost."));
        }

        string d = department.Trim(), g = grade.Trim();
        EstablishmentPost? post = await _db.EstablishmentPosts.FirstOrDefaultAsync(p => p.CompanyId == companyId && p.Department == d && p.Grade == g, ct).ConfigureAwait(false);
        if (post is null)
        {
            _db.EstablishmentPosts.Add(new EstablishmentPost(companyId, d, g, approvedPosts, annualCostPerPost));
        }
        else
        {
            post.ApprovedPosts = approvedPosts;
            post.AnnualCostPerPost = annualCostPerPost;
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result<long>> FileDocumentAsync(long companyId, long employeeId, string documentType, string? reference, DateOnly? expiresOn, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(documentType))
        {
            return Error.Validation("Hr.DocumentType", "A document needs a type.");
        }

        if (!await EmployeeExistsAsync(companyId, employeeId, ct).ConfigureAwait(false))
        {
            return NoEmployee(employeeId);
        }

        EmployeeDocument document = new(companyId, employeeId, documentType, reference?.Trim(), expiresOn);
        _db.EmployeeDocuments.Add(document);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return document.Id;
    }

    /// <inheritdoc />
    public async Task<Result<long>> RecordDisciplinaryAsync(long companyId, long employeeId, DateOnly date, string offence, DisciplinarySanction sanction, long? issuedByUserId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(offence) || !Enum.IsDefined(sanction))
        {
            return Error.Validation("Hr.DisciplinaryInvalid", "A disciplinary record needs the offence and a sanction.");
        }

        if (!await EmployeeExistsAsync(companyId, employeeId, ct).ConfigureAwait(false))
        {
            return NoEmployee(employeeId);
        }

        DisciplinaryRecord record = new(companyId, employeeId, date, offence.Trim(), sanction, HrRules.WarningExpiry(sanction, date), issuedByUserId);
        _db.DisciplinaryRecords.Add(record);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return record.Id;
    }

    /// <inheritdoc />
    public async Task<Result<long>> RecordTrainingAsync(long companyId, long employeeId, string course, DateOnly completedOn, DateOnly? expiresOn, string? provider, decimal cost, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(course) || cost < 0m || (expiresOn is DateOnly e && e <= completedOn))
        {
            return Error.Validation("Hr.TrainingInvalid", "Training needs a course, a non-negative cost and any renewal date after completion.");
        }

        if (!await EmployeeExistsAsync(companyId, employeeId, ct).ConfigureAwait(false))
        {
            return NoEmployee(employeeId);
        }

        TrainingRecord record = new(companyId, employeeId, course.Trim(), completedOn, expiresOn, provider?.Trim(), cost);
        _db.TrainingRecords.Add(record);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return record.Id;
    }

    /// <inheritdoc />
    public async Task<Result<long>> RecordAppraisalAsync(long companyId, long employeeId, DateOnly date, int rating, DateOnly nextDue, CancellationToken ct = default)
    {
        if (rating is < 1 or > 5 || nextDue <= date)
        {
            return Error.Validation("Hr.AppraisalInvalid", "An appraisal needs a rating from 1 to 5 and a next due date after it.");
        }

        if (!await EmployeeExistsAsync(companyId, employeeId, ct).ConfigureAwait(false))
        {
            return NoEmployee(employeeId);
        }

        EmployeeAppraisal appraisal = new(companyId, employeeId, date, rating, nextDue);
        _db.EmployeeAppraisals.Add(appraisal);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return appraisal.Id;
    }

    /// <inheritdoc />
    public async Task<Result<long>> RegisterGarnisheeAsync(long companyId, long employeeId, string reference, string creditor, int priority, decimal instalment, decimal totalAmount, string elementCode, DateOnly issuedOn,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(reference) || string.IsNullOrWhiteSpace(creditor) || priority < 1 || instalment <= 0m || totalAmount < instalment)
        {
            return Error.Validation("Hr.GarnisheeInvalid", "An order needs a reference, a creditor, a priority from 1, a positive instalment and a total at least one instalment.");
        }

        if (!await EmployeeExistsAsync(companyId, employeeId, ct).ConfigureAwait(false))
        {
            return NoEmployee(employeeId);
        }

        string code = elementCode.Trim().ToUpperInvariant();
        if (!await _db.PayElements.AnyAsync(p => p.CompanyId == companyId && p.Code == code && p.Type == PayElementType.Deduction, ct).ConfigureAwait(false))
        {
            return Error.Validation("Hr.GarnisheeElement", $"{code} is not a deduction pay element.");
        }

        string normalised = reference.Trim().ToUpperInvariant();
        if (await _db.GarnisheeOrders.AnyAsync(g => g.CompanyId == companyId && g.Reference == normalised, ct).ConfigureAwait(false))
        {
            return Error.Conflict("Hr.GarnisheeExists", $"Order {normalised} is already registered.");
        }

        GarnisheeOrder order = new(companyId, employeeId, normalised, creditor.Trim(), priority, instalment, totalAmount, code, issuedOn);
        _db.GarnisheeOrders.Add(order);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return order.Id;
    }
}
