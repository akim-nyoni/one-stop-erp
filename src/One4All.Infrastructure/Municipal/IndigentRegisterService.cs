using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Municipal;
using One4All.Application.Abstractions.Trading;
using One4All.Core.Common;
using One4All.Core.Municipal;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Municipal;

/// <summary>The indigent register and its subsidy run, crediting ratepayers through the sales ledger.</summary>
public sealed class IndigentRegisterService : IIndigentRegisterService
{
    private readonly One4AllDbContext _db;
    private readonly ISalesLedgerService _ar;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="ar">The sales ledger, which posts the subsidy credits.</param>
    public IndigentRegisterService(One4AllDbContext db, ISalesLedgerService ar)
    {
        _db = db;
        _ar = ar;
    }

    /// <inheritdoc />
    public async Task<Result<long>> RegisterAsync(long companyId, IndigentApplication application, decimal maximumIncome, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(application);
        if (application.SubsidyPercent is <= 0m or > 100m || application.ReviewDue < application.ApprovedFrom || application.HouseholdIncome < 0m)
        {
            return Error.Validation("Indigent.Invalid", "A registration needs a subsidy between 0 and 100% and a review date after approval.");
        }

        if (application.HouseholdIncome > maximumIncome)
        {
            return Error.Rule("Indigent.IncomeTooHigh", $"A household income of {application.HouseholdIncome:0.00} is above the {maximumIncome:0.00} threshold.");
        }

        if (!await _db.Set<Property>().AnyAsync(p => p.Id == application.PropertyId && p.CompanyId == companyId, ct).ConfigureAwait(false))
        {
            return Error.NotFound("Indigent.PropertyNotFound", $"Property {application.PropertyId} not found.");
        }

        if (await _db.Set<IndigentRegistration>().AnyAsync(r => r.PropertyId == application.PropertyId && r.Status == IndigentStatus.Active, ct).ConfigureAwait(false))
        {
            return Error.Conflict("Indigent.AlreadyRegistered", "The property already has an active registration; review it instead.");
        }

        IndigentRegistration registration = new(companyId, application.PropertyId, application.HouseholdIncome, application.SubsidyPercent, application.ApprovedFrom, application.ReviewDue, application.ApprovedByUserId);
        _db.Set<IndigentRegistration>().Add(registration);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return registration.Id;
    }

    /// <inheritdoc />
    public async Task<Result<bool>> ReviewAsync(long companyId, long registrationId, decimal householdIncome, decimal subsidyPercent, DateOnly reviewDue, decimal maximumIncome, long userId, CancellationToken ct = default)
    {
        IndigentRegistration? registration = await _db.Set<IndigentRegistration>()
            .FirstOrDefaultAsync(r => r.Id == registrationId && r.CompanyId == companyId && r.Status == IndigentStatus.Active, ct).ConfigureAwait(false);
        if (registration is null)
        {
            return Error.NotFound("Indigent.NotFound", $"Active registration {registrationId} not found.");
        }

        if (householdIncome > maximumIncome)
        {
            registration.Revoke();
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            return false;
        }

        if (subsidyPercent is <= 0m or > 100m || reviewDue <= registration.ReviewDue)
        {
            return Error.Validation("Indigent.Invalid", "A review needs a subsidy between 0 and 100% and a later review date.");
        }

        registration.Review(householdIncome, subsidyPercent, reviewDue, userId);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return true;
    }

    /// <inheritdoc />
    public Task<Result<IReadOnlyList<SubsidyLine>>> ApplySubsidiesAsync(long companyId, DateOnly monthEnd, string subsidyAccountCode, CancellationToken ct = default)
        => _db.AtomicAsync(() => ApplySubsidiesCoreAsync(companyId, monthEnd, subsidyAccountCode, ct), ct);

    // One unit: every household's credit for the month posts, or none does.
    private async Task<Result<IReadOnlyList<SubsidyLine>>> ApplySubsidiesCoreAsync(long companyId, DateOnly monthEnd, string subsidyAccountCode, CancellationToken ct)
    {
        DateOnly first = new(monthEnd.Year, monthEnd.Month, 1);
        DateOnly last = first.AddMonths(1).AddDays(-1);
        List<IndigentRegistration> registrations = await _db.Set<IndigentRegistration>().AsNoTracking()
            .Where(r => r.CompanyId == companyId && r.Status == IndigentStatus.Active)
            .OrderBy(r => r.PropertyId).ToListAsync(ct).ConfigureAwait(false);

        List<SubsidyLine> lines = [];
        foreach (IndigentRegistration registration in registrations.Where(r => r.CoversMonth(last)))
        {
            var property = await _db.Set<Property>().Where(p => p.Id == registration.PropertyId).Select(p => new { p.StandNumber, p.RatepayerCustomerId }).FirstAsync(ct).ConfigureAwait(false);
            string number = $"IND-{property.StandNumber}-{first:yyyyMM}";

            // Runs guard themselves: the ledger would take the same credit twice.
            if (await _db.OpenItems.AnyAsync(i => i.CompanyId == companyId && i.Ledger == TradingLedger.AccountsReceivable && i.TraderId == property.RatepayerCustomerId && i.DocumentNumber == number, ct).ConfigureAwait(false))
            {
                continue;
            }

            var billed = await _db.OpenItems
                .Where(i => i.CompanyId == companyId && i.Ledger == TradingLedger.AccountsReceivable && i.TraderId == property.RatepayerCustomerId
                            && i.ItemType == OpenItemType.Invoice && i.DocumentDate >= first && i.DocumentDate <= last)
                .Select(i => new { i.Currency, i.OriginalTransaction })
                .ToListAsync(ct).ConfigureAwait(false);
            decimal total = billed.Sum(b => b.OriginalTransaction);
            decimal subsidy = decimal.Round(total * registration.SubsidyPercent / 100m, 2, MidpointRounding.AwayFromZero);
            if (subsidy <= 0m)
            {
                continue;
            }

            Result<TradingPostingResult> credit = await _ar.PostCreditNoteAsync(new SalesCreditNoteRequest
            {
                CompanyId = companyId,
                CustomerId = property.RatepayerCustomerId,
                DocumentNumber = number,
                DocumentDate = last,
                Currency = billed[0].Currency,
                NetAmount = subsidy,
                SalesAccountCode = subsidyAccountCode,
            }, ct).ConfigureAwait(false);
            if (credit.IsFailure)
            {
                return credit.Error;
            }

            lines.Add(new SubsidyLine(registration.PropertyId, property.StandNumber, total, subsidy));
        }

        return Result<IReadOnlyList<SubsidyLine>>.Success(lines);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<IndigentReviewDue>> ReviewsDueAsync(long companyId, DateOnly asAtDate, int withinDays, CancellationToken ct = default)
    {
        DateOnly horizon = asAtDate.AddDays(withinDays);
        return await (
            from r in _db.Set<IndigentRegistration>().AsNoTracking()
            join p in _db.Set<Property>().AsNoTracking() on r.PropertyId equals p.Id
            where r.CompanyId == companyId && r.Status == IndigentStatus.Active && r.ReviewDue <= horizon
            orderby r.ReviewDue, r.Id
            select new IndigentReviewDue(r.Id, r.PropertyId, p.StandNumber, r.ReviewDue, r.ReviewDue < asAtDate))
            .ToListAsync(ct).ConfigureAwait(false);
    }
}
