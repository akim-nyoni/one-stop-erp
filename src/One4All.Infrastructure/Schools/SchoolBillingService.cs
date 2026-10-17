using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Schools;
using One4All.Application.Abstractions.Trading;
using One4All.Core.Accounting;
using One4All.Core.Common;
using One4All.Core.Schools;
using One4All.Core.System;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Schools;

/// <summary>
/// School fee billing (brief §4.11). Resolves a fee structure's compulsory total, applies each student's
/// bursary, and raises the net on the guardian/sponsor account through the central AR ledger - so fees
/// age, reconcile and appear on the guardian statement like any receivable.
/// </summary>
public sealed class SchoolBillingService : ISchoolBillingService
{
    private readonly One4AllDbContext _db;
    private readonly ISalesLedgerService _salesLedger;
    private readonly IAllocationService? _allocations;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="salesLedger">The AR ledger fees are raised on.</param>
    public SchoolBillingService(One4AllDbContext db, ISalesLedgerService salesLedger, IAllocationService? allocations = null,
        Application.Abstractions.Communications.IStatementDespatchService? despatch = null)
    {
        _despatch = despatch;
        _allocations = allocations;
        _db = db;
        _salesLedger = salesLedger;
    }

    private readonly Application.Abstractions.Communications.IStatementDespatchService? _despatch;

    /// <inheritdoc />
    public async Task<Result<Application.Abstractions.Communications.StatementDespatch>> SendFeeRemindersAsync(long companyId, DateOnly asAt, string templateCode, string language,
        decimal minimumBalance, CancellationToken ct = default)
    {
        if (_despatch is null)
        {
            return Error.Rule("School.NoDespatch", "Sending reminders is not available.");
        }

        // Whoever pays for an enrolled student: the sponsor where there is one, otherwise the guardian.
        List<long> payers = await _db.Students.AsNoTracking().Where(s => s.CompanyId == companyId && s.IsEnrolled)
            .Select(s => s.SponsorCustomerId ?? s.GuardianCustomerId).Distinct().ToListAsync(ct).ConfigureAwait(false);
        return payers.Count == 0
            ? Error.Rule("School.NoPayers", "No enrolled student has anyone paying for them.")
            : await _despatch.QueueAsync(companyId, asAt, templateCode, language, Math.Max(0.01m, minimumBalance), payers, $"Fee reminders {asAt:yyyy-MM-dd}", ct).ConfigureAwait(false);
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

        return await _db.AtomicAsync(() => BillOneAsync(company, student, structure, number, date, ct), ct).ConfigureAwait(false);
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

            Result<StudentBillResult> one = await _db.AtomicAsync(() => BillOneAsync(company, student, structure, number, date, ct), ct).ConfigureAwait(false);
            if (one.IsFailure)
            {
                return one.Error;
            }

            bills.Add(one.Value);
            totalNet += one.Value.NetFees;
        }

