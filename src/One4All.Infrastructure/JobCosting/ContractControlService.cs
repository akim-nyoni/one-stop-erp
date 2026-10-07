using System.Globalization;
using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.JobCosting;
using One4All.Core.Common;
using One4All.Core.JobCosting;
using One4All.Core.Payroll;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.JobCosting;

/// <summary>Runs variations, the bill of quantities and timesheets on contracts.</summary>
public sealed class ContractControlService : IContractControlService
{
    private readonly One4AllDbContext _db;
    private readonly Application.Abstractions.Inventory.IInventoryService _inventory;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="inventory">Issues materials to jobs.</param>
    public ContractControlService(One4AllDbContext db, Application.Abstractions.Inventory.IInventoryService inventory)
    {
        _db = db;
        _inventory = inventory;
    }

    private Task<JobContract?> ContractAsync(long companyId, long contractId, CancellationToken ct, bool withCosts = false)
        => (withCosts ? _db.JobContracts.Include(c => c.Costs) : _db.JobContracts.AsQueryable()).FirstOrDefaultAsync(c => c.Id == contractId && c.CompanyId == companyId, ct);

    /// <inheritdoc />
    public async Task<Result<long>> SubmitVariationAsync(long companyId, long contractId, string number, string description, decimal valueImpact, decimal costImpact, DateOnly date, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(number) || string.IsNullOrWhiteSpace(description))
        {
            return Error.Validation("Job.VariationInvalid", "A variation needs a number and a description.");
        }

        JobContract? contract = await ContractAsync(companyId, contractId, ct).ConfigureAwait(false);
        if (contract is null)
        {
            return Error.NotFound("Job.ContractNotFound", $"Contract {contractId} not found.");
        }

        if (contract.Status != JobStatus.Open)
        {
            return Error.Rule("Job.ContractNotOpen", $"Contract {contract.ContractNumber} is not open for variations.");
        }

        string normalised = number.Trim().ToUpperInvariant();
        if (await _db.VariationOrders.AnyAsync(v => v.JobContractId == contractId && v.Number == normalised, ct).ConfigureAwait(false))
        {
            return Error.Conflict("Job.VariationExists", $"Variation {normalised} already exists on {contract.ContractNumber}.");
        }

