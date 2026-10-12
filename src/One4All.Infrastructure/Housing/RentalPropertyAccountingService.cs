using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Accounting;
using One4All.Application.Abstractions.Housing;
using One4All.Core.Accounting;
using One4All.Core.Common;
using One4All.Core.Housing;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Housing;

/// <summary>Carries rental units as investment property or PPE and remeasures fair-value investment property.</summary>
public sealed class RentalPropertyAccountingService : IRentalPropertyAccountingService
{
    private readonly One4AllDbContext _db;
    private readonly IPostingEngine _engine;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="engine">The posting engine.</param>
    public RentalPropertyAccountingService(One4AllDbContext db, IPostingEngine engine)
    {
        _db = db;
        _engine = engine;
    }

    /// <inheritdoc />
    public Task<Result<string>> DesignateAsync(long companyId, string unitCode, RentalPropertyTreatment treatment, string assetAccountCode, decimal carryingAmount, string contraAccountCode,
        DateOnly on, CancellationToken ct = default)
        => _db.AtomicAsync(async () =>
        {
            if (treatment == RentalPropertyTreatment.NotDesignated || string.IsNullOrWhiteSpace(assetAccountCode) || carryingAmount < 0m
                || (carryingAmount > 0m && string.IsNullOrWhiteSpace(contraAccountCode)))
            {
                return Error.Validation("Hse.PropertyTreatment", "Designating a unit needs its treatment, the asset account, a carrying amount and (when there is one) the account it comes from.");
            }

            if (await UnitAsync(companyId, unitCode, ct).ConfigureAwait(false) is not RentalUnit unit)
            {
                return Error.NotFound("Hse.UnitNotFound", $"Unit {unitCode} not found.");
            }

            if (unit.Treatment != RentalPropertyTreatment.NotDesignated)
            {
                return Error.Conflict("Hse.PropertyDesignated", $"Unit {unit.Code} is already carried as {unit.Treatment}.");
            }

            string asset = assetAccountCode.Trim().ToUpperInvariant();
            string journal = string.Empty;
            if (carryingAmount > 0m)
            {
                Result<string> posted = await PostAsync(companyId, on, $"IP-{unit.Code}", $"{unit.Code} carried as {treatment}", asset, contraAccountCode.Trim().ToUpperInvariant(), carryingAmount, ct)
                    .ConfigureAwait(false);
                if (posted.IsFailure)
                {
                    return posted.Error;
                }

                journal = posted.Value;
            }

            unit.Designate(treatment, asset, carryingAmount, on);
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            return Result<string>.Success(journal);
        }, ct);

    /// <inheritdoc />
    public Task<Result<string>> RevalueAsync(long companyId, string unitCode, decimal fairValue, DateOnly on, string gainLossAccountCode, CancellationToken ct = default)
        => _db.AtomicAsync(async () =>
        {
            if (fairValue < 0m || string.IsNullOrWhiteSpace(gainLossAccountCode))
            {
                return Error.Validation("Hse.FairValue", "A remeasurement needs the fair value and the fair-value gain or loss account.");
            }

            if (await UnitAsync(companyId, unitCode, ct).ConfigureAwait(false) is not RentalUnit unit)
            {
                return Error.NotFound("Hse.UnitNotFound", $"Unit {unitCode} not found.");
            }

            if (unit.Treatment != RentalPropertyTreatment.InvestmentPropertyFairValue)
            {
                return Error.Rule("Hse.NotFairValue", $"Unit {unit.Code} is carried as {unit.Treatment}; only fair-value investment property is remeasured.");
            }

            if (unit.LastValuedOn is DateOnly last && on < last)
            {
                return Error.Rule("Hse.ValuationDate", $"Unit {unit.Code} was last valued on {last:yyyy-MM-dd}.");
            }

            decimal change = fairValue - unit.CarryingAmount;
            string journal = string.Empty;
            if (change != 0m)
            {
                string gainLoss = gainLossAccountCode.Trim().ToUpperInvariant();
                Result<string> posted = change > 0m
                    ? await PostAsync(companyId, on, $"FV-{unit.Code}-{on:yyyyMMdd}", $"{unit.Code} fair value gain", unit.AssetAccountCode!, gainLoss, change, ct).ConfigureAwait(false)
                    : await PostAsync(companyId, on, $"FV-{unit.Code}-{on:yyyyMMdd}", $"{unit.Code} fair value loss", gainLoss, unit.AssetAccountCode!, -change, ct).ConfigureAwait(false);
                if (posted.IsFailure)
                {
                    return posted.Error;
                }

                journal = posted.Value;
            }

            unit.Remeasure(fairValue, on);
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            return Result<string>.Success(journal);
        }, ct);

    /// <inheritdoc />
    public async Task<IReadOnlyList<RentalPropertyRow>> RegisterAsync(long companyId, CancellationToken ct = default)
        => await _db.Set<RentalUnit>().AsNoTracking().Where(u => u.CompanyId == companyId && u.Treatment != RentalPropertyTreatment.NotDesignated).OrderBy(u => u.Code)
            .Select(u => new RentalPropertyRow(u.Code, u.Treatment, u.AssetAccountCode ?? string.Empty, u.CarryingAmount, u.LastValuedOn)).ToListAsync(ct).ConfigureAwait(false);

    private Task<RentalUnit?> UnitAsync(long companyId, string unitCode, CancellationToken ct)
    {
        string code = (unitCode ?? string.Empty).Trim().ToUpperInvariant();
        return _db.Set<RentalUnit>().FirstOrDefaultAsync(u => u.CompanyId == companyId && u.Code == code, ct);
    }

    private async Task<Result<string>> PostAsync(long companyId, DateOnly on, string document, string narrative, string debit, string credit, decimal amount, CancellationToken ct)
    {
        CurrencyCode currency = await _db.Companies.Where(c => c.Id == companyId).Select(c => c.BaseCurrency).FirstAsync(ct).ConfigureAwait(false);
        Result<PostingReceipt> posted = await _engine.PostAsync(new JournalRequest
        {
            CompanyId = companyId, SourceModule = "GL", PostingDate = on, TransactionCurrency = currency, DocumentType = "InvestmentProperty", DocumentNumber = document, Narrative = narrative,
            Lines =
            [
                new JournalLineRequest { AccountCode = debit, Side = JournalSide.Debit, TransactionAmount = amount },
                new JournalLineRequest { AccountCode = credit, Side = JournalSide.Credit, TransactionAmount = amount },
            ],
        }, ct).ConfigureAwait(false);
        return posted.IsSuccess ? posted.Value.JournalNumber : posted.Error;
    }
}
