using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Accounting;
using One4All.Application.Abstractions.Trading;
using One4All.Application.Trading;
using One4All.Core.Accounting;
using One4All.Core.Common;
using One4All.Core.System;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Trading;

/// <summary>Assesses and books the expected credit loss allowance (see <see cref="IExpectedCreditLossService"/>).</summary>
public sealed class ExpectedCreditLossService : IExpectedCreditLossService
{
    private readonly One4AllDbContext _db;
    private readonly IPostingEngine _posting;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="posting">The posting engine.</param>
    public ExpectedCreditLossService(One4AllDbContext db, IPostingEngine posting)
    {
        _db = db;
        _posting = posting;
    }

    /// <inheritdoc />
    public async Task<Result<EclReport>> AssessAsync(long companyId, DateOnly asAtDate, EclMatrix matrix, string allowanceAccountCode, CancellationToken ct = default)
    {
        if (matrix.Bands.Count == 0 || matrix.Bands.All(b => b.FromDaysPastDue != 0))
        {
            return Error.Validation("Ecl.Matrix", "The matrix needs a band starting at 0 days past due.");
        }

        if (string.IsNullOrWhiteSpace(allowanceAccountCode))
        {
            return Error.Validation("Ecl.Account", "The allowance account is required.");
        }

        // Receivables as they stood at the date; each customer's credits go against their oldest debts first.
        List<OpenItemAsAt> items = await OpenItemsAsAt.LoadAsync(_db, companyId, TradingLedger.AccountsReceivable, asAtDate, ct).ConfigureAwait(false);
        List<EclExposure> exposures = [];
        foreach (IGrouping<long, OpenItemAsAt> customer in items.GroupBy(i => i.Item.TraderId))
        {
            decimal credit = customer.Where(i => i.Item.Sign == LedgerSign.Credit).Sum(i => i.OutstandingBase);
            foreach (OpenItemAsAt debt in customer.Where(i => i.Item.Sign == LedgerSign.Debit).OrderBy(i => i.Item.DueDate).ThenBy(i => i.Item.Id))
            {
                decimal covered = Math.Min(credit, debt.OutstandingBase);
                credit -= covered;
                decimal left = debt.OutstandingBase - covered;
                if (left > 0m)
                {
                    exposures.Add(new EclExposure(left, asAtDate.DayNumber - debt.Item.DueDate.DayNumber));
                }
            }
        }

        IReadOnlyList<EclBandResult> bands = ExpectedCreditLoss.Compute(exposures, matrix);
        decimal booked = await BookedAsync(companyId, allowanceAccountCode.Trim(), asAtDate, ct).ConfigureAwait(false);
        return new EclReport(asAtDate, bands, booked);
    }

    /// <inheritdoc />
    public Task<Result<EclPostingResult>> PostAsync(long companyId, DateOnly asAtDate, EclMatrix matrix, string allowanceAccountCode, string expenseAccountCode, CancellationToken ct = default)
        => _db.AtomicAsync(() => PostCoreAsync(companyId, asAtDate, matrix, allowanceAccountCode, expenseAccountCode, ct), ct);

    // One unit of work: the assessment and its adjusting journal.
    private async Task<Result<EclPostingResult>> PostCoreAsync(long companyId, DateOnly asAtDate, EclMatrix matrix, string allowanceAccountCode, string expenseAccountCode, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(expenseAccountCode))
        {
            return Error.Validation("Ecl.ExpenseAccount", "The bad-debt expense account is required.");
        }

        Result<EclReport> assessed = await AssessAsync(companyId, asAtDate, matrix, allowanceAccountCode, ct).ConfigureAwait(false);
        if (assessed.IsFailure)
        {
            return assessed.Error;
        }

        EclReport report = assessed.Value;
        if (report.Adjustment == 0m)
        {
            return new EclPostingResult(report, null);
        }

        bool charge = report.Adjustment > 0m;
        decimal amount = Math.Abs(report.Adjustment);
        Company company = await _db.Companies.FirstAsync(c => c.Id == companyId, ct).ConfigureAwait(false);
        Result<PostingReceipt> posted = await _posting.PostAsync(new JournalRequest
        {
            CompanyId = companyId,
            SourceModule = "AR",
            PostingDate = asAtDate,
            TransactionCurrency = company.BaseCurrency,
            EnforceManualJournalRules = false,
            DocumentType = "ExpectedCreditLoss",
            DocumentNumber = $"ECL-{asAtDate:yyyyMMdd}",
            Narrative = $"Expected credit loss allowance {(charge ? "raised" : "released")} to {report.TargetAllowance:N2}",
            Lines =
            [
                new JournalLineRequest { AccountCode = expenseAccountCode.Trim(), Side = charge ? JournalSide.Debit : JournalSide.Credit, TransactionAmount = amount },
                new JournalLineRequest { AccountCode = allowanceAccountCode.Trim(), Side = charge ? JournalSide.Credit : JournalSide.Debit, TransactionAmount = amount },
            ],
        }, ct).ConfigureAwait(false);

        if (posted.IsFailure)
        {
            return posted.Error;
        }

        return new EclPostingResult(report, posted.Value.JournalNumber);
    }

    // The allowance already on the contra-receivables account at the date (credit balance).
    private async Task<decimal> BookedAsync(long companyId, string allowanceAccountCode, DateOnly asAtDate, CancellationToken ct)
    {
        var totals = await (
            from l in _db.Set<JournalLine>()
            join j in _db.Journals on l.JournalEntryId equals j.Id
            where j.CompanyId == companyId && j.Status == JournalStatus.Posted && j.PostingDate <= asAtDate && l.AccountCode == allowanceAccountCode
            select new { l.BaseDebit, l.BaseCredit })
            .ToListAsync(ct).ConfigureAwait(false);
        return totals.Sum(t => t.BaseCredit) - totals.Sum(t => t.BaseDebit);
    }
}
