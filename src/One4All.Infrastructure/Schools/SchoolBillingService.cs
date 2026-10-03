using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Schools;
using One4All.Application.Abstractions.Trading;
using One4All.Core.Common;
using One4All.Core.Schools;
using One4All.Core.System;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Schools;

/// <summary>
/// School fee billing (brief §4.11). Resolves a fee structure's compulsory total, applies each student's
/// bursary, and raises the net on the guardian/sponsor account through the central AR ledger — so fees
/// age, reconcile and appear on the guardian statement like any receivable.
/// </summary>
public sealed class SchoolBillingService : ISchoolBillingService
{
    private readonly One4AllDbContext _db;
    private readonly ISalesLedgerService _salesLedger;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="salesLedger">The AR ledger fees are raised on.</param>
    public SchoolBillingService(One4AllDbContext db, ISalesLedgerService salesLedger)
    {
        _db = db;
        _salesLedger = salesLedger;
    }

    /// <inheritdoc />
    public async Task<Result<StudentBillResult>> BillStudentAsync(long companyId, long studentId, long feeStructureId, string invoiceNumber, DateOnly date, CancellationToken ct = default)
    {
        Student? student = await _db.Set<Student>().FirstOrDefaultAsync(s => s.Id == studentId && s.CompanyId == companyId, ct).ConfigureAwait(false);
        if (student is null)
        {
            return Error.NotFound("School.StudentNotFound", $"Student {studentId} not found.");
        }

        FeeStructure? structure = await _db.Set<FeeStructure>().Include(f => f.Items)
            .FirstOrDefaultAsync(f => f.Id == feeStructureId && f.CompanyId == companyId, ct).ConfigureAwait(false);
        if (structure is null)
        {
            return Error.NotFound("School.FeeStructureNotFound", $"Fee structure {feeStructureId} not found.");
        }

        Company? company = await _db.Set<Company>().FirstOrDefaultAsync(c => c.Id == companyId, ct).ConfigureAwait(false);
        if (company is null)
        {
            return Error.NotFound("School.CompanyNotFound", $"Company {companyId} not found.");
        }

        if (string.IsNullOrWhiteSpace(invoiceNumber))
        {
            return Error.Validation("School.InvoiceNumberRequired", "An invoice number is required.");
        }

        string number = invoiceNumber.Trim();
        if ((await AlreadyRaisedAsync(companyId, [number], ct).ConfigureAwait(false)).Count > 0)
        {
            return Error.Conflict("School.AlreadyBilled", $"Invoice {number} has already been raised.");
        }

        return await BillOneAsync(company, student, structure, number, date, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<Result<TermBillResult>> BillTermAsync(long companyId, long feeStructureId, string invoiceNumberPrefix, DateOnly date, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(invoiceNumberPrefix))
        {
            return Error.Validation("School.PrefixRequired", "A term billing run needs an invoice-number prefix identifying the term.");
        }

        string prefix = invoiceNumberPrefix.Trim();
        FeeStructure? structure = await _db.Set<FeeStructure>().Include(f => f.Items)
            .FirstOrDefaultAsync(f => f.Id == feeStructureId && f.CompanyId == companyId, ct).ConfigureAwait(false);
        if (structure is null)
        {
            return Error.NotFound("School.FeeStructureNotFound", $"Fee structure {feeStructureId} not found.");
        }

        Company? company = await _db.Set<Company>().FirstOrDefaultAsync(c => c.Id == companyId, ct).ConfigureAwait(false);
        if (company is null)
        {
            return Error.NotFound("School.CompanyNotFound", $"Company {companyId} not found.");
        }

        List<Student> students = await _db.Set<Student>()
            .Where(s => s.CompanyId == companyId && s.IsEnrolled && s.Grade == structure.Grade)
            .OrderBy(s => s.AdmissionNumber)
            .ToListAsync(ct).ConfigureAwait(false);

        // A number already on the receivables ledger means that student was billed by an earlier run of this term.
        HashSet<string> alreadyRaised = await AlreadyRaisedAsync(companyId, [.. students.Select(s => prefix + s.AdmissionNumber)], ct).ConfigureAwait(false);

        List<StudentBillResult> bills = [];
        decimal totalNet = 0m;
        int skipped = 0;
        foreach (Student student in students)
        {
            string number = prefix + student.AdmissionNumber;
            if (alreadyRaised.Contains(number))
            {
                skipped++;
                continue;
            }

            Result<StudentBillResult> one = await BillOneAsync(company, student, structure, number, date, ct).ConfigureAwait(false);
            if (one.IsFailure)
            {
                return one.Error;
            }

            bills.Add(one.Value);
            totalNet += one.Value.NetFees;
        }

        return new TermBillResult(bills, totalNet, skipped);
    }

    private async Task<HashSet<string>> AlreadyRaisedAsync(long companyId, List<string> numbers, CancellationToken ct)
        => (await _db.OpenItems
            .Where(i => i.CompanyId == companyId && i.Ledger == TradingLedger.AccountsReceivable && numbers.Contains(i.DocumentNumber))
            .Select(i => i.DocumentNumber)
            .ToListAsync(ct).ConfigureAwait(false)).ToHashSet(StringComparer.Ordinal);

    private async Task<Result<StudentBillResult>> BillOneAsync(Company company, Student student, FeeStructure structure, string invoiceNumber, DateOnly date, CancellationToken ct)
    {
        decimal gross = structure.CompulsoryTotal;
        decimal discount = student.ComputeDiscount(gross);
        decimal net = gross - discount;
        if (net <= 0m)
        {
            return new StudentBillResult(student.Id, gross, discount, 0m, null, null);
        }

        Result<TradingPostingResult> posted = await _salesLedger.PostInvoiceAsync(new SalesInvoiceRequest
        {
            CompanyId = company.Id,
            CustomerId = student.GuardianCustomerId,
            DocumentNumber = invoiceNumber,
            DocumentDate = date,
            Currency = company.BaseCurrency,
            NetAmount = net,
            TaxAmount = 0m,
            SalesAccountCode = structure.RevenueAccountCode,
            Narrative = $"{structure.Term} fees — {student.FullName} ({student.AdmissionNumber})",
        }, ct).ConfigureAwait(false);

        if (posted.IsFailure)
        {
            return posted.Error;
        }

        return new StudentBillResult(student.Id, gross, discount, net, posted.Value.OpenItemId, posted.Value.JournalNumber);
    }
}
