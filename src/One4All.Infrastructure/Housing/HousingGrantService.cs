using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Accounting;
using One4All.Application.Abstractions.Housing;
using One4All.Application.Abstractions.Numbering;
using One4All.Application.Housing;
using One4All.Core.Accounting;
using One4All.Core.Common;
using One4All.Core.Housing;
using One4All.Core.System;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Housing;

/// <summary>IAS 20 housing grants: deferred on receipt, recognised with the expenditure once conditions are assured.</summary>
public sealed class HousingGrantService : IHousingGrantService
{
    private readonly One4AllDbContext _db;
    private readonly INumberSequenceService _numbers;
    private readonly IPostingEngine _engine;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="numbers">The number sequences.</param>
    /// <param name="engine">The posting engine.</param>
    public HousingGrantService(One4AllDbContext db, INumberSequenceService numbers, IPostingEngine engine)
    {
        _db = db;
        _numbers = numbers;
        _engine = engine;
    }

    /// <inheritdoc />
    public Task<Result<string>> ReceiveAsync(GrantReceipt receipt, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        return _db.AtomicAsync(() => ReceiveCoreAsync(receipt, ct), ct);
    }

    private async Task<Result<string>> ReceiveCoreAsync(GrantReceipt receipt, CancellationToken ct)
    {
        if (receipt.Amount <= 0m || receipt.BudgetedExpenditure <= 0m || string.IsNullOrWhiteSpace(receipt.Grantor) || string.IsNullOrWhiteSpace(receipt.Conditions)
            || new[] { receipt.BankAccountCode, receipt.DeferredAccountCode, receipt.IncomeAccountCode }.Any(string.IsNullOrWhiteSpace))
        {
            return Error.Validation("Hse.GrantDetails", "A grant needs a grantor, its conditions, a positive amount and budget, and the bank, deferred and income accounts.");
        }

        Result<string> number = await _numbers.AllocateAsync(receipt.CompanyId, "GR", receipt.ReceivedOn, ct: ct).ConfigureAwait(false);
        if (number.IsFailure)
        {
            return number.Error;
        }

        Result<PostingReceipt> posted = await PostAsync(receipt.CompanyId, receipt.ReceivedOn, number.Value, $"Grant from {receipt.Grantor.Trim()} received into deferred income",
            receipt.BankAccountCode.Trim(), receipt.DeferredAccountCode.Trim(), receipt.Amount, ct).ConfigureAwait(false);
        if (posted.IsFailure)
        {
            return posted.Error;
        }

        _db.Set<HousingGrant>().Add(new HousingGrant(receipt.CompanyId, number.Value, receipt.Grantor.Trim(), receipt.SchemeId, receipt.Amount, receipt.BudgetedExpenditure,
            receipt.Conditions.Trim(), receipt.ReceivedOn, receipt.DeferredAccountCode.Trim(), receipt.IncomeAccountCode.Trim()));
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return number.Value;
    }

    /// <inheritdoc />
    public Task<Result<decimal>> AssureConditionsAsync(long companyId, string grantNumber, DateOnly date, CancellationToken ct = default)
        => _db.AtomicAsync(() => AssureCoreAsync(companyId, grantNumber, date, ct), ct);

