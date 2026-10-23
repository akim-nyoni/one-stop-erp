using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Accounting;
using One4All.Application.Abstractions.Housing;
using One4All.Application.Abstractions.Trading;
using One4All.Core.Common;
using One4All.Core.Housing;
using One4All.Core.System;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Housing;

/// <summary>
/// Housing, Land &amp; Estates (brief §4.10). Treats the land bank as IAS 2 inventory - acquisition and
/// servicing capitalise to WIP, subdivision releases cost to Stands Available for Sale, and the first sale
/// releases cost to cost of sales on transfer of control - while maintaining each stand's tamper-evident
/// chain of ownership. All money movements post through the central engine.
/// </summary>
public sealed class HousingService : IHousingService
{
    private readonly One4AllDbContext _db;
    private readonly IPostingEngine _posting;
    private readonly ISalesLedgerService _salesLedger;
    private readonly IHousingMunicipalLinkService _municipalLink;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="posting">The central posting engine.</param>
    /// <param name="salesLedger">The AR ledger (for stand sales).</param>
    /// <param name="municipalLink">The link to the municipal register.</param>
    public HousingService(One4AllDbContext db, IPostingEngine posting, ISalesLedgerService salesLedger, IHousingMunicipalLinkService municipalLink)
    {
        _db = db;
        _posting = posting;
        _salesLedger = salesLedger;
        _municipalLink = municipalLink;
    }

    /// <inheritdoc />
    public async Task<Result<long>> CreateSchemeAsync(long companyId, string code, string name, SchemeType schemeType, CancellationToken ct = default)
    {
        string key = (code ?? string.Empty).Trim().ToUpperInvariant();
        if (key.Length == 0 || string.IsNullOrWhiteSpace(name) || !Enum.IsDefined(schemeType))
        {
            return Error.Validation("Hse.Scheme", "A scheme needs a code, a name and its delivery model.");
        }

        if (await _db.HousingSchemes.AnyAsync(x => x.CompanyId == companyId && x.Code == key, ct).ConfigureAwait(false))
        {
            return Error.Conflict("Hse.SchemeExists", $"Scheme {key} already exists.");
        }

        HousingScheme scheme = new(companyId, key, name.Trim(), schemeType);
        _db.HousingSchemes.Add(scheme);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return scheme.Id;
    }

    /// <inheritdoc />
    public Task<Result<AcquisitionResult>> AcquireLandAsync(long companyId, string parcelNumber, string description, decimal areaSquareMetres, decimal cost, DateOnly date, string wipAccountCode, string fundingAccountCode, string documentNumber, CancellationToken ct = default)
        => _db.AtomicAsync(() => AcquireLandCoreAsync(companyId, parcelNumber, description, areaSquareMetres, cost, date, wipAccountCode, fundingAccountCode, documentNumber, ct), ct);

