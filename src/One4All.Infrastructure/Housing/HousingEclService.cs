using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Accounting;
using One4All.Application.Abstractions.Housing;
using One4All.Application.Trading;
using One4All.Core.Accounting;
using One4All.Core.Common;
using One4All.Core.Housing;
using One4All.Core.System;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using One4All.Infrastructure.Trading;

namespace One4All.Infrastructure.Housing;

/// <summary>Expected credit loss for housing debtors, by scheme and for council rentals, on a separate allowance.</summary>
public sealed class HousingEclService : IHousingEclService
{
    private const string RentalPortfolio = "Council rentals";
    private const string NoSchemePortfolio = "No scheme";

    private readonly One4AllDbContext _db;
    private readonly IPostingEngine _engine;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="engine">The posting engine.</param>
    public HousingEclService(One4AllDbContext db, IPostingEngine engine)
    {
        _db = db;
        _engine = engine;
    }

    /// <inheritdoc />
    public async Task<Result<HousingEclReport>> AssessAsync(HousingEclRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        foreach (EclMatrix matrix in new[] { request.InstalmentMatrix, request.RentalMatrix })
        {
            if (matrix.Bands.Count == 0 || matrix.Bands.All(b => b.FromDaysPastDue != 0))
            {
                return Error.Validation("Hse.EclMatrix", "Each matrix needs a band starting at 0 days past due.");
            }
        }

        if (string.IsNullOrWhiteSpace(request.AllowanceAccountCode))
        {
            return Error.Validation("Hse.EclAccount", "The housing allowance account is required.");
        }

        // Which portfolio each housing debtor belongs to: the scheme of the stand bought on instalments, else rentals.
        var buyers = await (
            from a in _db.Set<InstalmentAgreement>().AsNoTracking()
            join s in _db.HousingStands.AsNoTracking() on a.StandId equals s.Id
            join sc in _db.HousingSchemes.AsNoTracking() on s.SchemeId equals (long?)sc.Id into schemes
            from scheme in schemes.DefaultIfEmpty()
            where a.CompanyId == request.CompanyId && a.SignedOn <= request.AsAtDate
            select new { a.BuyerCustomerId, Scheme = scheme == null ? null : scheme.Code })
            .ToListAsync(ct).ConfigureAwait(false);
        List<long> tenants = await _db.Set<Tenancy>().AsNoTracking()
            .Where(t => t.CompanyId == request.CompanyId && t.StartDate <= request.AsAtDate)
            .Select(t => t.TenantCustomerId).Distinct().ToListAsync(ct).ConfigureAwait(false);
        Dictionary<long, string> portfolioOf = [];
        foreach (long tenant in tenants)
        {
            portfolioOf[tenant] = RentalPortfolio;
        }

        foreach (var buyer in buyers)
        {
            portfolioOf[buyer.BuyerCustomerId] = buyer.Scheme ?? NoSchemePortfolio;
        }

        // Receivables as they stood at the date; each debtor's credits go against their oldest debts first.
        List<OpenItemAsAt> items = await OpenItemsAsAt.LoadAsync(_db, request.CompanyId, TradingLedger.AccountsReceivable, request.AsAtDate, ct).ConfigureAwait(false);
        Dictionary<string, List<EclExposure>> exposures = [];
        foreach (IGrouping<long, OpenItemAsAt> debtor in items.Where(i => portfolioOf.ContainsKey(i.Item.TraderId)).GroupBy(i => i.Item.TraderId))
        {
            string portfolio = portfolioOf[debtor.Key];
            if (!exposures.TryGetValue(portfolio, out List<EclExposure>? list))
            {
                list = [];
                exposures[portfolio] = list;
            }

            decimal credit = debtor.Where(i => i.Item.Sign == LedgerSign.Credit).Sum(i => i.OutstandingBase);
            foreach (OpenItemAsAt debt in debtor.Where(i => i.Item.Sign == LedgerSign.Debit).OrderBy(i => i.Item.DueDate).ThenBy(i => i.Item.Id))
            {
                decimal covered = Math.Min(credit, debt.OutstandingBase);
                credit -= covered;
                decimal left = debt.OutstandingBase - covered;
                if (left > 0m)
                {
                    list.Add(new EclExposure(left, request.AsAtDate.DayNumber - debt.Item.DueDate.DayNumber));
                }
            }
        }

        List<HousingEclPortfolio> portfolios = [.. exposures.OrderBy(e => e.Key == RentalPortfolio).ThenBy(e => e.Key, StringComparer.Ordinal)
            .Select(e => new HousingEclPortfolio(e.Key, ExpectedCreditLoss.Compute(e.Value, e.Key == RentalPortfolio ? request.RentalMatrix : request.InstalmentMatrix)))];
        decimal booked = await BookedAsync(request.CompanyId, request.AllowanceAccountCode.Trim(), request.AsAtDate, ct).ConfigureAwait(false);
        return new HousingEclReport(request.AsAtDate, portfolios, booked, null);
    }

    /// <inheritdoc />
    public Task<Result<HousingEclReport>> PostAsync(HousingEclRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return _db.AtomicAsync(() => PostCoreAsync(request, ct), ct);
    }

    private async Task<Result<HousingEclReport>> PostCoreAsync(HousingEclRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.ExpenseAccountCode))
        {
            return Error.Validation("Hse.EclExpenseAccount", "The impairment expense account is required.");
        }

        Result<HousingEclReport> assessed = await AssessAsync(request, ct).ConfigureAwait(false);
        if (assessed.IsFailure || assessed.Value.Adjustment == 0m)
        {
            return assessed;
        }

        HousingEclReport report = assessed.Value;
        bool raise = report.Adjustment > 0m;
        decimal amount = Math.Abs(report.Adjustment);
        CurrencyCode currency = await _db.Set<Company>().Where(c => c.Id == request.CompanyId).Select(c => c.BaseCurrency).FirstAsync(ct).ConfigureAwait(false);
        Result<PostingReceipt> posted = await _engine.PostAsync(new JournalRequest
        {
            CompanyId = request.CompanyId, SourceModule = "AR", PostingDate = request.AsAtDate, TransactionCurrency = currency, DocumentType = "HousingExpectedCreditLoss",
            DocumentNumber = $"HECL-{request.AsAtDate:yyyyMMdd}", Narrative = $"Housing debtors allowance {(raise ? "raised" : "released")} to {report.TargetAllowance:N2}",
            Lines =
            [
                new JournalLineRequest { AccountCode = request.ExpenseAccountCode.Trim(), Side = raise ? JournalSide.Debit : JournalSide.Credit, TransactionAmount = amount },
                new JournalLineRequest { AccountCode = request.AllowanceAccountCode.Trim(), Side = raise ? JournalSide.Credit : JournalSide.Debit, TransactionAmount = amount },
            ],
        }, ct).ConfigureAwait(false);
        if (posted.IsFailure)
        {
            return posted.Error;
        }

        return report with { JournalNumber = posted.Value.JournalNumber };
    }

    // The allowance already booked at the date (its credit balance).
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
