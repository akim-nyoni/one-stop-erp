using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Crm;
using One4All.Application.Abstractions.MasterData;
using One4All.Core.Accounting;
using One4All.Core.Common;
using One4All.Core.Crm;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Crm;

/// <summary>Leads, activities and the opportunity–quotation link (brief §4.16).</summary>
public sealed class CrmService : ICrmService
{
    private readonly One4AllDbContext _db;
    private readonly IMasterDataService _master;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="master">Creates customers for converted leads.</param>
    public CrmService(One4AllDbContext db, IMasterDataService master)
    {
        _db = db;
        _master = master;
    }

    /// <inheritdoc />
    public async Task<Result<long>> CaptureLeadAsync(NewLead lead, CancellationToken ct = default)
    {
        string? email = string.IsNullOrWhiteSpace(lead.Email) ? null : lead.Email.Trim().ToLowerInvariant();
        string? phone = Digits(lead.Phone);
        if (string.IsNullOrWhiteSpace(lead.Name) || (email is null && phone is null) || string.IsNullOrWhiteSpace(lead.Source))
        {
            return Error.Validation("Crm.LeadInvalid", "A lead needs a name, an e-mail or phone, and where it came from.");
        }

        // Same e-mail or same phone (last nine digits): probably someone already known.
        List<Lead> earlier = await _db.Set<Lead>().AsNoTracking().Where(l => l.CompanyId == lead.CompanyId && (email != null && l.Email == email || phone != null && l.Phone != null))
            .OrderBy(l => l.Id).ToListAsync(ct).ConfigureAwait(false);
        Lead? duplicate = earlier.FirstOrDefault(l => (email is not null && l.Email == email) || (phone is not null && Tail(Digits(l.Phone)) == Tail(phone)));

        Lead created = new(lead.CompanyId, lead.Name.Trim(), lead.Organisation?.Trim(), email, phone, lead.Source.Trim().ToLowerInvariant())
        {
            OwnerUserId = lead.OwnerUserId, PossibleDuplicateOfId = duplicate?.Id,
        };
        _db.Add(created);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return created.Id;
    }

