using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Inventory;
using One4All.Application.Abstractions.JobCosting;
using One4All.Application.Abstractions.Numbering;
using One4All.Application.Abstractions.Trading;
using One4All.Application.JobCosting;
using One4All.Core.Common;
using One4All.Core.Inventory;
using One4All.Core.JobCosting;
using One4All.Infrastructure.Persistence;
using One4All.Licensing.Core;

namespace One4All.Infrastructure.JobCosting;

/// <summary>Runs the weighbridge: first and second weighs, manual-weight control, haulage and stockpile issues.</summary>
public sealed class WeighbridgeService : IWeighbridgeService
{
    private readonly One4AllDbContext _db;
    private readonly IInventoryService _inventory;
    private readonly INumberSequenceService _numbers;
    private readonly IClock _clock;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="inventory">Issues stockpile stock.</param>
    /// <param name="numbers">Numbers the tickets.</param>
    /// <param name="clock">Stamps time in and out.</param>
    /// <param name="ar">Posts the customer's invoice.</param>
    /// <param name="orders">Despatches a load against its sales order.</param>
    /// <param name="pricing">Prices a load from the customer's price book when no price is given.</param>
    public WeighbridgeService(One4AllDbContext db, IInventoryService inventory, INumberSequenceService numbers, IClock clock, ISalesLedgerService ar,
        Application.Abstractions.SalesOrders.ISalesOrderService? orders = null, IPricingService? pricing = null)
    {
        _orders = orders;
        _pricing = pricing;
        _db = db;
        _inventory = inventory;
        _numbers = numbers;
        _clock = clock;
        _ar = ar;
    }

    private readonly ISalesLedgerService _ar;
    private readonly Application.Abstractions.SalesOrders.ISalesOrderService? _orders;
    private readonly IPricingService? _pricing;

    /// <inheritdoc />
    public Task<Result<WeighbridgeInvoice>> InvoiceTicketsAsync(WeighbridgeInvoiceRequest request, CancellationToken ct = default)
        => _db.AtomicAsync(() => InvoiceTicketsCoreAsync(request, ct), ct);

    // One unit of work: the invoice posts and the tickets are marked billed together, so no load is billed twice or missed.
    private async Task<Result<WeighbridgeInvoice>> InvoiceTicketsCoreAsync(WeighbridgeInvoiceRequest request, CancellationToken ct)
    {
        if (request.PricePerTonne < 0m || string.IsNullOrWhiteSpace(request.SalesAccountCode) || string.IsNullOrWhiteSpace(request.DocumentNumber))
        {
            return Error.Validation("Wb.Invoice", "An invoice number and a sales account are required, and a price per tonne cannot be negative.");
        }

        Core.Trading.Customer? customer = await _db.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.Id == request.CustomerId && c.CompanyId == request.CompanyId, ct).ConfigureAwait(false);
        if (customer is null)
        {
            return Error.NotFound("Wb.Customer", "Customer not found.");
        }

        List<WeighbridgeTicket> tickets = await _db.WeighbridgeTickets
            .Where(t => t.CompanyId == request.CompanyId && t.CustomerId == request.CustomerId && t.Direction == WeighDirection.Outbound && t.IsComplete
                && t.InvoiceNumber == null && t.Date <= request.DocumentDate)
            .OrderBy(t => t.Date).ThenBy(t => t.Id).ToListAsync(ct).ConfigureAwait(false);
        if (tickets.Count == 0)
        {
            return Error.Rule("Wb.NothingToInvoice", $"{customer.Name} has no completed loads to invoice up to {request.DocumentDate:yyyy-MM-dd}.");
        }

