using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Schools;
using One4All.Core.Common;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Schools;

/// <summary>A student's fee documents picked out of the paying accounts.</summary>
public sealed class StudentAccountService : IStudentAccountService
{
    private readonly One4AllDbContext _db;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    public StudentAccountService(One4AllDbContext db) => _db = db;

    /// <inheritdoc />
    public async Task<Result<StudentAccount>> AccountAsync(long companyId, string admissionNumber, CancellationToken ct = default)
    {
        string key = (admissionNumber ?? string.Empty).Trim().ToUpperInvariant();
        var student = await _db.Students.AsNoTracking().IgnoreQueryFilters([One4AllDbContext.SoftDeleteFilter])
            .Where(s => s.CompanyId == companyId && s.AdmissionNumber.ToUpper() == key).Select(s => new { s.AdmissionNumber, s.FullName }).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (student is null)
        {
            return Error.NotFound("School.StudentNotFound", $"No student {key}.");
        }

        // Fee documents name the student as "(admission number)" in their narrative, whichever account they were charged to.
        string tag = $"({student.AdmissionNumber})";
        var documents = await (from o in _db.OpenItems.AsNoTracking()
                               join j in _db.Journals.AsNoTracking() on o.JournalEntryId equals j.Id
                               join c in _db.Customers.AsNoTracking() on o.TraderId equals c.Id
                               where o.CompanyId == companyId && o.Ledger == TradingLedger.AccountsReceivable && j.Narrative != null && j.Narrative.Contains(tag)
                                     && (o.ItemType == OpenItemType.Invoice || o.ItemType == OpenItemType.CreditNote || o.ItemType == OpenItemType.DebitNote)
                               orderby o.DocumentDate, o.Id
                               select new { o.DocumentDate, o.DocumentNumber, o.ItemType, o.Sign, o.OriginalBase, o.OutstandingBase, j.Narrative, Payer = c.Code })
            .ToListAsync(ct).ConfigureAwait(false);
        return new StudentAccount(student.AdmissionNumber, student.FullName, [.. documents.Select(d =>
        {
            decimal sign = d.Sign == LedgerSign.Debit ? 1m : -1m;
            string description = d.Narrative!.Replace($" - {student.FullName} {tag}", string.Empty, StringComparison.Ordinal);
            return new StudentAccountLine(d.DocumentDate, d.DocumentNumber, d.ItemType.ToString(), description, d.Payer, sign * d.OriginalBase, sign * (d.OriginalBase - d.OutstandingBase),
                sign * d.OutstandingBase);
        })]);
    }
}
