using System.Globalization;
using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Payroll;
using One4All.Core.Common;
using One4All.Core.Payroll;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Payroll;

/// <content>Maker-checker payroll: calculate, approve (once or twice), commit.</content>
public sealed partial class PayrollService : IPayrollApprovalService
{
    // Movement in net pay (either way, in percent) above which an employee is listed on the exception report when the company sets no
    // second-approval threshold.
    private const decimal DefaultExceptionMovementPercent = 25m;

    /// <inheritdoc />
    public Task<Result<PayrollDraftResult>> CalculateRunAsync(PayrollDraftRequest request, CancellationToken ct = default)
        => _db.AtomicAsync(() => CalculateRunCoreAsync(request, ct), ct);

    /// <inheritdoc />
    public async Task<Result<PayrollDraftResult>> ApproveRunAsync(long companyId, long draftId, long approverUserId, CancellationToken ct = default)
    {
        PayrollDraft? draft = await _db.PayrollDrafts.FirstOrDefaultAsync(d => d.Id == draftId && d.CompanyId == companyId, ct).ConfigureAwait(false);
        if (draft is null)
        {
            return Error.NotFound("Payroll.DraftNotFound", $"Payroll draft {draftId} not found.");
        }

        if (draft.Status != PayrollDraftStatus.Calculated)
        {
            return Error.Rule("Payroll.DraftNotPending", $"The draft is {draft.Status.ToString().ToLowerInvariant()}, not awaiting approval.");
        }

        if (!await _db.Users.AnyAsync(u => u.Id == approverUserId, ct).ConfigureAwait(false))
        {
            return Error.Validation("Payroll.ApproverNotFound", "The payroll must be approved by a user.");
        }

        if (approverUserId == draft.CalculatedByUserId || approverUserId == draft.FirstApproverUserId)
        {
            return Error.Rule("Payroll.SameApprover", approverUserId == draft.CalculatedByUserId
                ? "The payroll must be approved by someone other than whoever calculated it."
                : "This payroll moved enough from the last run to need a second, different approver.");
        }

        draft.Approve(approverUserId, DateTimeOffset.UtcNow);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Describe(draft);
    }

    /// <inheritdoc />
    public Task<Result<PayrollRunResult>> CommitRunAsync(long companyId, long draftId, long userId, CancellationToken ct = default)
        => _db.AtomicAsync(() => CommitRunCoreAsync(companyId, draftId, userId, ct), ct);

    private async Task<Result<PayrollDraftResult>> CalculateRunCoreAsync(PayrollDraftRequest request, CancellationToken ct)
    {
        string payGroup = PayGroupKey(request.PayGroup);
        string document = (request.DocumentNumber ?? string.Empty).Trim();
        if (document.Length == 0)
        {
            return Error.Validation("Payroll.DocumentRequired", "The run needs a document number.");
        }

        Result<PreparedRun> prepared = await PrepareRunAsync(request.CompanyId, payGroup, request.PeriodLabel, request.PayDate, document, ct).ConfigureAwait(false);
        if (prepared.IsFailure)
        {
            return prepared.Error;
        }

        PreparedRun run = prepared.Value;
        decimal? previousNet = await _db.Set<PayrollRunRecord>()
            .Where(r => r.CompanyId == request.CompanyId && r.PayGroup == payGroup && r.Kind == PayRunKind.Regular && r.ReversedOn == null && r.PayDate < request.PayDate)
            .OrderByDescending(r => r.PayDate).Select(r => (decimal?)r.Net).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        decimal net = run.Slips.Sum(s => s.NetPay);
        decimal? threshold = run.Company.PayrollSecondApprovalVariancePercent;
        bool second = threshold is decimal t && Movement(previousNet, net) is decimal m && Math.Abs(m) > t;

        foreach (PayrollDraft earlier in await _db.PayrollDrafts.Where(d => d.CompanyId == request.CompanyId && d.PayGroup == payGroup && d.PeriodLabel == run.Period
                     && (d.Status == PayrollDraftStatus.Calculated || d.Status == PayrollDraftStatus.Approved)).ToListAsync(ct).ConfigureAwait(false))
        {
            earlier.Supersede();
        }

        List<string> exceptions = await ExceptionsAsync(run, payGroup, request.PayDate, threshold ?? DefaultExceptionMovementPercent, ct).ConfigureAwait(false);
        PayrollDraft draft = new(request.CompanyId, payGroup, run.Period, request.PayDate, request.NetPayAccountCode, document, run.Slips.Count, run.Slips.Sum(s => s.GrossEarnings),
            run.Slips.Sum(s => s.TotalDeductions), net, previousNet, second, string.Join('\n', exceptions), request.UserId, DateTimeOffset.UtcNow);
        _db.PayrollDrafts.Add(draft);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Describe(draft);
    }

