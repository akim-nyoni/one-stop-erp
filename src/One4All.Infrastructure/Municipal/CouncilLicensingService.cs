using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Municipal;
using One4All.Application.Abstractions.Numbering;
using One4All.Application.Abstractions.Trading;
using One4All.Application.Municipal;
using One4All.Core.Common;
using One4All.Core.Municipal;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Municipal;

/// <summary>Council licences and permits, with fees and penalties invoiced through the receivables ledger.</summary>
public sealed class CouncilLicensingService : ICouncilLicensingService
{
    private readonly One4AllDbContext _db;
    private readonly INumberSequenceService _numbers;
    private readonly ISalesLedgerService _ar;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="numbers">The number sequences.</param>
    /// <param name="ar">The receivables ledger.</param>
    public CouncilLicensingService(One4AllDbContext db, INumberSequenceService numbers, ISalesLedgerService ar)
    {
        _db = db;
        _numbers = numbers;
        _ar = ar;
    }

    /// <inheritdoc />
    public Task<Result<IssuedLicence>> IssueAsync(IssueLicenceRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return _db.AtomicAsync(() => IssueCoreAsync(request, ct), ct);
    }

    private async Task<Result<IssuedLicence>> IssueCoreAsync(IssueLicenceRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Description))
        {
            return Error.Validation("Lic.Description", "Say what the licence covers.");
        }

        if (request.CycleMonths < 1 || request.Fee < 0m)
        {
            return Error.Validation("Lic.Terms", "A licence needs a renewal cycle of at least a month and a fee of zero or more.");
        }

        if (request.Fee > 0m && string.IsNullOrWhiteSpace(request.RevenueAccountCode))
        {
            return Error.Validation("Lic.RevenueAccount", "A licence with a fee needs a revenue account.");
        }

        Customer? holder = await _db.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.Id == request.HolderCustomerId && c.CompanyId == request.CompanyId, ct).ConfigureAwait(false);
        if (holder is null)
        {
            return Error.NotFound("Lic.HolderNotFound", $"Customer {request.HolderCustomerId} not found.");
        }

        if (request.PropertyId is long propertyId && !await _db.Properties.AnyAsync(p => p.Id == propertyId && p.CompanyId == request.CompanyId, ct).ConfigureAwait(false))
        {
            return Error.NotFound("Mun.PropertyNotFound", $"Property {propertyId} not found.");
        }

        Result<string> number = await _numbers.AllocateAsync(request.CompanyId, "LIC", request.IssuedOn, ct: ct).ConfigureAwait(false);
        if (number.IsFailure)
        {
            return number.Error;
        }

        DateOnly expires = request.IssuedOn.AddMonths(request.CycleMonths).AddDays(-1);
        string? deferred = string.IsNullOrWhiteSpace(request.DeferredIncomeAccountCode) ? null : request.DeferredIncomeAccountCode.Trim().ToUpperInvariant();
        string? invoice = null;
        if (request.Fee > 0m)
        {
            invoice = $"{number.Value}-1";
            Result<TradingPostingResult> invoiced = await _ar.PostInvoiceAsync(new SalesInvoiceRequest
            {
                CompanyId = request.CompanyId, CustomerId = holder.Id, DocumentNumber = invoice, DocumentDate = request.IssuedOn, Currency = holder.Currency,
                NetAmount = request.Fee, SalesAccountCode = deferred ?? request.RevenueAccountCode, Narrative = $"{request.Kind} licence {number.Value} to {expires:yyyy-MM-dd}",
            }, ct).ConfigureAwait(false);
            if (invoiced.IsFailure)
            {
                return invoiced.Error;
            }

            Defer(request.CompanyId, invoice, $"{request.Kind} licence {number.Value}", deferred, request.RevenueAccountCode.Trim(), request.Fee, request.IssuedOn, expires);
        }

        CouncilLicence licence = new(request.CompanyId, number.Value, request.Kind, holder.Id, request.PropertyId, request.Description.Trim(),
            request.IssuedOn, expires, request.CycleMonths, request.Fee, request.RevenueAccountCode.Trim())
        {
            DeferredIncomeAccountCode = deferred,
        };
        _db.Set<CouncilLicence>().Add(licence);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return new IssuedLicence(licence.LicenceNumber, licence.ExpiresOn, invoice);
    }

    /// <inheritdoc />
    public Task<Result<CouncilLicenceStatus>> RecordInspectionAsync(long companyId, string licenceNumber, DateOnly date, string inspector, bool passed, string? notes, CancellationToken ct = default)
        => _db.AtomicAsync(() => InspectCoreAsync(companyId, licenceNumber, date, inspector, passed, notes, ct), ct);

    private async Task<Result<CouncilLicenceStatus>> InspectCoreAsync(long companyId, string licenceNumber, DateOnly date, string inspector, bool passed, string? notes, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(inspector))
        {
            return Error.Validation("Lic.Inspector", "Say who inspected.");
        }

        if (!passed && string.IsNullOrWhiteSpace(notes))
        {
            return Error.Validation("Lic.Findings", "A failed inspection needs its findings.");
        }

        Result<CouncilLicence> found = await FindAsync(companyId, licenceNumber, ct).ConfigureAwait(false);
        if (found.IsFailure)
        {
            return found.Error;
        }

        CouncilLicence licence = found.Value;
        if (licence.Status == CouncilLicenceStatus.Revoked)
        {
            return Error.Rule("Lic.Revoked", $"Licence {licenceNumber} has been revoked.");
        }

        _db.Set<CouncilInspection>().Add(new CouncilInspection(companyId, licence.Id, date, inspector.Trim(), passed, notes?.Trim()));
        if (passed)
        {
            licence.Reinstate();
        }
        else
        {
            licence.Suspend();
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return licence.Status;
    }

    /// <inheritdoc />
    public Task<Result<LicenceRenewed>> RenewAsync(long companyId, string licenceNumber, DateOnly date, decimal penaltyPercent, string? penaltyAccountCode, CancellationToken ct = default)
        => _db.AtomicAsync(() => RenewCoreAsync(companyId, licenceNumber, date, penaltyPercent, penaltyAccountCode, ct), ct);

    private async Task<Result<LicenceRenewed>> RenewCoreAsync(long companyId, string licenceNumber, DateOnly date, decimal penaltyPercent, string? penaltyAccountCode, CancellationToken ct)
    {
        if (penaltyPercent < 0m)
        {
            return Error.Validation("Lic.Penalty", "The penalty cannot be negative.");
        }

        Result<CouncilLicence> found = await FindAsync(companyId, licenceNumber, ct).ConfigureAwait(false);
        if (found.IsFailure)
        {
            return found.Error;
        }

        CouncilLicence licence = found.Value;
        if (licence.Status == CouncilLicenceStatus.Revoked)
        {
            return Error.Rule("Lic.Revoked", $"Licence {licenceNumber} has been revoked.");
        }

        if (licence.Status == CouncilLicenceStatus.Suspended)
        {
            return Error.Rule("Lic.Suspended", $"Licence {licenceNumber} is suspended until an inspection passes.");
        }

        RenewalTerms terms = LicenceRenewal.Renew(licence.ExpiresOn, date, licence.CycleMonths, licence.Fee, penaltyPercent);
        if (terms.Penalty > 0m && string.IsNullOrWhiteSpace(penaltyAccountCode))
        {
            return Error.Validation("Lic.PenaltyAccount", "A late renewal with a penalty needs a penalty account.");
        }

        string? invoice = null;
        if (terms.Total > 0m)
        {
            invoice = $"{licence.LicenceNumber}-{licence.Renewals + 2}";
            List<RevenueLine> lines = [];
            if (terms.Fee > 0m)
            {
                lines.Add(new RevenueLine(licence.DeferredIncomeAccountCode ?? licence.RevenueAccountCode, terms.Fee));
            }

            if (terms.Penalty > 0m)
            {
                lines.Add(new RevenueLine(penaltyAccountCode!.Trim(), terms.Penalty));
            }

            Customer holder = await _db.Customers.AsNoTracking().FirstAsync(c => c.Id == licence.HolderCustomerId, ct).ConfigureAwait(false);
            Result<TradingPostingResult> invoiced = await _ar.PostInvoiceAsync(new SalesInvoiceRequest
            {
                CompanyId = companyId, CustomerId = holder.Id, DocumentNumber = invoice, DocumentDate = date, Currency = holder.Currency,
                NetAmount = terms.Total, SalesAccountCode = lines[0].AccountCode, RevenueLines = lines,
                Narrative = $"Renewal of licence {licence.LicenceNumber} to {terms.NewExpiry:yyyy-MM-dd}" + (terms.Late ? " with late penalty" : string.Empty),
            }, ct).ConfigureAwait(false);
            if (invoiced.IsFailure)
            {
                return invoiced.Error;
            }
        }

        if (invoice is not null && terms.Fee > 0m)
        {
            Defer(companyId, invoice, $"{licence.Kind} licence {licence.LicenceNumber} renewal", licence.DeferredIncomeAccountCode, licence.RevenueAccountCode, terms.Fee,
                terms.NewExpiry.AddMonths(-licence.CycleMonths).AddDays(1), terms.NewExpiry);
        }

        licence.Renew(terms.NewExpiry);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return new LicenceRenewed(terms, invoice);
    }

    // A fee held in the deferred account is earned evenly over the licence's cycle by the deferred-income run.
    private void Defer(long companyId, string invoice, string description, string? deferredAccount, string revenueAccount, decimal fee, DateOnly from, DateOnly to)
    {
        if (deferredAccount is not null)
        {
            _db.Set<Core.Accounting.DeferredIncomeSchedule>().Add(new Core.Accounting.DeferredIncomeSchedule(companyId, invoice, description, deferredAccount, revenueAccount, fee, from, to));
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ExpiringLicence>> RenewalsDueAsync(long companyId, DateOnly asAtDate, int withinDays, CancellationToken ct = default)
    {
        DateOnly horizon = asAtDate.AddDays(withinDays);
        var rows = await (
            from l in _db.Set<CouncilLicence>().AsNoTracking()
            join c in _db.Customers.AsNoTracking() on l.HolderCustomerId equals c.Id
            where l.CompanyId == companyId && l.Status != CouncilLicenceStatus.Revoked && l.ExpiresOn <= horizon
            orderby l.ExpiresOn, l.LicenceNumber
            select new { l.LicenceNumber, l.Kind, c.Code, l.Description, l.ExpiresOn })
            .ToListAsync(ct).ConfigureAwait(false);
        return rows.Select(r => new ExpiringLicence(r.LicenceNumber, r.Kind, r.Code, r.Description, r.ExpiresOn, r.ExpiresOn.DayNumber - asAtDate.DayNumber)).ToList();
    }

    /// <inheritdoc />
    public async Task<Result> RevokeAsync(long companyId, string licenceNumber, string reason, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return Result.Failure(Error.Validation("Lic.Reason", "Say why the licence is revoked."));
        }

        Result<CouncilLicence> found = await FindAsync(companyId, licenceNumber, ct).ConfigureAwait(false);
        if (found.IsFailure)
        {
            return Result.Failure(found.Error);
        }

        if (found.Value.Status == CouncilLicenceStatus.Revoked)
        {
            return Result.Failure(Error.Rule("Lic.Revoked", $"Licence {licenceNumber} has already been revoked."));
        }

        found.Value.Revoke(reason.Trim());
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    private async Task<Result<CouncilLicence>> FindAsync(long companyId, string licenceNumber, CancellationToken ct)
    {
        CouncilLicence? licence = await _db.Set<CouncilLicence>().FirstOrDefaultAsync(l => l.CompanyId == companyId && l.LicenceNumber == licenceNumber, ct).ConfigureAwait(false);
        if (licence is null)
        {
            return Error.NotFound("Lic.NotFound", $"Licence {licenceNumber} not found.");
        }

        return licence;
    }
}
