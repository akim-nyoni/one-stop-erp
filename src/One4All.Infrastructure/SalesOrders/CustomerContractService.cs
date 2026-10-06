using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.SalesOrders;
using One4All.Core.Common;
using One4All.Core.Inventory;
using One4All.Core.SalesOrders;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.SalesOrders;

/// <summary>Customer contracts drawn down by call-off orders (brief §4.13).</summary>
public sealed class CustomerContractService : ICustomerContractService
{
    private readonly One4AllDbContext _db;
    private readonly ISalesOrderService _orders;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="orders">The sales order service call-offs are raised through.</param>
    public CustomerContractService(One4AllDbContext db, ISalesOrderService orders)
    {
        _db = db;
        _orders = orders;
    }

    /// <inheritdoc />
    public async Task<Result<long>> CreateAsync(long companyId, long customerId, string contractNumber, DateOnly startDate, DateOnly endDate, IReadOnlyList<ContractLineRequest> lines,
        CancellationToken ct = default)
    {
        string number = (contractNumber ?? string.Empty).Trim().ToUpperInvariant();
        if (number.Length == 0 || endDate < startDate || lines.Count == 0 || lines.Any(l => l.AgreedQuantity <= 0m || l.UnitPrice < 0m) || lines.Select(l => l.ItemId).Distinct().Count() != lines.Count)
        {
            return Error.Validation("Sop.ContractInvalid", "A contract needs a number, an end on or after its start, and each item once with a positive quantity and a price.");
        }

        if (!await _db.Customers.AnyAsync(c => c.Id == customerId && c.CompanyId == companyId, ct).ConfigureAwait(false))
        {
            return Error.NotFound("Sop.CustomerNotFound", $"Customer {customerId} not found.");
        }

        if (await _db.CustomerContracts.AnyAsync(c => c.CompanyId == companyId && c.ContractNumber == number, ct).ConfigureAwait(false))
        {
            return Error.Conflict("Sop.ContractDuplicate", $"Contract {number} already exists.");
        }

        CustomerContract contract = new(companyId, customerId, number, startDate, endDate);
        foreach (ContractLineRequest line in lines)
        {
            contract.AddLine(line.ItemId, line.AgreedQuantity, line.UnitPrice);
        }

        _db.CustomerContracts.Add(contract);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return contract.Id;
    }

    /// <inheritdoc />
    public Task<Result<SalesOrderResult>> CallOffAsync(long companyId, string contractNumber, string orderNumber, DateOnly date, IReadOnlyList<CallOffLine> lines, CancellationToken ct = default)
        => _db.AtomicAsync(() => CallOffCoreAsync(companyId, contractNumber, orderNumber, date, lines, ct), ct);

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<ContractPosition>>> PositionAsync(long companyId, string contractNumber, CancellationToken ct = default)
    {
        CustomerContract? contract = await FindAsync(companyId, contractNumber, ct).ConfigureAwait(false);
        if (contract is null)
        {
            return Error.NotFound("Sop.ContractNotFound", $"No contract '{contractNumber}'.");
        }

        List<long> ids = [.. contract.Lines.Select(l => l.ItemId)];
        Dictionary<long, string> codes = await _db.Items.Where(i => ids.Contains(i.Id)).ToDictionaryAsync(i => i.Id, i => i.Code, ct).ConfigureAwait(false);
        List<ContractPosition> position = [.. contract.Lines.Select(l => new ContractPosition(codes.GetValueOrDefault(l.ItemId, "?"), l.AgreedQuantity, l.CalledOffQuantity, l.Remaining, l.UnitPrice))];
        return position;
    }

    private async Task<Result<SalesOrderResult>> CallOffCoreAsync(long companyId, string contractNumber, string orderNumber, DateOnly date, IReadOnlyList<CallOffLine> lines, CancellationToken ct)
    {
        CustomerContract? contract = await FindAsync(companyId, contractNumber, ct).ConfigureAwait(false);
        if (contract is null)
        {
            return Error.NotFound("Sop.ContractNotFound", $"No contract '{contractNumber}'.");
        }

        if (date < contract.StartDate || date > contract.EndDate)
        {
            return Error.Rule("Sop.ContractNotActive", $"Contract {contract.ContractNumber} runs {contract.StartDate:yyyy-MM-dd} to {contract.EndDate:yyyy-MM-dd}.");
        }

        if (lines.Count == 0 || lines.Any(l => l.Quantity <= 0m))
        {
            return Error.Validation("Sop.CallOffInvalid", "A call-off needs positive quantities.");
        }

        List<long> ids = [.. lines.Select(l => l.ItemId)];
        Dictionary<long, Item> items = await _db.Items.Where(i => ids.Contains(i.Id)).ToDictionaryAsync(i => i.Id, ct).ConfigureAwait(false);
        Customer customer = await _db.Customers.FirstAsync(c => c.Id == contract.CustomerId, ct).ConfigureAwait(false);
        List<SalesOrderLineRequest> orderLines = [];
        List<(CustomerContractLine Line, decimal Quantity)> drawn = [];
        foreach (IGrouping<long, CallOffLine> item in lines.GroupBy(l => l.ItemId))
        {
            CustomerContractLine? line = contract.Lines.FirstOrDefault(l => l.ItemId == item.Key);
            string code = items.TryGetValue(item.Key, out Item? found) ? found.Code : $"#{item.Key}";
            if (line is null || found is null)
            {
                return Error.Rule("Sop.NotOnContract", $"{code} is not on contract {contract.ContractNumber}.");
            }

            decimal quantity = item.Sum(l => l.Quantity);
            if (quantity > line.Remaining)
            {
                return Error.Rule("Sop.ContractExceeded", $"Only {line.Remaining:0.##} of {code} remain on contract {contract.ContractNumber}.");
            }

            if (string.IsNullOrWhiteSpace(found.RevenueAccountCode))
            {
                return Error.Validation("Sop.ItemRevenueAccount", $"{code} has no revenue account to sell it against.");
            }

            drawn.Add((line, quantity));
            orderLines.Add(new SalesOrderLineRequest(found.Id, found.Description, quantity, line.UnitPrice, found.TaxCode ?? customer.DefaultTaxCode, found.RevenueAccountCode));
        }

        Result<SalesOrderResult> order = await _orders.CreateOrderAsync(new CreateSalesOrderRequest
        {
            CompanyId = companyId, CustomerId = contract.CustomerId, OrderNumber = (orderNumber ?? string.Empty).Trim(), OrderDate = date, Currency = customer.Currency, Lines = orderLines,
        }, ct).ConfigureAwait(false);
        if (order.IsFailure)
        {
            return order.Error;
        }

        // The contract is drawn down only once the order stands.
        foreach ((CustomerContractLine line, decimal quantity) in drawn)
        {
            line.CallOff(quantity);
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return order;
    }

    private Task<CustomerContract?> FindAsync(long companyId, string contractNumber, CancellationToken ct)
    {
        string number = (contractNumber ?? string.Empty).Trim().ToUpperInvariant();
        return _db.CustomerContracts.Include(c => c.Lines).FirstOrDefaultAsync(c => c.CompanyId == companyId && c.ContractNumber == number, ct);
    }
}
