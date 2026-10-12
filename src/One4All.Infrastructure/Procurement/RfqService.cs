using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Communications;
using One4All.Application.Abstractions.Procurement;
using One4All.Application.Abstractions.Trading;
using One4All.Application.Procurement;
using One4All.Core.Accounting;
using One4All.Core.Common;
using One4All.Core.Communications;
using One4All.Core.Procurement;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using One4All.Infrastructure.Trading;

namespace One4All.Infrastructure.Procurement;

/// <summary>Requests for quotation: issue, quotes, comparison and award through the procurement service.</summary>
public sealed class RfqService : IRfqService
{
    private readonly One4AllDbContext _db;
    private readonly IProcurementService _procurement;
    private readonly INotificationService _notifications;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="procurement">The procurement service, which raises the awarded purchase order.</param>
    /// <param name="notifications">The notification service, which emails the request to suppliers.</param>
    public RfqService(One4AllDbContext db, IProcurementService procurement, INotificationService notifications)
    {
        _db = db;
        _procurement = procurement;
        _notifications = notifications;
    }

    /// <inheritdoc />
    public async Task<Result<string>> RenderAsync(long companyId, long rfqId, CancellationToken ct = default)
    {
        Rfq? rfq = await _db.Set<Rfq>().AsNoTracking().Include(r => r.Lines).FirstOrDefaultAsync(r => r.Id == rfqId && r.CompanyId == companyId, ct).ConfigureAwait(false);
        if (rfq is null)
        {
            return Error.NotFound("Rfq.NotFound", $"Request for quotation {rfqId} not found.");
        }

        List<long> itemIds = [.. rfq.Lines.Select(l => l.ItemId)];
        Dictionary<long, (string Code, string Unit)> items = await _db.Items.AsNoTracking().Where(i => itemIds.Contains(i.Id))
            .ToDictionaryAsync(i => i.Id, i => (i.Code, i.UnitOfMeasure), ct).ConfigureAwait(false);
        string companyName = await _db.Companies.Where(c => c.Id == companyId).Select(c => c.Name).FirstOrDefaultAsync(ct).ConfigureAwait(false) ?? string.Empty;

        const int Width = 72;
        string rule = new('-', Width);
        StringBuilder sb = new();
        sb.AppendLine(companyName);
        sb.AppendLine("REQUEST FOR QUOTATION");
        sb.AppendLine(rule);
        sb.AppendLine(CultureInfo.InvariantCulture, $"RFQ: {rfq.RfqNumber}    Issued: {rfq.IssuedOn:yyyy-MM-dd}    Quotes close: {rfq.ClosesOn:yyyy-MM-dd}");
        sb.AppendLine(rule);
        sb.AppendLine($"{"Item",-12} {"Description",-30} {"Quantity",10} {"Unit",-5} {"Unit price",10}");
        foreach (RfqLine line in rfq.Lines)
        {
            (string code, string unit) = items.TryGetValue(line.ItemId, out var item) ? item : (string.Empty, string.Empty);
            string description = line.Description.Length > 30 ? line.Description[..30] : line.Description;
            sb.AppendLine(CultureInfo.InvariantCulture, $"{code,-12} {description,-30} {line.Quantity,10:0.###} {unit,-5} {"__________",10}");
        }

        sb.AppendLine(rule);
        sb.AppendLine("Please quote your unit prices, lead time (days) and terms, and return this request");
        sb.AppendLine(CultureInfo.InvariantCulture, $"by {rfq.ClosesOn:d MMMM yyyy}, quoting reference {rfq.RfqNumber}.");
        sb.AppendLine("Lead time: ________ days     Terms: ______________________________");
        return sb.ToString();
    }