        // Each load at the price given, or else its sales order's price, or else the customer's price book for the product.
        List<long> orderIds = [.. tickets.Where(t => t.SalesOrderId is not null).Select(t => t.SalesOrderId!.Value).Distinct()];
        List<Core.SalesOrders.SalesOrder> orders = await _db.SalesOrders.Include(o => o.Lines).Where(o => orderIds.Contains(o.Id)).ToListAsync(ct).ConfigureAwait(false);
        decimal product = 0m;
        foreach (WeighbridgeTicket t in tickets)
        {
            Core.SalesOrders.SalesOrderLine? orderLine = orders.FirstOrDefault(o => o.Id == t.SalesOrderId)?.Lines.FirstOrDefault(l => l.ItemId == t.ItemId);
            decimal price = request.PricePerTonne;
            if (price == 0m && orderLine is not null)
            {
                price = orderLine.UnitPrice;
            }
            else if (price == 0m && _pricing is not null && t.ItemId is long itemId
                && await _pricing.PriceAsync(request.CompanyId, customer.Id, itemId, t.NetTonnes, t.Date, ct).ConfigureAwait(false) is { IsSuccess: true } listed && listed.Value.Currency == customer.Currency)
            {
                price = listed.Value.UnitPrice;
            }

            if (price <= 0m)
            {
                return Error.Rule("Wb.NoPrice", $"Ticket {t.TicketNumber} has no price: give a price per tonne, or price the product for the customer or on the order.");
            }

            product += decimal.Round(t.NetTonnes * price, 2, MidpointRounding.AwayFromZero);

            // A load against an order is invoiced on the order too, up to what it despatched.
            if (orderLine is not null)
            {
                orderLine.Invoice(Math.Min(t.NetTonnes, Math.Max(0m, orderLine.DespatchedQuantity - orderLine.InvoicedQuantity)));
            }
        }

        foreach (Core.SalesOrders.SalesOrder order in orders)
        {
            order.RecomputeStatus();
        }

        decimal haulage = tickets.Sum(t => t.HaulageCharge);
        string salesAccount = request.SalesAccountCode.Trim();
        string haulageAccount = string.IsNullOrWhiteSpace(request.HaulageAccountCode) ? salesAccount : request.HaulageAccountCode.Trim();
        decimal tax = 0m;
        string? taxAccount = null, taxCode = null;
        if (!string.IsNullOrWhiteSpace(request.TaxCode))
        {
            taxCode = request.TaxCode.Trim().ToUpperInvariant();
            Core.Accounting.TaxCode? code = await _db.Set<Core.Accounting.TaxCode>().Include(t => t.Rates).AsNoTracking()
                .FirstOrDefaultAsync(t => t.CompanyId == request.CompanyId && t.Code == taxCode, ct).ConfigureAwait(false);
            if (code?.OutputAccountCode is null)
            {
                return Error.NotFound("Wb.TaxCode", $"Tax code {taxCode} with an output-tax account not found.");
            }

            taxAccount = code.OutputAccountCode;
            tax = decimal.Round((product + haulage) * code.RateOn(request.DocumentDate) / 100m, 2, MidpointRounding.AwayFromZero);
        }

        List<RevenueLine> lines = [new RevenueLine(salesAccount, product)];
        if (haulage > 0m)
        {
            lines.Add(new RevenueLine(haulageAccount, haulage));
        }

        string invoice = request.DocumentNumber.Trim().ToUpperInvariant();
        Result<TradingPostingResult> posted = await _ar.PostInvoiceAsync(new SalesInvoiceRequest
        {
            CompanyId = request.CompanyId, CustomerId = customer.Id, DocumentNumber = invoice, DocumentDate = request.DocumentDate, Currency = customer.Currency,
            NetAmount = product + haulage, TaxAmount = tax, TaxAccountCode = taxAccount, TaxCode = taxCode, SalesAccountCode = salesAccount, RevenueLines = lines,
            EnteredByUserId = request.EnteredByUserId, Narrative = $"Weighbridge loads {tickets[0].TicketNumber} to {tickets[^1].TicketNumber}",
        }, ct).ConfigureAwait(false);
        if (posted.IsFailure)
        {
            return posted.Error;
        }

        foreach (WeighbridgeTicket ticket in tickets)
        {
            ticket.MarkInvoiced(invoice);
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return new WeighbridgeInvoice([.. tickets.Select(t => t.TicketNumber)], tickets.Sum(t => t.NetTonnes), product, haulage, tax);
    }

