using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Accounting;
using One4All.Application.Abstractions.Procurement;
using One4All.Application.Procurement;
using One4All.Core.Accounting;
using One4All.Core.Common;
using One4All.Core.Procurement;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Procurement;

/// <summary>Supplier rebate agreements and their accrual over AP purchases.</summary>
public sealed class RebateService : IRebateService
{
    private readonly One4AllDbContext _db;
    private readonly IPostingEngine _engine;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="engine">The posting engine.</param>
    public RebateService(One4AllDbContext db, IPostingEngine engine)
    {
        _db = db;
        _engine = engine;
    }

    /// <inheritdoc />
    public async Task<Result<long>> CreateAsync(long companyId, RebateAgreementRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Code) || request.To < request.From || request.Tiers.Count == 0
            || request.Tiers.Any(t => t.Threshold < 0m || t.RatePercent is <= 0m or > 100m))
        {
            return Error.Validation("Rebate.Invalid", "A rebate agreement needs a code, a period and tiers with rates between 0 and 100%.");
        }

        if (!await _db.Suppliers.AnyAsync(s => s.Id == request.SupplierId && s.CompanyId == companyId, ct).ConfigureAwait(false))
        {
            return Error.NotFound("Rebate.SupplierNotFound", $"Supplier {request.SupplierId} not found.");
        }

        RebateAgreement agreement = new(companyId, request.SupplierId, request.Code, request.From, request.To);
        foreach (RebateTier tier in request.Tiers.OrderBy(t => t.Threshold))
        {
            agreement.AddTier(tier.Threshold, tier.RatePercent);
        }

        _db.Set<RebateAgreement>().Add(agreement);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return agreement.Id;
    }

    /// <inheritdoc />
    public Task<Result<IReadOnlyList<RebatePosition>>> AccrueAsync(long companyId, DateOnly asAtDate, string receivableAccountCode, string incomeAccountCode, CancellationToken ct = default)
        => _db.AtomicAsync(() => AccrueCoreAsync(companyId, asAtDate, receivableAccountCode, incomeAccountCode, ct), ct);

    // One unit: the journal and every agreement's accrued figure are kept together.
    private async Task<Result<IReadOnlyList<RebatePosition>>> AccrueCoreAsync(long companyId, DateOnly asAtDate, string receivableAccountCode, string incomeAccountCode, CancellationToken ct)
    {
        List<RebateAgreement> agreements = await _db.Set<RebateAgreement>().Include(a => a.Tiers)
            .Where(a => a.CompanyId == companyId && a.FromDate <= asAtDate)
            .OrderBy(a => a.Code).ToListAsync(ct).ConfigureAwait(false);

        List<RebatePosition> positions = [];
        List<(RebateAgreement Agreement, decimal Delta)> changes = [];
        foreach (RebateAgreement agreement in agreements)
        {
            DateOnly until = agreement.ToDate < asAtDate ? agreement.ToDate : asAtDate;

            // Purchases: supplier invoices less credit notes in the period, in base currency.
            var items = await _db.OpenItems
                .Where(i => i.CompanyId == companyId && i.Ledger == TradingLedger.AccountsPayable && i.TraderId == agreement.SupplierId
                            && (i.ItemType == OpenItemType.Invoice || i.ItemType == OpenItemType.CreditNote)
                            && i.DocumentDate >= agreement.FromDate && i.DocumentDate <= until)
                .Select(i => new { i.ItemType, i.OriginalBase })
                .ToListAsync(ct).ConfigureAwait(false);
            decimal spend = items.Sum(i => i.ItemType == OpenItemType.Invoice ? i.OriginalBase : -i.OriginalBase);

            List<RebateTier> tiers = [.. agreement.Tiers.Select(t => new RebateTier(t.Threshold, t.RatePercent))];
            decimal earned = SupplierRebate.Earned(spend, tiers);
            decimal delta = earned - agreement.Accrued;
            positions.Add(new RebatePosition(agreement.Code, spend, SupplierRebate.RateFor(spend, tiers), earned, delta));
            if (delta != 0m)
            {
                changes.Add((agreement, delta));
            }
        }

        decimal net = changes.Sum(c => c.Delta);
        if (net != 0m)
        {
            CurrencyCode currency = await _db.Companies.Where(c => c.Id == companyId).Select(c => c.BaseCurrency).FirstAsync(ct).ConfigureAwait(false);
            Result<PostingReceipt> posted = await _engine.PostAsync(new JournalRequest
            {
                CompanyId = companyId,
                SourceModule = "POP",
                PostingDate = asAtDate,
                TransactionCurrency = currency,
                DocumentType = "RebateAccrual",
                DocumentNumber = $"REB-{asAtDate:yyyyMMdd}",
                Narrative = "Supplier rebates earned to date",
                Lines =
                [
                    new JournalLineRequest { AccountCode = receivableAccountCode, Side = net > 0m ? JournalSide.Debit : JournalSide.Credit, TransactionAmount = Math.Abs(net) },
                    new JournalLineRequest { AccountCode = incomeAccountCode, Side = net > 0m ? JournalSide.Credit : JournalSide.Debit, TransactionAmount = Math.Abs(net) },
                ],
            }, ct).ConfigureAwait(false);
            if (posted.IsFailure)
            {
                return posted.Error;
            }
        }

        foreach ((RebateAgreement agreement, decimal delta) in changes)
        {
            agreement.Accrue(delta);
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result<IReadOnlyList<RebatePosition>>.Success(positions);
    }
}
