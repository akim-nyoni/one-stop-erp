using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Accounting;
using One4All.Application.Abstractions.Payroll;
using One4All.Core.Accounting;
using One4All.Core.Common;
using One4All.Core.Payroll;
using One4All.Core.System;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Payroll;

/// <summary>The Pay and Close stages of a committed payroll run.</summary>
public sealed class PayrollSettlementService : IPayrollSettlementService
{
    private readonly One4AllDbContext _db;
    private readonly IPostingEngine _posting;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="posting">The posting engine.</param>
    public PayrollSettlementService(One4AllDbContext db, IPostingEngine posting)
    {
        _db = db;
        _posting = posting;
    }

    /// <inheritdoc />
    public Task<Result<PayrollPayment>> PayAsync(long companyId, string documentNumber, DateOnly paidOn, IReadOnlyDictionary<PayMethod, string> fundingAccounts, CancellationToken ct = default)
        => _db.AtomicAsync(async () =>
        {
            ArgumentNullException.ThrowIfNull(fundingAccounts);
            Result<PayrollRunRecord> found = await RunAsync(companyId, documentNumber, ct).ConfigureAwait(false);
            if (found.IsFailure)
            {
                return found.Error;
            }

            PayrollRunRecord run = found.Value;
            if (run.IsReversed || run.Kind == PayRunKind.Reversal)
            {
                return Error.Rule("Payroll.RunReversed", $"Run {run.DocumentNumber} has been reversed; there is nothing to pay.");
            }

            if (run.PaidOn is DateOnly paid)
            {
                return Error.Conflict("Payroll.RunPaid", $"Run {run.DocumentNumber} was paid on {paid:yyyy-MM-dd}.");
            }

            if (string.IsNullOrWhiteSpace(run.NetPayAccountCode))
            {
                return Error.Rule("Payroll.NoNetPayAccount", $"Run {run.DocumentNumber} does not record its net pay control account; pay it by journal.");
            }

            // Each employee's net pay by how they are paid.
            var slips = await (from p in _db.Set<Payslip>()
                               join e in _db.Set<Employee>() on p.EmployeeId equals e.Id
                               where p.CompanyId == companyId && p.RunReference == run.DocumentNumber
                               select new { p.NetPay, e.PaymentMethod }).ToListAsync(ct).ConfigureAwait(false);
            List<(PayMethod Method, string Account, decimal Amount)> byMethod = [];
            foreach (IGrouping<PayMethod, decimal> method in slips.Where(s => s.NetPay > 0m).GroupBy(s => s.PaymentMethod, s => s.NetPay).OrderBy(g => g.Key))
            {
                if (!fundingAccounts.TryGetValue(method.Key, out string? account) || string.IsNullOrWhiteSpace(account))
                {
                    return Error.Validation("Payroll.FundingAccount", $"Say which account pays the {method.Key} net pay ({method.Sum():N2}).");
                }

                byMethod.Add((method.Key, account.Trim().ToUpperInvariant(), method.Sum()));
            }

            decimal total = byMethod.Sum(m => m.Amount);
            if (total <= 0m)
            {
                return Error.Rule("Payroll.NothingToPay", $"Run {run.DocumentNumber} has no net pay to pay.");
            }

            CurrencyCode currency = await _db.Companies.Where(c => c.Id == companyId).Select(c => c.BaseCurrency).FirstAsync(ct).ConfigureAwait(false);
            Result<PostingReceipt> posted = await _posting.PostAsync(new JournalRequest
            {
                CompanyId = companyId, SourceModule = "GL", PostingDate = paidOn, TransactionCurrency = currency, DocumentType = "PAYROLLPAY", DocumentNumber = $"{run.DocumentNumber}-PAY",
                Narrative = $"Net pay for payroll {run.PeriodLabel} ({run.DocumentNumber})",
                Lines =
                [
                    new JournalLineRequest { AccountCode = run.NetPayAccountCode, Side = JournalSide.Debit, TransactionAmount = total },
                    .. byMethod.GroupBy(m => m.Account).Select(g => new JournalLineRequest { AccountCode = g.Key, Side = JournalSide.Credit, TransactionAmount = g.Sum(m => m.Amount) }),
                ],
            }, ct).ConfigureAwait(false);
            if (posted.IsFailure)
            {
                return posted.Error;
            }

            run.MarkPaid(paidOn, posted.Value.JournalNumber);
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            return Result<PayrollPayment>.Success(new PayrollPayment(run.DocumentNumber, posted.Value.JournalNumber, [.. byMethod]));
        }, ct);

