using System.Globalization;
using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Housing;
using One4All.Application.Abstractions.Reporting;
using One4All.Application.Housing;
using One4All.Core.Housing;
using One4All.Core.JobCosting;
using One4All.Infrastructure.Housing;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Reporting;

/// <summary>Supplies live rows for the Housing, Land &amp; Estates report catalogue (brief §14 I-B, the H series).</summary>
public sealed class HousingReportDataProvider : IReportDataProvider
{
    private static readonly AllocationStatus[] Live = [AllocationStatus.Proposed, AllocationStatus.Offered, AllocationStatus.Accepted];

    private readonly One4AllDbContext _db;
    private readonly IWaitingListService _waitingList;
    private readonly ITitleDeedService _titles;
    private readonly ITransferApplicationService _transfers;

    /// <summary>Creates the provider.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="waitingList">The waiting list (for rankings and scores).</param>
    /// <param name="titles">The title deed pipeline.</param>
    /// <param name="transfers">Change-of-ownership applications (for their live checks).</param>
    public HousingReportDataProvider(One4AllDbContext db, IWaitingListService waitingList, ITitleDeedService titles, ITransferApplicationService transfers)
    {
        _db = db;
        _waitingList = waitingList;
        _titles = titles;
        _transfers = transfers;
    }

    /// <inheritdoc />
    public bool CanHandle(string command) => command.StartsWith("Housing", StringComparison.OrdinalIgnoreCase) && !command.Equals("HousingStands", StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc />
    public async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> GetRowsAsync(long companyId, string command, IReadOnlyDictionary<string, object?> parameters, CancellationToken ct = default)
    {
        DateOnly asAt = AsAt(parameters);
        return command.ToUpperInvariant() switch
        {
            "HOUSINGLANDBANK" => await LandBankAsync(companyId, ct).ConfigureAwait(false),
            "HOUSINGACQUISITIONS" => await AcquisitionsAsync(companyId, ct).ConfigureAwait(false),
            "HOUSINGDUEDILIGENCE" => await DueDiligenceAsync(companyId, ct).ConfigureAwait(false),
            "HOUSINGCOMPENSATION" => await CompensationAsync(companyId, ct).ConfigureAwait(false),
            "HOUSINGLAYOUTS" => await LayoutsAsync(companyId, ct).ConfigureAwait(false),
            "HOUSINGSERVICING" => await ServicingAsync(companyId, ct).ConfigureAwait(false),
            "HOUSINGPRICEBUILDUP" => await PriceBuildUpAsync(companyId, asAt, ct).ConfigureAwait(false),
            "HOUSINGWAITINGLIST" => await WaitingListAsync(companyId, asAt, ct).ConfigureAwait(false),
            "HOUSINGWAITINGAGEING" => await WaitingAgeingAsync(companyId, asAt, ct).ConfigureAwait(false),
            "HOUSINGREGISTRATIONCHANGES" => await RegistrationChangesAsync(companyId, ct).ConfigureAwait(false),
            "HOUSINGALLOCATIONS" => await AllocationsAsync(companyId, ct).ConfigureAwait(false),
            "HOUSINGOFFERS" => await OffersAsync(companyId, asAt, ct).ConfigureAwait(false),
            "HOUSINGAPPEALS" => await AppealsAsync(companyId, ct).ConfigureAwait(false),
            "HOUSINGDOUBLEALLOCATIONS" => await DoubleAllocationsAsync(companyId, ct).ConfigureAwait(false),
            "HOUSINGREADYFORTITLE" => await ReadyForTitleAsync(companyId, ct).ConfigureAwait(false),
            "HOUSINGCHAINOFTITLE" => await ChainOfTitleAsync(companyId, parameters, ct).ConfigureAwait(false),
            "HOUSINGTRANSFERS" => await TransfersAsync(companyId, ct).ConfigureAwait(false),
            "HOUSINGTRANSFERSOPEN" => await TransfersOpenAsync(companyId, asAt, ct).ConfigureAwait(false),
            "HOUSINGTRANSFERSBLOCKED" => await TransfersBlockedAsync(companyId, asAt, ct).ConfigureAwait(false),
            "HOUSINGTITLEPIPELINE" => await TitlePipelineAsync(companyId, asAt, ct).ConfigureAwait(false),
            "HOUSINGDEEDSUNCOLLECTED" => await DeedsUncollectedAsync(companyId, asAt, ct).ConfigureAwait(false),
            "HOUSINGDEEDS" => await DeedsAsync(companyId, ct).ConfigureAwait(false),
            "HOUSINGBLOCKS" => await BlocksAsync(companyId, asAt, ct).ConfigureAwait(false),
            "HOUSINGBROKENCHAINS" => await BrokenChainsAsync(companyId, ct).ConfigureAwait(false),
            "HOUSINGSECTIONALUNITS" => await SectionalUnitsAsync(companyId, ct).ConfigureAwait(false),
            _ => [],
        };
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> LandBankAsync(long companyId, CancellationToken ct)
        => [.. (await _db.LandParcels.AsNoTracking().Where(p => p.CompanyId == companyId).OrderBy(p => p.ParcelNumber).ToListAsync(ct).ConfigureAwait(false))
            .Select(p => Row(("Parcel", p.ParcelNumber), ("Description", p.Description), ("AreaM2", p.AreaSquareMetres),
                ("MappedM2", p.Boundary is { } b ? b.AreaSquareMetres.ToString("N0", CultureInfo.InvariantCulture) : "—"), ("Status", p.Status.ToString()), ("Cost", p.AcquisitionCost)))];

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> AcquisitionsAsync(long companyId, CancellationToken ct)
        => [.. (await _db.Set<LandAcquisition>().AsNoTracking().Include(a => a.Costs).Where(a => a.CompanyId == companyId).OrderBy(a => a.AcquisitionNumber).ToListAsync(ct).ConfigureAwait(false))
            .Select(a => Row(("Acquisition", a.AcquisitionNumber), ("Parcel", a.ParcelNumber), ("Method", a.Method.ToString()), ("Counterparty", a.Counterparty),
                ("Resolution", a.ResolutionReference), ("Status", a.Status.ToString()), ("Cost", a.TotalCost)))];

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> DueDiligenceAsync(long companyId, CancellationToken ct)
    {
        List<LandAcquisition> open = await _db.Set<LandAcquisition>().AsNoTracking().Include(a => a.Checks).Include(a => a.Costs)
            .Where(a => a.CompanyId == companyId && a.Status == AcquisitionStatus.Open).OrderBy(a => a.AcquisitionNumber).ToListAsync(ct).ConfigureAwait(false);
        return [.. open.Select(a => (Acquisition: a, Problems: AcquisitionReadiness.Problems(a.Method, [.. a.Checks.Select(c => new SignedOffItem(c.Item, c.Acceptable))], a.ValuedAt is not null,
                AcquisitionReadiness.BuildUp(a.Costs.Select(c => new CostElement(c.Component, c.Amount))))))
            .Where(x => x.Problems.Count > 0)
            .Select(x => Row(("Acquisition", x.Acquisition.AcquisitionNumber), ("Parcel", x.Acquisition.ParcelNumber), ("Outstanding", string.Join(" ", x.Problems))))];
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> CompensationAsync(long companyId, CancellationToken ct)
        => [.. (await (from c in _db.Set<CompensationClaim>().AsNoTracking()
                       join p in _db.LandParcels.AsNoTracking() on c.LandParcelId equals p.Id
                       where c.CompanyId == companyId
                       orderby c.ClaimNumber
                       select new { c.ClaimNumber, p.ParcelNumber, c.Claimant, c.LodgedOn, c.Status, c.Provision, c.SettledAmount }).ToListAsync(ct).ConfigureAwait(false))
            .Select(c => Row(("Claim", c.ClaimNumber), ("Parcel", c.ParcelNumber), ("Claimant", c.Claimant), ("Lodged", Date(c.LodgedOn)), ("Status", c.Status.ToString()),
                ("Provision", c.Provision), ("Settled", c.SettledAmount ?? 0m)))];

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> LayoutsAsync(long companyId, CancellationToken ct)
    {
        List<LayoutPlan> plans = await _db.Set<LayoutPlan>().AsNoTracking().Include(p => p.Versions).ThenInclude(v => v.Uses)
            .Where(p => p.CompanyId == companyId).OrderBy(p => p.PlanNumber).ToListAsync(ct).ConfigureAwait(false);
        Dictionary<long, string> parcels = await _db.LandParcels.AsNoTracking().Where(p => p.CompanyId == companyId).ToDictionaryAsync(p => p.Id, p => p.ParcelNumber, ct).ConfigureAwait(false);
        return [.. plans.Select(p =>
        {
            LayoutPlanVersion? inForce = p.InForce;
            LayoutPlanVersion latest = p.Latest;
            string status = inForce is null ? "Awaiting approval" : latest.ApprovedOn is null ? $"v{latest.Version} awaiting approval" : "Approved";
            LayoutPlanVersion shown = inForce ?? latest;
            return Row(("Plan", p.PlanNumber), ("Parcel", parcels.GetValueOrDefault(p.LandParcelId, "?")), ("Version", shown.Version), ("Status", status),
                ("Stands", LayoutChanges.Stands(shown.Uses.ToDictionary(u => u.Use, u => u.Stands))),
                ("TurnaroundDays", inForce?.ApprovedOn is DateOnly approved ? (approved.DayNumber - inForce.SubmittedOn.DayNumber).ToString(CultureInfo.InvariantCulture) : "—"));
        })];
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> ServicingAsync(long companyId, CancellationToken ct)
        => [.. (await (from c in _db.Set<ServicingCharge>().AsNoTracking()
                       join l in _db.Set<ServicingLink>().AsNoTracking() on c.ServicingLinkId equals l.Id
                       join j in _db.Set<JobContract>().AsNoTracking() on l.JobContractId equals j.Id
                       join s in _db.HousingStands.AsNoTracking() on c.StandId equals s.Id
                       where c.CompanyId == companyId
                       orderby s.StandNumber, c.Date, c.Id
                       select new { s.StandNumber, j.ContractNumber, l.Basis, c.Date, c.JournalNumber, c.Amount }).ToListAsync(ct).ConfigureAwait(false))
            .Select(c => Row(("Stand", c.StandNumber), ("Contract", c.ContractNumber), ("Basis", c.Basis.ToString()), ("Date", Date(c.Date)), ("Journal", c.JournalNumber), ("Amount", c.Amount)))];

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> PriceBuildUpAsync(long companyId, DateOnly asAt, CancellationToken ct)
    {
        List<HousingStand> stands = await _db.HousingStands.AsNoTracking().Where(s => s.CompanyId == companyId && s.Status == StandStatus.Available).OrderBy(s => s.StandNumber).ToListAsync(ct).ConfigureAwait(false);
        List<IReadOnlyDictionary<string, object?>> rows = [];
        foreach (HousingStand stand in stands)
        {
            if (await StandPrices.QuoteAsync(_db, stand, asAt, ct).ConfigureAwait(false) is { } quote)
            {
                PriceBuildUp p = quote.BuildUp;
                decimal marginPercent = p.Price == 0m ? 0m : Math.Round(p.Margin / p.Price * 100m, 1, MidpointRounding.AwayFromZero);
                rows.Add(Row(("Stand", stand.StandNumber), ("Scope", quote.Scope.ToString()), ("Cost", p.Cost), ("Overhead", p.Overhead), ("Levy", p.Levy), ("Margin", p.Margin),
                    ("MarginPercent", marginPercent.ToString("0.0", CultureInfo.InvariantCulture)), ("Price", p.Price)));
            }
        }

        return rows;
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> WaitingListAsync(long companyId, DateOnly asAt, CancellationToken ct)
    {
        List<IReadOnlyDictionary<string, object?>> rows = [];
        foreach (HousingCategory category in Enum.GetValues<HousingCategory>())
        {
            foreach (WaitingListPlace place in await _waitingList.RankingAsync(companyId, category, WaitingListOrder.Seniority, asAt, ct).ConfigureAwait(false))
            {
                rows.Add(Row(("Category", category.ToString()), ("Position", place.Position), ("WaitingListNumber", place.WaitingListNumber), ("Name", place.FullName),
                    ("Registered", Date(place.RegistrationDate)), ("Score", place.Score.ToString("0.##", CultureInfo.InvariantCulture))));
            }
        }

        return rows;
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> WaitingAgeingAsync(long companyId, DateOnly asAt, CancellationToken ct)
    {
        var active = await _db.HousingApplicants.AsNoTracking()
            .Where(a => a.CompanyId == companyId && a.Status == ApplicantStatus.Active && a.WaitingListNumber != null && a.RegistrationDate <= asAt)
            .Select(a => new { a.Category, a.RegistrationDate }).ToListAsync(ct).ConfigureAwait(false);
        return [.. active.GroupBy(a => a.Category).OrderBy(g => g.Key).Select(g =>
        {
            Dictionary<WaitingAgeBand, int> bands = g.GroupBy(a => WaitingListAgeing.Band(a.RegistrationDate, asAt)).ToDictionary(b => b.Key, b => b.Count());
            return Row(("Category", g.Key.ToString()), ("UnderOne", bands.GetValueOrDefault(WaitingAgeBand.UnderOne)), ("OneToThree", bands.GetValueOrDefault(WaitingAgeBand.OneToThree)),
                ("ThreeToFive", bands.GetValueOrDefault(WaitingAgeBand.ThreeToFive)), ("FiveToTen", bands.GetValueOrDefault(WaitingAgeBand.FiveToTen)),
                ("TenPlus", bands.GetValueOrDefault(WaitingAgeBand.TenPlus)), ("Total", g.Count()));
        })];
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> RegistrationChangesAsync(long companyId, CancellationToken ct)
        => [.. (await (from c in _db.Set<RegistrationDateChange>().AsNoTracking()
                       join a in _db.HousingApplicants.AsNoTracking() on c.ApplicantId equals a.Id
                       where c.CompanyId == companyId
                       orderby c.Id
                       select new { a.WaitingListNumber, c.OldDate, c.NewDate, c.Reason, c.Status, c.RequestedByUserId, c.DecidedByUserId }).ToListAsync(ct).ConfigureAwait(false))
            .Select(c => Row(("WaitingListNumber", c.WaitingListNumber), ("OldDate", Date(c.OldDate)), ("NewDate", Date(c.NewDate)), ("Reason", c.Reason), ("Status", c.Status.ToString()),
                ("RequestedBy", c.RequestedByUserId), ("DecidedBy", c.DecidedByUserId?.ToString(CultureInfo.InvariantCulture) ?? "—")))];

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> AllocationsAsync(long companyId, CancellationToken ct)
        => [.. (await (from a in _db.Set<StandAllocation>().AsNoTracking()
                       join r in _db.Set<AllocationRun>().AsNoTracking() on a.AllocationRunId equals r.Id
                       join s in _db.HousingStands.AsNoTracking() on a.StandId equals s.Id
                       join p in _db.HousingApplicants.AsNoTracking() on a.ApplicantId equals p.Id
                       where a.CompanyId == companyId && a.CertificateNumber != null
                       orderby a.CertificateNumber
                       select new { a.CertificateNumber, r.RunNumber, s.StandNumber, p.WaitingListNumber, p.FullName, a.Status, r.ApprovedOn, a.OfferedPrice }).ToListAsync(ct).ConfigureAwait(false))
            .Select(a => Row(("Certificate", a.CertificateNumber), ("Run", a.RunNumber), ("Stand", a.StandNumber), ("WaitingListNumber", a.WaitingListNumber), ("Beneficiary", a.FullName),
                ("Status", a.Status.ToString()), ("ApprovedOn", a.ApprovedOn is DateOnly d ? Date(d) : "—"), ("Price", a.OfferedPrice ?? 0m)))];

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> OffersAsync(long companyId, DateOnly asAt, CancellationToken ct)
        => [.. (await (from a in _db.Set<StandAllocation>().AsNoTracking()
                       join s in _db.HousingStands.AsNoTracking() on a.StandId equals s.Id
                       join p in _db.HousingApplicants.AsNoTracking() on a.ApplicantId equals p.Id
                       where a.CompanyId == companyId && a.Status == AllocationStatus.Offered && a.OfferExpires != null
                       orderby a.OfferExpires, a.CertificateNumber
                       select new { a.CertificateNumber, s.StandNumber, p.FullName, Expires = a.OfferExpires!.Value }).ToListAsync(ct).ConfigureAwait(false))
            .Select(o =>
            {
                int days = o.Expires.DayNumber - asAt.DayNumber;
                return Row(("Certificate", o.CertificateNumber), ("Stand", o.StandNumber), ("Beneficiary", o.FullName), ("Expires", Date(o.Expires)), ("DaysLeft", days),
                    ("State", days < 0 ? "Expired" : days <= 7 ? "Expiring" : "Outstanding"));
            })];

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> AppealsAsync(long companyId, CancellationToken ct)
        => [.. (await (from a in _db.Set<AllocationAppeal>().AsNoTracking()
                       join p in _db.HousingApplicants.AsNoTracking() on a.ApplicantId equals p.Id
                       join r in _db.Set<AllocationRun>().AsNoTracking() on a.AllocationRunId equals r.Id
                       where a.CompanyId == companyId
                       orderby a.AppealNumber
                       select new { a.AppealNumber, p.WaitingListNumber, r.RunNumber, a.LodgedOn, a.Status, a.HearingDate, a.OutcomeReasons }).ToListAsync(ct).ConfigureAwait(false))
            .Select(a => Row(("Appeal", a.AppealNumber), ("WaitingListNumber", a.WaitingListNumber), ("Run", a.RunNumber), ("Lodged", Date(a.LodgedOn)), ("Status", a.Status.ToString()),
                ("Hearing", a.HearingDate is DateOnly h ? Date(h) : "—"), ("Outcome", a.OutcomeReasons ?? "—")))];

    // Every route a stand can be promised through, counted per stand; more than one is an incident.
    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> DoubleAllocationsAsync(long companyId, CancellationToken ct)
    {
        List<(long Stand, string Route)> claims = [];
        claims.AddRange((await _db.Set<StandAllocation>().AsNoTracking().Where(a => a.CompanyId == companyId && Live.Contains(a.Status))
            .Select(a => new { a.StandId, a.CertificateNumber, a.Status }).ToListAsync(ct).ConfigureAwait(false)).Select(a => (a.StandId, $"allocation {a.CertificateNumber ?? "(proposed)"} ({a.Status})")));
        claims.AddRange((await (from c in _db.Set<CooperativeStand>().AsNoTracking() join co in _db.Set<Cooperative>().AsNoTracking() on c.CooperativeId equals co.Id
                                where c.CompanyId == companyId select new { c.StandId, co.Code }).ToListAsync(ct).ConfigureAwait(false)).Select(c => (c.StandId, $"cooperative {c.Code}")));
        claims.AddRange((await _db.Set<EnumeratedHousehold>().AsNoTracking().Where(h => h.CompanyId == companyId && h.StandId != null)
            .Select(h => new { Stand = h.StandId!.Value, h.HouseholdNumber }).ToListAsync(ct).ConfigureAwait(false)).Select(h => (h.Stand, $"regularisation {h.HouseholdNumber}")));
        claims.AddRange((await (from d in _db.Set<DeveloperAgreementStand>().AsNoTracking() join a in _db.Set<DeveloperAgreement>().AsNoTracking() on d.DeveloperAgreementId equals a.Id
                                where a.CompanyId == companyId && !(d.Quota == StandQuota.Council && d.HandedOverOn != null)
                                select new { d.StandId, a.AgreementNumber, d.Quota }).ToListAsync(ct).ConfigureAwait(false)).Select(d => (d.StandId, $"developer {d.AgreementNumber} ({d.Quota} quota)")));
        List<IGrouping<long, (long Stand, string Route)>> doubled = [.. claims.GroupBy(c => c.Stand).Where(g => g.Count() > 1)];
        if (doubled.Count == 0)
        {
            return [];
        }

        List<long> ids = [.. doubled.Select(g => g.Key)];
        Dictionary<long, string> numbers = await _db.HousingStands.AsNoTracking().Where(s => ids.Contains(s.Id)).ToDictionaryAsync(s => s.Id, s => s.StandNumber, ct).ConfigureAwait(false);
        return [.. doubled.OrderBy(g => numbers[g.Key], StringComparer.Ordinal)
            .Select(g => Row(("Stand", numbers[g.Key]), ("Claims", g.Count()), ("Routes", string.Join("; ", g.Select(c => c.Route)))))];
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> ReadyForTitleAsync(long companyId, CancellationToken ct)
    {
        List<HousingStand> sold = await _db.HousingStands.AsNoTracking().Include(s => s.Ownership)
            .Where(s => s.CompanyId == companyId && s.CurrentHolderCustomerId != null && (s.Status == StandStatus.Sold || s.Status == StandStatus.Transferred)
                && !_db.Set<TitleDeed>().Any(t => t.StandId == s.Id)
                && !_db.Set<InstalmentAgreement>().Any(a => a.StandId == s.Id && a.Status == InstalmentAgreementStatus.Active))
            .OrderBy(s => s.StandNumber).ToListAsync(ct).ConfigureAwait(false);
        List<long> holders = [.. sold.Select(s => s.CurrentHolderCustomerId!.Value).Distinct()];
        Dictionary<long, decimal> owing = await _db.OpenItems.Where(i => i.CompanyId == companyId && i.Ledger == Core.Trading.TradingLedger.AccountsReceivable && holders.Contains(i.TraderId))
            .GroupBy(i => i.TraderId).Select(g => new { g.Key, Net = g.Sum(i => i.Sign == Core.Trading.LedgerSign.Debit ? i.OutstandingBase : -i.OutstandingBase) })
            .ToDictionaryAsync(x => x.Key, x => x.Net, ct).ConfigureAwait(false);
        return [.. sold.Where(s => owing.GetValueOrDefault(s.CurrentHolderCustomerId!.Value) <= 0m).Select(s =>
        {
            OwnershipRecord? sale = s.Ownership.LastOrDefault(o => o.ChangeType is OwnershipChangeType.Sale or OwnershipChangeType.Cession);
            return Row(("Stand", s.StandNumber), ("Holder", sale?.HolderName ?? "?"), ("Status", s.Status.ToString()), ("SoldOn", sale is null ? "—" : Date(sale.EffectiveDate)), ("Price", s.SellingPrice));
        })];
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> ChainOfTitleAsync(long companyId, IReadOnlyDictionary<string, object?> parameters, CancellationToken ct)
    {
        string? only = parameters.TryGetValue("StandNumber", out object? value) && value is string text && text.Length > 0 ? text.Trim() : null;
        List<HousingStand> stands = await _db.HousingStands.AsNoTracking().Include(s => s.Ownership)
            .Where(s => s.CompanyId == companyId && (only == null || s.StandNumber == only)).OrderBy(s => s.StandNumber).ToListAsync(ct).ConfigureAwait(false);
        return [.. stands.SelectMany(s =>
        {
            string verified = s.VerifyOwnershipChain() ? "OK" : "BROKEN";
            return s.Ownership.Select(o => Row(("Stand", s.StandNumber), ("Sequence", o.Sequence), ("ChangeType", o.ChangeType.ToString()), ("Holder", o.HolderName),
                ("Effective", Date(o.EffectiveDate)), ("ChainVerified", verified), ("Consideration", o.Consideration)));
        })];
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> TransfersAsync(long companyId, CancellationToken ct)
        => [.. (await (from t in _db.Set<TransferApplication>().AsNoTracking()
                       join s in _db.HousingStands.AsNoTracking() on t.StandId equals s.Id
                       join f in _db.Customers.AsNoTracking() on t.FromCustomerId equals f.Id
                       join c in _db.Customers.AsNoTracking() on t.ToCustomerId equals c.Id
                       where t.CompanyId == companyId && t.Status == TransferStatus.Completed
                       orderby t.DecidedOn, t.ApplicationNumber
                       select new { t.ApplicationNumber, s.StandNumber, t.TransferType, From = f.Name, To = c.Name, t.DecidedOn, t.ClearanceNumber, t.Consideration }).ToListAsync(ct).ConfigureAwait(false))
            .Select(t => Row(("Application", t.ApplicationNumber), ("Stand", t.StandNumber), ("Type", t.TransferType.ToString()), ("From", t.From), ("To", t.To),
                ("Completed", t.DecidedOn is DateOnly d ? Date(d) : "—"), ("Clearance", t.ClearanceNumber ?? "—"), ("Consideration", t.Consideration)))];

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> TransfersOpenAsync(long companyId, DateOnly asAt, CancellationToken ct)
    {
        List<TransferApplication> open = await _db.Set<TransferApplication>().AsNoTracking().Include(t => t.Approvals)
            .Where(t => t.CompanyId == companyId && t.Status != TransferStatus.Completed && t.Status != TransferStatus.Rejected && t.LodgedOn <= asAt)
            .OrderBy(t => t.LodgedOn).ThenBy(t => t.ApplicationNumber).ToListAsync(ct).ConfigureAwait(false);
        Dictionary<long, string> stands = await StandNumbersAsync(companyId, ct).ConfigureAwait(false);
        return [.. open.Select(t => Row(("Application", t.ApplicationNumber), ("Stand", stands.GetValueOrDefault(t.StandId, "?")), ("Type", t.TransferType.ToString()), ("Status", t.Status.ToString()),
            ("Approvals", t.Approvals.Count), ("DaysOpen", asAt.DayNumber - t.LodgedOn.DayNumber),
            ("Waiting", t.Status switch
            {
                TransferStatus.Lodged => "Checklist verification and submission",
                TransferStatus.Submitted => t.Approvals.Count == 0 ? "First approval" : "Second approval",
                _ => "Completion (registers update)",
            })))];
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> TransfersBlockedAsync(long companyId, DateOnly asAt, CancellationToken ct)
    {
        List<TransferApplication> open = await _db.Set<TransferApplication>().AsNoTracking()
            .Where(t => t.CompanyId == companyId && t.Status != TransferStatus.Completed && t.Status != TransferStatus.Rejected)
            .OrderBy(t => t.ApplicationNumber).ToListAsync(ct).ConfigureAwait(false);
        Dictionary<long, string> stands = await StandNumbersAsync(companyId, ct).ConfigureAwait(false);
        List<IReadOnlyDictionary<string, object?>> rows = [];
        foreach (TransferApplication transfer in open)
        {
            var checklist = await _transfers.ChecklistAsync(companyId, transfer.ApplicationNumber, asAt, ct).ConfigureAwait(false);
            foreach (TransferChecklistLine line in checklist.IsSuccess ? checklist.Value.Where(l => l.IsLive && !l.Satisfied) : [])
            {
                rows.Add(Row(("Application", transfer.ApplicationNumber), ("Stand", stands.GetValueOrDefault(transfer.StandId, "?")), ("Check", line.Requirement.ToString()), ("Detail", line.Detail)));
            }
        }

        return rows;
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> TitlePipelineAsync(long companyId, DateOnly asAt, CancellationToken ct)
        => [.. (await _titles.PipelineAsync(companyId, asAt, ct).ConfigureAwait(false)).Applications
            .Select(t => Row(("Application", t.ApplicationNumber), ("Stand", t.StandNumber), ("Stage", t.Stage.ToString()), ("DaysAtStage", t.DaysAtStage),
                ("DaysSinceApplied", t.DaysSinceApplied), ("Conveyancer", t.Conveyancer ?? "—")))];

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> DeedsUncollectedAsync(long companyId, DateOnly asAt, CancellationToken ct)
        => [.. (await (from t in _db.Set<TitleDeed>().AsNoTracking()
                       join s in _db.HousingStands.AsNoTracking() on t.StandId equals s.Id
                       join c in _db.Customers.AsNoTracking() on t.HolderCustomerId equals c.Id
                       where t.CompanyId == companyId && t.Stage == TitleStage.InCustody && t.StageSince <= asAt
                       orderby t.StageSince
                       select new { t.ApplicationNumber, s.StandNumber, t.DeedNumber, c.Name, c.Email, c.MobileNumber, t.StageSince }).ToListAsync(ct).ConfigureAwait(false))
            .Select(t => Row(("Application", t.ApplicationNumber), ("Stand", t.StandNumber), ("Deed", t.DeedNumber ?? "—"), ("Owner", t.Name),
                ("Contact", Contact(t.MobileNumber, t.Email)), ("DaysHeld", asAt.DayNumber - t.StageSince.DayNumber)))];

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> DeedsAsync(long companyId, CancellationToken ct)
        => [.. (await (from t in _db.Set<TitleDeed>().AsNoTracking()
                       join s in _db.HousingStands.AsNoTracking() on t.StandId equals s.Id
                       where t.CompanyId == companyId && t.DeedNumber != null
                       orderby t.RegisteredOn, t.DeedNumber
                       select new { t.DeedNumber, s.StandNumber, t.RegisteredOn, t.Stage, t.Conveyancer, t.ReceivedBy }).ToListAsync(ct).ConfigureAwait(false))
            .Select(t => Row(("Deed", t.DeedNumber), ("Stand", t.StandNumber), ("Registered", t.RegisteredOn is DateOnly r ? Date(r) : "—"),
                ("Custody", t.Stage switch
                {
                    TitleStage.HandedOver => $"Handed over to {t.ReceivedBy}",
                    TitleStage.InCustody => "Council custody",
                    _ => $"With conveyancer {t.Conveyancer}",
                })))];

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> BlocksAsync(long companyId, DateOnly asAt, CancellationToken ct)
        => [.. (await (from b in _db.Set<StandBlock>().AsNoTracking()
                       join s in _db.HousingStands.AsNoTracking() on b.StandId equals s.Id
                       where b.CompanyId == companyId && b.PlacedOn <= asAt
                       orderby s.StandNumber, b.PlacedOn
                       select new { s.StandNumber, b.Reason, b.Detail, b.DocumentReference, b.PlacedOn, b.LiftedOn }).ToListAsync(ct).ConfigureAwait(false))
            .Select(b =>
            {
                bool lifted = b.LiftedOn is DateOnly l && l <= asAt;
                return Row(("Stand", b.StandNumber), ("Reason", b.Reason.ToString()), ("Detail", b.Detail), ("Document", b.DocumentReference), ("PlacedOn", Date(b.PlacedOn)),
                    ("State", lifted ? $"Lifted {Date(b.LiftedOn!.Value)}" : "In force"), ("AgeDays", (lifted ? b.LiftedOn!.Value : asAt).DayNumber - b.PlacedOn.DayNumber));
            })];

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> BrokenChainsAsync(long companyId, CancellationToken ct)
    {
        List<HousingStand> stands = await _db.HousingStands.AsNoTracking().Include(s => s.Ownership).Where(s => s.CompanyId == companyId).OrderBy(s => s.StandNumber).ToListAsync(ct).ConfigureAwait(false);
        List<IReadOnlyDictionary<string, object?>> rows = [];
        foreach (HousingStand stand in stands)
        {
            if (!stand.VerifyOwnershipChain())
            {
                rows.Add(Row(("Stand", stand.StandNumber), ("Problem", "The ownership chain does not verify — a record was altered or removed.")));
            }

            if (stand.Status is StandStatus.Sold or StandStatus.Transferred && stand.CurrentHolderCustomerId is null)
            {
                rows.Add(Row(("Stand", stand.StandNumber), ("Problem", $"{stand.Status} but no recorded holder.")));
            }

            if (stand.CurrentHolderCustomerId is long holder && stand.Ownership.LastOrDefault()?.HolderCustomerId != holder)
            {
                rows.Add(Row(("Stand", stand.StandNumber), ("Problem", "The current holder is not the last holder on the ownership chain.")));
            }
        }

        return rows;
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> SectionalUnitsAsync(long companyId, CancellationToken ct)
    {
        List<SectionalScheme> schemes = await _db.Set<SectionalScheme>().AsNoTracking().Include(s => s.Units).ThenInclude(u => u.Owners)
            .Where(s => s.CompanyId == companyId).OrderBy(s => s.Code).ToListAsync(ct).ConfigureAwait(false);
        List<long> owners = [.. schemes.SelectMany(s => s.Units).Select(u => u.CurrentOwnerCustomerId).OfType<long>().Distinct()];
        Dictionary<long, string> names = await _db.Customers.AsNoTracking().Where(c => owners.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Name, ct).ConfigureAwait(false);
        return [.. schemes.SelectMany(s => s.Units.OrderBy(u => u.UnitNumber, StringComparer.Ordinal).Select(u => Row(("Scheme", s.Code), ("Building", s.Name), ("Unit", u.UnitNumber),
            ("FloorArea", u.FloorArea.ToString("0.##", CultureInfo.InvariantCulture)), ("Quota", u.ParticipationQuota.ToString("0.####", CultureInfo.InvariantCulture)),
            ("Owner", u.CurrentOwnerCustomerId is long id ? names.GetValueOrDefault(id, "?") : "(unsold)"))))];
    }

    private Task<Dictionary<long, string>> StandNumbersAsync(long companyId, CancellationToken ct)
        => _db.HousingStands.AsNoTracking().Where(s => s.CompanyId == companyId).ToDictionaryAsync(s => s.Id, s => s.StandNumber, ct);

    private static string Contact(string? mobile, string? email)
    {
        string joined = string.Join(" / ", new[] { mobile, email }.Where(x => !string.IsNullOrWhiteSpace(x)));
        return joined.Length == 0 ? "no contact on file" : joined;
    }

    private static DateOnly AsAt(IReadOnlyDictionary<string, object?> parameters)
        => parameters.TryGetValue("AsAtDate", out object? value) && value is DateOnly date ? date : DateOnly.FromDateTime(DateTime.Today);

    private static string Date(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static Dictionary<string, object?> Row(params (string Key, object? Value)[] cells)
    {
        Dictionary<string, object?> row = new(StringComparer.OrdinalIgnoreCase);
        foreach ((string key, object? value) in cells)
        {
            row[key] = value;
        }

        return row;
    }
}