    private async Task<Result<decimal>> AssureCoreAsync(long companyId, string grantNumber, DateOnly date, CancellationToken ct)
    {
        Result<HousingGrant> found = await FindAsync(companyId, grantNumber, ct).ConfigureAwait(false);
        if (found.IsFailure)
        {
            return found.Error;
        }

        if (found.Value.ConditionsAssuredOn is not null)
        {
            return Error.Rule("Hse.ConditionsAssured", $"Grant {grantNumber}'s conditions are already assured.");
        }

        found.Value.AssureConditions(date);
        return await RecogniseAsync(found.Value, found.Value.ExpenditureToDate, date, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<Result<decimal>> RecordExpenditureAsync(long companyId, string grantNumber, decimal expenditureToDate, DateOnly date, CancellationToken ct = default)
        => _db.AtomicAsync(() => ExpenditureCoreAsync(companyId, grantNumber, expenditureToDate, date, ct), ct);

    private async Task<Result<decimal>> ExpenditureCoreAsync(long companyId, string grantNumber, decimal expenditureToDate, DateOnly date, CancellationToken ct)
    {
        Result<HousingGrant> found = await FindAsync(companyId, grantNumber, ct).ConfigureAwait(false);
        if (found.IsFailure)
        {
            return found.Error;
        }

        if (expenditureToDate < found.Value.ExpenditureToDate)
        {
            return Error.Validation("Hse.ExpenditureToDate", "Expenditure to date is cumulative and cannot go down.");
        }

        return await RecogniseAsync(found.Value, expenditureToDate, date, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<GrantDisclosure>> DisclosureAsync(long companyId, CancellationToken ct = default)
    {
        List<HousingGrant> grants = await _db.Set<HousingGrant>().AsNoTracking().Where(g => g.CompanyId == companyId).OrderBy(g => g.GrantNumber).ToListAsync(ct).ConfigureAwait(false);
        return [.. grants.Select(g => new GrantDisclosure(g.GrantNumber, g.Grantor, g.Amount, g.RecognisedToDate, g.Deferred, g.ConditionsAssuredOn is not null, g.Conditions))];
    }

    // Recognises the movement between the income due to date and what is already recognised.
    private async Task<Result<decimal>> RecogniseAsync(HousingGrant grant, decimal expenditureToDate, DateOnly date, CancellationToken ct)
    {
        decimal target = GrantRecognition.TargetToDate(grant.Amount, grant.BudgetedExpenditure, expenditureToDate, grant.ConditionsAssuredOn is not null);
        decimal movement = target - grant.RecognisedToDate;
        if (movement > 0m)
        {
            Result<PostingReceipt> posted = await PostAsync(grant.CompanyId, date, $"{grant.GrantNumber}-{date:yyyyMMdd}", $"Grant {grant.GrantNumber} recognised with expenditure to date",
                grant.DeferredAccountCode, grant.IncomeAccountCode, movement, ct).ConfigureAwait(false);
            if (posted.IsFailure)
            {
                return posted.Error;
            }
        }

        grant.Recognise(expenditureToDate, Math.Max(target, grant.RecognisedToDate));
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return grant.RecognisedToDate;
    }

    private async Task<Result<PostingReceipt>> PostAsync(long companyId, DateOnly date, string document, string narrative, string debitAccount, string creditAccount, decimal amount, CancellationToken ct)
    {
        CurrencyCode currency = await _db.Set<Company>().Where(c => c.Id == companyId).Select(c => c.BaseCurrency).FirstAsync(ct).ConfigureAwait(false);
        return await _engine.PostAsync(new JournalRequest
        {
            CompanyId = companyId, SourceModule = "GL", PostingDate = date, TransactionCurrency = currency, DocumentType = "HousingGrant", DocumentNumber = document, Narrative = narrative,
            Lines =
            [
                new JournalLineRequest { AccountCode = debitAccount, Side = JournalSide.Debit, TransactionAmount = amount },
                new JournalLineRequest { AccountCode = creditAccount, Side = JournalSide.Credit, TransactionAmount = amount },
            ],
        }, ct).ConfigureAwait(false);
    }

    private async Task<Result<HousingGrant>> FindAsync(long companyId, string grantNumber, CancellationToken ct)
    {
        string number = (grantNumber ?? string.Empty).Trim();
        HousingGrant? grant = await _db.Set<HousingGrant>().FirstOrDefaultAsync(g => g.CompanyId == companyId && g.GrantNumber == number, ct).ConfigureAwait(false);
        if (grant is null)
        {
            return Error.NotFound("Hse.GrantNotFound", $"Grant {number} not found.");
        }

        return grant;
    }
}
