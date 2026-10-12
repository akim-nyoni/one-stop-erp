using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Accounting;
using One4All.Application.Abstractions.Housing;
using One4All.Application.Abstractions.Numbering;
using One4All.Application.Abstractions.Trading;
using One4All.Application.Housing;
using One4All.Application.Municipal;
using One4All.Core.Accounting;
using One4All.Core.Common;
using One4All.Core.Housing;
using One4All.Core.System;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Housing;

/// <summary>Council rental housing: units, tenancies, deposits, the rent run and voids, posting through the ledger.</summary>
public sealed class RentalHousingService : IRentalHousingService
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
    public RentalHousingService(One4AllDbContext db, INumberSequenceService numbers, ISalesLedgerService ar, IPostingEngine engine)
    {
        _db = db;
        _numbers = numbers;
        _ar = ar;
        _engine = engine;
    }

    /// <inheritdoc />
    public async Task<Result<long>> AddUnitAsync(long companyId, string code, string description, RentalUnitType unitType, int rooms, decimal monthlyRent, string rentAccountCode, DateOnly vacantSince, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(description) || rooms < 0 || monthlyRent < 0m || string.IsNullOrWhiteSpace(rentAccountCode))
        {
            return Error.Validation("Hse.UnitDetails", "A unit needs a code, a description, a rent of zero or more and a rental income account.");
        }

        string unitCode = code.Trim().ToUpperInvariant();
        if (await _db.Set<RentalUnit>().AnyAsync(u => u.CompanyId == companyId && u.Code == unitCode, ct).ConfigureAwait(false))
        {
            return Error.Conflict("Hse.UnitExists", $"Unit {unitCode} already exists.");
        }

        RentalUnit unit = new(companyId, unitCode, description.Trim(), unitType, rooms, monthlyRent, rentAccountCode.Trim(), vacantSince);
        _db.Set<RentalUnit>().Add(unit);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return unit.Id;
    }

    /// <inheritdoc />
    public Task<Result<string>> LetAsync(LetUnitRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return _db.AtomicAsync(() => LetCoreAsync(request, ct), ct);
    }

    private async Task<Result<string>> LetCoreAsync(LetUnitRequest request, CancellationToken ct)
    {
        if (request.Deposit < 0m || (request.Deposit > 0m && (string.IsNullOrWhiteSpace(request.BankAccountCode) || string.IsNullOrWhiteSpace(request.DepositAccountCode))))
        {
            return Error.Validation("Hse.LetDetails", "A deposit cannot be negative and needs a bank account and a deposit liability account.");
        }

        string unitCode = (request.UnitCode ?? string.Empty).Trim().ToUpperInvariant();
        RentalUnit? unit = await _db.Set<RentalUnit>().FirstOrDefaultAsync(u => u.CompanyId == request.CompanyId && u.Code == unitCode, ct).ConfigureAwait(false);
        if (unit is null)
        {
            return Error.NotFound("Hse.UnitNotFound", $"Unit {unitCode} not found.");
        }

        if (unit.IsLet || await _db.Set<Tenancy>().AnyAsync(t => t.UnitId == unit.Id && t.Status == TenancyStatus.Active, ct).ConfigureAwait(false))
        {
            return Error.Conflict("Hse.UnitLet", $"Unit {unitCode} is already let.");
        }

        Customer? tenant = await _db.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.Id == request.TenantCustomerId && c.CompanyId == request.CompanyId, ct).ConfigureAwait(false);
        if (tenant is null)
        {
            return Error.NotFound("Hse.TenantNotFound", $"Customer {request.TenantCustomerId} not found.");
        }

        Result<string> number = await _numbers.AllocateAsync(request.CompanyId, "TN", request.StartDate, ct: ct).ConfigureAwait(false);
        if (number.IsFailure)
        {
            return number.Error;
        }

        if (request.Deposit > 0m)
        {
            Result<PostingReceipt> banked = await PostAsync(request.CompanyId, request.StartDate, $"{number.Value}-DEP", $"Rental deposit on unit {unit.Code}",
                request.BankAccountCode.Trim(), request.DepositAccountCode.Trim(), request.Deposit, ct).ConfigureAwait(false);
            if (banked.IsFailure)
            {
                return banked.Error;
            }
        }

        _db.Set<Tenancy>().Add(new Tenancy(request.CompanyId, number.Value, unit.Id, tenant.Id, request.StartDate, unit.MonthlyRent, request.Deposit,
            string.IsNullOrWhiteSpace(request.DepositAccountCode) ? string.Empty : request.DepositAccountCode.Trim()));
        unit.Let(request.StartDate);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return number.Value;
    }

    /// <inheritdoc />
    public Task<Result<RentRunResult>> RunRentAsync(long companyId, DateOnly month, CancellationToken ct = default)
        => _db.AtomicAsync(() => RunCoreAsync(companyId, month, ct), ct);

    private async Task<Result<RentRunResult>> RunCoreAsync(long companyId, DateOnly month, CancellationToken ct)
    {
        (DateOnly first, DateOnly last) = ProRata.Month(month);
        List<Tenancy> tenancies = await _db.Set<Tenancy>().Include(t => t.RentChanges)
            .Where(t => t.CompanyId == companyId && t.StartDate <= last && (t.EndDate == null || t.EndDate >= first))
            .OrderBy(t => t.TenancyNumber).ToListAsync(ct).ConfigureAwait(false);
        int billed = 0;
        decimal total = 0m;
        foreach (Tenancy tenancy in tenancies)
        {
            Result<decimal> rent = await BillMonthAsync(tenancy, month, ct).ConfigureAwait(false);
            if (rent.IsFailure)
            {
                return rent.Error;
            }

            if (rent.Value > 0m)
            {
                billed++;
                total += rent.Value;
            }
        }

        return new RentRunResult(billed, total);
    }

    /// <inheritdoc />
    public Task<Result<TenancyEnded>> EndTenancyAsync(long companyId, string tenancyNumber, DateOnly endDate, string refundBankAccountCode, CancellationToken ct = default)
        => _db.AtomicAsync(() => EndCoreAsync(companyId, tenancyNumber, endDate, refundBankAccountCode, ct), ct);

    private async Task<Result<TenancyEnded>> EndCoreAsync(long companyId, string tenancyNumber, DateOnly endDate, string refundBankAccountCode, CancellationToken ct)
    {
        string number = (tenancyNumber ?? string.Empty).Trim();
        Tenancy? tenancy = await _db.Set<Tenancy>().Include(t => t.RentChanges).Include(t => t.Occupants).FirstOrDefaultAsync(t => t.CompanyId == companyId && t.TenancyNumber == number, ct)
            .ConfigureAwait(false);
        if (tenancy is null)
        {
            return Error.NotFound("Hse.TenancyNotFound", $"Tenancy {number} not found.");
        }

        if (tenancy.Status != TenancyStatus.Active)
        {
            return Error.Rule("Hse.TenancyEnded", $"Tenancy {number} has already ended.");
        }

        if (endDate < tenancy.StartDate)
        {
            return Error.Validation("Hse.EndDate", "A tenancy cannot end before it starts.");
        }

        // Final rent: every month from the start to the end not yet billed, the last one pro rata.
        tenancy.End(endDate);
        decimal finalRent = 0m;
        for (DateOnly month = new(tenancy.StartDate.Year, tenancy.StartDate.Month, 1); month <= endDate; month = month.AddMonths(1))
        {
            Result<decimal> rent = await BillMonthAsync(tenancy, month, ct).ConfigureAwait(false);
            if (rent.IsFailure)
            {
                return rent.Error;
            }

            finalRent += rent.Value;
        }

        // The deposit: applied to what the tenant owes (a receipt funded from the deposit liability), the rest refunded.
        var items = await _db.OpenItems.Where(i => i.CompanyId == companyId && i.Ledger == TradingLedger.AccountsReceivable && i.TraderId == tenancy.TenantCustomerId && i.OutstandingBase != 0m)
            .Select(i => new { i.Sign, i.OutstandingBase }).ToListAsync(ct).ConfigureAwait(false);
        decimal owed = items.Sum(i => i.Sign == LedgerSign.Debit ? i.OutstandingBase : -i.OutstandingBase);
        DepositSettlement settlement = RentalArithmetic.Settle(tenancy.Deposit, owed);
        if (settlement.AppliedToArrears > 0m)
        {
            Customer tenant = await _db.Customers.AsNoTracking().FirstAsync(c => c.Id == tenancy.TenantCustomerId, ct).ConfigureAwait(false);
            Result<TradingPostingResult> applied = await _ar.PostReceiptAsync(new ReceiptRequest
            {
                CompanyId = companyId, CustomerId = tenant.Id, DocumentNumber = $"{tenancy.TenancyNumber}-DA", ReceiptDate = endDate, Currency = tenant.Currency,
                Amount = settlement.AppliedToArrears, BankAccountCode = tenancy.DepositAccountCode, Narrative = $"Deposit applied on ending tenancy {tenancy.TenancyNumber}",
            }, ct).ConfigureAwait(false);
            if (applied.IsFailure)
            {
                return applied.Error;
            }
        }

        if (settlement.Refund > 0m)
        {
            if (string.IsNullOrWhiteSpace(refundBankAccountCode))
            {
                return Error.Validation("Hse.RefundBank", "A deposit refund needs a bank account.");
            }

            Result<PostingReceipt> refunded = await PostAsync(companyId, endDate, $"{tenancy.TenancyNumber}-REF", $"Deposit refund on ending tenancy {tenancy.TenancyNumber}",
                tenancy.DepositAccountCode, refundBankAccountCode.Trim(), settlement.Refund, ct).ConfigureAwait(false);
            if (refunded.IsFailure)
            {
                return refunded.Error;
            }
        }

        // Lodger permits end with the tenancy.
        foreach (LodgerPermit permit in await _db.Set<LodgerPermit>().Where(p => p.TenancyId == tenancy.Id && p.Status == LodgerPermitStatus.Active).ToListAsync(ct).ConfigureAwait(false))
        {
            if (permit.LastDay > endDate)
            {
                permit.Revoke(endDate, $"Tenancy {tenancy.TenancyNumber} ended");
            }
        }

        RentalUnit unit = await _db.Set<RentalUnit>().FirstAsync(u => u.Id == tenancy.UnitId, ct).ConfigureAwait(false);
        unit.Vacate(endDate.AddDays(1));
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return new TenancyEnded(finalRent, settlement);
    }

    /// <inheritdoc />
    public async Task<Result> SetRentTariffAsync(long companyId, RentalUnitType unitType, string? suburb, RentIncomeCategory? category, DateOnly effectiveFrom, decimal monthlyRent,
        CancellationToken ct = default)
    {
        if (effectiveFrom.Day != 1 || monthlyRent < 0m || !Enum.IsDefined(unitType) || category is RentIncomeCategory c && !Enum.IsDefined(c))
        {
            return Result.Failure(Error.Validation("Hse.RentTariff", "A rent tariff starts on the first of a month, for a known unit type and category, and cannot be negative."));
        }

        string? place = string.IsNullOrWhiteSpace(suburb) ? null : suburb.Trim();
        RentTariff? existing = await _db.Set<RentTariff>().FirstOrDefaultAsync(t => t.CompanyId == companyId && t.UnitType == unitType && t.Suburb == place
            && t.IncomeCategory == category && t.EffectiveFrom == effectiveFrom, ct).ConfigureAwait(false);
        if (existing is null)
        {
            _db.Set<RentTariff>().Add(new RentTariff(companyId, unitType, place, category, effectiveFrom, monthlyRent));
        }
        else
        {
            existing.MonthlyRent = monthlyRent;
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result> SetUnitSuburbAsync(long companyId, string unitCode, string? suburb, CancellationToken ct = default)
    {
        string code = (unitCode ?? string.Empty).Trim().ToUpperInvariant();
        RentalUnit? unit = await _db.Set<RentalUnit>().FirstOrDefaultAsync(u => u.CompanyId == companyId && u.Code == code, ct).ConfigureAwait(false);
        if (unit is null)
        {
            return Result.Failure(Error.NotFound("Hse.UnitNotFound", $"Rental unit {code} was not found."));
        }

        unit.Suburb = string.IsNullOrWhiteSpace(suburb) ? null : suburb.Trim();
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result> SetIncomeCategoryAsync(long companyId, string tenancyNumber, RentIncomeCategory? category, CancellationToken ct = default)
    {
        string number = (tenancyNumber ?? string.Empty).Trim();
        Tenancy? tenancy = await _db.Set<Tenancy>().FirstOrDefaultAsync(t => t.CompanyId == companyId && t.TenancyNumber == number, ct).ConfigureAwait(false);
        if (tenancy is null)
        {
            return Result.Failure(Error.NotFound("Hse.TenancyNotFound", $"Tenancy {number} was not found."));
        }

        tenancy.IncomeCategory = category;
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public Task<Result<RentReviewResult>> ApplyRentTariffsAsync(long companyId, DateOnly effectiveFrom, CancellationToken ct = default)
        => _db.AtomicAsync(async () =>
        {
            if (effectiveFrom.Day != 1)
            {
                return Error.Validation("Hse.RentReview", "A rent review takes effect from the first of a month.");
            }

            List<RentTariffRate> rates = await _db.Set<RentTariff>().AsNoTracking().Where(t => t.CompanyId == companyId && t.EffectiveFrom <= effectiveFrom)
                .Select(t => new RentTariffRate(t.UnitType, t.Suburb, t.IncomeCategory, t.EffectiveFrom, t.MonthlyRent)).ToListAsync(ct).ConfigureAwait(false);
            if (rates.Count == 0)
            {
                return Error.Validation("Hse.NoTariffs", $"No rent tariff is in force on {effectiveFrom:yyyy-MM-dd}.");
            }

            Dictionary<long, RentalUnit> units = await _db.Set<RentalUnit>().Where(u => u.CompanyId == companyId).ToDictionaryAsync(u => u.Id, ct).ConfigureAwait(false);
            int unitsChanged = 0;
            foreach (RentalUnit unit in units.Values)
            {
                if (RentTariffs.Rent(rates, unit.UnitType, unit.Suburb, null, effectiveFrom) is decimal asking && asking != unit.MonthlyRent)
                {
                    unit.MonthlyRent = asking;
                    unitsChanged++;
                }
            }

            List<Tenancy> running = await _db.Set<Tenancy>().Include(t => t.RentChanges)
                .Where(t => t.CompanyId == companyId && t.Status == TenancyStatus.Active).ToListAsync(ct).ConfigureAwait(false);
            int tenanciesChanged = 0;
            foreach (Tenancy tenancy in running.Where(t => t.RentChanges.All(c => c.EffectiveFrom != effectiveFrom)))
            {
                RentalUnit unit = units[tenancy.UnitId];
                if (RentTariffs.Rent(rates, unit.UnitType, unit.Suburb, tenancy.IncomeCategory, effectiveFrom) is decimal rent && rent != tenancy.RentOn(effectiveFrom))
                {
                    tenancy.ChangeRent(effectiveFrom, rent);
                    tenanciesChanged++;
                }
            }

            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            return Result<RentReviewResult>.Success(new RentReviewResult(unitsChanged, tenanciesChanged));
        }, ct);

    /// <inheritdoc />
    public Task<Result<RentReviewResult>> ReviewRentsAsync(long companyId, DateOnly effectiveFrom, decimal percent, RentalUnitType? unitType, CancellationToken ct = default)
        => _db.AtomicAsync(() => ReviewCoreAsync(companyId, effectiveFrom, percent, unitType, ct), ct);

    private async Task<Result<RentReviewResult>> ReviewCoreAsync(long companyId, DateOnly effectiveFrom, decimal percent, RentalUnitType? unitType, CancellationToken ct)
    {
        if (effectiveFrom.Day != 1 || percent <= -100m)
        {
            return Error.Validation("Hse.RentReview", "A rent review takes effect from the first of a month, and cannot cut rents by 100% or more.");
        }

        List<RentalUnit> units = await _db.Set<RentalUnit>()
            .Where(u => u.CompanyId == companyId && (unitType == null || u.UnitType == unitType)).ToListAsync(ct).ConfigureAwait(false);
        List<long> unitIds = [.. units.Select(u => u.Id)];
        List<Tenancy> running = await _db.Set<Tenancy>().Include(t => t.RentChanges)
            .Where(t => t.CompanyId == companyId && t.Status == TenancyStatus.Active && unitIds.Contains(t.UnitId)).ToListAsync(ct).ConfigureAwait(false);
        if (running.Any(t => t.RentChanges.Any(c => c.EffectiveFrom == effectiveFrom)))
        {
            return Error.Conflict("Hse.RentReviewed", $"Rents have already been reviewed from {effectiveFrom:yyyy-MM-dd}.");
        }

        foreach (RentalUnit unit in units)
        {
            unit.MonthlyRent = RentalArithmetic.Escalate(unit.MonthlyRent, percent);
        }

        foreach (Tenancy tenancy in running)
        {
            tenancy.ChangeRent(effectiveFrom, RentalArithmetic.Escalate(tenancy.RentOn(effectiveFrom.AddDays(-1)), percent));
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return new RentReviewResult(units.Count, running.Count);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<VoidUnit>> VoidsAsync(long companyId, DateOnly asAtDate, CancellationToken ct = default)
    {
        var units = await _db.Set<RentalUnit>().AsNoTracking()
            .Where(u => u.CompanyId == companyId && u.VacantSince != null && u.VacantSince <= asAtDate)
            .Select(u => new { u.Code, VacantSince = u.VacantSince!.Value, u.MonthlyRent, u.VoidReason, u.RefurbishmentJobNumber }).ToListAsync(ct).ConfigureAwait(false);
        return [.. units
            .Select(u => new VoidUnit(u.Code, u.VacantSince, RentalArithmetic.DaysVoid(u.VacantSince, asAtDate), RentalArithmetic.RentLost(u.MonthlyRent, RentalArithmetic.DaysVoid(u.VacantSince, asAtDate)))
            {
                Reason = u.VoidReason, RefurbishmentJob = u.RefurbishmentJobNumber,
            })
            .OrderByDescending(v => v.DaysVoid).ThenBy(v => v.UnitCode, StringComparer.Ordinal)];
    }

    // Raises one month's rent on a tenancy, pro rata, unless already raised; returns the rent raised.
    private async Task<Result<decimal>> BillMonthAsync(Tenancy tenancy, DateOnly month, CancellationToken ct)
    {
        decimal rent = RentalArithmetic.RentFor(tenancy.RentOn(new DateOnly(month.Year, month.Month, 1)), month, tenancy.StartDate, tenancy.EndDate);
        string document = $"{tenancy.TenancyNumber}-{month:yyyyMM}";
        if (rent <= 0m || await _db.OpenItems.AnyAsync(i => i.CompanyId == tenancy.CompanyId && i.Ledger == TradingLedger.AccountsReceivable && i.DocumentNumber == document, ct).ConfigureAwait(false))
        {
            return 0m;
        }

        RentalUnit unit = await _db.Set<RentalUnit>().AsNoTracking().FirstAsync(u => u.Id == tenancy.UnitId, ct).ConfigureAwait(false);
        Customer tenant = await _db.Customers.AsNoTracking().FirstAsync(c => c.Id == tenancy.TenantCustomerId, ct).ConfigureAwait(false);
        (DateOnly monthStart, DateOnly monthEnd) = ProRata.Month(month);
        DateOnly billDate = tenancy.StartDate > monthStart ? tenancy.StartDate : monthStart;

        // Lodger fees: each permit in force during the month adds its fee, to its own income account.
        List<RevenueLine> fees = [.. (await _db.Set<LodgerPermit>().AsNoTracking().Where(p => p.TenancyId == tenancy.Id && p.MonthlyFee > 0m).ToListAsync(ct).ConfigureAwait(false))
            .Where(p => p.InForceBetween(monthStart, monthEnd)).GroupBy(p => p.FeeAccountCode!, StringComparer.Ordinal).Select(g => new RevenueLine(g.Key, g.Sum(p => p.MonthlyFee)))];
        decimal feeTotal = fees.Sum(f => f.NetAmount);
        List<RevenueLine> revenue = feeTotal == 0m ? [] : [new RevenueLine(unit.RentAccountCode, rent), .. fees];
        Result<TradingPostingResult> invoiced = await _ar.PostInvoiceAsync(new SalesInvoiceRequest
        {
            CompanyId = tenancy.CompanyId, CustomerId = tenant.Id, DocumentNumber = document, DocumentDate = billDate, Currency = tenant.Currency,
            NetAmount = rent + feeTotal, SalesAccountCode = unit.RentAccountCode, RevenueLines = revenue,
            Narrative = $"Rent {monthStart:MMM yyyy}, unit {unit.Code} to {monthEnd:dd MMM}" + (feeTotal > 0m ? FormattableString.Invariant($" + lodger fees {feeTotal:0.00}") : string.Empty),
        }, ct).ConfigureAwait(false);
        if (invoiced.IsFailure)
        {
            return invoiced.Error;
        }

        return rent + feeTotal;
    }

    private async Task<Result<PostingReceipt>> PostAsync(long companyId, DateOnly date, string document, string narrative, string debitAccount, string creditAccount, decimal amount, CancellationToken ct)
    {
        CurrencyCode currency = await _db.Set<Company>().Where(c => c.Id == companyId).Select(c => c.BaseCurrency).FirstAsync(ct).ConfigureAwait(false);
        return await _engine.PostAsync(new JournalRequest
        {
            CompanyId = companyId, SourceModule = "CB", PostingDate = date, TransactionCurrency = currency, DocumentType = "RentalDeposit", DocumentNumber = document, Narrative = narrative,
            Lines =
            [
                new JournalLineRequest { AccountCode = debitAccount, Side = JournalSide.Debit, TransactionAmount = amount },
                new JournalLineRequest { AccountCode = creditAccount, Side = JournalSide.Credit, TransactionAmount = amount },
            ],
        }, ct).ConfigureAwait(false);
    }
}
