using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Accounting;
using One4All.Application.Abstractions.Housing;
using One4All.Application.Abstractions.Numbering;
using One4All.Application.Abstractions.Trading;
using One4All.Core.Accounting;
using One4All.Core.Common;
using One4All.Core.Housing;
using One4All.Core.System;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Housing;

/// <summary>Repossession of instalment-sale stands: receivable derecognised, stand back to inventory at the lower of cost and NRV.</summary>
public sealed class RepossessionService : IRepossessionService
{
    private readonly One4AllDbContext _db;
    private readonly INumberSequenceService _numbers;
    private readonly ISalesLedgerService _ar;
    private readonly IAutoAllocationService _allocation;
    private readonly IPostingEngine _engine;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="numbers">The number sequences.</param>
    /// <param name="ar">The receivables ledger.</param>
    /// <param name="allocation">Auto-allocation, to match the buyer's receipts to the receivable first.</param>
    /// <param name="engine">The posting engine.</param>
    public RepossessionService(One4AllDbContext db, INumberSequenceService numbers, ISalesLedgerService ar, IAutoAllocationService allocation, IPostingEngine engine)
    {
        _db = db;
        _numbers = numbers;
        _ar = ar;
        _allocation = allocation;
        _engine = engine;
    }

    /// <inheritdoc />
    public Task<Result<RepossessionEntry>> RepossessAsync(RepossessionRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return _db.AtomicAsync(() => RepossessCoreAsync(request, ct), ct);
    }

    private async Task<Result<RepossessionEntry>> RepossessCoreAsync(RepossessionRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Reason) || request.NetRealisableValue < 0m
            || string.IsNullOrWhiteSpace(request.StandsAvailableAccountCode) || string.IsNullOrWhiteSpace(request.GainLossAccountCode))
        {
            return Error.Validation("Hse.RepossessionDetails", "A repossession needs a reason, a net realisable value of zero or more, and the stands and gain/loss accounts.");
        }

        if (request.RequestedByUserId == request.ApprovedByUserId)
        {
            return Error.Rule("Hse.SelfApproval", "A repossession must be approved by someone other than who requested it.");
        }

        string number = (request.AgreementNumber ?? string.Empty).Trim();
        InstalmentAgreement? agreement = await _db.Set<InstalmentAgreement>().Include(a => a.Lines)
            .FirstOrDefaultAsync(a => a.CompanyId == request.CompanyId && a.AgreementNumber == number, ct).ConfigureAwait(false);
        if (agreement is null)
        {
            return Error.NotFound("Hse.AgreementNotFound", $"Agreement {number} not found.");
        }

        if (agreement.Status is not (InstalmentAgreementStatus.Active or InstalmentAgreementStatus.Completed))
        {
            return Error.Rule("Hse.AgreementClosed", $"Agreement {number} is {agreement.Status}.");
        }

        HousingStand stand = await _db.HousingStands.Include(s => s.Ownership).FirstAsync(s => s.Id == agreement.StandId, ct).ConfigureAwait(false);
        if (stand.Status != StandStatus.Sold || stand.CurrentHolderCustomerId != agreement.BuyerCustomerId)
        {
            return Error.Rule("Hse.StandNotHeld", $"Stand {stand.StandNumber} is no longer held by the buyer under {number}.");
        }

        // Derecognise the receivable: receipts matched first, then every debt still open written off to gain/loss.
        Result<AutoAllocationResult> matched = await _allocation.AllocateTraderAsync(request.CompanyId, TradingLedger.AccountsReceivable, agreement.BuyerCustomerId, request.Date, ct).ConfigureAwait(false);
        if (matched.IsFailure)
        {
            return matched.Error;
        }

        List<long> open = await _db.OpenItems.AsNoTracking()
            .Where(i => i.CompanyId == request.CompanyId && i.Ledger == TradingLedger.AccountsReceivable && i.TraderId == agreement.BuyerCustomerId
                && i.Sign == LedgerSign.Debit && i.OutstandingTransaction > 0m)
            .OrderBy(i => i.DocumentDate).ThenBy(i => i.Id).Select(i => i.Id).ToListAsync(ct).ConfigureAwait(false);
        decimal derecognised = 0m;
        foreach (long itemId in open)
        {
            Result<TradingPostingResult> written = await _ar.WriteOffAsync(request.CompanyId, itemId, request.Date, request.GainLossAccountCode.Trim(), $"Repossession under {number}: {request.Reason.Trim()}", ct).ConfigureAwait(false);
            if (written.IsFailure)
            {
                return written.Error;
            }

            derecognised += written.Value.BaseAmount;
        }

        // Re-recognise the stand in inventory at the lower of its previous carrying amount and net realisable value.
        decimal previous = stand.AllocatedCost;
        decimal inventory = Math.Min(previous, request.NetRealisableValue);
        Result<string> reference = await _numbers.AllocateAsync(request.CompanyId, "RP", request.Date, ct: ct).ConfigureAwait(false);
        if (reference.IsFailure)
        {
            return reference.Error;
        }

        if (inventory > 0m)
        {
            CurrencyCode currency = await _db.Set<Company>().Where(c => c.Id == request.CompanyId).Select(c => c.BaseCurrency).FirstAsync(ct).ConfigureAwait(false);
            Result<PostingReceipt> restocked = await _engine.PostAsync(new JournalRequest
            {
                CompanyId = request.CompanyId, SourceModule = "GL", PostingDate = request.Date, TransactionCurrency = currency, DocumentType = "Repossession",
                DocumentNumber = reference.Value, Narrative = $"Stand {stand.StandNumber} repossessed under {number}, returned to inventory",
                Lines =
                [
                    new JournalLineRequest { AccountCode = request.StandsAvailableAccountCode.Trim(), Side = JournalSide.Debit, TransactionAmount = inventory },
                    new JournalLineRequest { AccountCode = request.GainLossAccountCode.Trim(), Side = JournalSide.Credit, TransactionAmount = inventory },
                ],
            }, ct).ConfigureAwait(false);
            if (restocked.IsFailure)
            {
                return restocked.Error;
            }
        }

        stand.Repossess(request.Date, inventory);
        agreement.Repossess(request.Date);
        Repossession record = new(request.CompanyId, reference.Value, agreement.Id, stand.Id, request.Date, derecognised, previous, request.NetRealisableValue, inventory,
            request.Reason.Trim(), request.RequestedByUserId, request.ApprovedByUserId);
        _db.Set<Repossession>().Add(record);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return new RepossessionEntry(record.Reference, number, stand.StandNumber, record.Date, derecognised, inventory, record.GainOrLoss);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<RepossessionEntry>> RegisterAsync(long companyId, DateOnly start, DateOnly end, CancellationToken ct = default)
    {
        var rows = await (
            from r in _db.Set<Repossession>().AsNoTracking()
            join a in _db.Set<InstalmentAgreement>().AsNoTracking() on r.AgreementId equals a.Id
            join s in _db.HousingStands.AsNoTracking() on r.StandId equals s.Id
            where r.CompanyId == companyId && r.Date >= start && r.Date <= end
            orderby r.Date, r.Reference
            select new { r.Reference, a.AgreementNumber, s.StandNumber, r.Date, r.ReceivableDerecognised, r.InventoryRecognised })
            .ToListAsync(ct).ConfigureAwait(false);
        return [.. rows.Select(r => new RepossessionEntry(r.Reference, r.AgreementNumber, r.StandNumber, r.Date, r.ReceivableDerecognised, r.InventoryRecognised, r.InventoryRecognised - r.ReceivableDerecognised))];
    }
}
