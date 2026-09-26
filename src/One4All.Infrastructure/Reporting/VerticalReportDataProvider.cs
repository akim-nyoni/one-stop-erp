using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Budgeting;
using One4All.Application.Abstractions.Inventory;
using One4All.Application.Abstractions.Reporting;
using One4All.Core.Budgeting;
using One4All.Core.Common;
using One4All.Core.FixedAssets;
using One4All.Core.Municipal;
using One4All.Core.Payroll;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Reporting;

/// <summary>
/// Supplies live rows for the vertical-module reports (inventory, fixed assets, payroll, municipality and
/// budgeting) from their read-side services and registers, so the whole system — not just the ledgers and
/// receipting — renders live figures through the generic report runner.
/// </summary>
public sealed class VerticalReportDataProvider : IReportDataProvider
{
    private static readonly HashSet<string> Commands = new(StringComparer.OrdinalIgnoreCase)
    {
        "StockValuation", "StockMovements", "Reorder", "SlowStock", "StockAgeing", "AbcAnalysis", "ItemMaster", "AssetRegister", "DepreciationForecast", "AssetMaintenance", "AssetDisposals", "PayrollSummary", "PayrollDeductions", "EmployeeRegister", "EmployeeLoans", "LeaveBalances", "PropertyRegister", "BudgetVsActual", "BudgetList", "Pipeline", "ExpenseClaims",
    };

    private readonly One4AllDbContext _db;
    private readonly IInventoryService _inventory;
    private readonly IBudgetingService _budgeting;
    private readonly One4All.Application.Abstractions.FixedAssets.IFixedAssetService _fixedAssets;

    /// <summary>Creates the provider.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="inventory">The inventory service (stock valuation).</param>
    /// <param name="budgeting">The budgeting service (budget vs actual).</param>
    /// <param name="fixedAssets">The fixed-asset service (depreciation forecast).</param>
    public VerticalReportDataProvider(One4AllDbContext db, IInventoryService inventory, IBudgetingService budgeting, One4All.Application.Abstractions.FixedAssets.IFixedAssetService fixedAssets)
    {
        _db = db;
        _inventory = inventory;
        _budgeting = budgeting;
        _fixedAssets = fixedAssets;
    }

    /// <inheritdoc />
    public bool CanHandle(string command) => Commands.Contains(command);

