using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Procurement;
using One4All.Core.Common;
using One4All.Core.Procurement;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Procurement;

/// <summary>The invoice dispute queue over the procurement service's three-way match.</summary>
public sealed class InvoiceDisputeService : IInvoiceDisputeService
{
    private const string PriceMismatch = "Pop.PriceMismatch";
    private const string QuantityMismatch = "Pop.QuantityMismatch";

    // Accepting a price dispute posts the invoice as presented, so the price check must let any difference through.
    private const decimal AcceptAnyPrice = 1_000_000m;

    private readonly One4AllDbContext _db;
    private readonly IProcurementService _procurement;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="procurement">The procurement service, which matches and posts invoices.</param>
    public InvoiceDisputeService(One4AllDbContext db, IProcurementService procurement)
    {
        _db = db;
        _procurement = procurement;
    }

    /// <inheritdoc />
    public async Task<Result<InvoiceSubmission>> SubmitAsync(MatchInvoiceRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        Result<MatchInvoiceResult> match = await _procurement.MatchInvoiceAsync(request, ct).ConfigureAwait(false);
        if (match.IsSuccess)
        {
            return new InvoiceSubmission(match.Value, null, null);
        }

        if (match.Error.Code is not (PriceMismatch or QuantityMismatch))
        {
            return match.Error;
        }

        // The failed match left nothing behind; the dispute is saved on its own so it is kept.
        long supplierId = await _db.Set<PurchaseOrder>().Where(p => p.Id == request.PurchaseOrderId).Select(p => p.SupplierId).FirstAsync(ct).ConfigureAwait(false);
        InvoiceDispute dispute = new(request.CompanyId, request.PurchaseOrderId, supplierId, request.InvoiceNumber.Trim(), request.Date, match.Error.Code, match.Error.Message);
        foreach (InvoiceMatchLine line in request.Lines)
        {
            dispute.AddLine(line.PurchaseOrderLineId, line.Quantity, line.UnitPrice);
        }

        _db.Set<InvoiceDispute>().Add(dispute);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return new InvoiceSubmission(null, dispute.Id, match.Error.Message);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<OpenDispute>> OpenAsync(long companyId, CancellationToken ct = default)
    {
        List<InvoiceDispute> open = await _db.Set<InvoiceDispute>().AsNoTracking().Include(d => d.Lines)
            .Where(d => d.CompanyId == companyId && d.Status == DisputeStatus.Open)
            .OrderBy(d => d.InvoiceDate).ThenBy(d => d.Id)
            .ToListAsync(ct).ConfigureAwait(false);
        return [.. open.Select(d => new OpenDispute(d.Id, d.SupplierId, d.InvoiceNumber, d.InvoiceDate, d.ReasonCode, d.Reason,
            d.Lines.Sum(l => decimal.Round(l.Quantity * l.UnitPrice, 2, MidpointRounding.AwayFromZero))))];
    }

    /// <inheritdoc />
    public Task<Result<MatchInvoiceResult>> AcceptAsync(long companyId, long disputeId, long userId, string note, string priceVarianceAccountCode, CancellationToken ct = default)
        => _db.AtomicAsync(() => AcceptCoreAsync(companyId, disputeId, userId, note, priceVarianceAccountCode, ct), ct);

    // One unit: the posting and the decision are kept together.
    private async Task<Result<MatchInvoiceResult>> AcceptCoreAsync(long companyId, long disputeId, long userId, string note, string priceVarianceAccountCode, CancellationToken ct)
    {
        Result<InvoiceDispute> found = await FindOpenAsync(companyId, disputeId, note, ct).ConfigureAwait(false);
        if (found.IsFailure)
        {
            return found.Error;
        }

        InvoiceDispute dispute = found.Value;
        if (dispute.ReasonCode == QuantityMismatch)
        {
            return Error.Rule("Dispute.Quantity", "An invoice for more than was received cannot be accepted; receive the goods or reject it.");
        }

        Result<MatchInvoiceResult> posted = await _procurement.MatchInvoiceAsync(new MatchInvoiceRequest
        {
            CompanyId = companyId,
            PurchaseOrderId = dispute.PurchaseOrderId,
            InvoiceNumber = dispute.InvoiceNumber,
            Date = dispute.InvoiceDate,
            TolerancePercent = AcceptAnyPrice,
            PriceVarianceAccountCode = priceVarianceAccountCode,
            Lines = [.. dispute.Lines.Select(l => new InvoiceMatchLine(l.PurchaseOrderLineId, l.Quantity, l.UnitPrice))],
        }, ct).ConfigureAwait(false);
        if (posted.IsFailure)
        {
            return posted.Error;
        }

        dispute.Accept(userId, note.Trim(), posted.Value.JournalNumber);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return posted.Value;
    }

    /// <inheritdoc />
    public async Task<Result> RejectAsync(long companyId, long disputeId, long userId, string note, CancellationToken ct = default)
    {
        Result<InvoiceDispute> found = await FindOpenAsync(companyId, disputeId, note, ct).ConfigureAwait(false);
        if (found.IsFailure)
        {
            return Result.Failure(found.Error);
        }

        found.Value.Reject(userId, note.Trim());
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    private async Task<Result<InvoiceDispute>> FindOpenAsync(long companyId, long disputeId, string note, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(note))
        {
            return Error.Validation("Dispute.NoteRequired", "Deciding a disputed invoice requires a note.");
        }

        InvoiceDispute? dispute = await _db.Set<InvoiceDispute>().Include(d => d.Lines)
            .FirstOrDefaultAsync(d => d.Id == disputeId && d.CompanyId == companyId, ct).ConfigureAwait(false);
        if (dispute is null)
        {
            return Error.NotFound("Dispute.NotFound", $"Invoice dispute {disputeId} not found.");
        }

        if (dispute.Status != DisputeStatus.Open)
        {
            return Error.Rule("Dispute.NotOpen", $"Invoice dispute {disputeId} is already {dispute.Status}.");
        }

        return dispute;
    }
}
