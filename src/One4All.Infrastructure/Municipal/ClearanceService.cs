using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Municipal;
using One4All.Application.Abstractions.Numbering;
using One4All.Application.Municipal;
using One4All.Core.Common;
using One4All.Core.Municipal;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Municipal;

/// <summary>Rates clearance quotes and certificates over the ratepayer's AR account.</summary>
public sealed class ClearanceService : IClearanceService
{
    private readonly One4AllDbContext _db;
    private readonly INumberSequenceService _numbers;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="numbers">The number sequences.</param>
    public ClearanceService(One4AllDbContext db, INumberSequenceService numbers)
    {
        _db = db;
        _numbers = numbers;
    }

    /// <inheritdoc />
    public async Task<Result<ClearanceQuote>> QuoteAsync(long companyId, long propertyId, DateOnly date, int monthsInAdvance, CancellationToken ct = default)
    {
        var property = await _db.Properties.AsNoTracking().Where(p => p.Id == propertyId && p.CompanyId == companyId)
            .Select(p => new { p.RatepayerCustomerId }).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (property is null)
        {
            return Error.NotFound("Mun.PropertyNotFound", $"Property {propertyId} not found.");
        }

        var items = await _db.OpenItems
            .Where(i => i.CompanyId == companyId && i.Ledger == TradingLedger.AccountsReceivable && i.TraderId == property.RatepayerCustomerId)
            .Select(i => new { i.Sign, i.ItemType, i.DocumentDate, i.OutstandingBase, i.OriginalBase })
            .ToListAsync(ct).ConfigureAwait(false);
        decimal balance = items.Sum(i => i.Sign == LedgerSign.Debit ? i.OutstandingBase : -i.OutstandingBase);

        // What was billed in the whole months before the application.
        DateOnly lookBackEnd = new DateOnly(date.Year, date.Month, 1).AddDays(-1);
        DateOnly lookBackStart = new DateOnly(date.Year, date.Month, 1).AddMonths(-RatesClearance.LookBackMonths);
        decimal billed = items.Where(i => i.ItemType == OpenItemType.Invoice && i.DocumentDate >= lookBackStart && i.DocumentDate <= lookBackEnd).Sum(i => i.OriginalBase);
        return RatesClearance.Quote(balance, billed, RatesClearance.LookBackMonths, monthsInAdvance);
    }

    /// <inheritdoc />
    public Task<Result<IssuedClearance>> IssueAsync(long companyId, long propertyId, DateOnly date, int monthsInAdvance, int validDays, long userId, CancellationToken ct = default)
        => _db.AtomicAsync(() => IssueCoreAsync(companyId, propertyId, date, monthsInAdvance, validDays, userId, ct), ct);

    private async Task<Result<IssuedClearance>> IssueCoreAsync(long companyId, long propertyId, DateOnly date, int monthsInAdvance, int validDays, long userId, CancellationToken ct)
    {
        if (validDays < 1)
        {
            return Error.Validation("Mun.ClearanceValidity", "A certificate must be valid for at least a day.");
        }

        Result<ClearanceQuote> quote = await QuoteAsync(companyId, propertyId, date, monthsInAdvance, ct).ConfigureAwait(false);
        if (quote.IsFailure)
        {
            return quote.Error;
        }

        if (!quote.Value.CanIssue)
        {
            return Error.Rule("Mun.ClearanceDue", $"{quote.Value.AmountDue:0.00} must be paid first: the balance of {quote.Value.Balance:0.00} and {quote.Value.MonthsInAdvance} months in advance ({quote.Value.AdvanceRequired:0.00}).");
        }

        Result<string> number = await _numbers.AllocateAsync(companyId, "CC", date, ct: ct).ConfigureAwait(false);
        if (number.IsFailure)
        {
            return number.Error;
        }

        string stand = await _db.Properties.Where(p => p.Id == propertyId).Select(p => p.StandNumber).FirstAsync(ct).ConfigureAwait(false);
        ClearanceCertificate certificate = new(companyId, number.Value, propertyId, date, date.AddDays(validDays - 1), quote.Value.AdvanceRequired, userId);
        _db.Set<ClearanceCertificate>().Add(certificate);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return new IssuedClearance(certificate.CertificateNumber, stand, certificate.IssuedOn, certificate.ValidUntil, certificate.AdvanceHeld);
    }
}