    /// <inheritdoc />
    public async Task<Result<RfqDespatch>> SendAsync(long companyId, long rfqId, CancellationToken ct = default)
    {
        Rfq? rfq = await _db.Set<Rfq>().AsNoTracking().FirstOrDefaultAsync(r => r.Id == rfqId && r.CompanyId == companyId, ct).ConfigureAwait(false);
        if (rfq is null)
        {
            return Error.NotFound("Rfq.NotFound", $"Request for quotation {rfqId} not found.");
        }

        if (rfq.Status != RfqStatus.Open)
        {
            return Error.Rule("Rfq.NotOpen", $"RFQ {rfq.RfqNumber} is {rfq.Status}; only an open request is sent.");
        }

        Result<string> text = await RenderAsync(companyId, rfqId, ct).ConfigureAwait(false);
        if (text.IsFailure)
        {
            return text.Error;
        }

        List<string> recipients = [], noEmail = [];
        foreach (var supplier in await _db.Suppliers.AsNoTracking().Where(s => s.CompanyId == companyId && rfq.InvitedSupplierIds.Contains(s.Id)).OrderBy(s => s.Code)
            .Select(s => new { s.Id, s.Code }).ToListAsync(ct).ConfigureAwait(false))
        {
            IReadOnlyList<string> emails = await TraderDirectory.RecipientsAsync(_db, companyId, TradingLedger.AccountsPayable, supplier.Id, TraderDocument.PurchaseOrder, ct).ConfigureAwait(false);
            if (emails.Count == 0)
            {
                noEmail.Add(supplier.Code);
            }

            recipients.AddRange(emails);
        }

        if (recipients.Count == 0)
        {
            return new RfqDespatch(null, 0, noEmail);
        }

        string subject = $"Request for quotation {rfq.RfqNumber} (closes {rfq.ClosesOn:yyyy-MM-dd})";
        Result<long> batch = await _notifications.QueueDirectAsync(companyId, subject, MessageChannel.Email, [.. recipients.Distinct(StringComparer.OrdinalIgnoreCase)], subject,
            $"Please find attached our request for quotation {rfq.RfqNumber}. Quotes close on {rfq.ClosesOn:d MMMM yyyy}.",
            new MessageAttachment($"rfq-{rfq.RfqNumber}.txt", "text/plain", Encoding.UTF8.GetBytes(text.Value)), ct).ConfigureAwait(false);
        return batch.IsFailure ? batch.Error : new RfqDespatch(batch.Value, recipients.Distinct(StringComparer.OrdinalIgnoreCase).Count(), noEmail);
    }