    /// <inheritdoc />
    public async Task<Result<string>> RenderTicketAsync(long companyId, string ticketNumber, CancellationToken ct = default)
    {
        string number = (ticketNumber ?? string.Empty).Trim().ToUpperInvariant();
        WeighbridgeTicket? ticket = await _db.WeighbridgeTickets.AsNoTracking().FirstOrDefaultAsync(t => t.CompanyId == companyId && t.TicketNumber.ToUpper() == number, ct).ConfigureAwait(false);
        if (ticket is null)
        {
            return Error.NotFound("Wb.TicketNotFound", $"No weighbridge ticket {number}.");
        }

        string company = await _db.Companies.Where(c => c.Id == companyId).Select(c => c.Name).FirstOrDefaultAsync(ct).ConfigureAwait(false) ?? string.Empty;
        string? party = ticket.CustomerId is long customer
            ? await _db.Customers.Where(c => c.Id == customer).Select(c => c.Code + " " + c.Name).FirstOrDefaultAsync(ct).ConfigureAwait(false)
            : ticket.SupplierId is long supplier ? await _db.Suppliers.Where(s => s.Id == supplier).Select(s => s.Code + " " + s.Name).FirstOrDefaultAsync(ct).ConfigureAwait(false) : null;
        string? order = ticket.SalesOrderId is long id ? await _db.SalesOrders.Where(o => o.Id == id).Select(o => o.OrderNumber).FirstOrDefaultAsync(ct).ConfigureAwait(false) : null;

        System.Globalization.CultureInfo inv = System.Globalization.CultureInfo.InvariantCulture;
        const int Width = 48;
        string rule = new('-', Width);
        string Row(string label, string value) => label.PadRight(16) + value;
        string Kg(decimal kg) => kg.ToString("#,##0", inv) + " kg";
        System.Text.StringBuilder sb = new();
        sb.AppendLine(company);
        sb.AppendLine("WEIGHBRIDGE TICKET");
        sb.AppendLine(rule);
        sb.AppendLine(Row("Ticket", ticket.TicketNumber));
        sb.AppendLine(Row("Date", ticket.Date.ToString("yyyy-MM-dd", inv)));
        sb.AppendLine(Row("Direction", ticket.Direction.ToString()));
        sb.AppendLine(Row("Vehicle", ticket.VehicleRegistration));
        sb.AppendLine(Row("Driver", ticket.DriverName ?? "-"));
        sb.AppendLine(Row("Product", ticket.Product));
        sb.AppendLine(Row(ticket.Direction == WeighDirection.Outbound ? "Customer" : "Supplier", party ?? "-"));
        if (order is not null)
        {
            sb.AppendLine(Row("Sales order", order));
        }

        sb.AppendLine(Row("Site", ticket.SiteName ?? "-"));
        sb.AppendLine(rule);
        sb.AppendLine(Row("Gross", ticket.IsComplete || ticket.Direction == WeighDirection.Inbound ? Kg(ticket.GrossWeightKg) : "-"));
        sb.AppendLine(Row("Tare", ticket.IsComplete || ticket.Direction == WeighDirection.Outbound ? Kg(ticket.TareWeightKg) : "-"));
        sb.AppendLine(Row("Deduction", Kg(ticket.DeductionKg)));
        sb.AppendLine(Row("NET", ticket.IsComplete ? $"{Kg(ticket.NetWeightKg)}  ({ticket.NetTonnes.ToString("0.000", inv)} t, {ticket.NetCubicMetres.ToString("0.00", inv)} m3)" : "on the bridge"));
        if (ticket.HaulageCharge > 0m)
        {
            sb.AppendLine(Row("Haulage", ticket.HaulageCharge.ToString("N2", inv)));
        }

        sb.AppendLine(Row("Time in", ticket.TimeInUtc?.ToString("yyyy-MM-dd HH:mm", inv) ?? "-"));
        sb.AppendLine(Row("Time out", ticket.TimeOutUtc?.ToString("yyyy-MM-dd HH:mm", inv) ?? "-"));
        if (ticket.IsManualWeight)
        {
            sb.AppendLine(Row("KEYED WEIGHT", ticket.ManualReason ?? string.Empty));
        }

        sb.AppendLine(rule);
        sb.AppendLine("Driver: ____________________");
        sb.AppendLine("Weighbridge clerk: __________");
        return sb.ToString();
    }

    /// <inheritdoc />
    public Task<Result<(long TicketId, string TicketNumber)>> FirstWeighAsync(FirstWeighRequest request, CancellationToken ct = default)
        => _db.AtomicAsync(() => FirstWeighCoreAsync(request, ct), ct);