        return new TermBillResult(bills, totalNet, skipped);
    }

    /// <inheritdoc />
    public async Task<Result> SetSiblingDiscountAsync(long companyId, int fromChild, decimal percent, CancellationToken ct = default)
    {
        if (fromChild < 2 || percent is < 0m or > 100m)
        {
            return Result.Failure(Error.Validation("School.SiblingDiscountInvalid", "A sibling discount starts at the second child or later and is between 0 and 100%."));
        }

        SiblingDiscount? rule = await _db.Set<SiblingDiscount>().FirstOrDefaultAsync(r => r.CompanyId == companyId && r.FromChild == fromChild, ct).ConfigureAwait(false);
        if (percent == 0m)
        {
            if (rule is not null)
            {
                _db.Remove(rule);
            }
        }
        else if (rule is null)
        {
            _db.Add(new SiblingDiscount(companyId, fromChild, percent));
        }
        else
        {
            rule.Percent = percent;
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    private async Task<HashSet<string>> AlreadyRaisedAsync(long companyId, List<string> numbers, CancellationToken ct)
        => (await _db.OpenItems
            .Where(i => i.CompanyId == companyId && i.Ledger == TradingLedger.AccountsReceivable && numbers.Contains(i.DocumentNumber))
            .Select(i => i.DocumentNumber)
            .ToListAsync(ct).ConfigureAwait(false)).ToHashSet(StringComparer.Ordinal);

    private async Task<Result<StudentBillResult>> BillOneAsync(Company company, Student student, FeeStructure structure, string invoiceNumber, DateOnly date, CancellationToken ct)
    {
        Result extras = await BillExtrasAsync(company, student, structure, invoiceNumber, date, ct).ConfigureAwait(false);
        if (extras.IsFailure)
        {
            return extras.Error;
        }

        // A joiner pays from the day they joined when the term is pro rata.
        decimal share = structure.ShareOfTerm(student.AdmittedOn, student.LeftOn);
        if (share == 0m)
        {
            return new StudentBillResult(student.Id, 0m, 0m, 0m, null, null) { ShareOfTerm = 0m };
        }

        List<long> optionIds = await _db.Set<StudentFeeOption>().AsNoTracking().Where(o => o.StudentId == student.Id).Select(o => o.FeeItemId).ToListAsync(ct).ConfigureAwait(false);
        List<(string Account, decimal Amount)> optional = [.. structure.Items.Where(i => !i.IsCompulsory && optionIds.Contains(i.Id))
            .Select(i => (i.RevenueAccountCode ?? structure.RevenueAccountCode, decimal.Round(i.Amount * share, 2, MidpointRounding.AwayFromZero)))];
        decimal optionalTotal = optional.Sum(o => o.Amount);

        // Siblings: enrolled students sharing the guardian, in admission (id) order.
        decimal gross = decimal.Round(structure.CompulsoryTotal * share, 2, MidpointRounding.AwayFromZero);
        List<SiblingDiscount> rules = await _db.Set<SiblingDiscount>().AsNoTracking().Where(r => r.CompanyId == company.Id).ToListAsync(ct).ConfigureAwait(false);
        decimal siblingPercent = 0m;
        if (rules.Count > 0)
        {
            int ordinal = await _db.Set<Student>().CountAsync(s => s.CompanyId == company.Id && s.IsEnrolled && s.GuardianCustomerId == student.GuardianCustomerId && s.Id < student.Id, ct)
                .ConfigureAwait(false) + 1;
            siblingPercent = SiblingDiscount.PercentFor(rules, ordinal);
        }

        // A staff member's child takes the staff discount while the parent is employed - the better of it and the sibling discount.
        decimal staffPercent = 0m;
        if (student.StaffEmployeeId is long parent && company.StaffChildDiscountPercent > 0m
            && await _db.Employees.AnyAsync(e => e.Id == parent && e.Status != Core.Payroll.EmployeeStatus.Terminated, ct).ConfigureAwait(false))
        {
            staffPercent = company.StaffChildDiscountPercent;
        }

        (decimal bursary, decimal familyDiscount) = student.ComputeDiscounts(gross, Math.Max(siblingPercent, staffPercent), date);
        bool staffWins = staffPercent > siblingPercent;
        decimal sibling = staffWins ? 0m : familyDiscount, staffDiscount = staffWins ? familyDiscount : 0m;
        decimal discount = bursary + familyDiscount;
        decimal compulsoryNet = Math.Max(0m, gross - discount);
        decimal net = compulsoryNet + optionalTotal;
        if (net <= 0m)
        {
            return new StudentBillResult(student.Id, gross, discount, 0m, null, null, sibling) { ShareOfTerm = share, StaffDiscount = staffDiscount };
        }

        // Optional items go to their own revenue accounts (deferred fees: all through deferred income).
        List<RevenueLine> revenue = structure.DefersIncome ? []
            : [.. new[] { (Account: structure.RevenueAccountCode, Amount: compulsoryNet) }.Concat(optional).Where(r => r.Amount > 0m)
                .GroupBy(r => r.Account, StringComparer.Ordinal).Select(g => new RevenueLine(g.Key, g.Sum(r => r.Amount)))];
        if (revenue.Count == 1)
        {
            revenue = [];
        }

        Result<TradingPostingResult> posted = await _salesLedger.PostInvoiceAsync(new SalesInvoiceRequest
        {
            CompanyId = company.Id,
            CustomerId = student.PayerCustomerId,
            DocumentNumber = invoiceNumber,
            DocumentDate = date,
            Currency = company.BaseCurrency,
            NetAmount = net,
            TaxAmount = 0m,
            SalesAccountCode = structure.DefersIncome ? structure.DeferredIncomeAccountCode! : structure.RevenueAccountCode,
            RevenueLines = revenue,
            Narrative = $"{structure.Term} fees - {student.FullName} ({student.AdmissionNumber})" + (share < 1m ? FormattableString.Invariant($" [{share:P0} of term]") : string.Empty),
        }, ct).ConfigureAwait(false);

        if (posted.IsFailure)
        {
            return posted.Error;
        }

        if (structure.DefersIncome)
        {
            // Fees paid in advance are earned over the term (IFRS 15): billed to deferred income, recognised by the
            // deferred income run.
            _db.Set<DeferredIncomeSchedule>().Add(new DeferredIncomeSchedule(company.Id, invoiceNumber, $"{structure.Term} fees - {student.FullName}",
                structure.DeferredIncomeAccountCode!, structure.RevenueAccountCode, net, structure.TermStart!.Value, structure.TermEnd!.Value));
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        return new StudentBillResult(student.Id, gross, discount, net, posted.Value.OpenItemId, posted.Value.JournalNumber, sibling) { OptionalFees = optionalTotal, ShareOfTerm = share, StaffDiscount = staffDiscount };
    }

    /// <inheritdoc />
    public async Task<Result> SetTermAsync(long companyId, long feeStructureId, DateOnly termStart, DateOnly termEnd, bool proRata, CancellationToken ct = default)
    {
        if (termEnd < termStart)
        {
            return Result.Failure(Error.Validation("School.TermDates", "The term cannot end before it starts."));
        }

        FeeStructure? structure = await _db.Set<FeeStructure>().FirstOrDefaultAsync(f => f.Id == feeStructureId && f.CompanyId == companyId, ct).ConfigureAwait(false);
        if (structure is null)
        {
            return Result.Failure(Error.NotFound("School.FeeStructureNotFound", $"Fee structure {feeStructureId} not found."));
        }

        if (structure.DefersIncome && (structure.TermStart != termStart || structure.TermEnd != termEnd))
        {
            structure.DeferOverTerm(termStart, termEnd, structure.DeferredIncomeAccountCode!);
        }

        structure.SetTerm(termStart, termEnd, proRata);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result> SetOptionAsync(long companyId, long studentId, long feeItemId, bool taking, CancellationToken ct = default)
    {
        bool optionalItem = await (from item in _db.Set<FeeItem>()
                                   join structure in _db.Set<FeeStructure>() on item.FeeStructureId equals structure.Id
                                   where item.Id == feeItemId && structure.CompanyId == companyId && !item.IsCompulsory
                                   select item.Id).AnyAsync(ct).ConfigureAwait(false);
        if (!optionalItem)
        {
            return Result.Failure(Error.NotFound("School.OptionNotFound", $"Fee item {feeItemId} is not an optional item of this school."));
        }

        if (!await _db.Set<Student>().AnyAsync(s => s.Id == studentId && s.CompanyId == companyId, ct).ConfigureAwait(false))
        {
            return Result.Failure(Error.NotFound("School.StudentNotFound", $"Student {studentId} not found."));
        }

        StudentFeeOption? option = await _db.Set<StudentFeeOption>().FirstOrDefaultAsync(o => o.StudentId == studentId && o.FeeItemId == feeItemId, ct).ConfigureAwait(false);
        if (taking && option is null)
        {
            _db.Set<StudentFeeOption>().Add(new StudentFeeOption(companyId, studentId, feeItemId));
        }
        else if (!taking && option is not null)
        {
            _db.Set<StudentFeeOption>().Remove(option);
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public Task<Result<WithdrawalResult>> WithdrawAsync(long companyId, long studentId, DateOnly leftOn, long feeStructureId, string invoiceNumberPrefix, CancellationToken ct = default)
        => _db.AtomicAsync(async () =>
        {
            Student? student = await _db.Set<Student>().FirstOrDefaultAsync(s => s.Id == studentId && s.CompanyId == companyId, ct).ConfigureAwait(false);
            if (student is null)
            {
                return Error.NotFound("School.StudentNotFound", $"Student {studentId} not found.");
            }

            if (!student.IsEnrolled)
            {
                return Error.Rule("School.NotEnrolled", $"{student.AdmissionNumber} is not enrolled.");
            }

            FeeStructure? structure = await _db.Set<FeeStructure>().AsNoTracking().FirstOrDefaultAsync(f => f.Id == feeStructureId && f.CompanyId == companyId, ct).ConfigureAwait(false);
            if (structure is null)
            {
                return Error.NotFound("School.FeeStructureNotFound", $"Fee structure {feeStructureId} not found.");
            }

            student.Withdraw(leftOn);
            string invoiceNumber = (invoiceNumberPrefix ?? string.Empty).Trim() + student.AdmissionNumber;
            OpenItem? invoice = await _db.OpenItems.AsNoTracking().FirstOrDefaultAsync(i => i.CompanyId == companyId && i.Ledger == TradingLedger.AccountsReceivable && i.DocumentNumber == invoiceNumber, ct)
                .ConfigureAwait(false);
            decimal credit = 0m;
            string? creditNumber = null;
            if (invoice is not null && structure.ProRata && structure.TermStart is not null)
            {
                // The unused share of what was billed for the days the student was enrolled this term.
                decimal billedShare = structure.ShareOfTerm(student.AdmittedOn, null);
                decimal usedShare = structure.ShareOfTerm(student.AdmittedOn, leftOn);
                decimal billed = invoice.OriginalTransaction;
                credit = billedShare <= 0m ? 0m : billed - decimal.Round(billed * usedShare / billedShare, 2, MidpointRounding.AwayFromZero);
            }

            if (credit > 0m)
            {
                Company company = await _db.Set<Company>().FirstAsync(c => c.Id == companyId, ct).ConfigureAwait(false);
                creditNumber = invoiceNumber + "-CR";
                Result<TradingPostingResult> posted = await _salesLedger.PostCreditNoteAsync(new SalesCreditNoteRequest
                {
                    CompanyId = companyId, CustomerId = invoice!.TraderId, DocumentNumber = creditNumber, DocumentDate = leftOn, Currency = company.BaseCurrency, NetAmount = credit,
                    SalesAccountCode = structure.DefersIncome ? structure.DeferredIncomeAccountCode! : structure.RevenueAccountCode,
                    Narrative = $"{structure.Term} fees - {student.FullName} ({student.AdmissionNumber}) [left {leftOn:yyyy-MM-dd}]",
                }, ct).ConfigureAwait(false);
                if (posted.IsFailure)
                {
                    return posted.Error;
                }

                // Set straight against the term invoice, so the family's account shows what is still owed.
                if (_allocations is not null && invoice.OutstandingTransaction > 0m)
                {
                    Result<AllocationResult> allocated = await _allocations.AllocateAsync(companyId, TradingLedger.AccountsReceivable, invoice.Id, posted.Value.OpenItemId,
                        Math.Min(credit, invoice.OutstandingTransaction), leftOn, ct).ConfigureAwait(false);
                    if (allocated.IsFailure)
                    {
                        return allocated.Error;
                    }
                }

                if (structure.DefersIncome
                    && await _db.Set<DeferredIncomeSchedule>().FirstOrDefaultAsync(x => x.CompanyId == companyId && x.Reference == invoiceNumber, ct).ConfigureAwait(false) is DeferredIncomeSchedule schedule)
                {
                    schedule.Curtail(leftOn, credit);
                }
            }

            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            return Result<WithdrawalResult>.Success(new WithdrawalResult(creditNumber, credit));
        }, ct);

    // Transport and boarding are billed on their own invoices (-TR, -HS) to their own revenue accounts, without bursary discounts;
    // the narrative keeps the fee pattern so arrears and collections see them, tagged with the route or hostel.
    private async Task<Result> BillExtrasAsync(Company company, Student student, FeeStructure structure, string invoiceNumber, DateOnly date, CancellationToken ct)
    {
        List<(string Suffix, string Tag, decimal Fee, string Account)> extras = [];
        if (student.TransportRouteId is long routeId && await _db.Set<TransportRoute>().AsNoTracking().FirstOrDefaultAsync(r => r.Id == routeId, ct).ConfigureAwait(false) is { FeePerTerm: > 0m } route)
        {
            extras.Add(("-TR", $"transport {route.Code}", route.FeePerTerm, route.RevenueAccountCode));
        }

        if (student.IsBoarder && student.HostelId is long hostelId && await _db.Set<Hostel>().AsNoTracking().FirstOrDefaultAsync(h => h.Id == hostelId, ct).ConfigureAwait(false) is { FeePerTerm: > 0m } hostel)
        {
            extras.Add(("-HS", $"boarding {hostel.Code}", hostel.FeePerTerm, hostel.RevenueAccountCode));
        }

        foreach ((string suffix, string tag, decimal fee, string account) in extras)
        {
            Result<TradingPostingResult> posted = await _salesLedger.PostInvoiceAsync(new SalesInvoiceRequest
            {
                CompanyId = company.Id, CustomerId = student.PayerCustomerId, DocumentNumber = invoiceNumber + suffix, DocumentDate = date, Currency = company.BaseCurrency, NetAmount = fee, TaxAmount = 0m,
                SalesAccountCode = account, Narrative = $"{structure.Term} fees - {student.FullName} ({student.AdmissionNumber}) [{tag}]",
            }, ct).ConfigureAwait(false);
            if (posted.IsFailure)
            {
                return Result.Failure(posted.Error);
            }
        }

        return Result.Success();
    }
}
