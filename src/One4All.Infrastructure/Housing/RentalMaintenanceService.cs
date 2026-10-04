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

/// <summary>Repairs and inspections of council rental units, with costs and recharges through the ledger.</summary>
public sealed class RentalMaintenanceService : IRentalMaintenanceService
{
    private readonly One4AllDbContext _db;
    private readonly INumberSequenceService _numbers;
    private readonly ISalesLedgerService _ar;
    private readonly IPostingEngine _engine;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="numbers">The number sequences.</param>
    /// <param name="ar">The receivables ledger.</param>
    /// <param name="engine">The posting engine.</param>
    public RentalMaintenanceService(One4AllDbContext db, INumberSequenceService numbers, ISalesLedgerService ar, IPostingEngine engine)
    {
        _db = db;
        _numbers = numbers;
        _ar = ar;
        _engine = engine;
    }

    /// <inheritdoc />
    public async Task<Result<string>> ReportRepairAsync(long companyId, string unitCode, DateOnly reportedOn, string description, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            return Error.Validation("Hse.RepairDescription", "Describe the fault.");
        }

        Result<RentalUnit> unit = await FindUnitAsync(companyId, unitCode, ct).ConfigureAwait(false);
        if (unit.IsFailure)
        {
            return unit.Error;
        }

        long? tenancyId = await _db.Set<Tenancy>().Where(t => t.UnitId == unit.Value.Id && t.Status == TenancyStatus.Active)
            .Select(t => (long?)t.Id).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        Result<string> number = await _numbers.AllocateAsync(companyId, "RPR", reportedOn, ct: ct).ConfigureAwait(false);
        if (number.IsFailure)
        {
            return number.Error;
        }

