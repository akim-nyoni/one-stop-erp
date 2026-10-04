using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Housing;
using One4All.Application.Abstractions.Trading;
using One4All.Application.Housing;
using One4All.Core.Common;
using One4All.Core.Housing;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Housing;

/// <summary>Sectional title: units, quotas, unit transfers and body corporate levies.</summary>
public sealed class SectionalTitleService : ISectionalTitleService
{
    private readonly One4AllDbContext _db;
    private readonly ISalesLedgerService _ar;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="ar">The sales ledger.</param>
    public SectionalTitleService(One4AllDbContext db, ISalesLedgerService ar)
    {
        _db = db;
        _ar = ar;
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<SectionalUnitEntry>>> RegisterAsync(long companyId, string code, string name, long standId, IReadOnlyList<UnitArea> units, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(units);
        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(name) || units.Count == 0 || units.Any(u => u.FloorArea <= 0m || string.IsNullOrWhiteSpace(u.UnitNumber))
            || units.Select(u => u.UnitNumber.Trim().ToUpperInvariant()).Distinct().Count() != units.Count)
        {
            return Error.Validation("Hse.SectionalScheme", "A scheme needs a code, a name and units, each numbered once with a positive floor area.");
        }

        string key = code.Trim().ToUpperInvariant();
        if (!await _db.HousingStands.AnyAsync(s => s.Id == standId && s.CompanyId == companyId, ct).ConfigureAwait(false))
        {
            return Error.NotFound("Hse.StandNotFound", $"Stand {standId} not found.");
        }

        if (await _db.Set<SectionalScheme>().AnyAsync(s => s.CompanyId == companyId && (s.Code == key || s.StandId == standId), ct).ConfigureAwait(false))
        {
            return Error.Conflict("Hse.SectionalExists", $"Scheme {key} already exists, or the stand is already divided.");
        }

        List<UnitArea> normalised = [.. units.Select(u => u with { UnitNumber = u.UnitNumber.Trim().ToUpperInvariant() })];
        IReadOnlyList<UnitAmount> quotas = SectionalQuotas.FromFloorAreas(normalised);
        SectionalScheme scheme = new(companyId, key, name.Trim(), standId);
        for (int i = 0; i < normalised.Count; i++)
        {
            scheme.AddUnit(new SectionalUnit(normalised[i].UnitNumber, normalised[i].FloorArea, quotas[i].Amount));
        }

        _db.Set<SectionalScheme>().Add(scheme);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return await UnitsAsync(companyId, key, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<Result> TransferUnitAsync(long companyId, string code, string unitNumber, long toCustomerId, decimal consideration, DateOnly date, CancellationToken ct = default)
    {
        if (consideration < 0m)
        {
            return Result.Failure(Error.Validation("Hse.UnitTransfer", "The consideration cannot be negative."));
        }

        Result<SectionalScheme> found = await FindAsync(companyId, code, ct).ConfigureAwait(false);
        if (found.IsFailure)
        {
            return Result.Failure(found.Error);
        }

        SectionalUnit? unit = found.Value.Units.FirstOrDefault(u => u.UnitNumber == (unitNumber ?? string.Empty).Trim().ToUpperInvariant());
        if (unit is null)
        {
            return Result.Failure(Error.NotFound("Hse.UnitNotFound", $"Unit {unitNumber} is not in {found.Value.Code}."));
        }

        if (!await _db.Customers.AnyAsync(c => c.Id == toCustomerId && c.CompanyId == companyId, ct).ConfigureAwait(false))
        {
            return Result.Failure(Error.NotFound("Hse.OwnerNotFound", $"Customer {toCustomerId} not found."));
        }

        if (unit.CurrentOwnerCustomerId == toCustomerId)
        {
            return Result.Failure(Error.Rule("Hse.SameOwner", $"Unit {unit.UnitNumber} already belongs to that owner."));
        }

        UnitOwnership? current = unit.Owners.FirstOrDefault(o => o.OwnedTo is null);
        if (current is not null && date <= current.OwnedFrom)
        {
            return Result.Failure(Error.Validation("Hse.UnitTransferDate", "A transfer must be dated after the current owner's ownership began."));
        }

        if (await StandClaims.AnyBlockedAsync(_db, [found.Value.StandId], ct).ConfigureAwait(false))
        {
            return Result.Failure(Error.Rule("Hse.StandBlocked", "The building's stand is blocked by a caveat or an open land dispute; no unit can be transferred."));
        }

        unit.Transfer(toCustomerId, date, consideration);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<SectionalUnitEntry>>> UnitsAsync(long companyId, string code, CancellationToken ct = default)
    {
        Result<SectionalScheme> found = await FindAsync(companyId, code, ct).ConfigureAwait(false);
        if (found.IsFailure)
        {
            return found.Error;
        }

        List<long> ownerIds = [.. found.Value.Units.Select(u => u.CurrentOwnerCustomerId).OfType<long>()];
        Dictionary<long, string> names = await _db.Customers.Where(c => ownerIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Name, ct).ConfigureAwait(false);
        return Result<IReadOnlyList<SectionalUnitEntry>>.Success([.. found.Value.Units.OrderBy(u => u.UnitNumber, StringComparer.Ordinal)
            .Select(u => new SectionalUnitEntry(u.UnitNumber, u.FloorArea, u.ParticipationQuota, u.CurrentOwnerCustomerId is long id ? names[id] : null))]);
    }

    /// <inheritdoc />
    public Task<Result<SectionalLevyResult>> RaiseLevyAsync(long companyId, string code, int period, decimal levy, string incomeAccountCode, DateOnly date, CancellationToken ct = default)
        => _db.AtomicAsync(() => RaiseLevyCoreAsync(companyId, code, period, levy, incomeAccountCode, date, ct), ct);

    private async Task<Result<SectionalLevyResult>> RaiseLevyCoreAsync(long companyId, string code, int period, decimal levy, string incomeAccountCode, DateOnly date, CancellationToken ct)
    {
        if (levy <= 0m || period % 100 is < 1 or > 12 || period / 100 < 2000 || string.IsNullOrWhiteSpace(incomeAccountCode))
        {
            return Error.Validation("Hse.SectionalLevy", "A levy needs a positive amount, a period as yyyyMM and its income account.");
        }

        Result<SectionalScheme> found = await FindAsync(companyId, code, ct).ConfigureAwait(false);
        if (found.IsFailure)
        {
            return found.Error;
        }

        SectionalScheme scheme = found.Value;
        if (await _db.Set<SectionalLevyRun>().AnyAsync(r => r.SectionalSchemeId == scheme.Id && r.Period == period, ct).ConfigureAwait(false))
        {
            return Error.Conflict("Hse.LevyRaised", $"The {period} levy for {scheme.Code} has already been raised.");
        }

        List<SectionalUnit> units = [.. scheme.Units.OrderBy(u => u.UnitNumber, StringComparer.Ordinal)];
        IReadOnlyList<UnitAmount> split = SectionalQuotas.SplitLevy([.. units.Select(u => new UnitAmount(u.UnitNumber, u.ParticipationQuota))], levy);
        List<UnitAmount> billed = [];
        decimal unbilled = 0m;
        for (int i = 0; i < units.Count; i++)
        {
            if (units[i].CurrentOwnerCustomerId is not long ownerId)
            {
                unbilled += split[i].Amount;
                continue;
            }

            Customer owner = await _db.Customers.AsNoTracking().FirstAsync(c => c.Id == ownerId, ct).ConfigureAwait(false);
            Result<TradingPostingResult> invoiced = await _ar.PostInvoiceAsync(new SalesInvoiceRequest
            {
                CompanyId = companyId, CustomerId = ownerId, DocumentNumber = $"BC-{scheme.Code}-{units[i].UnitNumber}-{period}", DocumentDate = date, Currency = owner.Currency,
                NetAmount = split[i].Amount, SalesAccountCode = incomeAccountCode.Trim(), Narrative = $"{scheme.Name} body corporate levy {period}, unit {units[i].UnitNumber}",
            }, ct).ConfigureAwait(false);
            if (invoiced.IsFailure)
            {
                return invoiced.Error;
            }

            billed.Add(split[i]);
        }

        _db.Set<SectionalLevyRun>().Add(new SectionalLevyRun(companyId, scheme.Id, period, levy, levy - unbilled, date));
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return new SectionalLevyResult(period, billed, unbilled);
    }

    private async Task<Result<SectionalScheme>> FindAsync(long companyId, string code, CancellationToken ct)
    {
        string key = (code ?? string.Empty).Trim().ToUpperInvariant();
        SectionalScheme? scheme = await _db.Set<SectionalScheme>().Include(s => s.Units).ThenInclude(u => u.Owners)
            .FirstOrDefaultAsync(s => s.CompanyId == companyId && s.Code == key, ct).ConfigureAwait(false);
        if (scheme is null)
        {
            return Error.NotFound("Hse.SectionalNotFound", $"Sectional scheme {key} not found.");
        }

        return scheme;
    }
}
