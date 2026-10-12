using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Municipal;
using One4All.Application.Abstractions.Trading;
using One4All.Application.Municipal;
using One4All.Core.Common;
using One4All.Core.Municipal;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Municipal;

/// <summary>The owner register and property transfers through rates clearance.</summary>
public sealed class OwnershipService : IOwnershipService
{
    private const string RegisterReference = "REGISTER";

    private readonly One4AllDbContext _db;

    private readonly ISalesLedgerService _ar;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="ar">Posts the transfer settlement.</param>
    public OwnershipService(One4AllDbContext db, ISalesLedgerService ar)
    {
        _db = db;
        _ar = ar;
    }

    /// <inheritdoc />
    public Task<Result<IReadOnlyList<TransferSettlementLine>>> SettleTransferAsync(long companyId, long propertyId, DateOnly transferDate, CancellationToken ct = default)
        => _db.AtomicAsync(() => SettleCoreAsync(companyId, propertyId, transferDate, ct), ct);

    // One unit of work: the seller's and the buyer's documents post together or not at all.
    private async Task<Result<IReadOnlyList<TransferSettlementLine>>> SettleCoreAsync(long companyId, long propertyId, DateOnly transferDate, CancellationToken ct)
    {
        Property? property = await _db.Properties.Include(p => p.Services).AsNoTracking().FirstOrDefaultAsync(p => p.Id == propertyId && p.CompanyId == companyId, ct).ConfigureAwait(false);
        if (property is null)
        {
            return Error.NotFound("Mun.PropertyNotFound", $"Property {propertyId} not found.");
        }

        List<PropertyOwner> owners = await _db.Set<PropertyOwner>().AsNoTracking().Where(o => o.PropertyId == propertyId && o.IsRatepayer).ToListAsync(ct).ConfigureAwait(false);
        PropertyOwner? seller = owners.FirstOrDefault(o => o.OwnedTo == transferDate.AddDays(-1));
        PropertyOwner? buyer = owners.FirstOrDefault(o => o.OwnedFrom == transferDate && o.Reference != RegisterReference);
        if (seller is null || buyer is null)
        {
            return Error.NotFound("Mun.NoTransfer", $"Stand {property.StandNumber} was not transferred on {transferDate:yyyy-MM-dd}.");
        }

        if ((await MunicipalCharges.AlreadyRaisedAsync(_db, companyId, [$"{buyer.Reference}-S1", $"{buyer.Reference}-B1"], ct).ConfigureAwait(false)).Count > 0)
        {
            return Error.Conflict("Mun.TransferSettled", $"The transfer under {buyer.Reference} has already been settled.");
        }

        decimal valuation = ValuationRoll.ValueAt(await ValuationHistory.LoadAsync(_db, property.Id, ct).ConfigureAwait(false), transferDate, property.Valuation);
        List<(TransferSettlementLine Line, string Account)> lines = [];
        foreach (PropertyServiceLink link in property.Services.Where(s => s.IsActive && s.MeterId == null && s.StartDate < transferDate))
        {
            Tariff? linked = await _db.Set<Tariff>().Include(t => t.Blocks).Include(t => t.Seasons).AsNoTracking()
                .FirstOrDefaultAsync(t => t.Id == link.TariffId && t.CompanyId == companyId, ct).ConfigureAwait(false);
            if (linked is null)
            {
                continue;
            }

            Tariff tariff = TariffVersions.InForce(await TariffVersions.LoadAsync(_db, linked, ct).ConfigureAwait(false), linked, transferDate);
            CycleSlot slot = BillingCycles.Slot(tariff.Cycle, tariff.CycleStartMonth, tariff.Instalments, transferDate);
            decimal charge = decimal.Round(tariff.CalculateCharge(0m, valuation, property.SizeSquareMetres, slot.PeriodStart.Month) * link.Quantity, 2, MidpointRounding.AwayFromZero);
            decimal billed = TransferApportionment.BilledSoFar(charge, slot);
            decimal share = TransferApportionment.SellerShare(charge, slot, transferDate);
            if (charge > 0m && billed != share)
            {
                lines.Add((new TransferSettlementLine(tariff.Code, slot.PeriodStart, slot.PeriodEnd, charge, billed, share, billed - share), tariff.RevenueAccountCode));
            }
        }

        Dictionary<long, Core.Trading.Customer> customers = await _db.Customers.AsNoTracking().Where(c => c.Id == seller.CustomerId || c.Id == buyer.CustomerId)
            .ToDictionaryAsync(c => c.Id, ct).ConfigureAwait(false);
        int n = 0;
        foreach (var group in lines.GroupBy(l => (l.Account, Credit: l.Line.Settlement > 0m)))
        {
            n++;
            decimal amount = Math.Abs(group.Sum(l => l.Line.Settlement));
            string narrative = $"Transfer of stand {property.StandNumber} on {transferDate:yyyy-MM-dd}: " + string.Join(", ", group.Select(l => $"{l.Line.TariffCode} {l.Line.PeriodStart:yyyy-MM-dd} to {l.Line.PeriodEnd:yyyy-MM-dd}"));
            Core.Trading.Customer from = customers[group.Key.Credit ? seller.CustomerId : buyer.CustomerId], to = customers[group.Key.Credit ? buyer.CustomerId : seller.CustomerId];
            Result<TradingPostingResult> credited = await _ar.PostCreditNoteAsync(new SalesCreditNoteRequest
            {
                CompanyId = companyId, CustomerId = from.Id, DocumentNumber = $"{buyer.Reference}-{(group.Key.Credit ? "S" : "B")}{n}", DocumentDate = transferDate, Currency = from.Currency,
                NetAmount = amount, SalesAccountCode = group.Key.Account, Narrative = narrative, ReasonCode = "TRANSFER",
            }, ct).ConfigureAwait(false);
            if (credited.IsFailure)
            {
                return credited.Error;
            }

            Result<TradingPostingResult> charged = await _ar.PostInvoiceAsync(new SalesInvoiceRequest
            {
                CompanyId = companyId, CustomerId = to.Id, DocumentNumber = $"{buyer.Reference}-{(group.Key.Credit ? "B" : "S")}{n}", DocumentDate = transferDate, Currency = to.Currency,
                NetAmount = amount, SalesAccountCode = group.Key.Account, Narrative = narrative,
            }, ct).ConfigureAwait(false);
            if (charged.IsFailure)
            {
                return charged.Error;
            }
        }

        return Result.Success<IReadOnlyList<TransferSettlementLine>>([.. lines.Select(l => l.Line)]);
    }