    /// <inheritdoc />
    public async Task<Result<PayrollRunStage>> CloseAsync(long companyId, string documentNumber, DateOnly closedOn, CancellationToken ct = default)
    {
        Result<PayrollRunRecord> found = await RunAsync(companyId, documentNumber, ct).ConfigureAwait(false);
        if (found.IsFailure)
        {
            return found.Error;
        }

        PayrollRunRecord run = found.Value;
        if (run.ClosedOn is DateOnly closed)
        {
            return Error.Conflict("Payroll.RunClosed", $"Run {run.DocumentNumber} was closed on {closed:yyyy-MM-dd}.");
        }

        if (run.PaidOn is null)
        {
            return Error.Rule("Payroll.RunNotPaid", $"Run {run.DocumentNumber} has not been paid; pay it before closing.");
        }

        decimal outstanding = await OutstandingAsync(companyId, run, ct).ConfigureAwait(false);
        if (outstanding != 0m)
        {
            return Error.Rule("Payroll.NetPayNotCleared", $"{outstanding:N2} of run {run.DocumentNumber}'s net pay is still on account {run.NetPayAccountCode}; clear it before closing.");
        }

        run.Close(closedOn);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Stage(run, outstanding);
    }

    /// <inheritdoc />
    public async Task<Result<PayrollRunStage>> StageAsync(long companyId, string documentNumber, CancellationToken ct = default)
    {
        Result<PayrollRunRecord> found = await RunAsync(companyId, documentNumber, ct).ConfigureAwait(false);
        return found.IsFailure ? found.Error : Stage(found.Value, await OutstandingAsync(companyId, found.Value, ct).ConfigureAwait(false));
    }

    // What the run's own journals leave on the net pay control: the credit from the run less the payment debit.
    private async Task<decimal> OutstandingAsync(long companyId, PayrollRunRecord run, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(run.NetPayAccountCode))
        {
            return 0m;
        }

        string account = run.NetPayAccountCode;
        List<string> journals = [run.JournalNumber, .. run.PaymentJournalNumber is string pay ? [pay] : Array.Empty<string>()];
        var lines = await (from l in _db.Set<JournalLine>()
                           join j in _db.Journals on l.JournalEntryId equals j.Id
                           where j.CompanyId == companyId && journals.Contains(j.JournalNumber) && l.AccountCode == account
                           select new { l.BaseCredit, l.BaseDebit }).ToListAsync(ct).ConfigureAwait(false);
        return lines.Sum(l => l.BaseCredit - l.BaseDebit);
    }

    private static PayrollRunStage Stage(PayrollRunRecord run, decimal outstanding) => new(run.DocumentNumber, run.Net, run.PaidOn, run.ClosedOn, outstanding);

    private async Task<Result<PayrollRunRecord>> RunAsync(long companyId, string documentNumber, CancellationToken ct)
    {
        string key = (documentNumber ?? string.Empty).Trim();
        PayrollRunRecord? run = await _db.Set<PayrollRunRecord>().FirstOrDefaultAsync(r => r.CompanyId == companyId && r.DocumentNumber == key, ct).ConfigureAwait(false);
        return run is null ? Error.NotFound("Payroll.RunNotFound", $"Payroll run {key} not found.") : run;
    }
}