    private async Task<Result<PayrollRunResult>> CommitRunCoreAsync(long companyId, long draftId, long userId, CancellationToken ct)
    {
        PayrollDraft? draft = await _db.PayrollDrafts.FirstOrDefaultAsync(d => d.Id == draftId && d.CompanyId == companyId, ct).ConfigureAwait(false);
        if (draft is null)
        {
            return Error.NotFound("Payroll.DraftNotFound", $"Payroll draft {draftId} not found.");
        }

        if (draft.Status != PayrollDraftStatus.Approved)
        {
            return Error.Rule("Payroll.NotApproved", draft.Status == PayrollDraftStatus.Calculated ? "The payroll has not been approved yet." : $"The draft is {draft.Status.ToString().ToLowerInvariant()}.");
        }

        // What is committed must be what was approved: recalculate and compare.
        Result<PreparedRun> prepared = await PrepareRunAsync(companyId, draft.PayGroup, draft.PeriodLabel, draft.PayDate, draft.DocumentNumber, ct).ConfigureAwait(false);
        if (prepared.IsFailure)
        {
            return prepared.Error;
        }

        List<Payslip> slips = prepared.Value.Slips;
        if (slips.Count != draft.Employees || slips.Sum(s => s.GrossEarnings) != draft.Gross || slips.Sum(s => s.TotalDeductions) != draft.Deductions || slips.Sum(s => s.NetPay) != draft.Net)
        {
            return Error.Conflict("Payroll.ChangedSinceApproval", "Pay data changed after the payroll was approved; recalculate and approve it again.");
        }

        Result<PayrollRunResult> run = await CommitPreparedAsync(prepared.Value, draft.PayGroup, draft.PayDate, draft.NetPayAccountCode, draft.DocumentNumber, ct).ConfigureAwait(false);
        if (run.IsFailure)
        {
            return run.Error;
        }

        draft.Commit(userId, DateTimeOffset.UtcNow, run.Value.JournalNumber);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return run;
    }

    // The exception report: negative or zero net pay, a large movement from the employee's last regular payslip in the group, and
    // deductions suspended by net-pay protection.
    private async Task<List<string>> ExceptionsAsync(PreparedRun run, string payGroup, DateOnly payDate, decimal movementPercent, CancellationToken ct)
    {
        List<long> ids = [.. run.Slips.Select(s => s.EmployeeId)];
        Dictionary<long, string> numbers = await _db.Set<Employee>().Where(e => ids.Contains(e.Id)).ToDictionaryAsync(e => e.Id, e => e.EmployeeNumber, ct).ConfigureAwait(false);
        var previous = (await _db.Set<Payslip>().Where(p => p.CompanyId == run.Company.Id && ids.Contains(p.EmployeeId) && p.PayGroup == payGroup && p.RunKind == PayRunKind.Regular && p.PayDate < payDate)
                .Select(p => new { p.EmployeeId, p.PayDate, p.NetPay }).ToListAsync(ct).ConfigureAwait(false))
            .GroupBy(p => p.EmployeeId).ToDictionary(g => g.Key, g => g.OrderByDescending(p => p.PayDate).First().NetPay);
        static string N(decimal amount) => amount.ToString("N2", CultureInfo.InvariantCulture);

        List<string> issues = [];
        foreach (Payslip slip in run.Slips.OrderBy(s => numbers.GetValueOrDefault(s.EmployeeId), StringComparer.Ordinal))
        {
            string who = numbers.GetValueOrDefault(slip.EmployeeId, $"#{slip.EmployeeId}");
            if (slip.NetPay < 0m)
            {
                issues.Add($"{who}: negative net pay {N(slip.NetPay)}");
            }
            else if (slip.NetPay == 0m)
            {
                issues.Add($"{who}: zero net pay");
            }

            if (previous.TryGetValue(slip.EmployeeId, out decimal before) && Movement(before, slip.NetPay) is decimal m && Math.Abs(m) > movementPercent)
            {
                issues.Add($"{who}: net pay {N(slip.NetPay)} moved {m.ToString("0.0", CultureInfo.InvariantCulture)}% from {N(before)}");
            }
        }

        foreach (DeductionArrear suspended in run.Arrears.Where(a => a.Attempted > a.Deducted).OrderBy(a => numbers.GetValueOrDefault(a.EmployeeId), StringComparer.Ordinal))
        {
            issues.Add($"{numbers.GetValueOrDefault(suspended.EmployeeId)}: {suspended.ElementCode} suspended {N(suspended.Attempted - suspended.Deducted)} by net-pay protection");
        }

        return issues;
    }

    private static decimal? Movement(decimal? before, decimal now)
        => before is decimal b && b != 0m ? decimal.Round((now - b) / Math.Abs(b) * 100m, 1, MidpointRounding.ToEven) : null;

    private static PayrollDraftResult Describe(PayrollDraft draft)
        => new(draft.Id, draft.Status, draft.Employees, draft.Gross, draft.Deductions, draft.Net, draft.PreviousNet, Movement(draft.PreviousNet, draft.Net), draft.RequiresSecondApproval,
            draft.Exceptions.Length == 0 ? [] : draft.Exceptions.Split('\n'));
}