    // One unit of work: the ticket number and the ticket commit together.
    private async Task<Result<(long TicketId, string TicketNumber)>> FirstWeighCoreAsync(FirstWeighRequest request, CancellationToken ct)
    {
        string registration = request.Vehicle.Trim().ToUpperInvariant();
        WeighbridgeVehicle? vehicle = await _db.WeighbridgeVehicles.AsNoTracking().FirstOrDefaultAsync(v => v.CompanyId == request.CompanyId && v.Registration == registration, ct).ConfigureAwait(false);
        if (vehicle is { IsActive: false })
        {
            return Error.Rule("Wb.VehicleBlocked", $"Vehicle {registration} is barred from the weighbridge.");
        }

        decimal? weight = request.WeightKg;
        if (weight is null && request.Direction == WeighDirection.Outbound)
        {
            weight = vehicle?.StoredTareKg;
        }

        if (weight is null)
        {
            return Error.Validation("Wb.Weight", "The first weigh needs a weight (or a stored tare for an outbound vehicle).");
        }

        if (WeighbridgeRules.ValidateWeight(weight.Value, request.IsManual, request.ManualReason, request.OperatorUserId, request.AuthorisedByUserId) is { } invalid)
        {
            return invalid;
        }

        Item? item = await _db.Items.AsNoTracking().FirstOrDefaultAsync(i => i.Id == request.ItemId && i.CompanyId == request.CompanyId, ct).ConfigureAwait(false);
        if (item is null)
        {
            return Error.NotFound("Wb.ProductNotFound", "The product is not an item of this company.");
        }

        // A load against a sales order: an open order of the same customer that has a line for the product.
        long? orderId = null;
        if (!string.IsNullOrWhiteSpace(request.SalesOrderNumber))
        {
            string orderNumber = request.SalesOrderNumber.Trim().ToUpperInvariant();
            var order = await _db.SalesOrders.AsNoTracking().Where(o => o.CompanyId == request.CompanyId && o.OrderNumber.ToUpper() == orderNumber)
                .Select(o => new { o.Id, o.CustomerId, o.Status, HasProduct = o.Lines.Any(l => l.ItemId == request.ItemId) }).FirstOrDefaultAsync(ct).ConfigureAwait(false);
            if (order is null)
            {
                return Error.NotFound("Wb.OrderNotFound", $"No sales order {orderNumber}.");
            }

            if (request.Direction != WeighDirection.Outbound || order.CustomerId != request.CustomerId || !order.HasProduct
                || order.Status is Core.SalesOrders.SalesOrderStatus.Cancelled or Core.SalesOrders.SalesOrderStatus.Invoiced)
            {
                return Error.Rule("Wb.OrderMismatch", $"Sales order {orderNumber} is not an open order of this customer for this product.");
            }

            orderId = order.Id;
        }

        Result<string> number = await _numbers.AllocateAsync(request.CompanyId, "WB", request.Date, ct: ct).ConfigureAwait(false);
        if (number.IsFailure)
        {
            return number.Error;
        }

        WeighbridgeTicket ticket = request.Direction == WeighDirection.Outbound
            ? WeighbridgeTicket.Outbound(request.CompanyId, number.Value, registration, item.Description, weight.Value, request.CubicMetresPerTonne, request.Date)
            : new WeighbridgeTicket(request.CompanyId, number.Value, registration, item.Description, weight.Value, request.CubicMetresPerTonne, request.Date);
        ticket.ItemId = item.Id;
        ticket.CustomerId = request.CustomerId;
        ticket.SupplierId = request.SupplierId;
        ticket.DriverName = request.Driver?.Trim();
        ticket.SiteName = request.Site?.Trim();
        ticket.WarehouseId = request.WarehouseId;
        ticket.DistanceKm = request.DistanceKm;
        ticket.BatchRunNumber = request.BatchRunNumber;
        ticket.SalesOrderId = orderId;
        ticket.TimeInUtc = _clock.UtcNow;
        ticket.OperatorUserId = request.OperatorUserId;
        if (request.IsManual)
        {
            ticket.IsManualWeight = true;
            ticket.ManualReason = request.ManualReason!.Trim();
            ticket.ManualAuthorisedByUserId = request.AuthorisedByUserId;
        }

        _db.WeighbridgeTickets.Add(ticket);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return (ticket.Id, ticket.TicketNumber);
    }

    /// <inheritdoc />
    public Task<Result<WeighbridgeCompletion>> SecondWeighAsync(SecondWeighRequest request, CancellationToken ct = default)
        => _db.AtomicAsync(() => SecondWeighCoreAsync(request, ct), ct);