    /// <inheritdoc />
    public async Task<Result> AdvanceLeadAsync(long companyId, long leadId, LeadStatus status, CancellationToken ct = default)
    {
        Lead? lead = await LeadAsync(companyId, leadId, ct).ConfigureAwait(false);
        if (lead is null || !lead.Advance(status))
        {
            return Result.Failure(Error.Rule("Crm.LeadStage", "Leads move to Contacted or Qualified, and not once converted or disqualified."));
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result> DisqualifyLeadAsync(long companyId, long leadId, string reason, CancellationToken ct = default)
    {
        Lead? lead = await LeadAsync(companyId, leadId, ct).ConfigureAwait(false);
        if (lead is null || string.IsNullOrWhiteSpace(reason) || !lead.Disqualify(reason.Trim()))
        {
            return Result.Failure(Error.Rule("Crm.LeadStage", "A lead not yet converted is disqualified with a reason."));
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public Task<Result<long>> ConvertLeadAsync(long companyId, long leadId, string opportunityName, decimal estimatedValue, string? customerCode, CurrencyCode? currency,
        CancellationToken ct = default)
        => _db.AtomicAsync(async () =>
        {
            Lead? lead = await LeadAsync(companyId, leadId, ct).ConfigureAwait(false);
            if (lead is null || lead.Status != LeadStatus.Qualified)
            {
                return Error.Rule("Crm.NotQualified", "Only a qualified lead is converted.");
            }

            if (string.IsNullOrWhiteSpace(opportunityName) || estimatedValue < 0m)
            {
                return Error.Validation("Crm.OpportunityInvalid", "An opportunity needs a name and a value that is not negative.");
            }

            long? customerId = null;
            if (!string.IsNullOrWhiteSpace(customerCode))
            {
                if (currency is not CurrencyCode cur)
                {
                    return Error.Validation("Crm.CurrencyRequired", "A new customer needs its trading currency.");
                }

                Result<long> customer = await _master.CreateCustomerAsync(companyId, customerCode, lead.Organisation ?? lead.Name, cur, ct: ct).ConfigureAwait(false);
                if (customer.IsFailure)
                {
                    return customer.Error;
                }

                customerId = customer.Value;
            }

            Opportunity opportunity = new(companyId, opportunityName.Trim(), lead.Name, estimatedValue, customerId);
            opportunity.MoveTo(OpportunityStage.Qualified);
            _db.Add(opportunity);
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            lead.Convert(opportunity.Id);

            // The lead's history follows it.
            foreach (CrmActivity activity in await _db.Set<CrmActivity>().Where(a => a.LeadId == leadId).ToListAsync(ct).ConfigureAwait(false))
            {
                activity.OpportunityId ??= opportunity.Id;
                activity.CustomerId ??= customerId;
            }

            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            return Result<long>.Success(opportunity.Id);
        }, ct);

    /// <inheritdoc />
    public async Task<Result<long>> LogActivityAsync(NewActivity activity, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(activity.Subject) || (activity.LeadId is null && activity.OpportunityId is null && activity.CustomerId is null))
        {
            return Error.Validation("Crm.ActivityInvalid", "An activity needs a subject and a lead, opportunity or customer.");
        }

        if (activity.LeadId is long leadId && await LeadAsync(activity.CompanyId, leadId, ct).ConfigureAwait(false) is null
            || activity.OpportunityId is long oppId && !await _db.Opportunities.AnyAsync(o => o.Id == oppId && o.CompanyId == activity.CompanyId, ct).ConfigureAwait(false)
            || activity.CustomerId is long custId && !await _db.Customers.AnyAsync(c => c.Id == custId && c.CompanyId == activity.CompanyId, ct).ConfigureAwait(false))
        {
            return Error.NotFound("Crm.NotFound", "The lead, opportunity or customer was not found.");
        }

        CrmActivity created = new(activity.CompanyId, activity.Type, activity.Subject.Trim(), activity.OwnerUserId)
        {
            DueOnUtc = activity.DueOnUtc, Notes = activity.Notes, LeadId = activity.LeadId, OpportunityId = activity.OpportunityId, CustomerId = activity.CustomerId,
        };
        _db.Add(created);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return created.Id;
    }

    /// <inheritdoc />
    public async Task<Result> CompleteActivityAsync(long companyId, long activityId, string? outcome, CancellationToken ct = default)
    {
        CrmActivity? activity = await _db.Set<CrmActivity>().FirstOrDefaultAsync(a => a.Id == activityId && a.CompanyId == companyId, ct).ConfigureAwait(false);
        if (activity is null || activity.DoneOnUtc is not null)
        {
            return Result.Failure(Error.Rule("Crm.ActivityDone", "No open activity with that id."));
        }

        activity.Complete(DateTimeOffset.UtcNow, outcome);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<CrmActivity>> AgendaAsync(long companyId, long ownerUserId, DateTimeOffset dueBy, CancellationToken ct = default)
        => await _db.Set<CrmActivity>().AsNoTracking()
            .Where(a => a.CompanyId == companyId && a.OwnerUserId == ownerUserId && a.DoneOnUtc == null && a.DueOnUtc != null && a.DueOnUtc <= dueBy)
            .OrderBy(a => a.DueOnUtc).ToListAsync(ct).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<IReadOnlyList<CrmActivity>> TimelineAsync(long companyId, long? leadId, long? opportunityId, long? customerId, CancellationToken ct = default)
        => await _db.Set<CrmActivity>().AsNoTracking()
            .Where(a => a.CompanyId == companyId && ((leadId != null && a.LeadId == leadId) || (opportunityId != null && a.OpportunityId == opportunityId) || (customerId != null && a.CustomerId == customerId)))
            .OrderByDescending(a => a.Id).ToListAsync(ct).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<Result> LinkQuotationAsync(long companyId, long opportunityId, long quotationId, CancellationToken ct = default)
    {
        Opportunity? opportunity = await _db.Opportunities.FirstOrDefaultAsync(o => o.Id == opportunityId && o.CompanyId == companyId, ct).ConfigureAwait(false);
        var quote = await _db.Quotations.AsNoTracking().Where(q => q.Id == quotationId && q.CompanyId == companyId).Select(q => new { q.CustomerId }).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (opportunity is null || quote is null)
        {
            return Result.Failure(Error.NotFound("Crm.NotFound", "No such opportunity or quotation."));
        }

        if (!opportunity.IsOpen || (opportunity.CustomerId is long customer && customer != quote.CustomerId))
        {
            return Result.Failure(Error.Rule("Crm.QuotationMismatch", "The quotation must be for the opportunity's customer, and the opportunity still open."));
        }

        opportunity.QuotationId = quotationId;
        opportunity.CustomerId ??= quote.CustomerId;
        if (opportunity.Stage is OpportunityStage.Lead or OpportunityStage.Qualified)
        {
            opportunity.MoveTo(OpportunityStage.Proposal);
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<LeadFunnelLine>> LeadFunnelAsync(long companyId, CancellationToken ct = default)
        => [.. (await _db.Set<Lead>().AsNoTracking().Where(l => l.CompanyId == companyId).Select(l => new { l.Source, l.Status }).ToListAsync(ct).ConfigureAwait(false))
            .GroupBy(l => l.Source).OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g =>
            {
                int leads = g.Count(), converted = g.Count(l => l.Status == LeadStatus.Converted);
                return new LeadFunnelLine(g.Key, leads, g.Count(l => l.Status is LeadStatus.Qualified or LeadStatus.Converted), converted, g.Count(l => l.Status == LeadStatus.Disqualified),
                    leads == 0 ? 0m : Math.Round(converted * 100m / leads, 1, MidpointRounding.AwayFromZero));
            })];

    private static string? Digits(string? phone)
    {
        string digits = new([.. (phone ?? string.Empty).Where(char.IsDigit)]);
        return digits.Length >= 7 ? digits : null;
    }

    private static string? Tail(string? digits) => digits is null ? null : digits.Length > 9 ? digits[^9..] : digits;

    private Task<Lead?> LeadAsync(long companyId, long leadId, CancellationToken ct) => _db.Set<Lead>().FirstOrDefaultAsync(l => l.Id == leadId && l.CompanyId == companyId, ct);
}