    /// <inheritdoc />
    public async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> GetRowsAsync(long companyId, string command, IReadOnlyDictionary<string, object?> parameters, CancellationToken ct = default)
    {
        return command.ToUpperInvariant() switch
        {
            "STOCKVALUATION" => await StockValuationAsync(companyId, ct).ConfigureAwait(false),
            "STOCKMOVEMENTS" => await StockMovementsAsync(companyId, parameters, ct).ConfigureAwait(false),
            "REORDER" => await ReorderAsync(companyId, ct).ConfigureAwait(false),
            "SLOWSTOCK" => await SlowStockAsync(companyId, parameters, ct).ConfigureAwait(false),
            "STOCKAGEING" => await StockAgeingAsync(companyId, parameters, ct).ConfigureAwait(false),
            "ABCANALYSIS" => await AbcAnalysisAsync(companyId, ct).ConfigureAwait(false),
            "ITEMMASTER" => await ItemMasterAsync(companyId, ct).ConfigureAwait(false),
            "ASSETREGISTER" => await AssetRegisterAsync(companyId, ct).ConfigureAwait(false),
            "DEPRECIATIONFORECAST" => await DepreciationForecastAsync(companyId, parameters, ct).ConfigureAwait(false),
            "ASSETMAINTENANCE" => await AssetMaintenanceAsync(companyId, ct).ConfigureAwait(false),
            "ASSETDISPOSALS" => await AssetDisposalsAsync(companyId, ct).ConfigureAwait(false),
            "PAYROLLSUMMARY" => await PayrollSummaryAsync(companyId, parameters, ct).ConfigureAwait(false),
            "PAYROLLDEDUCTIONS" => await PayrollDeductionsAsync(companyId, parameters, ct).ConfigureAwait(false),
            "EMPLOYEEREGISTER" => await EmployeeRegisterAsync(companyId, ct).ConfigureAwait(false),
            "EMPLOYEELOANS" => await EmployeeLoansAsync(companyId, ct).ConfigureAwait(false),
            "LEAVEBALANCES" => await LeaveBalancesAsync(companyId, ct).ConfigureAwait(false),
            "PROPERTYREGISTER" => await PropertyRegisterAsync(companyId, ct).ConfigureAwait(false),
            "BUDGETVSACTUAL" => await BudgetVsActualAsync(companyId, parameters, ct).ConfigureAwait(false),
            "BUDGETLIST" => await BudgetListAsync(companyId, ct).ConfigureAwait(false),
            "PIPELINE" => await PipelineAsync(companyId, ct).ConfigureAwait(false),
            "EXPENSECLAIMS" => await ExpenseClaimsAsync(companyId, ct).ConfigureAwait(false),
            _ => [],
        };
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> SlowStockAsync(long companyId, IReadOnlyDictionary<string, object?> parameters, CancellationToken ct)
    {
        DateOnly since = ReportParameters.Date(parameters, "SinceDate");

        var balances = await (
            from b in _db.Set<Core.Inventory.StockBalance>()
            join i in _db.Set<Core.Inventory.Item>() on b.ItemId equals i.Id
            join w in _db.Set<Core.Inventory.Warehouse>() on b.WarehouseId equals w.Id
            where b.CompanyId == companyId && b.QuantityOnHand > 0m
            select new { b.ItemId, b.WarehouseId, i.Code, i.Description, WarehouseCode = w.Code, b.QuantityOnHand, b.TotalValue })
            .ToListAsync(ct).ConfigureAwait(false);

        Dictionary<(long, long), DateOnly> lastMovement = await _db.Set<Core.Inventory.StockMovement>()
            .Where(m => m.CompanyId == companyId)
            .GroupBy(m => new { m.ItemId, m.WarehouseId })
            .Select(g => new { g.Key.ItemId, g.Key.WarehouseId, Last = g.Max(x => x.Date) })
            .ToDictionaryAsync(x => (x.ItemId, x.WarehouseId), x => x.Last, ct).ConfigureAwait(false);

        return balances
            .Select(b => (b, Last: lastMovement.TryGetValue((b.ItemId, b.WarehouseId), out DateOnly d) ? (DateOnly?)d : null))
            .Where(x => x.Last is null || x.Last < since)
            .OrderByDescending(x => x.b.TotalValue)
            .Select(x => Row(new()
            {
                ["ItemCode"] = x.b.Code,
                ["Description"] = x.b.Description,
                ["Warehouse"] = x.b.WarehouseCode,
                ["Quantity"] = x.b.QuantityOnHand,
                ["Value"] = x.b.TotalValue,
                ["LastMovement"] = x.Last is { } lm ? lm.ToDateTime(TimeOnly.MinValue) : (object?)string.Empty,
            }))
            .ToList();
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> StockAgeingAsync(long companyId, IReadOnlyDictionary<string, object?> parameters, CancellationToken ct)
    {
        DateOnly asAt = ReportParameters.Date(parameters, "AsAtDate");

        var balances = await (
            from b in _db.Set<Core.Inventory.StockBalance>()
            join i in _db.Set<Core.Inventory.Item>() on b.ItemId equals i.Id
            join w in _db.Set<Core.Inventory.Warehouse>() on b.WarehouseId equals w.Id
            where b.CompanyId == companyId && b.QuantityOnHand > 0m
            select new { b.ItemId, b.WarehouseId, i.Code, i.Description, WarehouseCode = w.Code, b.QuantityOnHand, b.TotalValue })
            .ToListAsync(ct).ConfigureAwait(false);

        Dictionary<(long, long), DateOnly> lastMovement = await _db.Set<Core.Inventory.StockMovement>()
            .Where(m => m.CompanyId == companyId)
            .GroupBy(m => new { m.ItemId, m.WarehouseId })
            .Select(g => new { g.Key.ItemId, g.Key.WarehouseId, Last = g.Max(x => x.Date) })
            .ToDictionaryAsync(x => (x.ItemId, x.WarehouseId), x => x.Last, ct).ConfigureAwait(false);

        return balances
            .Select(b => (b, Last: lastMovement.TryGetValue((b.ItemId, b.WarehouseId), out DateOnly d) ? (DateOnly?)d : null))
            .OrderByDescending(x => x.b.TotalValue)
            .Select(x =>
            {
                int age = x.Last is { } lm ? asAt.DayNumber - lm.DayNumber : int.MaxValue;
                string band = age <= 30 ? "0-30" : age <= 60 ? "31-60" : age <= 90 ? "61-90" : "90+";
                return Row(new()
                {
                    ["ItemCode"] = x.b.Code,
                    ["Description"] = x.b.Description,
                    ["Warehouse"] = x.b.WarehouseCode,
                    ["Quantity"] = x.b.QuantityOnHand,
                    ["Value"] = x.b.TotalValue,
                    ["AgeBand"] = band,
                });
            })
            .ToList();
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> ItemMasterAsync(long companyId, CancellationToken ct)
    {
        List<Core.Inventory.Item> items = await _db.Set<Core.Inventory.Item>()
            .Where(i => i.CompanyId == companyId)
            .OrderBy(i => i.Code)
            .ToListAsync(ct).ConfigureAwait(false);

        return items.Select(i => Row(new()
        {
            ["Code"] = i.Code,
            ["Description"] = i.Description,
            ["Uom"] = i.UnitOfMeasure,
            ["ReorderLevel"] = i.ReorderLevel,
            ["RevenueAccount"] = i.RevenueAccountCode ?? string.Empty,
            ["CostAccount"] = i.CostOfSalesAccountCode ?? string.Empty,
            ["Active"] = i.IsActive ? "Yes" : string.Empty,
        })).ToList();
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> AbcAnalysisAsync(long companyId, CancellationToken ct)
    {
        var items = await (
            from b in _db.Set<Core.Inventory.StockBalance>()
            join i in _db.Set<Core.Inventory.Item>() on b.ItemId equals i.Id
            where b.CompanyId == companyId
            group new { b.TotalValue } by new { i.Code, i.Description } into g
            select new { g.Key.Code, g.Key.Description, Value = g.Sum(x => x.TotalValue) })
            .ToListAsync(ct).ConfigureAwait(false);

        List<(string Code, string Description, decimal Value)> ranked = items
            .Where(x => x.Value > 0m)
            .OrderByDescending(x => x.Value)
            .Select(x => (x.Code, x.Description, x.Value))
            .ToList();

        decimal total = ranked.Sum(x => x.Value);
        List<IReadOnlyDictionary<string, object?>> rows = [];
        decimal cumulative = 0m;
        foreach ((string code, string description, decimal value) in ranked)
        {
            cumulative += value;
            decimal cumulativePercent = total == 0m ? 0m : decimal.Round(cumulative / total * 100m, 2, MidpointRounding.ToEven);
            // Pareto bands: A holds the top ~80% of value, B the next ~15%, C the tail.
            string abcClass = cumulativePercent <= 80m ? "A" : cumulativePercent <= 95m ? "B" : "C";
            rows.Add(Row(new()
            {
                ["ItemCode"] = code,
                ["Description"] = description,
                ["Value"] = value,
                ["CumulativePercent"] = cumulativePercent,
                ["Class"] = abcClass,
            }));
        }

        return rows;
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> ReorderAsync(long companyId, CancellationToken ct)
    {
        List<Core.Inventory.Item> items = await _db.Set<Core.Inventory.Item>()
            .Where(i => i.CompanyId == companyId && i.ReorderLevel > 0m)
            .OrderBy(i => i.Code)
            .ToListAsync(ct).ConfigureAwait(false);

        Dictionary<long, decimal> onHand = await _db.Set<Core.Inventory.StockBalance>()
            .Where(b => b.CompanyId == companyId)
            .GroupBy(b => b.ItemId)
            .Select(g => new { ItemId = g.Key, Qty = g.Sum(x => x.QuantityOnHand) })
            .ToDictionaryAsync(x => x.ItemId, x => x.Qty, ct).ConfigureAwait(false);

        return items
            .Select(i => (Item: i, Qty: onHand.GetValueOrDefault(i.Id)))
            .Where(x => x.Qty <= x.Item.ReorderLevel)
            .Select(x => Row(new()
            {
                ["ItemCode"] = x.Item.Code,
                ["Description"] = x.Item.Description,
                ["OnHand"] = x.Qty,
                ["ReorderLevel"] = x.Item.ReorderLevel,
                ["Suggested"] = x.Item.ReorderQuantity > 0m ? x.Item.ReorderQuantity : x.Item.ReorderLevel - x.Qty,
            }))
            .ToList();
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> StockMovementsAsync(long companyId, IReadOnlyDictionary<string, object?> parameters, CancellationToken ct)
    {
        DateOnly fromDate = ReportParameters.TryDate(parameters, "FromDate") ?? DateOnly.MinValue;
        DateOnly toDate = ReportParameters.TryDate(parameters, "ToDate") ?? DateOnly.MaxValue;

        var rows = await (
            from m in _db.Set<Core.Inventory.StockMovement>()
            join i in _db.Set<Core.Inventory.Item>() on m.ItemId equals i.Id
            join w in _db.Set<Core.Inventory.Warehouse>() on m.WarehouseId equals w.Id
            where m.CompanyId == companyId && m.Date >= fromDate && m.Date <= toDate
            orderby m.Date, m.Id
            select new { m.Date, i.Code, WarehouseCode = w.Code, m.Type, m.Quantity, m.Value, m.Reference })
            .ToListAsync(ct).ConfigureAwait(false);

        return rows.Select(r => Row(new()
        {
            ["Date"] = r.Date.ToDateTime(TimeOnly.MinValue),
            ["Item"] = r.Code,
            ["Warehouse"] = r.WarehouseCode,
            ["Type"] = r.Type.ToString(),
            ["Qty"] = r.Quantity,
            ["Value"] = r.Value,
            ["Reference"] = r.Reference ?? string.Empty,
        })).ToList();
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> StockValuationAsync(long companyId, CancellationToken ct)
    {
        IReadOnlyList<StockValuationLine> lines = await _inventory.StockValuationAsync(companyId, null, ct).ConfigureAwait(false);
        return lines.Select(l => Row(new()
        {
            ["ItemCode"] = l.ItemCode,
            ["Description"] = l.Description,
            ["WarehouseCode"] = l.WarehouseCode,
            ["Quantity"] = l.Quantity,
            ["AverageCost"] = l.AverageCost,
            ["Value"] = l.Value,
        })).ToList();
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> DepreciationForecastAsync(long companyId, IReadOnlyDictionary<string, object?> parameters, CancellationToken ct)
    {
        DateOnly start = ReportParameters.Date(parameters, "StartMonth");
        long months = ReportParameters.Long(parameters, "Months");
        int horizon = months <= 0 ? 12 : (int)Math.Min(months, 600);

        Result<Application.Abstractions.FixedAssets.DepreciationForecastReport> report =
            await _fixedAssets.DepreciationForecastAsync(companyId, start, horizon, ct).ConfigureAwait(false);
        if (report.IsFailure)
        {
            return [];
        }

        return report.Value.Periods.Select(p => Row(new()
        {
            ["Period"] = p.Period,
            ["Charge"] = p.Charge,
            ["ClosingNetBookValue"] = p.ClosingNetBookValue,
        })).ToList();
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> AssetDisposalsAsync(long companyId, CancellationToken ct)
    {
        List<Core.FixedAssets.AssetDisposal> disposals = await _db.Set<Core.FixedAssets.AssetDisposal>()
            .Where(d => d.CompanyId == companyId)
            .OrderByDescending(d => d.Date)
            .ToListAsync(ct).ConfigureAwait(false);

        return disposals.Select(d => Row(new()
        {
            ["AssetNumber"] = d.AssetNumber,
            ["Date"] = d.Date.ToDateTime(TimeOnly.MinValue),
            ["Proceeds"] = d.Proceeds,
            ["NetBookValue"] = d.NetBookValue,
            ["GainOrLoss"] = d.GainOrLoss,
            ["Journal"] = d.JournalNumber,
        })).ToList();
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> AssetMaintenanceAsync(long companyId, CancellationToken ct)
    {
        var rows = await (
            from m in _db.Set<Core.FixedAssets.AssetMaintenance>()
            join a in _db.Set<Core.FixedAssets.FixedAsset>() on m.AssetId equals a.Id
            where m.CompanyId == companyId
            orderby m.Date descending, a.AssetNumber
            select new { a.AssetNumber, a.Description, MaintDate = m.Date, MaintDescription = m.Description, m.Cost, m.Provider })
            .ToListAsync(ct).ConfigureAwait(false);

        return rows.Select(r => Row(new()
        {
            ["AssetNumber"] = r.AssetNumber,
            ["Asset"] = r.Description,
            ["Date"] = r.MaintDate.ToDateTime(TimeOnly.MinValue),
            ["Work"] = r.MaintDescription,
            ["Provider"] = r.Provider ?? string.Empty,
            ["Cost"] = r.Cost,
        })).ToList();
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> AssetRegisterAsync(long companyId, CancellationToken ct)
    {
        List<FixedAsset> assets = await _db.Set<FixedAsset>()
            .Where(a => a.CompanyId == companyId)
            .OrderBy(a => a.AssetNumber)
            .ToListAsync(ct).ConfigureAwait(false);
        return assets.Select(a => Row(new()
        {
            ["AssetNumber"] = a.AssetNumber,
            ["Description"] = a.Description,
            ["Cost"] = a.Cost,
            ["AccumulatedDepreciation"] = a.AccumulatedDepreciation,
            ["NetBookValue"] = a.NetBookValue,
        })).ToList();
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> PipelineAsync(long companyId, CancellationToken ct)
    {
        List<Core.Crm.Opportunity> opps = await _db.Set<Core.Crm.Opportunity>()
            .Where(o => o.CompanyId == companyId && o.Stage != Core.Crm.OpportunityStage.Won && o.Stage != Core.Crm.OpportunityStage.Lost)
            .OrderByDescending(o => o.EstimatedValue)
            .ToListAsync(ct).ConfigureAwait(false);

        return opps.Select(o => Row(new()
        {
            ["Name"] = o.Name,
            ["Contact"] = o.ContactName,
            ["Stage"] = o.Stage.ToString(),
            ["Probability"] = o.ProbabilityPercent,
            ["Estimated"] = o.EstimatedValue,
            ["Weighted"] = o.WeightedValue,
        })).ToList();
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> ExpenseClaimsAsync(long companyId, CancellationToken ct)
    {
        List<Core.Expenses.ExpenseClaim> claims = await _db.Set<Core.Expenses.ExpenseClaim>()
            .Include(c => c.Lines)
            .Where(c => c.CompanyId == companyId)
            .OrderByDescending(c => c.ClaimDate)
            .ToListAsync(ct).ConfigureAwait(false);
        Dictionary<long, string> employees = await _db.Set<Core.Payroll.Employee>()
            .Where(e => e.CompanyId == companyId)
            .ToDictionaryAsync(e => e.Id, e => e.FullName, ct).ConfigureAwait(false);

        return claims.Select(c => Row(new()
        {
            ["Reference"] = c.Reference,
            ["Employee"] = employees.GetValueOrDefault(c.EmployeeId, $"#{c.EmployeeId}"),
            ["ClaimDate"] = c.ClaimDate.ToDateTime(TimeOnly.MinValue),
            ["Total"] = c.Total,
            ["Status"] = c.Status.ToString(),
            ["Journal"] = c.JournalNumber ?? string.Empty,
        })).ToList();
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> LeaveBalancesAsync(long companyId, CancellationToken ct)
    {
        List<Core.Payroll.EmployeeLeaveBalance> balances = await _db.Set<Core.Payroll.EmployeeLeaveBalance>()
            .Where(b => b.CompanyId == companyId)
            .ToListAsync(ct).ConfigureAwait(false);
        Dictionary<long, string> employees = await _db.Set<Core.Payroll.Employee>()
            .Where(e => e.CompanyId == companyId)
            .ToDictionaryAsync(e => e.Id, e => e.FullName, ct).ConfigureAwait(false);

        return balances
            .OrderBy(b => employees.GetValueOrDefault(b.EmployeeId, string.Empty)).ThenBy(b => b.LeaveType)
            .Select(b => Row(new()
            {
                ["Employee"] = employees.GetValueOrDefault(b.EmployeeId, $"#{b.EmployeeId}"),
                ["LeaveType"] = b.LeaveType,
                ["Accrued"] = b.AccruedDays,
                ["Taken"] = b.TakenDays,
                ["Balance"] = b.BalanceDays,
            })).ToList();
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> EmployeeLoansAsync(long companyId, CancellationToken ct)
    {
        List<Core.Payroll.EmployeeLoan> loans = await _db.Set<Core.Payroll.EmployeeLoan>()
            .Where(l => l.CompanyId == companyId && l.Status == Core.Payroll.EmployeeLoanStatus.Active)
            .OrderByDescending(l => l.Outstanding)
            .ToListAsync(ct).ConfigureAwait(false);
        Dictionary<long, string> employees = await _db.Set<Core.Payroll.Employee>()
            .Where(e => e.CompanyId == companyId)
            .ToDictionaryAsync(e => e.Id, e => e.FullName, ct).ConfigureAwait(false);

        return loans.Select(l => Row(new()
        {
            ["Employee"] = employees.GetValueOrDefault(l.EmployeeId, $"#{l.EmployeeId}"),
            ["Reference"] = l.Reference,
            ["Principal"] = l.Principal,
            ["Outstanding"] = l.Outstanding,
            ["Installment"] = l.Installment,
        })).ToList();
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> EmployeeRegisterAsync(long companyId, CancellationToken ct)
    {
        List<Employee> employees = await _db.Set<Employee>()
            .Where(e => e.CompanyId == companyId)
            .OrderBy(e => e.EmployeeNumber)
            .ToListAsync(ct).ConfigureAwait(false);

        return employees.Select(e => Row(new()
        {
            ["EmployeeNumber"] = e.EmployeeNumber,
            ["FullName"] = e.FullName,
            ["Department"] = e.Department ?? string.Empty,
            ["CostCentre"] = e.CostCentre ?? string.Empty,
            ["Status"] = e.Status.ToString(),
            ["BasicSalary"] = e.BasicSalary,
        })).ToList();
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> PayrollDeductionsAsync(long companyId, IReadOnlyDictionary<string, object?> parameters, CancellationToken ct)
    {
        DateOnly from = ReportParameters.TryDate(parameters, "FromDate") ?? DateOnly.MinValue;
        DateOnly to = ReportParameters.TryDate(parameters, "ToDate") ?? DateOnly.MaxValue;
        List<Payslip> payslips = await _db.Set<Payslip>()
            .Include(p => p.Lines)
            .Where(p => p.CompanyId == companyId && p.PayDate >= from && p.PayDate <= to)
            .ToListAsync(ct).ConfigureAwait(false);

        // Aggregate deduction lines by element across all payslips in the period (statutory-return view).
        return payslips
            .SelectMany(p => p.Lines)
            .Where(l => l.Type == Core.Payroll.PayElementType.Deduction)
            .GroupBy(l => new { l.ElementCode, l.Description })
            .OrderBy(g => g.Key.ElementCode, StringComparer.Ordinal)
            .Select(g => Row(new()
            {
                ["ElementCode"] = g.Key.ElementCode,
                ["Description"] = g.Key.Description,
                ["Employees"] = g.Count(),
                ["Amount"] = g.Sum(l => l.Amount),
            })).ToList();
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> PayrollSummaryAsync(long companyId, IReadOnlyDictionary<string, object?> parameters, CancellationToken ct)
    {
        DateOnly from = ReportParameters.TryDate(parameters, "FromDate") ?? DateOnly.MinValue;
        DateOnly to = ReportParameters.TryDate(parameters, "ToDate") ?? DateOnly.MaxValue;
        List<Payslip> payslips = await _db.Set<Payslip>()
            .Where(p => p.CompanyId == companyId && p.PayDate >= from && p.PayDate <= to)
            .ToListAsync(ct).ConfigureAwait(false);
        Dictionary<long, string> employees = await _db.Set<Employee>()
            .Where(e => e.CompanyId == companyId)
            .ToDictionaryAsync(e => e.Id, e => $"{e.EmployeeNumber} — {e.FullName}", ct).ConfigureAwait(false);

        return payslips
            .OrderBy(p => employees.GetValueOrDefault(p.EmployeeId, string.Empty))
            .Select(p => Row(new()
            {
                ["Employee"] = employees.GetValueOrDefault(p.EmployeeId, $"#{p.EmployeeId}"),
                ["Gross"] = p.GrossEarnings,
                ["Deductions"] = p.TotalDeductions,
                ["Net"] = p.NetPay,
            })).ToList();
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> PropertyRegisterAsync(long companyId, CancellationToken ct)
    {
        List<Property> properties = await _db.Set<Property>()
            .Where(p => p.CompanyId == companyId)
            .OrderBy(p => p.StandNumber)
            .ToListAsync(ct).ConfigureAwait(false);
        Dictionary<long, string> ratepayers = await _db.Set<Customer>()
            .Where(c => c.CompanyId == companyId)
            .ToDictionaryAsync(c => c.Id, c => c.Name, ct).ConfigureAwait(false);

        return properties.Select(p => Row(new()
        {
            ["StandNumber"] = p.StandNumber,
            ["Ward"] = p.Ward,
            ["Ratepayer"] = ratepayers.GetValueOrDefault(p.RatepayerCustomerId, $"#{p.RatepayerCustomerId}"),
            ["Valuation"] = p.Valuation,
        })).ToList();
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> BudgetListAsync(long companyId, CancellationToken ct)
    {
        List<Budget> budgets = await _db.Set<Budget>().Include(b => b.Lines)
            .Where(b => b.CompanyId == companyId)
            .ToListAsync(ct).ConfigureAwait(false);

        return budgets
            .OrderBy(b => b.Label).ThenBy(b => b.VersionNumber)
            .Select(b => Row(new()
            {
                ["Label"] = b.Label,
                ["Type"] = b.Type.ToString(),
                ["Department"] = b.Department ?? string.Empty,
                ["Version"] = b.VersionNumber,
                ["Status"] = b.Status.ToString(),
                ["Active"] = b.IsActive ? "Yes" : string.Empty,
                ["AnnualTotal"] = b.AnnualTotal,
            })).ToList();
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> BudgetVsActualAsync(long companyId, IReadOnlyDictionary<string, object?> parameters, CancellationToken ct)
    {
        long fiscalYearId = ReportParameters.Long(parameters, "FiscalYearId");
        string department = ReportParameters.String(parameters, "Department");
        int period = (int)ReportParameters.Long(parameters, "Period");
        if (fiscalYearId == 0)
        {
            return [];
        }

        if (period is < 1 or > 12)
        {
            period = 12;
        }

        var result = await _budgeting.BudgetVsActualAsync(companyId, fiscalYearId, BudgetType.OriginalApproved, string.IsNullOrWhiteSpace(department) ? null : department, period, ct).ConfigureAwait(false);
        if (result.IsFailure)
        {
            return [];
        }

        return result.Value.Rows.Select(r => Row(new()
        {
            ["AccountCode"] = r.AccountCode,
            ["Budget"] = r.Budget,
            ["Actual"] = r.Actual,
            ["Variance"] = r.Variance,
        })).ToList();
    }

    private static IReadOnlyDictionary<string, object?> Row(Dictionary<string, object?> values) => values;
}
