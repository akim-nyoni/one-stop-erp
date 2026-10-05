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

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    public ContractControlService(One4AllDbContext db) => _db = db;

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
        contract.CaptureCost(costHead, JobCostSource.Labour, sheet.Cost, date, $"TS {employee.EmployeeNumber}");
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return sheet.Id;
    }
}