    // One unit of work: its postings and record changes commit together or not at all.
    private async Task<Result<AcquisitionResult>> AcquireLandCoreAsync(long companyId, string parcelNumber, string description, decimal areaSquareMetres, decimal cost, DateOnly date, string wipAccountCode, string fundingAccountCode, string documentNumber, CancellationToken ct)
    {
        if (cost <= 0m)
        {
            return Error.Validation("Housing.CostRequired", "Acquisition cost must be positive.");
        }

        Company? company = await _db.Set<Company>().FirstOrDefaultAsync(c => c.Id == companyId, ct).ConfigureAwait(false);
        if (company is null)
        {
            return Error.NotFound("Housing.CompanyNotFound", $"Company {companyId} not found.");
        }

        LandParcel parcel = new(companyId, parcelNumber, description, areaSquareMetres, cost);
        _db.Set<LandParcel>().Add(parcel);

        Result<PostingReceipt> posted = await _posting.PostAsync(new JournalRequest
        {
            CompanyId = companyId,
            SourceModule = "GL",
            PostingDate = date,
            TransactionCurrency = company.BaseCurrency,
            DocumentType = "LAND-ACQ",
            DocumentNumber = documentNumber,
            Narrative = $"Land acquisition {parcel.ParcelNumber}",
            Lines =
            [
                new JournalLineRequest { AccountCode = wipAccountCode, Side = JournalSide.Debit, TransactionAmount = cost },
                new JournalLineRequest { AccountCode = fundingAccountCode, Side = JournalSide.Credit, TransactionAmount = cost },
            ],
        }, ct).ConfigureAwait(false);

        if (posted.IsFailure)
        {
            return posted.Error;
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return new AcquisitionResult(parcel.Id, posted.Value.JournalNumber);
    }

    /// <inheritdoc />
    public Task<Result<SubdivisionResult>> SubdivideAsync(long companyId, long landParcelId, long? schemeId, IReadOnlyList<NewStand> stands, ApportionMethod method, DateOnly date, string wipAccountCode, string standsAvailableAccountCode, CancellationToken ct = default)
        => _db.AtomicAsync(() => SubdivideCoreAsync(companyId, landParcelId, schemeId, stands, method, date, wipAccountCode, standsAvailableAccountCode, ct), ct);

    // One unit of work: its postings and record changes commit together or not at all.
    private async Task<Result<SubdivisionResult>> SubdivideCoreAsync(long companyId, long landParcelId, long? schemeId, IReadOnlyList<NewStand> stands, ApportionMethod method, DateOnly date, string wipAccountCode, string standsAvailableAccountCode, CancellationToken ct)
    {
        if (stands.Count == 0)
        {
            return Error.Validation("Housing.NoStands", "At least one stand is required to subdivide.");
        }

        LandParcel? parcel = await _db.Set<LandParcel>().FirstOrDefaultAsync(p => p.Id == landParcelId && p.CompanyId == companyId, ct).ConfigureAwait(false);
        if (parcel is null)
        {
            return Error.NotFound("Housing.ParcelNotFound", $"Land parcel {landParcelId} not found.");
        }

        if (parcel.Status == LandParcelStatus.Subdivided)
        {
            return Error.Rule("Housing.AlreadySubdivided", $"Parcel {parcel.ParcelNumber} is already subdivided.");
        }

        Company? company = await _db.Set<Company>().FirstOrDefaultAsync(c => c.Id == companyId, ct).ConfigureAwait(false);
        if (company is null)
        {
            return Error.NotFound("Housing.CompanyNotFound", $"Company {companyId} not found.");
        }

        IReadOnlyList<decimal> shares = Apportion(parcel.AcquisitionCost, stands, method);
        List<long> standIds = [];
        List<HousingStand> created = [];
        for (int i = 0; i < stands.Count; i++)
        {
            HousingStand stand = new(companyId, parcel.Id, schemeId, stands[i].StandNumber, stands[i].SizeSquareMetres, shares[i], date);
            _db.Set<HousingStand>().Add(stand);
            created.Add(stand);
        }

        parcel.MarkSubdivided();

        Result<PostingReceipt> posted = await _posting.PostAsync(new JournalRequest
        {
            CompanyId = companyId,
            SourceModule = "GL",
            PostingDate = date,
            TransactionCurrency = company.BaseCurrency,
            DocumentType = "SUBDIVIDE",
            DocumentNumber = parcel.ParcelNumber,
            Narrative = $"Subdivision of {parcel.ParcelNumber} into {stands.Count} stands",
            Lines =
            [
                new JournalLineRequest { AccountCode = standsAvailableAccountCode, Side = JournalSide.Debit, TransactionAmount = parcel.AcquisitionCost },
                new JournalLineRequest { AccountCode = wipAccountCode, Side = JournalSide.Credit, TransactionAmount = parcel.AcquisitionCost },
            ],
        }, ct).ConfigureAwait(false);

        if (posted.IsFailure)
        {
            return posted.Error;
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        standIds.AddRange(created.Select(s => s.Id));
        return new SubdivisionResult(standIds, posted.Value.JournalNumber);
    }

    /// <inheritdoc />
    public Task<Result<StandSaleResult>> SellStandAsync(long companyId, long standId, long buyerCustomerId, string buyerName, decimal sellingPrice, DateOnly date, string salesAccountCode, string standsAvailableAccountCode, string costOfSalesAccountCode, string invoiceNumber, CancellationToken ct = default)
        => _db.AtomicAsync(() => SellStandCoreAsync(companyId, standId, buyerCustomerId, buyerName, sellingPrice, date, salesAccountCode, standsAvailableAccountCode, costOfSalesAccountCode, invoiceNumber, ct), ct);

    // One unit of work: its postings and record changes commit together or not at all.
    private async Task<Result<StandSaleResult>> SellStandCoreAsync(long companyId, long standId, long buyerCustomerId, string buyerName, decimal sellingPrice, DateOnly date, string salesAccountCode, string standsAvailableAccountCode, string costOfSalesAccountCode, string invoiceNumber, CancellationToken ct)
    {
        HousingStand? stand = await _db.Set<HousingStand>().Include(s => s.Ownership)
            .FirstOrDefaultAsync(s => s.Id == standId && s.CompanyId == companyId, ct).ConfigureAwait(false);
        if (stand is null)
        {
            return Error.NotFound("Housing.StandNotFound", $"Stand {standId} not found.");
        }

        if (stand.Status is StandStatus.Sold or StandStatus.Transferred)
        {
            return Error.Rule("Housing.AlreadySold", $"Stand {stand.StandNumber} has already been sold or transferred.");
        }

        if (stand.Status is not (StandStatus.Available or StandStatus.Offered or StandStatus.Allocated or StandStatus.Withheld))
        {
            return Error.Rule("Housing.StandNotForSale", $"Stand {stand.StandNumber} is {stand.Status} and cannot be sold.");
        }

        if (await StandClaims.AnyBlockedAsync(_db, [standId], ct).ConfigureAwait(false))
        {
            return Error.Rule("Housing.StandBlocked", $"Stand {stand.StandNumber} is blocked by a caveat or an open land dispute and cannot be sold.");
        }

        Company? company = await _db.Set<Company>().FirstOrDefaultAsync(c => c.Id == companyId, ct).ConfigureAwait(false);
        if (company is null)
        {
            return Error.NotFound("Housing.CompanyNotFound", $"Company {companyId} not found.");
        }

        // Transfer of control: raise the price on the beneficiary's AR account.
        Result<TradingPostingResult> sale = await _salesLedger.PostInvoiceAsync(new SalesInvoiceRequest
        {
            CompanyId = companyId,
            CustomerId = buyerCustomerId,
            DocumentNumber = invoiceNumber,
            DocumentDate = date,
            Currency = company.BaseCurrency,
            NetAmount = sellingPrice,
            TaxAmount = 0m,
            SalesAccountCode = salesAccountCode,
            Narrative = $"Sale of stand {stand.StandNumber} to {buyerName}",
        }, ct).ConfigureAwait(false);

        if (sale.IsFailure)
        {
            return sale.Error;
        }

        // Release the stand's apportioned cost to cost of sales.
        Result<PostingReceipt> cogs = await _posting.PostAsync(new JournalRequest
        {
            CompanyId = companyId,
            SourceModule = "GL",
            PostingDate = date,
            TransactionCurrency = company.BaseCurrency,
            DocumentType = "LAND-COGS",
            DocumentNumber = invoiceNumber,
            Narrative = $"Cost of sale - stand {stand.StandNumber}",
            Lines =
            [
                new JournalLineRequest { AccountCode = costOfSalesAccountCode, Side = JournalSide.Debit, TransactionAmount = stand.AllocatedCost },
                new JournalLineRequest { AccountCode = standsAvailableAccountCode, Side = JournalSide.Credit, TransactionAmount = stand.AllocatedCost },
            ],
        }, ct).ConfigureAwait(false);

        if (cogs.IsFailure)
        {
            return cogs.Error;
        }

        stand.Sell(buyerCustomerId, buyerName, sellingPrice, date);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        Result<MunicipalLinkResult> linked = await _municipalLink.OnEventAsync(companyId, standId, buyerCustomerId, MunicipalLinkTrigger.Sale, date, null, ct).ConfigureAwait(false);
        if (linked.IsFailure)
        {
            return linked.Error;
        }

        return new StandSaleResult(sale.Value.OpenItemId, sale.Value.JournalNumber, cogs.Value.JournalNumber, stand.AllocatedCost);
    }

    /// <inheritdoc />
    public Task<Result> TransferOwnershipAsync(long companyId, long standId, long newHolderCustomerId, string newHolderName, decimal consideration, DateOnly date, bool housingAccountClear, bool dualApprovalRecorded, CancellationToken ct = default)
        => _db.AtomicAsync(() => TransferOwnershipCoreAsync(companyId, standId, newHolderCustomerId, newHolderName, consideration, date, housingAccountClear, dualApprovalRecorded, ct), ct);

    // One unit of work: its postings and record changes commit together or not at all.
    private async Task<Result> TransferOwnershipCoreAsync(long companyId, long standId, long newHolderCustomerId, string newHolderName, decimal consideration, DateOnly date, bool housingAccountClear, bool dualApprovalRecorded, CancellationToken ct)
    {
        HousingStand? stand = await _db.Set<HousingStand>().Include(s => s.Ownership)
            .FirstOrDefaultAsync(s => s.Id == standId && s.CompanyId == companyId, ct).ConfigureAwait(false);
        if (stand is null)
        {
            return Result.Failure(Error.NotFound("Housing.StandNotFound", $"Stand {standId} not found."));
        }

        if (await StandClaims.AnyBlockedAsync(_db, [standId], ct).ConfigureAwait(false))
        {
            return Result.Failure(Error.Rule("Housing.StandBlocked", $"Stand {stand.StandNumber} is blocked by a caveat or an open land dispute and cannot be transferred."));
        }

        if (await StandClaims.OpenTitleApplicationAsync(_db, standId, ct).ConfigureAwait(false) is string titleApplication)
        {
            return Result.Failure(Error.Rule("Hse.TitleApplicationOpen", $"Stand {stand.StandNumber} has title application {titleApplication} in progress; it cannot be transferred until the deed is registered."));
        }

        if (!housingAccountClear)
        {
            return Result.Failure(Error.Rule("Housing.AccountNotClear", "Transfer blocked: the outgoing holder's housing account is not clear (brief §4.10.6)."));
        }

        if (!dualApprovalRecorded)
        {
            return Result.Failure(Error.Rule("Housing.ApprovalRequired", "Transfer blocked: value-banded dual approval is required (brief §4.10.6/§7)."));
        }

        stand.Cede(newHolderCustomerId, newHolderName, consideration, date);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    private static IReadOnlyList<decimal> Apportion(decimal total, IReadOnlyList<NewStand> stands, ApportionMethod method)
    {
        decimal[] shares = new decimal[stands.Count];
        if (method == ApportionMethod.ByArea)
        {
            decimal totalArea = stands.Sum(s => s.SizeSquareMetres);
            if (totalArea > 0m)
            {
                decimal running = 0m;
                for (int i = 0; i < stands.Count; i++)
                {
                    shares[i] = decimal.Round(total * stands[i].SizeSquareMetres / totalArea, 4, MidpointRounding.ToEven);
                    running += shares[i];
                }

                // Push any rounding remainder onto the last stand so the shares sum exactly to the total.
                shares[^1] += total - running;
                return shares;
            }
        }

        decimal per = decimal.Round(total / stands.Count, 4, MidpointRounding.ToEven);
        decimal allocated = 0m;
        for (int i = 0; i < stands.Count; i++)
        {
            shares[i] = per;
            allocated += per;
        }

        shares[^1] += total - allocated;
        return shares;
    }
}
