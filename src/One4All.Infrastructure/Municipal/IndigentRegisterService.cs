using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Municipal;
using One4All.Application.Abstractions.Security;
using One4All.Application.Abstractions.Trading;
using One4All.Core.Accounting;
using One4All.Core.Common;
using One4All.Core.Housing;
using One4All.Core.Municipal;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Municipal;

/// <summary>The indigent register and its subsidy run, crediting ratepayers through the sales ledger.</summary>
public sealed class IndigentRegisterService : IIndigentRegisterService
{
    private const string ApprovePermission = "Municipal.Indigent";

    private readonly One4AllDbContext _db;
    private readonly ISalesLedgerService _ar;
    private readonly IPermissionService _permissions;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="ar">The sales ledger, which posts the subsidy credits.</param>
    /// <param name="permissions">Checks the authority of whoever approves a registration.</param>
    public IndigentRegisterService(One4AllDbContext db, ISalesLedgerService ar, IPermissionService permissions)
    {
        _db = db;
        _ar = ar;
        _permissions = permissions;
    }

    /// <inheritdoc />
    public async Task<Result> SetPolicyAsync(long companyId, decimal maximumIncome, CancellationToken ct = default)
    {
        if (maximumIncome < 0m)
        {
            return Result.Failure(Error.Validation("Indigent.Policy", "The qualifying maximum income cannot be negative."));
        }

        IndigentPolicy? policy = await _db.Set<IndigentPolicy>().FirstOrDefaultAsync(p => p.CompanyId == companyId, ct).ConfigureAwait(false);
        if (policy is null)
        {
            _db.Set<IndigentPolicy>().Add(new IndigentPolicy(companyId, maximumIncome));
        }
        else
        {
            policy.Set(maximumIncome);
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<decimal?> PolicyAsync(long companyId, CancellationToken ct = default)
        => await _db.Set<IndigentPolicy>().AsNoTracking().Where(p => p.CompanyId == companyId).Select(p => (decimal?)p.MaximumIncome).FirstOrDefaultAsync(ct).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<Result<long>> RequestAsync(long companyId, IndigentApplication application, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(application);
        if (application.SubsidyPercent is <= 0m or > 100m || application.ReviewDue < application.ApprovedFrom || application.HouseholdIncome < 0m)
        {
            return Error.Validation("Indigent.Invalid", "A registration needs a subsidy between 0 and 100% and a review date after approval.");
        }

        Result qualifies = await QualifiesAsync(companyId, application.HouseholdIncome, ct).ConfigureAwait(false);
        if (qualifies.IsFailure)
        {
            return qualifies.Error;
        }

        if (!await _db.Set<Property>().AnyAsync(p => p.Id == application.PropertyId && p.CompanyId == companyId, ct).ConfigureAwait(false))
        {
            return Error.NotFound("Indigent.PropertyNotFound", $"Property {application.PropertyId} not found.");
        }

        if (await _db.Set<IndigentRegistration>().AnyAsync(r => r.PropertyId == application.PropertyId && (r.Status == IndigentStatus.Active || r.Status == IndigentStatus.Pending), ct)
            .ConfigureAwait(false))
        {
            return Error.Conflict("Indigent.AlreadyRegistered", "The property already has an active registration or one awaiting approval; review or decide it instead.");
        }

        IndigentRegistration registration = new(companyId, application.PropertyId, application.HouseholdIncome, application.SubsidyPercent, application.ApprovedFrom, application.ReviewDue,
            application.RequestedByUserId);
        _db.Set<IndigentRegistration>().Add(registration);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return registration.Id;
    }

    /// <inheritdoc />
    public async Task<Result> ApproveAsync(long companyId, long registrationId, long approverUserId, CancellationToken ct = default)
    {
        Result<IndigentRegistration> pending = await DecidableAsync(companyId, registrationId, approverUserId, ct).ConfigureAwait(false);
        if (pending.IsFailure)
        {
            return Result.Failure(pending.Error);
        }

        // The threshold may have moved since the request: the household must still qualify on the day it is approved.
        Result qualifies = await QualifiesAsync(companyId, pending.Value.HouseholdIncome, ct).ConfigureAwait(false);
        if (qualifies.IsFailure)
        {
            return qualifies;
        }

        pending.Value.Approve(approverUserId);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result> RejectAsync(long companyId, long registrationId, long userId, string reason, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return Result.Failure(Error.Validation("Indigent.Reason", "Give the reason for turning the registration down."));
        }

        Result<IndigentRegistration> pending = await DecidableAsync(companyId, registrationId, userId, ct).ConfigureAwait(false);
        if (pending.IsFailure)
        {
            return Result.Failure(pending.Error);
        }

        pending.Value.Reject(userId, reason.Trim());
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PendingIndigentRegistration>> PendingAsync(long companyId, CancellationToken ct = default)
        => await (
            from r in _db.Set<IndigentRegistration>().AsNoTracking()
            join p in _db.Set<Property>().AsNoTracking() on r.PropertyId equals p.Id
            where r.CompanyId == companyId && r.Status == IndigentStatus.Pending
            orderby r.Id
            select new PendingIndigentRegistration(r.Id, r.PropertyId, p.StandNumber, r.HouseholdIncome, r.SubsidyPercent, r.ApprovedFrom, r.RequestedByUserId))
            .ToListAsync(ct).ConfigureAwait(false);

    // A pending registration, decided by someone other than who requested it, holding the indigent approve right.
    private async Task<Result<IndigentRegistration>> DecidableAsync(long companyId, long registrationId, long userId, CancellationToken ct)
    {
        IndigentRegistration? registration = await _db.Set<IndigentRegistration>()
            .FirstOrDefaultAsync(r => r.Id == registrationId && r.CompanyId == companyId && r.Status == IndigentStatus.Pending, ct).ConfigureAwait(false);
        if (registration is null)
        {
            return Error.NotFound("Indigent.NotPending", $"Registration {registrationId} is not awaiting approval.");
        }

        if (registration.RequestedByUserId == userId)
        {
            return Error.Forbidden("Indigent.SelfApproval", "You keyed this registration; another user approves or rejects it.");
        }

        if (!(await _permissions.ResolveAsync(userId, ct).ConfigureAwait(false)).Has(ApprovePermission, Core.Security.OperationFlags.Approve))
        {
            return Error.Forbidden("Indigent.NotAuthorised", $"User {userId} may not approve indigent registrations.");
        }

        return registration;
    }

    // The household's income measured against the council's threshold, which must be set.
    private async Task<Result> QualifiesAsync(long companyId, decimal householdIncome, CancellationToken ct)
    {
        if (await PolicyAsync(companyId, ct).ConfigureAwait(false) is not decimal maximum)
        {
            return Result.Failure(Error.Rule("Indigent.NoPolicy", "The council has not set the qualifying maximum household income; set it on the indigent register first."));
        }

        return householdIncome > maximum
            ? Result.Failure(Error.Rule("Indigent.IncomeTooHigh", $"A household income of {householdIncome:0.00} is above the {maximum:0.00} threshold."))
            : Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result<bool>> ReviewAsync(long companyId, long registrationId, decimal householdIncome, decimal subsidyPercent, DateOnly reviewDue, long userId, CancellationToken ct = default)
    {
        IndigentRegistration? registration = await _db.Set<IndigentRegistration>()
            .FirstOrDefaultAsync(r => r.Id == registrationId && r.CompanyId == companyId && r.Status == IndigentStatus.Active, ct).ConfigureAwait(false);
        if (registration is null)
        {
            return Error.NotFound("Indigent.NotFound", $"Active registration {registrationId} not found.");
        }

        if (await PolicyAsync(companyId, ct).ConfigureAwait(false) is not decimal maximum)
        {
            return Error.Rule("Indigent.NoPolicy", "The council has not set the qualifying maximum household income; set it on the indigent register first.");
        }

        if (householdIncome > maximum)
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

        // Interest and penalties are not subsidised: finance charges are interest items (not invoices), and a penalty charge credits an interest /
        // penalty income account (the finance-charge policy's or an instalment agreement's), so what an invoice credits to one of those is left out.
        List<string> chargeAccounts = [.. (await _db.Set<FinanceChargePolicy>().AsNoTracking().Where(p => p.CompanyId == companyId).Select(p => p.InterestIncomeAccountCode)
                .ToListAsync(ct).ConfigureAwait(false))
            .Concat(await _db.Set<InstalmentAgreement>().AsNoTracking().Where(a => a.CompanyId == companyId).Select(a => a.InterestAccountCode).Distinct().ToListAsync(ct).ConfigureAwait(false))
            .Where(a => !string.IsNullOrWhiteSpace(a)).Distinct(StringComparer.Ordinal)];

        List<SubsidyLine> lines = [];
        foreach (IndigentRegistration registration in registrations.Where(r => r.CoversMonth(last)))
        {
            var property = await _db.Set<Property>().Where(p => p.Id == registration.PropertyId).Select(p => new { p.StandNumber, p.RatepayerCustomerId }).FirstAsync(ct).ConfigureAwait(false);
            var billed = await _db.OpenItems
                .Where(i => i.CompanyId == companyId && i.Ledger == TradingLedger.AccountsReceivable && i.TraderId == property.RatepayerCustomerId
                            && i.ItemType == OpenItemType.Invoice && i.DocumentDate >= first && i.DocumentDate <= last)
                .Select(i => new { i.Currency, i.OriginalTransaction, i.JournalEntryId })
                .ToListAsync(ct).ConfigureAwait(false);
            List<long> journals = [.. billed.Where(b => b.JournalEntryId is not null).Select(b => b.JournalEntryId!.Value).Distinct()];
            Dictionary<long, decimal> penalties = journals.Count == 0 || chargeAccounts.Count == 0 ? []
                : (await _db.Set<JournalLine>().AsNoTracking()
                    .Where(l => l.CompanyId == companyId && journals.Contains(l.JournalEntryId) && chargeAccounts.Contains(l.AccountCode) && l.TransactionCredit > 0m)
                    .Select(l => new { l.JournalEntryId, l.TransactionCredit }).ToListAsync(ct).ConfigureAwait(false))
                .GroupBy(l => l.JournalEntryId).ToDictionary(g => g.Key, g => g.Sum(l => l.TransactionCredit));

            // Each currency's eligible charges earn their subsidy in that currency, under their own credit note.
            foreach (var currency in billed.GroupBy(b => b.Currency).OrderBy(g => g.Key.ToString(), StringComparer.Ordinal))
            {
                decimal eligible = currency.Sum(b => Math.Max(0m, b.OriginalTransaction - (b.JournalEntryId is long j ? penalties.GetValueOrDefault(j) : 0m)));
                decimal subsidy = decimal.Round(eligible * registration.SubsidyPercent / 100m, 2, MidpointRounding.AwayFromZero);
                string number = $"IND-{property.StandNumber}-{first:yyyyMM}-{currency.Key}";

                // Runs guard themselves: the ledger would take the same credit twice.
                if (subsidy <= 0m || await _db.OpenItems.AnyAsync(i => i.CompanyId == companyId && i.Ledger == TradingLedger.AccountsReceivable && i.TraderId == property.RatepayerCustomerId
                        && i.DocumentNumber == number, ct).ConfigureAwait(false))
                {
                    continue;
                }

                Result<TradingPostingResult> credit = await _ar.PostCreditNoteAsync(new SalesCreditNoteRequest
                {
                    CompanyId = companyId,
                    CustomerId = property.RatepayerCustomerId,
                    DocumentNumber = number,
                    DocumentDate = last,
                    Currency = currency.Key,
                    NetAmount = subsidy,
                    SalesAccountCode = subsidyAccountCode,
                }, ct).ConfigureAwait(false);
                if (credit.IsFailure)
                {
                    return credit.Error;
                }

                lines.Add(new SubsidyLine(registration.PropertyId, property.StandNumber, currency.Key, eligible, subsidy));
            }
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