    // One unit of work: the completed ticket and the stockpile issue commit together or not at all.
    private async Task<Result<WeighbridgeCompletion>> SecondWeighCoreAsync(SecondWeighRequest request, CancellationToken ct)
    {
        WeighbridgeTicket? ticket = await _db.WeighbridgeTickets.FirstOrDefaultAsync(t => t.Id == request.TicketId && t.CompanyId == request.CompanyId, ct).ConfigureAwait(false);
        if (ticket is null)
        {
            return Error.NotFound("Wb.TicketNotFound", $"Ticket {request.TicketId} not found.");
        }

        if (ticket.IsComplete)
        {
            return Error.Rule("Wb.TicketComplete", $"Ticket {ticket.TicketNumber} is already complete.");
        }

        if (WeighbridgeRules.ValidateWeight(request.WeightKg, request.IsManual, request.ManualReason, request.OperatorUserId, request.AuthorisedByUserId) is { } invalid)
        {
            return invalid;
        }

        if (request.DeductionKg < 0m)
        {
            return Error.Validation("Wb.Deduction", "A deduction cannot be negative.");
        }

        ticket.DeductionKg = request.DeductionKg;
        ticket.Complete(request.WeightKg, _clock.UtcNow);
        if (ticket.NetWeightKg <= 0m)
        {
            return Error.Rule("Wb.NoNetWeight", $"The weights leave no net load on ticket {ticket.TicketNumber}; re-weigh.");
        }

        if (request.IsManual)
        {
            ticket.IsManualWeight = true;
            ticket.ManualReason = string.Join("; ", new[] { ticket.ManualReason, request.ManualReason!.Trim() }.Where(r => !string.IsNullOrEmpty(r)));
            ticket.ManualAuthorisedByUserId = request.AuthorisedByUserId;
        }

        if (ticket.DistanceKm is decimal distance)
        {
            List<HaulageBand> bands = [.. (await _db.HaulageZones.AsNoTracking().Where(z => z.CompanyId == request.CompanyId).ToListAsync(ct).ConfigureAwait(false))
                .Select(z => new HaulageBand(z.Code, z.FromKm, z.ToKm, z.RatePerTonne, z.MinimumCharge))];
            if (WeighbridgeRules.Haulage(bands, distance, ticket.NetTonnes) is { } charge)
            {
                ticket.HaulageZoneCode = charge.Zone;
                ticket.HaulageCharge = charge.Charge;
            }
        }

        if (ticket is { Direction: WeighDirection.Outbound, WarehouseId: long stockpile, ItemId: long itemId })
        {
            // A load against a sales order is despatched on the order (its delivery, stock issued at cost), as far as the order still wants it;
            // anything over is issued from the stockpile as it stands.
            decimal toIssue = ticket.NetTonnes;
            if (_orders is not null && ticket.SalesOrderId is long orderId)
            {
                var line = await _db.Set<Core.SalesOrders.SalesOrderLine>().AsNoTracking().Where(l => l.SalesOrderId == orderId && l.ItemId == itemId)
                    .Select(l => new { l.Id, Outstanding = l.OrderedQuantity - l.DespatchedQuantity }).FirstOrDefaultAsync(ct).ConfigureAwait(false);
                decimal onOrder = Math.Min(toIssue, Math.Max(0m, line?.Outstanding ?? 0m));
                if (line is not null && onOrder > 0m)
                {
                    Result<Application.Abstractions.SalesOrders.DespatchResult> despatched = await _orders.DespatchAsync(new Application.Abstractions.SalesOrders.DespatchRequest
                    {
                        CompanyId = request.CompanyId, SalesOrderId = orderId, WarehouseId = stockpile, Date = ticket.Date, Reference = ticket.TicketNumber,
                        Lines = [new Application.Abstractions.SalesOrders.DespatchLine(line.Id, onOrder)], Vehicle = ticket.VehicleRegistration, Driver = ticket.DriverName,
                    }, ct).ConfigureAwait(false);
                    if (despatched.IsFailure)
                    {
                        return despatched.Error;
                    }

                    toIssue -= onOrder;
                }
            }

            if (toIssue > 0m)
            {
                Result<StockMovementResult> issue = await _inventory.IssueAsync(new IssueStockRequest
                {
                    CompanyId = request.CompanyId, ItemId = itemId, WarehouseId = stockpile, Date = ticket.Date, Quantity = toIssue, Reference = ticket.TicketNumber,
                }, ct).ConfigureAwait(false);
                if (issue.IsFailure)
                {
                    return issue.Error;
                }
            }
        }

        decimal? maxGross = await _db.WeighbridgeVehicles.AsNoTracking().Where(v => v.CompanyId == request.CompanyId && v.Registration == ticket.VehicleRegistration)
            .Select(v => v.MaxGrossKg).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return new WeighbridgeCompletion(ticket.NetTonnes, ticket.NetCubicMetres, ticket.HaulageCharge, maxGross is decimal max && ticket.GrossWeightKg > max);
    }
}
