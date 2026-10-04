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
        unit.Let();
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return number.Value;
    }

    /// <inheritdoc />
    public Task<Result<RentRunResult>> RunRentAsync(long companyId, DateOnly month, CancellationToken ct = default)
        => _db.AtomicAsync(() => RunCoreAsync(companyId, month, ct), ct);

    private async Task<Result<RentRunResult>> RunCoreAsync(long companyId, DateOnly month, CancellationToken ct)
    {
        (DateOnly first, DateOnly last) = ProRata.Month(month);
        List<Tenancy> tenancies = await _db.Set<Tenancy>()
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
        Tenancy? tenancy = await _db.Set<Tenancy>().FirstOrDefaultAsync(t => t.CompanyId == companyId && t.TenancyNumber == number, ct).ConfigureAwait(false);
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

        RentalUnit unit = await _db.Set<RentalUnit>().FirstAsync(u => u.Id == tenancy.UnitId, ct).ConfigureAwait(false);
        unit.Vacate(endDate.AddDays(1));
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return new TenancyEnded(finalRent, settlement);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<VoidUnit>> VoidsAsync(long companyId, DateOnly asAtDate, CancellationToken ct = default)
    {
        var units = await _db.Set<RentalUnit>().AsNoTracking()
            .Where(u => u.CompanyId == companyId && u.VacantSince != null && u.VacantSince <= asAtDate)
            .Select(u => new { u.Code, VacantSince = u.VacantSince!.Value, u.MonthlyRent }).ToListAsync(ct).ConfigureAwait(false);
        return [.. units
            .Select(u => new VoidUnit(u.Code, u.VacantSince, RentalArithmetic.DaysVoid(u.VacantSince, asAtDate), RentalArithmetic.RentLost(u.MonthlyRent, RentalArithmetic.DaysVoid(u.VacantSince, asAtDate))))
            .OrderByDescending(v => v.DaysVoid).ThenBy(v => v.UnitCode, StringComparer.Ordinal)];
    }

    // Raises one month's rent on a tenancy, pro rata, unless already raised; returns the rent raised.
    private async Task<Result<decimal>> BillMonthAsync(Tenancy tenancy, DateOnly month, CancellationToken ct)
    {
        decimal rent = RentalArithmetic.RentFor(tenancy.MonthlyRent, month, tenancy.StartDate, tenancy.EndDate);
        string document = $"{tenancy.TenancyNumber}-{month:yyyyMM}";
        if (rent <= 0m || await _db.OpenItems.AnyAsync(i => i.CompanyId == tenancy.CompanyId && i.Ledger == TradingLedger.AccountsReceivable && i.DocumentNumber == document, ct).ConfigureAwait(false))
        {
            return 0m;
        }

        RentalUnit unit = await _db.Set<RentalUnit>().AsNoTracking().FirstAsync(u => u.Id == tenancy.UnitId, ct).ConfigureAwait(false);
        Customer tenant = await _db.Customers.AsNoTracking().FirstAsync(c => c.Id == tenancy.TenantCustomerId, ct).ConfigureAwait(false);
        (DateOnly monthStart, DateOnly monthEnd) = ProRata.Month(month);
        DateOnly billDate = tenancy.StartDate > monthStart ? tenancy.StartDate : monthStart;
        Result<TradingPostingResult> invoiced = await _ar.PostInvoiceAsync(new SalesInvoiceRequest
        {
            CompanyId = tenancy.CompanyId, CustomerId = tenant.Id, DocumentNumber = document, DocumentDate = billDate, Currency = tenant.Currency,
            NetAmount = rent, SalesAccountCode = unit.RentAccountCode, Narrative = $"Rent {monthStart:MMM yyyy}, unit {unit.Code} to {monthEnd:dd MMM}",
        }, ct).ConfigureAwait(false);
        if (invoiced.IsFailure)
        {
            return invoiced.Error;
        }

        return rent;
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