        VariationOrder variation = new(companyId, contractId, normalised, description.Trim(), valueImpact, costImpact, date);
        _db.VariationOrders.Add(variation);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return variation.Id;
    }

    /// <inheritdoc />
    public Task<Result> DecideVariationAsync(long companyId, long variationId, bool approve, DateOnly date, long? userId, string? note, CancellationToken ct = default)
        => _db.AtomicAsync(() => DecideCoreAsync(companyId, variationId, approve, date, userId, note, ct), ct);

    // One unit of work: the decision and the contract's new value and budget commit together.
    private async Task<Result> DecideCoreAsync(long companyId, long variationId, bool approve, DateOnly date, long? userId, string? note, CancellationToken ct)
    {
        VariationOrder? variation = await _db.VariationOrders.FirstOrDefaultAsync(v => v.Id == variationId && v.CompanyId == companyId, ct).ConfigureAwait(false);
        if (variation is null)
        {
            return Result.Failure(Error.NotFound("Job.VariationNotFound", $"Variation {variationId} not found."));
        }

        if (variation.Status != VariationStatus.Submitted)
        {
            return Result.Failure(Error.Rule("Job.VariationDecided", $"Variation {variation.Number} is already {variation.Status.ToString().ToLowerInvariant()}."));
        }

        if (date < variation.SubmittedOn)
        {
            return Result.Failure(Error.Validation("Job.DecisionBeforeSubmission", "A variation cannot be decided before it was submitted."));
        }

        if (approve)
        {
            JobContract contract = (await ContractAsync(companyId, variation.JobContractId, ct, withCosts: true).ConfigureAwait(false))!;
            if (contract.ContractValue + variation.ValueImpact < 0m)
            {
                return Result.Failure(Error.Rule("Job.VariationValue", "The variation would take the contract value below zero."));
            }

            Result revised = contract.ReviseBudget(contract.BudgetCost + variation.CostImpact);
            if (revised.IsFailure)
            {
                return revised;
            }

            contract.ContractValue += variation.ValueImpact;
        }

        variation.Decide(approve, date, userId, note?.Trim());
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result<long>> AddBoqItemAsync(long companyId, long contractId, string itemCode, string description, string unit, decimal quantity, decimal rate, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(itemCode) || string.IsNullOrWhiteSpace(description) || string.IsNullOrWhiteSpace(unit) || quantity <= 0m || rate < 0m)
        {
            return Error.Validation("Job.BoqInvalid", "A BOQ line needs a reference, description, unit, positive quantity and a non-negative rate.");
        }

        if (await ContractAsync(companyId, contractId, ct).ConfigureAwait(false) is null)
        {
            return Error.NotFound("Job.ContractNotFound", $"Contract {contractId} not found.");
        }

        string normalised = itemCode.Trim().ToUpperInvariant();
        if (await _db.BoqItems.AnyAsync(b => b.JobContractId == contractId && b.ItemCode == normalised, ct).ConfigureAwait(false))
        {
            return Error.Conflict("Job.BoqExists", $"BOQ item {normalised} already exists on the contract.");
        }

        BoqItem item = new(companyId, contractId, normalised, description.Trim(), unit.Trim(), quantity, rate);
        _db.BoqItems.Add(item);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return item.Id;
    }

    /// <inheritdoc />
    public async Task<Result<BoqImportPreview>> PreviewBoqImportAsync(long companyId, long contractId, string csv, CancellationToken ct = default)
    {
        if (await ContractAsync(companyId, contractId, ct).ConfigureAwait(false) is null)
        {
            return Error.NotFound("Job.ContractNotFound", $"Contract {contractId} not found.");
        }

        HashSet<string> existing = [.. await _db.BoqItems.Where(b => b.JobContractId == contractId).Select(b => b.ItemCode).ToListAsync(ct).ConfigureAwait(false)];
        HashSet<string> seen = new(StringComparer.Ordinal);
        List<BoqImportLine> lines = [];
        string[] rows = (csv ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        for (int n = 0; n < rows.Length; n++)
        {
            if (rows[n].Trim().Length == 0)
            {
                continue;
            }

            List<string> cells = SplitCsv(rows[n]);
            string Cell(int i) => i < cells.Count ? cells[i].Trim() : string.Empty;
            bool header = lines.Count == 0 && Cell(0).Equals("item", StringComparison.OrdinalIgnoreCase);
            if (header)
            {
                continue;
            }

            string code = Cell(0).ToUpperInvariant();
            decimal? quantity = decimal.TryParse(Cell(3), NumberStyles.Number, CultureInfo.InvariantCulture, out decimal q) ? q : null;
            decimal? rate = decimal.TryParse(Cell(4), NumberStyles.Number, CultureInfo.InvariantCulture, out decimal r) ? r : null;
            string? head = Cell(5).Length == 0 ? null : Cell(5);
            string? error = cells.Count < 5 ? "Needs item, description, unit, quantity and rate."
                : code.Length == 0 || Cell(1).Length == 0 || Cell(2).Length == 0 ? "Item, description and unit are required."
                : code.Length > 40 ? "The item reference is longer than 40 characters."
                : quantity is not > 0m ? $"Quantity '{Cell(3)}' must be a positive number."
                : rate is not >= 0m ? $"Rate '{Cell(4)}' must be a number of nil or more."
                : existing.Contains(code) ? $"Item {code} is already on the contract."
                : !seen.Add(code) ? $"Item {code} appears more than once in the file."
                : null;
            lines.Add(new BoqImportLine(n + 1, code, Cell(1), Cell(2), quantity, rate, head, error));
        }

        return new BoqImportPreview(lines);
    }

    /// <inheritdoc />
    public Task<Result<int>> ImportBoqAsync(long companyId, long contractId, string csv, CancellationToken ct = default)
        => _db.AtomicAsync(async () =>
        {
            Result<BoqImportPreview> preview = await PreviewBoqImportAsync(companyId, contractId, csv, ct).ConfigureAwait(false);
            if (preview.IsFailure)
            {
                return preview.Error;
            }

            if (preview.Value.Lines.Count == 0)
            {
                return Error.Validation("Job.BoqEmpty", "The BOQ file has no lines.");
            }

            if (preview.Value.ErrorCount > 0)
            {
                BoqImportLine first = preview.Value.Lines.First(l => l.Error is not null);
                return Error.Validation("Job.BoqErrors", $"{preview.Value.ErrorCount} line(s) have errors — first, line {first.Line}: {first.Error}");
            }

            foreach (BoqImportLine line in preview.Value.Lines)
            {
                _db.BoqItems.Add(new BoqItem(companyId, contractId, line.ItemCode, line.Description, line.Unit, line.Quantity!.Value, line.Rate!.Value) { CostHead = line.CostHead });
            }

            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            return Result<int>.Success(preview.Value.Lines.Count);
        }, ct);

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<BoqCostHeadRow>>> BoqByCostHeadAsync(long companyId, long contractId, CancellationToken ct = default)
    {
        if (await ContractAsync(companyId, contractId, ct).ConfigureAwait(false) is null)
        {
            return Error.NotFound("Job.ContractNotFound", $"Contract {contractId} not found.");
        }

        const string none = "(none)";
        List<BoqItem> items = await _db.BoqItems.AsNoTracking().Where(b => b.JobContractId == contractId).ToListAsync(ct).ConfigureAwait(false);
        Dictionary<string, decimal> actual = await _db.Set<JobCostEntry>().AsNoTracking().Where(e => e.JobContractId == contractId)
            .GroupBy(e => e.CostHead).Select(g => new { g.Key, Total = g.Sum(e => e.Amount) }).ToDictionaryAsync(x => x.Key, x => x.Total, StringComparer.OrdinalIgnoreCase, ct).ConfigureAwait(false);
        IEnumerable<string> heads = items.Select(b => b.CostHead ?? none).Concat(actual.Keys).Distinct(StringComparer.OrdinalIgnoreCase);
        List<BoqCostHeadRow> rows = [.. heads.OrderBy(h => h, StringComparer.OrdinalIgnoreCase).Select(head =>
        {
            List<BoqItem> under = [.. items.Where(b => string.Equals(b.CostHead ?? none, head, StringComparison.OrdinalIgnoreCase))];
            return new BoqCostHeadRow(head, under.Sum(b => b.Value), under.Sum(b => decimal.Round(b.MeasuredQuantity * b.Rate, 2, MidpointRounding.AwayFromZero)), actual.GetValueOrDefault(head));
        })];
        return rows;
    }

    // Splits a CSV row, honouring double-quoted fields (with "" for a quote inside one).
    private static List<string> SplitCsv(string row)
    {
        List<string> cells = [];
        System.Text.StringBuilder cell = new();
        bool quoted = false;
        for (int i = 0; i < row.Length; i++)
        {
            char ch = row[i];
            if (quoted)
            {
                if (ch == '"' && i + 1 < row.Length && row[i + 1] == '"')
                {
                    cell.Append('"');
                    i++;
                }
                else if (ch == '"')
                {
                    quoted = false;
                }
                else
                {
                    cell.Append(ch);
                }
            }
            else if (ch == '"')
            {
                quoted = true;
            }
            else if (ch == ',')
            {
                cells.Add(cell.ToString());
                cell.Clear();
            }
            else
            {
                cell.Append(ch);
            }
        }

        cells.Add(cell.ToString());
        return cells;
    }

    /// <inheritdoc />
    public async Task<Result> MeasureAsync(long companyId, long boqItemId, decimal quantityToDate, DateOnly date, CancellationToken ct = default)
    {
        if (quantityToDate < 0m)
        {
            return Result.Failure(Error.Validation("Job.MeasureNegative", "A measured quantity cannot be negative."));
        }

        BoqItem? item = await _db.BoqItems.FirstOrDefaultAsync(b => b.Id == boqItemId && b.CompanyId == companyId, ct).ConfigureAwait(false);
        if (item is null)
        {
            return Result.Failure(Error.NotFound("Job.BoqNotFound", $"BOQ item {boqItemId} not found."));
        }

        if (item.MeasuredOn is DateOnly last && date < last)
        {
            return Result.Failure(Error.Validation("Job.MeasureOutOfOrder", $"{item.ItemCode} was last measured on {last:yyyy-MM-dd}; a measurement cannot be earlier."));
        }

        item.Measure(quantityToDate, date);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public Task<Result<decimal>> IssueMaterialsAsync(long companyId, long contractId, long itemId, long warehouseId, DateOnly date, decimal quantity, string workInProgressAccountCode, CancellationToken ct = default)
        => _db.AtomicAsync(() => IssueMaterialsCoreAsync(companyId, contractId, itemId, warehouseId, date, quantity, workInProgressAccountCode, ct), ct);

    // One unit of work: the stock issue to WIP and the job's materials cost commit together.
    private async Task<Result<decimal>> IssueMaterialsCoreAsync(long companyId, long contractId, long itemId, long warehouseId, DateOnly date, decimal quantity, string workInProgressAccountCode, CancellationToken ct)
    {
        if (quantity <= 0m || string.IsNullOrWhiteSpace(workInProgressAccountCode))
        {
            return Error.Validation("Job.MaterialsInvalid", "A materials issue needs a positive quantity and the contract's WIP account.");
        }

        JobContract? contract = await ContractAsync(companyId, contractId, ct, withCosts: true).ConfigureAwait(false);
        if (contract is null)
        {
            return Error.NotFound("Job.ContractNotFound", $"Contract {contractId} not found.");
        }

        if (contract.Status != JobStatus.Open)
        {
            return Error.Rule("Job.ContractNotOpen", $"Contract {contract.ContractNumber} is not open.");
        }

        Result<Application.Abstractions.Inventory.StockMovementResult> issue = await _inventory.IssueAsync(new Application.Abstractions.Inventory.IssueStockRequest
        {
            CompanyId = companyId, ItemId = itemId, WarehouseId = warehouseId, Date = date, Quantity = quantity, Reference = contract.ContractNumber, ChargeAccountCode = workInProgressAccountCode,
        }, ct).ConfigureAwait(false);
        if (issue.IsFailure)
        {
            return issue.Error;
        }

        string itemCode = await _db.Items.Where(i => i.Id == itemId).Select(i => i.Code).SingleAsync(ct).ConfigureAwait(false);
        await JobCosts.CaptureAsync(_db, contract, "Materials", JobCostSource.StockIssue, issue.Value.Value, date, $"{itemCode} × {quantity:0.###}", ct).ConfigureAwait(false);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return issue.Value.Value;
    }

    /// <inheritdoc />
    public Task<Result<long>> RecordTimeAsync(long companyId, long contractId, long employeeId, DateOnly date, decimal hours, decimal hourlyCost, string? costHead = null, CancellationToken ct = default)
        => _db.AtomicAsync(() => RecordTimeCoreAsync(companyId, contractId, employeeId, date, hours, hourlyCost, string.IsNullOrWhiteSpace(costHead) ? "Labour" : costHead.Trim(), ct), ct);

    // One unit of work: the timesheet and the job cost it charges commit together.
    private async Task<Result<long>> RecordTimeCoreAsync(long companyId, long contractId, long employeeId, DateOnly date, decimal hours, decimal hourlyCost, string costHead, CancellationToken ct)
    {
        if (hours <= 0m || hours > 24m || hourlyCost < 0m)
        {
            return Error.Validation("Job.TimeInvalid", "Hours must be more than 0 and at most 24 a day, at a non-negative rate.");
        }

        JobContract? contract = await ContractAsync(companyId, contractId, ct, withCosts: true).ConfigureAwait(false);
        if (contract is null)
        {
            return Error.NotFound("Job.ContractNotFound", $"Contract {contractId} not found.");
        }

        Employee? employee = await _db.Set<Employee>().AsNoTracking().FirstOrDefaultAsync(e => e.Id == employeeId && e.CompanyId == companyId, ct).ConfigureAwait(false);
        if (employee is null)
        {
            return Error.NotFound("Job.EmployeeNotFound", $"Employee {employeeId} not found.");
        }

        decimal booked = await _db.JobTimesheets.Where(t => t.EmployeeId == employeeId && t.Date == date).SumAsync(t => t.Hours, ct).ConfigureAwait(false);
        if (booked + hours > 24m)
        {
            return Error.Rule("Job.TimeOverbooked", $"{employee.EmployeeNumber} already has {booked:0.##} hours booked on {date:yyyy-MM-dd}.");
        }

        JobTimesheet sheet = new(companyId, contractId, employeeId, date, hours, hourlyCost, costHead);
        _db.JobTimesheets.Add(sheet);
        await JobCosts.CaptureAsync(_db, contract, costHead, JobCostSource.Labour, sheet.Cost, date, $"TS {employee.EmployeeNumber}", ct).ConfigureAwait(false);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return sheet.Id;
    }
}