    /// <inheritdoc />
    public async Task<Result<long>> IssueAsync(long companyId, RfqRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.RfqNumber) || request.Lines.Count == 0 || request.Lines.Any(l => l.Quantity <= 0m)
            || request.SupplierIds.Distinct().Count() < 2 || request.ClosesOn < request.IssuedOn)
        {
            return Error.Validation("Rfq.Invalid", "A request for quotation needs a number, requirements, at least two suppliers and a closing date after issue.");
        }

        string number = request.RfqNumber.Trim();
        if (await _db.Set<Rfq>().AnyAsync(r => r.CompanyId == companyId && r.RfqNumber == number, ct).ConfigureAwait(false))
        {
            return Error.Conflict("Rfq.Duplicate", $"Request for quotation {number} already exists.");
        }

        List<long> invited = [.. request.SupplierIds.Distinct()];
        int known = await _db.Suppliers.CountAsync(s => s.CompanyId == companyId && invited.Contains(s.Id) && !s.OnHold && !s.IsBlacklisted, ct).ConfigureAwait(false);
        if (known != invited.Count)
        {
            return Error.Rule("Rfq.Supplier", "Every invited supplier must exist, not be on hold and not be blacklisted.");
        }

        Rfq rfq = new(companyId, number, request.IssuedOn, request.ClosesOn, invited);
        foreach (RfqLineRequest line in request.Lines)
        {
            rfq.AddLine(line.ItemId, line.Description, line.Quantity);
        }

        _db.Set<Rfq>().Add(rfq);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return rfq.Id;
    }

    /// <inheritdoc />
    public async Task<Result> RecordQuoteAsync(long companyId, long rfqId, QuoteRequest quote, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(quote);
        Result<Rfq> found = await LoadAsync(companyId, rfqId, ct).ConfigureAwait(false);
        if (found.IsFailure)
        {
            return Result.Failure(found.Error);
        }

        Rfq rfq = found.Value;
        if (rfq.Status != RfqStatus.Open)
        {
            return Result.Failure(Error.Rule("Rfq.NotOpen", $"{rfq.RfqNumber} is {rfq.Status}."));
        }

        if (!rfq.InvitedSupplierIds.Contains(quote.SupplierId))
        {
            return Result.Failure(Error.Rule("Rfq.NotInvited", "Only an invited supplier can quote."));
        }

        if (quote.ReceivedOn > rfq.ClosesOn)
        {
            return Result.Failure(Error.Rule("Rfq.Closed", $"{rfq.RfqNumber} closed on {rfq.ClosesOn:yyyy-MM-dd}; a late quote cannot be accepted."));
        }

        if (quote.LeadTimeDays < 0 || rfq.Lines.Any(l => !quote.UnitPrices.TryGetValue(l.ItemId, out decimal price) || price < 0m))
        {
            return Result.Failure(Error.Validation("Rfq.Incomplete", "A quote must price every requirement and give a lead time."));
        }

        RfqQuote recorded = new(quote.SupplierId, quote.ReceivedOn, quote.LeadTimeDays, string.IsNullOrWhiteSpace(quote.Terms) ? null : quote.Terms.Trim());
        foreach (RfqLine line in rfq.Lines)
        {
            recorded.AddPrice(line.ItemId, quote.UnitPrices[line.ItemId]);
        }

        rfq.RecordQuote(recorded);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result<RfqComparison>> CompareAsync(long companyId, long rfqId, decimal priceWeight = TenderEvaluation.DefaultPriceWeight, CancellationToken ct = default)
    {
        if (priceWeight is < 0m or > 100m)
        {
            return Error.Validation("Rfq.Weight", "The price weight is out of 100.");
        }

        Result<Rfq> found = await LoadAsync(companyId, rfqId, ct).ConfigureAwait(false);
        if (found.IsFailure)
        {
            return found.Error;
        }

        Rfq rfq = found.Value;
        Dictionary<long, string> codes = await _db.Suppliers.Where(s => rfq.InvitedSupplierIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, s => s.Code, ct).ConfigureAwait(false);
        IReadOnlyList<TenderScore> scores = TenderEvaluation.Score([.. rfq.Quotes.Select(q => new TenderBid(q.SupplierId, Total(rfq, q), q.LeadTimeDays))], priceWeight);
        List<QuoteComparison> quotes = [.. scores.Select(s =>
        {
            RfqQuote quote = rfq.Quotes.Single(q => q.SupplierId == s.SupplierId);
            return new QuoteComparison(s.SupplierId, codes.GetValueOrDefault(s.SupplierId, string.Empty), quote.Prices.ToDictionary(p => p.ItemId, p => p.UnitPrice), s);
        })];
        List<long> notQuoted = [.. rfq.InvitedSupplierIds.Where(id => rfq.Quotes.All(q => q.SupplierId != id))];
        return new RfqComparison(rfq.RfqNumber, quotes, notQuoted, rfq.AwardedSupplierId, rfq.AwardReason);
    }

    /// <inheritdoc />
    public Task<Result<PurchaseOrderResult>> AwardAsync(long companyId, long rfqId, long supplierId, DateOnly orderDate, long userId, string? reason, CancellationToken ct = default)
        => _db.AtomicAsync(() => AwardCoreAsync(companyId, rfqId, supplierId, orderDate, userId, reason, ct), ct);

    // One unit: the purchase order and the award are kept together.
    private async Task<Result<PurchaseOrderResult>> AwardCoreAsync(long companyId, long rfqId, long supplierId, DateOnly orderDate, long userId, string? reason, CancellationToken ct)
    {
        Result<RfqComparison> comparison = await CompareAsync(companyId, rfqId, TenderEvaluation.DefaultPriceWeight, ct).ConfigureAwait(false);
        if (comparison.IsFailure)
        {
            return comparison.Error;
        }

        Rfq rfq = (await LoadAsync(companyId, rfqId, ct).ConfigureAwait(false)).Value;
        if (rfq.Status != RfqStatus.Open)
        {
            return Error.Rule("Rfq.NotOpen", $"{rfq.RfqNumber} is {rfq.Status}.");
        }

        RfqQuote? quote = rfq.Quotes.FirstOrDefault(q => q.SupplierId == supplierId);
        if (quote is null)
        {
            return Error.Rule("Rfq.NoQuote", "Only a supplier who quoted can be awarded.");
        }

        bool best = comparison.Value.Quotes[0].SupplierId == supplierId;
        if (!best && string.IsNullOrWhiteSpace(reason))
        {
            return Error.Rule("Rfq.AwardReason", "Awarding to other than the best-scored quote needs a reason for the adjudication record.");
        }

        CurrencyCode currency = await _db.Suppliers.Where(s => s.Id == supplierId).Select(s => s.Currency).FirstAsync(ct).ConfigureAwait(false);
        Result<PurchaseOrderResult> order = await _procurement.CreatePurchaseOrderAsync(new CreatePurchaseOrderRequest
        {
            CompanyId = companyId,
            SupplierId = supplierId,
            OrderNumber = $"{rfq.RfqNumber}-PO",
            OrderDate = orderDate,
            RequiredBy = orderDate.AddDays(quote.LeadTimeDays),
            Currency = currency,
            Lines = [.. rfq.Lines.Select(l => new PurchaseOrderLineRequest(l.ItemId, l.Description, l.Quantity, quote.Prices.Single(p => p.ItemId == l.ItemId).UnitPrice))],
        }, ct).ConfigureAwait(false);
        if (order.IsFailure)
        {
            return order.Error;
        }

        rfq.Award(supplierId, order.Value.PurchaseOrderId, userId, best ? null : reason!.Trim());
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return order.Value;
    }

    private static decimal Total(Rfq rfq, RfqQuote quote)
        => rfq.Lines.Sum(l => decimal.Round(l.Quantity * quote.Prices.Single(p => p.ItemId == l.ItemId).UnitPrice, 2, MidpointRounding.AwayFromZero));

    private async Task<Result<Rfq>> LoadAsync(long companyId, long rfqId, CancellationToken ct)
    {
        Rfq? rfq = await _db.Set<Rfq>()
            .Include(r => r.Lines)
            .Include(r => r.Quotes).ThenInclude(q => q.Prices)
            .FirstOrDefaultAsync(r => r.Id == rfqId && r.CompanyId == companyId, ct).ConfigureAwait(false);
        if (rfq is null)
        {
            return Error.NotFound("Rfq.NotFound", $"Request for quotation {rfqId} not found.");
        }

        return rfq;
    }
}