        _db.Set<UnitRepair>().Add(new UnitRepair(companyId, number.Value, unit.Value.Id, tenancyId, reportedOn, description.Trim()));
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return number.Value;
    }

    /// <inheritdoc />
    public Task<Result<string?>> CompleteRepairAsync(RepairCompletion completion, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(completion);
        return _db.AtomicAsync(() => CompleteCoreAsync(completion, ct), ct);
    }

    private async Task<Result<string?>> CompleteCoreAsync(RepairCompletion completion, CancellationToken ct)
    {
        if (completion.Cost < 0m || (completion.Cost > 0m && (string.IsNullOrWhiteSpace(completion.ExpenseAccountCode) || string.IsNullOrWhiteSpace(completion.PaidFromAccountCode))))
        {
            return Error.Validation("Hse.RepairCost", "A repair cost cannot be negative and needs an expense account and the account it was paid from.");
        }

        if (completion.TenantCaused && completion.Cost > 0m && string.IsNullOrWhiteSpace(completion.RechargeAccountCode))
        {
            return Error.Validation("Hse.RechargeAccount", "A recharge to the tenant needs a recharge account.");
        }

        string number = (completion.RepairNumber ?? string.Empty).Trim();
        UnitRepair? repair = await _db.Set<UnitRepair>().FirstOrDefaultAsync(r => r.CompanyId == completion.CompanyId && r.RepairNumber == number, ct).ConfigureAwait(false);
        if (repair is null)
        {
            return Error.NotFound("Hse.RepairNotFound", $"Repair {number} not found.");
        }

        if (repair.Status != RepairStatus.Reported)
        {
            return Error.Rule("Hse.RepairClosed", $"Repair {number} is {repair.Status}.");
        }

        if (completion.TenantCaused && repair.TenancyId is null)
        {
            return Error.Rule("Hse.NoTenant", $"Repair {number} was reported while the unit was empty; there is no tenant to recharge.");
        }

        if (completion.Cost > 0m)
        {
            CurrencyCode currency = await _db.Set<Company>().Where(c => c.Id == completion.CompanyId).Select(c => c.BaseCurrency).FirstAsync(ct).ConfigureAwait(false);
            Result<PostingReceipt> costed = await _engine.PostAsync(new JournalRequest
            {
                CompanyId = completion.CompanyId, SourceModule = "GL", PostingDate = completion.Date, TransactionCurrency = currency, DocumentType = "UnitRepair",
                DocumentNumber = number, Narrative = $"Repair {number}: {repair.Description}",
                Lines =
                [
                    new JournalLineRequest { AccountCode = completion.ExpenseAccountCode.Trim(), Side = JournalSide.Debit, TransactionAmount = completion.Cost },
                    new JournalLineRequest { AccountCode = completion.PaidFromAccountCode.Trim(), Side = JournalSide.Credit, TransactionAmount = completion.Cost },
                ],
            }, ct).ConfigureAwait(false);
            if (costed.IsFailure)
            {
                return costed.Error;
            }
        }

        string? recharge = null;
        if (completion.TenantCaused && completion.Cost > 0m)
        {
            long tenantId = await _db.Set<Tenancy>().Where(t => t.Id == repair.TenancyId).Select(t => t.TenantCustomerId).FirstAsync(ct).ConfigureAwait(false);
            Customer tenant = await _db.Customers.AsNoTracking().FirstAsync(c => c.Id == tenantId, ct).ConfigureAwait(false);
            recharge = $"{number}-RC";
            Result<TradingPostingResult> invoiced = await _ar.PostInvoiceAsync(new SalesInvoiceRequest
            {
                CompanyId = completion.CompanyId, CustomerId = tenant.Id, DocumentNumber = recharge, DocumentDate = completion.Date, Currency = tenant.Currency,
                NetAmount = completion.Cost, SalesAccountCode = completion.RechargeAccountCode!.Trim(), Narrative = $"Recharge of tenant-caused repair {number}: {repair.Description}",
            }, ct).ConfigureAwait(false);
            if (invoiced.IsFailure)
            {
                return invoiced.Error;
            }
        }

        repair.Complete(completion.Date, completion.Cost, completion.TenantCaused, recharge);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result<string?>.Success(recharge);
    }

    /// <inheritdoc />
    public async Task<Result<long>> RecordInspectionAsync(long companyId, string unitCode, DateOnly date, string inspector, int conditionGrade, string findings, bool followUpRequired, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(inspector) || string.IsNullOrWhiteSpace(findings) || conditionGrade is < 1 or > 5)
        {
            return Error.Validation("Hse.InspectionDetails", "An inspection needs an inspector, findings and a condition grade from 1 to 5.");
        }

        Result<RentalUnit> unit = await FindUnitAsync(companyId, unitCode, ct).ConfigureAwait(false);
        if (unit.IsFailure)
        {
            return unit.Error;
        }

        UnitInspection inspection = new(companyId, unit.Value.Id, date, inspector.Trim(), conditionGrade, findings.Trim(), followUpRequired);
        _db.Set<UnitInspection>().Add(inspection);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return inspection.Id;
    }

    /// <inheritdoc />
    public async Task<Result> CloseFollowUpAsync(long companyId, long inspectionId, DateOnly date, CancellationToken ct = default)
    {
        UnitInspection? inspection = await _db.Set<UnitInspection>().FirstOrDefaultAsync(i => i.Id == inspectionId && i.CompanyId == companyId, ct).ConfigureAwait(false);
        if (inspection is null)
        {
            return Result.Failure(Error.NotFound("Hse.InspectionNotFound", $"Inspection {inspectionId} not found."));
        }

        if (!inspection.FollowUpRequired || inspection.FollowUpDoneOn is not null)
        {
            return Result.Failure(Error.Rule("Hse.NoFollowUp", "There is no open follow-up on this inspection."));
        }

        inspection.CloseFollowUp(date);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result<UnitMaintenanceHistory>> HistoryAsync(long companyId, string unitCode, CancellationToken ct = default)
    {
        Result<RentalUnit> unit = await FindUnitAsync(companyId, unitCode, ct).ConfigureAwait(false);
        if (unit.IsFailure)
        {
            return unit.Error;
        }

        long unitId = unit.Value.Id;
        var repairs = await _db.Set<UnitRepair>().AsNoTracking().Where(r => r.UnitId == unitId)
            .Select(r => new { r.Status, r.Cost, r.TenantCaused }).ToListAsync(ct).ConfigureAwait(false);
        var inspections = await _db.Set<UnitInspection>().AsNoTracking().Where(i => i.UnitId == unitId)
            .OrderByDescending(i => i.InspectedOn).ThenByDescending(i => i.Id)
            .Select(i => new { i.ConditionGrade, i.FollowUpRequired, i.FollowUpDoneOn }).ToListAsync(ct).ConfigureAwait(false);
        return new UnitMaintenanceHistory(
            unit.Value.Code,
            repairs.Count(r => r.Status == RepairStatus.Reported),
            repairs.Where(r => r.Status == RepairStatus.Completed).Sum(r => r.Cost),
            repairs.Where(r => r.Status == RepairStatus.Completed && r.TenantCaused).Sum(r => r.Cost),
            inspections.Count > 0 ? inspections[0].ConditionGrade : null,
            inspections.Count(i => i.FollowUpRequired && i.FollowUpDoneOn == null));
    }

    private async Task<Result<RentalUnit>> FindUnitAsync(long companyId, string unitCode, CancellationToken ct)
    {
        string code = (unitCode ?? string.Empty).Trim().ToUpperInvariant();
        RentalUnit? unit = await _db.Set<RentalUnit>().AsNoTracking().FirstOrDefaultAsync(u => u.CompanyId == companyId && u.Code == code, ct).ConfigureAwait(false);
        if (unit is null)
        {
            return Error.NotFound("Hse.UnitNotFound", $"Unit {code} not found.");
        }

        return unit;
    }
}