    /// <inheritdoc />
    public Task<Result> RegisterOwnersAsync(long companyId, long propertyId, IReadOnlyList<OwnerShare> owners, DateOnly ownedFrom, string? deedsReference, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(owners);
        return _db.AtomicAsync(() => RegisterCoreAsync(companyId, propertyId, owners, ownedFrom, deedsReference, ct), ct);
    }

    private async Task<Result> RegisterCoreAsync(long companyId, long propertyId, IReadOnlyList<OwnerShare> owners, DateOnly ownedFrom, string? deedsReference, CancellationToken ct)
    {
        Result<Property> property = await CheckAsync(companyId, propertyId, owners, ct).ConfigureAwait(false);
        if (property.IsFailure)
        {
            return Result.Failure(property.Error);
        }

        if (await _db.Set<PropertyOwner>().AnyAsync(o => o.PropertyId == propertyId, ct).ConfigureAwait(false))
        {
            return Result.Failure(Error.Conflict("Mun.OwnersRegistered", $"Stand {property.Value.StandNumber} already has owners; change them by transfer."));
        }

        Record(property.Value, owners, ownedFrom, RegisterReference, deedsReference);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public Task<Result> TransferAsync(long companyId, long propertyId, IReadOnlyList<OwnerShare> buyers, DateOnly transferDate, string clearanceNumber, string? deedsReference, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(buyers);
        return _db.AtomicAsync(() => TransferCoreAsync(companyId, propertyId, buyers, transferDate, clearanceNumber, deedsReference, ct), ct);
    }

    private async Task<Result> TransferCoreAsync(long companyId, long propertyId, IReadOnlyList<OwnerShare> buyers, DateOnly transferDate, string clearanceNumber, string? deedsReference, CancellationToken ct)
    {
        Result<Property> property = await CheckAsync(companyId, propertyId, buyers, ct).ConfigureAwait(false);
        if (property.IsFailure)
        {
            return Result.Failure(property.Error);
        }

        string number = (clearanceNumber ?? string.Empty).Trim();
        ClearanceCertificate? certificate = await _db.Set<ClearanceCertificate>().AsNoTracking()
            .FirstOrDefaultAsync(c => c.CompanyId == companyId && c.CertificateNumber == number && c.PropertyId == propertyId, ct).ConfigureAwait(false);
        if (certificate is null)
        {
            return Result.Failure(Error.NotFound("Mun.ClearanceNotFound", $"No clearance certificate {number} for stand {property.Value.StandNumber}."));
        }

        if (transferDate < certificate.IssuedOn || transferDate > certificate.ValidUntil)
        {
            return Result.Failure(Error.Rule("Mun.ClearanceExpired", $"Certificate {number} is valid from {certificate.IssuedOn:yyyy-MM-dd} to {certificate.ValidUntil:yyyy-MM-dd}."));
        }

        if (await _db.Set<PropertyOwner>().AnyAsync(o => o.CompanyId == companyId && o.Reference == number, ct).ConfigureAwait(false))
        {
            return Result.Failure(Error.Conflict("Mun.ClearanceUsed", $"Certificate {number} has already been used for a transfer."));
        }

        List<PropertyOwner> current = await _db.Set<PropertyOwner>().Where(o => o.PropertyId == propertyId && o.OwnedTo == null).ToListAsync(ct).ConfigureAwait(false);
        if (current.Any(o => o.OwnedFrom >= transferDate))
        {
            return Result.Failure(Error.Rule("Mun.TransferDate", "The transfer must be after the current owners' ownership began."));
        }

        foreach (PropertyOwner seller in current)
        {
            seller.End(transferDate.AddDays(-1));
        }

        Record(property.Value, buyers, transferDate, number, deedsReference);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<OwnerEntry>> OwnersAsync(long companyId, long propertyId, DateOnly asAtDate, CancellationToken ct = default)
        => (await QueryAsync(companyId, propertyId, ct).ConfigureAwait(false))
            .Where(o => o.OwnedFrom <= asAtDate && (o.OwnedTo == null || o.OwnedTo >= asAtDate))
            .OrderByDescending(o => o.IsRatepayer).ThenByDescending(o => o.SharePercent).ToList();

    /// <inheritdoc />
    public async Task<IReadOnlyList<OwnerEntry>> HistoryAsync(long companyId, long propertyId, CancellationToken ct = default)
        => await QueryAsync(companyId, propertyId, ct).ConfigureAwait(false);

    private async Task<List<OwnerEntry>> QueryAsync(long companyId, long propertyId, CancellationToken ct)
        => await (
            from o in _db.Set<PropertyOwner>().AsNoTracking()
            join c in _db.Customers.AsNoTracking() on o.CustomerId equals c.Id
            where o.CompanyId == companyId && o.PropertyId == propertyId
            orderby o.OwnedFrom, o.Id
            select new OwnerEntry(c.Code, o.SharePercent, o.IsRatepayer, o.OwnedFrom, o.OwnedTo, o.Reference))
            .ToListAsync(ct).ConfigureAwait(false);

    private async Task<Result<Property>> CheckAsync(long companyId, long propertyId, IReadOnlyList<OwnerShare> owners, CancellationToken ct)
    {
        string? problem = OwnershipShares.Problem(owners);
        if (problem is not null)
        {
            return Error.Validation("Mun.Shares", problem);
        }

        Property? property = await _db.Properties.FirstOrDefaultAsync(p => p.Id == propertyId && p.CompanyId == companyId, ct).ConfigureAwait(false);
        if (property is null)
        {
            return Error.NotFound("Mun.PropertyNotFound", $"Property {propertyId} not found.");
        }

        List<long> ids = owners.Select(o => o.CustomerId).ToList();
        int known = await _db.Customers.CountAsync(c => c.CompanyId == companyId && ids.Contains(c.Id), ct).ConfigureAwait(false);
        if (known != ids.Count)
        {
            return Error.NotFound("Mun.OwnerNotFound", "An owner is not a customer of this council.");
        }

        return property;
    }

    private void Record(Property property, IReadOnlyList<OwnerShare> owners, DateOnly from, string reference, string? deedsReference)
    {
        foreach (OwnerShare owner in owners)
        {
            _db.Set<PropertyOwner>().Add(new PropertyOwner(property.CompanyId, property.Id, owner.CustomerId, owner.SharePercent, owner.IsRatepayer, from, reference, deedsReference?.Trim()));
        }

        property.RatepayerCustomerId = owners.Single(o => o.IsRatepayer).CustomerId;
    }

    /// <inheritdoc />
    public async Task<Result<long>> AddPartyAsync(long companyId, long propertyId, NewPropertyParty party, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(party.Name))
        {
            return Error.Validation("Municipal.PartyName", "A tenant, occupier or agent needs a name.");
        }

        if (!await _db.Properties.AnyAsync(p => p.CompanyId == companyId && p.Id == propertyId, ct).ConfigureAwait(false))
        {
            return Error.NotFound("Municipal.PropertyNotFound", $"Property {propertyId} not found.");
        }

        long? customerId = null;
        if (!string.IsNullOrWhiteSpace(party.CustomerCode))
        {
            string code = party.CustomerCode.Trim().ToUpperInvariant();
            customerId = await _db.Customers.Where(c => c.CompanyId == companyId && c.Code == code).Select(c => (long?)c.Id).FirstOrDefaultAsync(ct).ConfigureAwait(false);
            if (customerId is null)
            {
                return Error.NotFound("Municipal.CustomerNotFound", $"Customer {code} not found.");
            }
        }

        if (party.ReceivesBills && string.IsNullOrWhiteSpace(party.Address) && string.IsNullOrWhiteSpace(party.Email))
        {
            return Error.Validation("Municipal.PartyAddress", "A party sent copies of the bills needs an address or an e-mail address.");
        }

        static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        PropertyParty added = new(companyId, propertyId, party.Role, party.Name.Trim(), party.From)
        {
            CustomerId = customerId, Phone = Clean(party.Phone), Email = Clean(party.Email), Address = Clean(party.Address), ReceivesBills = party.ReceivesBills,
        };
        _db.Set<PropertyParty>().Add(added);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return added.Id;
    }

    /// <inheritdoc />
    public async Task<Result> EndPartyAsync(long companyId, long partyId, DateOnly lastDay, CancellationToken ct = default)
    {
        if (await _db.Set<PropertyParty>().FirstOrDefaultAsync(p => p.CompanyId == companyId && p.Id == partyId, ct).ConfigureAwait(false) is not PropertyParty party)
        {
            return Result.Failure(Error.NotFound("Municipal.PartyNotFound", $"Party {partyId} not found."));
        }

        if (party.To is not null || lastDay < party.From)
        {
            return Result.Failure(Error.Conflict("Municipal.PartyEnded", "The party has already ended, or the last day is before it started."));
        }

        party.End(lastDay);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PropertyPartyEntry>> PartiesAsync(long companyId, long propertyId, DateOnly? asAtDate, CancellationToken ct = default)
    {
        IQueryable<PropertyParty> parties = _db.Set<PropertyParty>().AsNoTracking().Where(p => p.CompanyId == companyId && p.PropertyId == propertyId);
        if (asAtDate is DateOnly on)
        {
            parties = parties.Where(p => p.From <= on && (p.To == null || p.To >= on));
        }

        return await parties.OrderBy(p => p.Role).ThenBy(p => p.From)
            .Select(p => new PropertyPartyEntry(p.Id, p.Role, p.Name, _db.Customers.Where(c => c.Id == p.CustomerId).Select(c => c.Code).FirstOrDefault(), p.Phone, p.Email, p.Address,
                p.ReceivesBills, p.From, p.To))
            .ToListAsync(ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<Result> SetCorrespondenceAddressAsync(long companyId, long propertyId, string? address, CancellationToken ct = default)
    {
        if (await _db.Properties.FirstOrDefaultAsync(p => p.CompanyId == companyId && p.Id == propertyId, ct).ConfigureAwait(false) is not Property property)
        {
            return Result.Failure(Error.NotFound("Municipal.PropertyNotFound", $"Property {propertyId} not found."));
        }

        property.CorrespondenceAddress = string.IsNullOrWhiteSpace(address) ? null : address.Trim();
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<BillRecipient>>> BillRecipientsAsync(long companyId, long propertyId, DateOnly asAtDate, CancellationToken ct = default)
    {
        if (await _db.Properties.AsNoTracking().FirstOrDefaultAsync(p => p.CompanyId == companyId && p.Id == propertyId, ct).ConfigureAwait(false) is not Property property)
        {
            return Error.NotFound("Municipal.PropertyNotFound", $"Property {propertyId} not found.");
        }

        var ratepayer = await _db.Customers.AsNoTracking().Where(c => c.Id == property.RatepayerCustomerId).Select(c => new { c.Name, c.Email }).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        List<BillRecipient> recipients = [];
        if (ratepayer is not null)
        {
            recipients.Add(new BillRecipient(ratepayer.Name, property.CorrespondenceAddress ?? property.Address, ratepayer.Email, "Ratepayer"));
        }

        recipients.AddRange((await PartiesAsync(companyId, propertyId, asAtDate, ct).ConfigureAwait(false)).Where(p => p.ReceivesBills)
            .Select(p => new BillRecipient(p.Name, p.Address, p.Email, p.Role switch { PropertyPartyRole.ManagingAgent => "Managing agent", _ => p.Role.ToString() })));
        return recipients;
    }
}
