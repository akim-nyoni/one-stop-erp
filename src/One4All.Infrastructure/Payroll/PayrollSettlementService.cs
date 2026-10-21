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
    public Task<Result<PayrollPayment>> PayAsync(long companyId, string documentNumber, DateOnly paidOn, IReadOnlyDictionary<PayMethod, string> fundingAccounts,
        IReadOnlyDictionary<CurrencyCode, string>? currencyAccounts = null, CancellationToken ct = default)
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

            // Each employee's net pay: the earnings paid in another currency go out in it, then any split paid in another currency; the rest by how they are paid.
            var slips = await (from p in _db.Set<Payslip>().Include(p => p.Lines)
                               join e in _db.Set<Employee>() on p.EmployeeId equals e.Id
                               where p.CompanyId == companyId && p.RunReference == run.DocumentNumber
                               select new { Slip = p, e.PaymentMethod }).ToListAsync(ct).ConfigureAwait(false);
            Dictionary<string, CurrencyCode> paidIn = await _db.PayElements.Where(e => e.CompanyId == companyId && e.PaidInCurrency != null)
                .ToDictionaryAsync(e => e.Code, e => e.PaidInCurrency!.Value, StringComparer.OrdinalIgnoreCase, ct).ConfigureAwait(false);
            CurrencyCode currency = await _db.Companies.Where(c => c.Id == companyId).Select(c => c.BaseCurrency).FirstAsync(ct).ConfigureAwait(false);
            List<long> employeeIds = [.. slips.Select(s => s.Slip.EmployeeId).Distinct()];
            ILookup<long, EmployeePaymentSplit> splits = (await _db.PaymentSplits.AsNoTracking().Where(s => s.CompanyId == companyId && employeeIds.Contains(s.EmployeeId)).OrderBy(s => s.Id)
                .ToListAsync(ct).ConfigureAwait(false)).ToLookup(s => s.EmployeeId);

            Dictionary<CurrencyCode, decimal> foreignBase = [];
            List<(PayMethod Method, decimal Net)> baseNet = [];
            foreach (var s in slips.Where(s => s.Slip.NetPay > 0m))
            {
                decimal inForeign = 0m;
                foreach (IGrouping<CurrencyCode, PayslipLine> lines in s.Slip.Lines.Where(l => l.Type == PayElementType.Earning && paidIn.TryGetValue(l.ElementCode, out CurrencyCode c) && c != currency)
                    .GroupBy(l => paidIn[l.ElementCode]))
                {
                    // Never more than the employee's net: deductions come off the base-currency part first, then the foreign part.
                    decimal portion = Math.Min(lines.Sum(l => l.Amount), s.Slip.NetPay - inForeign);
                    foreignBase[lines.Key] = foreignBase.GetValueOrDefault(lines.Key) + portion;
                    inForeign += portion;
                }

                // Then the employee's splits paid in another currency move their share of what is left into it (the splits share it out in order,
                // as the bank file does, so a base split ahead of them keeps its place).
                foreach ((EmployeePaymentSplit split, decimal amount) in EmployeePaymentSplit.Allocate(splits[s.Slip.EmployeeId], s.Slip.NetPay, s.Slip.NetPay - inForeign)
                    .Where(x => x.Amount > 0m && x.Split.Currency is { } c && c != currency).ToList())
                {
                    CurrencyCode to = split.Currency!.Value;
                    foreignBase[to] = foreignBase.GetValueOrDefault(to) + amount;
                    inForeign += amount;
                }

                baseNet.Add((s.PaymentMethod, s.Slip.NetPay - inForeign));
            }

            // What is owed in each currency is fixed at the pay date's rate - what was taxed and shown on the payslip; paid later at the payment
            // date's rate it costs a different base amount, and the difference is a realised exchange gain or loss.
            List<(CurrencyCode Currency, string Account, decimal Amount, decimal BaseAmount, decimal Rate)> byCurrency = [];
            decimal exchange = 0m;
            foreach ((CurrencyCode foreign, decimal baseAmount) in foreignBase.Where(f => f.Value > 0m).OrderBy(f => f.Key.Value, StringComparer.Ordinal))
            {
                if (currencyAccounts is null || !currencyAccounts.TryGetValue(foreign, out string? account) || string.IsNullOrWhiteSpace(account))
                {
                    return Error.Validation("Payroll.CurrencyAccount", $"Say which {foreign} account pays the net pay in {foreign} ({baseAmount:N2} in base).");
                }

                decimal? payDateRate = await Accounting.ExchangeRateResolver.FindAsync(_db, foreign, currency, ExchangeRateType.Daily, run.PayDate, ct).ConfigureAwait(false);
                decimal? rate = await Accounting.ExchangeRateResolver.FindAsync(_db, foreign, currency, ExchangeRateType.Daily, paidOn, ct).ConfigureAwait(false);
                if (payDateRate is not > 0m || rate is not > 0m)
                {
                    return Error.Rule("Payroll.NoRate", $"There is no {foreign} rate on {(payDateRate is not > 0m ? run.PayDate : paidOn):yyyy-MM-dd} to pay the {foreign} net pay at.");
                }

                decimal owed = decimal.Round(baseAmount / payDateRate.Value, 2, MidpointRounding.AwayFromZero);
                decimal paidBase = decimal.Round(owed * rate.Value, 2, MidpointRounding.AwayFromZero);
                exchange += baseAmount - paidBase;
                byCurrency.Add((foreign, account.Trim().ToUpperInvariant(), owed, paidBase, rate.Value));
            }

            // A gain (the currency weakened) is credited to FX gain, a loss debited to FX loss.
            Result<string?> fx = await PayrollCurrencies.ExchangeAccountAsync(_db, companyId, exchange, "Net pay", ct).ConfigureAwait(false);
            if (fx.IsFailure)
            {
                return fx.Error;
            }

            string? exchangeAccount = fx.Value;

            List<(PayMethod Method, string Account, decimal Amount)> byMethod = [];
            foreach (IGrouping<PayMethod, decimal> method in baseNet.Where(s => s.Net > 0m).GroupBy(s => s.Method, s => s.Net).OrderBy(g => g.Key))
            {
                if (!fundingAccounts.TryGetValue(method.Key, out string? account) || string.IsNullOrWhiteSpace(account))
                {
                    return Error.Validation("Payroll.FundingAccount", $"Say which account pays the {method.Key} net pay ({method.Sum():N2}).");
                }

                byMethod.Add((method.Key, account.Trim().ToUpperInvariant(), method.Sum()));
            }

            decimal total = byMethod.Sum(m => m.Amount) + byCurrency.Sum(c => c.BaseAmount) + exchange;
            if (total <= 0m)
            {
                return Error.Rule("Payroll.NothingToPay", $"Run {run.DocumentNumber} has no net pay to pay.");
            }

            Result<PostingReceipt> posted = await _posting.PostAsync(new JournalRequest
            {
                CompanyId = companyId, SourceModule = "GL", PostingDate = paidOn, TransactionCurrency = currency, DocumentType = "PAYROLLPAY", DocumentNumber = $"{run.DocumentNumber}-PAY",
                Narrative = $"Net pay for payroll {run.PeriodLabel} ({run.DocumentNumber})",
                Lines =
                [
                    new JournalLineRequest { AccountCode = run.NetPayAccountCode, Side = JournalSide.Debit, TransactionAmount = total },
                    .. byMethod.GroupBy(m => m.Account).Select(g => new JournalLineRequest { AccountCode = g.Key, Side = JournalSide.Credit, TransactionAmount = g.Sum(m => m.Amount) }),
                    .. byCurrency.Select(c => new JournalLineRequest
                    {
                        AccountCode = c.Account, Side = JournalSide.Credit, TransactionAmount = c.Amount, Currency = c.Currency, ExchangeRate = c.Rate, BaseAmount = c.BaseAmount,
                        Narrative = $"Net pay in {c.Currency}",
                    }),
                    .. (exchangeAccount is null ? Array.Empty<JournalLineRequest>() :
                    [
                        new JournalLineRequest
                        {
                            AccountCode = exchangeAccount, Side = exchange > 0m ? JournalSide.Credit : JournalSide.Debit, TransactionAmount = Math.Abs(exchange),
                            Narrative = "Exchange difference on net pay paid in another currency",
                        },
                    ]),
                ],
            }, ct).ConfigureAwait(false);
            if (posted.IsFailure)
            {
                return posted.Error;
            }

            run.MarkPaid(paidOn, posted.Value.JournalNumber);
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            return Result<PayrollPayment>.Success(new PayrollPayment(run.DocumentNumber, posted.Value.JournalNumber, [.. byMethod])
            {
                ByCurrency = [.. byCurrency.Select(c => (c.Currency, c.Account, c.Amount, c.BaseAmount))],
            });
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
