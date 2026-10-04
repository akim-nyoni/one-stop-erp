using System.Globalization;
using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Housing;
using One4All.Application.Abstractions.Reporting;
using One4All.Application.Housing;
using One4All.Core.Housing;
using One4All.Core.JobCosting;
using One4All.Infrastructure.Housing;
using One4All.Infrastructure.Persistence;
using One4All.Infrastructure.Trading;
using Microsoft.Extensions.DependencyInjection;

namespace One4All.Infrastructure.Reporting;

/// <summary>Supplies live rows for the Housing, Land &amp; Estates report catalogue (brief §14 I-B, the H series).</summary>
public sealed class HousingReportDataProvider : IReportDataProvider
{
    private static readonly AllocationStatus[] Live = [AllocationStatus.Proposed, AllocationStatus.Offered, AllocationStatus.Accepted];

    private readonly One4AllDbContext _db;
    private readonly IWaitingListService _waitingList;
    private readonly ITitleDeedService _titles;
    private readonly ITransferApplicationService _transfers;
    private readonly IDeveloperAgreementService _developers;
    private readonly IInformalSettlementService _settlements;
    private readonly IAllocationService _allocations;
    private readonly IServiceProvider _services;

    /// <summary>Creates the provider.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="waitingList">The waiting list (for rankings and scores).</param>
    /// <param name="titles">The title deed pipeline.</param>
    /// <param name="transfers">Change-of-ownership applications (for their live checks).</param>
    /// <param name="developers">Developer agreements (for their reconciliation).</param>
    /// <param name="settlements">Informal settlements (for regularisation progress).</param>
    /// <param name="allocations">Allocation runs (for ballot re-verification).</param>
    /// <param name="services">Resolves the services the less common reports need (ECL, leases, permissions) only when run.</param>
    public HousingReportDataProvider(One4AllDbContext db, IWaitingListService waitingList, ITitleDeedService titles, ITransferApplicationService transfers,
        IDeveloperAgreementService developers, IInformalSettlementService settlements, IAllocationService allocations, IServiceProvider services)
    {
        _allocations = allocations;
        _services = services;
        _db = db;
        _waitingList = waitingList;
        _titles = titles;
        _transfers = transfers;
        _developers = developers;
        _settlements = settlements;
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
            "HOUSINGCOVENANTS" => await CovenantsAsync(companyId, asAt, ct).ConfigureAwait(false),
            "HOUSINGREPOSSESSIONS" => await RepossessionsAsync(companyId, ct).ConfigureAwait(false),
            "HOUSINGRENTROLL" => await RentRollAsync(companyId, ct).ConfigureAwait(false),
            "HOUSINGSTOCK" => await StockAsync(companyId, ct).ConfigureAwait(false),
            "HOUSINGVOIDS" => await VoidsAsync(companyId, asAt, ct).ConfigureAwait(false),
            "HOUSINGEVICTIONS" => await EvictionsAsync(companyId, ct).ConfigureAwait(false),
            "HOUSINGLEASEEXPIRY" => await LeaseExpiryAsync(companyId, asAt, ct).ConfigureAwait(false),
            "HOUSINGINSPECTIONSDUE" => await InspectionsDueAsync(companyId, asAt, ct).ConfigureAwait(false),
            "HOUSINGDEVELOPERS" => await DevelopersAsync(companyId, asAt, ct).ConfigureAwait(false),
            "HOUSINGCOOPERATIVES" => await CooperativesAsync(companyId, ct).ConfigureAwait(false),
            "HOUSINGSETTLEMENTS" => await SettlementsAsync(companyId, ct).ConfigureAwait(false),
            "HOUSINGREGULARISATION" => await RegularisationAsync(companyId, ct).ConfigureAwait(false),
            "HOUSINGGRANTS" => await GrantsAsync(companyId, ct).ConfigureAwait(false),
            "HOUSINGACQUISITIONCOSTS" => await AcquisitionCostsAsync(companyId, ct).ConfigureAwait(false),
            "HOUSINGSUBDIVISION" => await SubdivisionAsync(companyId, ct).ConfigureAwait(false),
            "HOUSINGPRICELIST" => await PriceListAsync(companyId, ct).ConfigureAwait(false),
            "HOUSINGWAITINGSUMMARY" => await WaitingSummaryAsync(companyId, asAt, ct).ConfigureAwait(false),
            "HOUSINGSCOREDERIVATION" => await ScoreDerivationAsync(companyId, asAt, ct).ConfigureAwait(false),
            "HOUSINGAPPLICANTOWNERS" => await ApplicantOwnersAsync(companyId, ct).ConfigureAwait(false),
            "HOUSINGPROPOSEDALLOCATIONS" => await ProposedAllocationsAsync(companyId, ct).ConfigureAwait(false),
            "HOUSINGBALLOTS" => await BallotsAsync(companyId, ct).ConfigureAwait(false),
            "HOUSINGALLOCATIONSBYMONTH" => await AllocationsByMonthAsync(companyId, ct).ConfigureAwait(false),
            "HOUSINGFAILEDOFFERS" => await FailedOffersAsync(companyId, ct).ConfigureAwait(false),
            "HOUSINGMULTIPLESTANDS" => await MultipleStandsAsync(companyId, ct).ConfigureAwait(false),
            "HOUSINGUNCONVERTED" => await UnconvertedAsync(companyId, asAt, ct).ConfigureAwait(false),
            "HOUSINGAGREEMENTS" => await AgreementsAsync(companyId, ct).ConfigureAwait(false),
            "HOUSINGAMORTISATION" => await AmortisationAsync(companyId, parameters, ct).ConfigureAwait(false),
            "HOUSINGDEBTORSAGEING" => await InstalmentAgeingAsync(companyId, asAt, ct).ConfigureAwait(false),
            "HOUSINGRENTAGEING" => await RentAgeingAsync(companyId, asAt, ct).ConfigureAwait(false),
            "HOUSINGINSTALMENTARREARS" => await InstalmentArrearsAsync(companyId, asAt, ct).ConfigureAwait(false),
            "HOUSINGEARLYSETTLEMENTS" => await EarlySettlementsAsync(companyId, ct).ConfigureAwait(false),
            "HOUSINGECL" => await EclAsync(companyId, asAt, ct).ConfigureAwait(false),
            "HOUSINGESTATETRANSFERS" => await EstateTransfersAsync(companyId, ct).ConfigureAwait(false),
            "HOUSINGLEVIES" => await LeviesAsync(companyId, ct).ConfigureAwait(false),
            "HOUSINGCONVEYANCERS" => await ConveyancersAsync(companyId, ct).ConfigureAwait(false),
            "HOUSINGTENANTDEPOSITS" => await TenantDepositsAsync(companyId, ct).ConfigureAwait(false),
            "HOUSINGREPAIRCOSTS" => await RepairCostsAsync(companyId, ct).ConfigureAwait(false),
            "HOUSINGLEASESCHEDULE" => await LeaseScheduleAsync(companyId, ct).ConfigureAwait(false),
            "HOUSINGAUDITTRAIL" => await AuditTrailAsync(companyId, asAt, ct).ConfigureAwait(false),
            "HOUSINGSODVIOLATIONS" => await SodViolationsAsync(ct).ConfigureAwait(false),
            "HOUSINGDASHBOARD" => await DashboardAsync(companyId, asAt, ct).ConfigureAwait(false),
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

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> CovenantsAsync(long companyId, DateOnly asAt, CancellationToken ct)
    {
        List<DevelopmentCovenant> covenants = await _db.Set<DevelopmentCovenant>().AsNoTracking().Include(c => c.Steps).Where(c => c.CompanyId == companyId)
            .OrderBy(c => c.Deadline).ThenBy(c => c.CovenantNumber).ToListAsync(ct).ConfigureAwait(false);
        Dictionary<long, string> stands = await StandNumbersAsync(companyId, ct).ConfigureAwait(false);
        Dictionary<long, string> holders = await CustomerNamesAsync(companyId, ct).ConfigureAwait(false);
        return [.. covenants.Select(c => Row(("Covenant", c.CovenantNumber), ("Stand", stands.GetValueOrDefault(c.StandId, "?")), ("Holder", holders.GetValueOrDefault(c.HolderCustomerId, "?")),
            ("Deadline", Date(c.Deadline)), ("Milestone", c.Latest?.ToString() ?? "None yet"), ("Extensions", c.Extensions), ("Status", c.Status.ToString()),
            ("DaysLeft", c.Deadline.DayNumber - asAt.DayNumber)))];
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> RepossessionsAsync(long companyId, CancellationToken ct)
    {
        List<Repossession> repossessions = await _db.Set<Repossession>().AsNoTracking().Where(r => r.CompanyId == companyId).OrderBy(r => r.Date).ThenBy(r => r.Reference).ToListAsync(ct).ConfigureAwait(false);
        Dictionary<long, string> stands = await StandNumbersAsync(companyId, ct).ConfigureAwait(false);
        return [.. repossessions.Select(r => Row(("Reference", r.Reference), ("Stand", stands.GetValueOrDefault(r.StandId, "?")), ("Date", Date(r.Date)), ("Reason", r.Reason),
            ("Receivable", r.ReceivableDerecognised), ("Nrv", r.NetRealisableValue), ("Inventory", r.InventoryRecognised), ("GainOrLoss", r.GainOrLoss)))];
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> RentRollAsync(long companyId, CancellationToken ct)
    {
        var tenancies = await (from t in _db.Set<Tenancy>().AsNoTracking()
                               join u in _db.Set<RentalUnit>().AsNoTracking() on t.UnitId equals u.Id
                               join c in _db.Customers.AsNoTracking() on t.TenantCustomerId equals c.Id
                               where t.CompanyId == companyId && t.Status == TenancyStatus.Active
                               orderby u.Code
                               select new { u.Code, u.UnitType, t.TenancyNumber, t.TenantCustomerId, c.Name, t.StartDate, t.MonthlyRent }).ToListAsync(ct).ConfigureAwait(false);
        Dictionary<long, decimal> arrears = await NetOwingAsync(companyId, [.. tenancies.Select(t => t.TenantCustomerId)], ct).ConfigureAwait(false);
        return [.. tenancies.Select(t => Row(("Unit", t.Code), ("Type", t.UnitType.ToString()), ("Tenancy", t.TenancyNumber), ("Tenant", t.Name), ("Since", Date(t.StartDate)),
            ("Rent", t.MonthlyRent), ("Arrears", Math.Max(0m, arrears.GetValueOrDefault(t.TenantCustomerId)))))];
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> StockAsync(long companyId, CancellationToken ct)
    {
        List<RentalUnit> units = await _db.Set<RentalUnit>().AsNoTracking().Where(u => u.CompanyId == companyId).OrderBy(u => u.Code).ToListAsync(ct).ConfigureAwait(false);
        Dictionary<long, UnitInspection> latest = (await _db.Set<UnitInspection>().AsNoTracking().Where(i => i.CompanyId == companyId).ToListAsync(ct).ConfigureAwait(false))
            .GroupBy(i => i.UnitId).ToDictionary(g => g.Key, g => g.OrderBy(i => i.InspectedOn).ThenBy(i => i.Id).Last());
        return [.. units.Select(u => Row(("Unit", u.Code), ("Description", u.Description), ("Type", u.UnitType.ToString()), ("Rooms", u.Rooms), ("Occupancy", u.IsLet ? "Let" : "Void"),
            ("Inspected", latest.TryGetValue(u.Id, out UnitInspection? i) ? Date(i.InspectedOn) : "never"), ("Grade", latest.TryGetValue(u.Id, out UnitInspection? g) ? g.ConditionGrade.ToString(CultureInfo.InvariantCulture) : "—")))];
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> VoidsAsync(long companyId, DateOnly asAt, CancellationToken ct)
        => [.. (await _db.Set<RentalUnit>().AsNoTracking().Where(u => u.CompanyId == companyId && u.VacantSince != null && u.VacantSince <= asAt)
                .OrderBy(u => u.VacantSince).ThenBy(u => u.Code).ToListAsync(ct).ConfigureAwait(false))
            .Select(u =>
            {
                int days = asAt.DayNumber - u.VacantSince!.Value.DayNumber;
                return Row(("Unit", u.Code), ("Type", u.UnitType.ToString()), ("VacantSince", Date(u.VacantSince.Value)), ("DaysVoid", days),
                    ("RentLost", Math.Round(u.MonthlyRent * 12m / 365m * days, 2, MidpointRounding.AwayFromZero)));
            })];

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> EvictionsAsync(long companyId, CancellationToken ct)
        => [.. (await (from e in _db.Set<EvictionCase>().AsNoTracking().Include(e => e.Steps)
                       join t in _db.Set<Tenancy>().AsNoTracking() on e.TenancyId equals t.Id
                       where e.CompanyId == companyId
                       orderby e.CaseNumber
                       select new { Case = e, t.TenancyNumber }).ToListAsync(ct).ConfigureAwait(false))
            .Select(x => Row(("Case", x.Case.CaseNumber), ("Tenancy", x.TenancyNumber), ("Stage", x.Case.Current.Stage.ToString()), ("StageDate", Date(x.Case.Current.Date)),
                ("Status", x.Case.Status.ToString()), ("Arrears", x.Case.ArrearsAtOpening)))];

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> LeaseExpiryAsync(long companyId, DateOnly asAt, CancellationToken ct)
    {
        DateOnly horizon = asAt.AddYears(1);
        Dictionary<long, string> stands = await StandNumbersAsync(companyId, ct).ConfigureAwait(false);
        List<(string Kind, string Reference, string Subject, DateOnly Ends)> due = [];
        foreach (StandLease lease in await _db.Set<StandLease>().AsNoTracking().Where(l => l.CompanyId == companyId).ToListAsync(ct).ConfigureAwait(false))
        {
            if (lease.EndDate >= asAt && lease.EndDate <= horizon)
            {
                due.Add(("Lease", lease.LeaseNumber, stands.GetValueOrDefault(lease.StandId, "?"), lease.EndDate));
            }
        }

        due.AddRange((await (from t in _db.Set<Tenancy>().AsNoTracking() join u in _db.Set<RentalUnit>().AsNoTracking() on t.UnitId equals u.Id
                             where t.CompanyId == companyId && t.Status == TenancyStatus.Active && t.EndDate != null && t.EndDate >= asAt && t.EndDate <= horizon
                             select new { t.TenancyNumber, u.Code, Ends = t.EndDate!.Value }).ToListAsync(ct).ConfigureAwait(false)).Select(t => ("Tenancy", t.TenancyNumber, t.Code, t.Ends)));
        return [.. due.OrderBy(d => d.Ends).ThenBy(d => d.Reference, StringComparer.Ordinal)
            .Select(d => Row(("Kind", d.Kind), ("Reference", d.Reference), ("Subject", d.Subject), ("Ends", Date(d.Ends)), ("DaysLeft", d.Ends.DayNumber - asAt.DayNumber)))];
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> InspectionsDueAsync(long companyId, DateOnly asAt, CancellationToken ct)
    {
        List<RentalUnit> units = await _db.Set<RentalUnit>().AsNoTracking().Where(u => u.CompanyId == companyId).OrderBy(u => u.Code).ToListAsync(ct).ConfigureAwait(false);
        ILookup<long, UnitInspection> inspections = (await _db.Set<UnitInspection>().AsNoTracking().Where(i => i.CompanyId == companyId && i.InspectedOn <= asAt).ToListAsync(ct).ConfigureAwait(false))
            .ToLookup(i => i.UnitId);
        List<IReadOnlyDictionary<string, object?>> rows = [];
        foreach (RentalUnit unit in units)
        {
            UnitInspection? last = inspections[unit.Id].OrderBy(i => i.InspectedOn).ThenBy(i => i.Id).LastOrDefault();
            List<string> reasons = [];
            if (last is null)
            {
                reasons.Add("Never inspected.");
            }
            else if (last.InspectedOn.AddYears(1) <= asAt)
            {
                reasons.Add($"Last inspected over a year ago.");
            }

            reasons.AddRange(inspections[unit.Id].Where(i => i.FollowUpRequired && i.FollowUpDoneOn is null).Select(i => $"Follow-up outstanding from {Date(i.InspectedOn)}: {i.Findings}"));
            if (reasons.Count > 0)
            {
                rows.Add(Row(("Unit", unit.Code), ("LastInspected", last is null ? "never" : Date(last.InspectedOn)), ("Grade", last?.ConditionGrade.ToString(CultureInfo.InvariantCulture) ?? "—"),
                    ("Due", string.Join(" ", reasons))));
            }
        }

        return rows;
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> DevelopersAsync(long companyId, DateOnly asAt, CancellationToken ct)
    {
        List<string> numbers = await _db.Set<DeveloperAgreement>().AsNoTracking().Where(a => a.CompanyId == companyId).OrderBy(a => a.AgreementNumber).Select(a => a.AgreementNumber).ToListAsync(ct).ConfigureAwait(false);
        List<IReadOnlyDictionary<string, object?>> rows = [];
        foreach (string number in numbers)
        {
            var reconciled = await _developers.ReconcileAsync(companyId, number, asAt, ct).ConfigureAwait(false);
            if (reconciled.IsFailure)
            {
                continue;
            }

            foreach (QuotaDelivery quota in reconciled.Value.Quotas)
            {
                bool council = quota.Quota == StandQuota.Council;
                rows.Add(Row(("Agreement", number), ("Quota", quota.Quota.ToString()), ("Contracted", quota.Contracted), ("HandedOver", quota.HandedOver), ("Outstanding", quota.Outstanding),
                    ("OverdueMilestones", council ? (reconciled.Value.OverdueMilestones.Count == 0 ? "—" : string.Join(", ", reconciled.Value.OverdueMilestones)) : string.Empty),
                    ("PenaltyAccrued", council ? reconciled.Value.PenaltyAccrued : 0m)));
            }
        }

        return rows;
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> CooperativesAsync(long companyId, CancellationToken ct)
    {
        List<Cooperative> cooperatives = await _db.Set<Cooperative>().AsNoTracking().Include(c => c.Members).Where(c => c.CompanyId == companyId).OrderBy(c => c.Code).ToListAsync(ct).ConfigureAwait(false);
        var stands = (await _db.Set<CooperativeStand>().AsNoTracking().Where(s => s.CompanyId == companyId).Select(s => new { s.CooperativeId, s.MemberId }).ToListAsync(ct).ConfigureAwait(false))
            .GroupBy(s => s.CooperativeId).ToDictionary(g => g.Key, g => (Held: g.Count(), Sub: g.Count(s => s.MemberId != null)));
        return [.. cooperatives.Select(c => Row(("Cooperative", c.Code), ("Name", c.Name), ("Registration", c.RegistrationNumber), ("Members", c.Members.Count(m => m.IsActive)),
            ("Stands", stands.GetValueOrDefault(c.Id).Held), ("SubAllocated", stands.GetValueOrDefault(c.Id).Sub)))];
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> SettlementsAsync(long companyId, CancellationToken ct)
    {
        List<InformalSettlement> settlements = await _db.Set<InformalSettlement>().AsNoTracking().Where(s => s.CompanyId == companyId).OrderBy(s => s.Code).ToListAsync(ct).ConfigureAwait(false);
        var households = (await _db.Set<EnumeratedHousehold>().AsNoTracking().Where(h => h.CompanyId == companyId).Select(h => new { h.SettlementId, h.IsEligible }).ToListAsync(ct).ConfigureAwait(false))
            .GroupBy(h => h.SettlementId).ToDictionary(g => g.Key, g => (All: g.Count(), Eligible: g.Count(h => h.IsEligible)));
        return [.. settlements.Select(s => Row(("Settlement", s.Code), ("Name", s.Name), ("Ward", s.Ward), ("CutOff", Date(s.CutOffDate)), ("Households", households.GetValueOrDefault(s.Id).All),
            ("Eligible", households.GetValueOrDefault(s.Id).Eligible), ("Plan", s.PlanApprovedOn is DateOnly approved ? $"Approved {Date(approved)}" : "Not approved")))];
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> RegularisationAsync(long companyId, CancellationToken ct)
    {
        List<string> codes = await _db.Set<InformalSettlement>().AsNoTracking().Where(s => s.CompanyId == companyId).OrderBy(s => s.Code).Select(s => s.Code).ToListAsync(ct).ConfigureAwait(false);
        List<IReadOnlyDictionary<string, object?>> rows = [];
        foreach (string code in codes)
        {
            var summary = await _settlements.SummaryAsync(companyId, code, ct).ConfigureAwait(false);
            if (summary.IsSuccess)
            {
                SettlementSummary s = summary.Value;
                rows.Add(Row(("Settlement", s.Code), ("Enumerated", s.Enumerated), ("Eligible", s.Eligible), ("WaitingListed", s.WaitingListed), ("Regularised", s.Regularised), ("Outstanding", s.Outstanding)));
            }
        }

        return rows;
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> GrantsAsync(long companyId, CancellationToken ct)
        => [.. (await _db.Set<HousingGrant>().AsNoTracking().Where(g => g.CompanyId == companyId).OrderBy(g => g.GrantNumber).ToListAsync(ct).ConfigureAwait(false))
            .Select(g => Row(("Grant", g.GrantNumber), ("Grantor", g.Grantor), ("Received", Date(g.ReceivedOn)), ("Conditions", g.ConditionsAssuredOn is DateOnly d ? Date(d) : "Not yet"),
                ("Amount", g.Amount), ("Spent", g.ExpenditureToDate), ("Recognised", g.RecognisedToDate), ("Deferred", g.Deferred)))];

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> AcquisitionCostsAsync(long companyId, CancellationToken ct)
    {
        List<LandAcquisition> acquisitions = await _db.Set<LandAcquisition>().AsNoTracking().Include(a => a.Costs).Where(a => a.CompanyId == companyId)
            .OrderBy(a => a.AcquisitionNumber).ToListAsync(ct).ConfigureAwait(false);
        List<IReadOnlyDictionary<string, object?>> rows = [];
        foreach (LandAcquisition a in acquisitions)
        {
            foreach (CostElement element in AcquisitionReadiness.BuildUp(a.Costs.Select(c => new CostElement(c.Component, c.Amount))))
            {
                rows.Add(Row(("Acquisition", a.AcquisitionNumber), ("Parcel", a.ParcelNumber), ("Element", element.Component.ToString()), ("PerM2", PerM2(element.Amount, a.AreaSquareMetres)), ("Amount", element.Amount)));
            }
        }

        return rows;
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> SubdivisionAsync(long companyId, CancellationToken ct)
        => [.. (await (from s in _db.HousingStands.AsNoTracking()
                       join p in _db.LandParcels.AsNoTracking() on s.LandParcelId equals p.Id
                       join sc in _db.HousingSchemes.AsNoTracking() on s.SchemeId equals sc.Id into schemes
                       from sc in schemes.DefaultIfEmpty()
                       where s.CompanyId == companyId
                       orderby p.ParcelNumber, s.StandNumber
                       select new { p.ParcelNumber, s.StandNumber, s.SizeSquareMetres, Scheme = sc == null ? null : sc.Code, s.Status, s.AllocatedCost }).ToListAsync(ct).ConfigureAwait(false))
            .Select(s => Row(("Parcel", s.ParcelNumber), ("Stand", s.StandNumber), ("SizeM2", s.SizeSquareMetres.ToString("0.##", CultureInfo.InvariantCulture)), ("Scheme", s.Scheme ?? "—"),
                ("Status", s.Status.ToString()), ("Cost", s.AllocatedCost)))];

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> PriceListAsync(long companyId, CancellationToken ct)
    {
        List<StandPricingRule> rules = await _db.Set<StandPricingRule>().AsNoTracking().Where(r => r.CompanyId == companyId).OrderBy(r => r.EffectiveFrom).ThenBy(r => r.Id).ToListAsync(ct).ConfigureAwait(false);
        Dictionary<long, string> schemes = await _db.HousingSchemes.AsNoTracking().Where(s => s.CompanyId == companyId).ToDictionaryAsync(s => s.Id, s => s.Code, ct).ConfigureAwait(false);
        Dictionary<long, string> stands = await StandNumbersAsync(companyId, ct).ConfigureAwait(false);
        return [.. rules.Select(r => Row(("Scope", r.Scope.ToString()),
            ("Applies", r.Scope switch
            {
                PricingScope.Stand => $"Stand {stands.GetValueOrDefault(r.StandId ?? 0, "?")}",
                PricingScope.SizeBand => $"{schemes.GetValueOrDefault(r.SchemeId ?? 0, "?")} {r.MinSize:0.##}–{r.MaxSize:0.##} m²",
                _ => $"Scheme {schemes.GetValueOrDefault(r.SchemeId ?? 0, "?")}",
            }),
            ("Effective", Date(r.EffectiveFrom)),
            ("Terms", string.Create(CultureInfo.InvariantCulture, $"{r.OverheadPercent:0.##}% / {r.LevyPerStand:0.##} + {r.LevyPerSquareMetre:0.##}/m² / {r.MarginPercent:0.##}%{(r.RoundTo > 0 ? $", rounded to {r.RoundTo:0.##}" : string.Empty)}")),
            ("Approved", r.ApprovedOn is DateOnly d ? $"{Date(d)} by user {r.ApprovedByUserId}" : "Awaiting approval")))];
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> WaitingSummaryAsync(long companyId, DateOnly asAt, CancellationToken ct)
        => [.. (await _db.HousingApplicants.AsNoTracking()
                .Where(a => a.CompanyId == companyId && a.Status == ApplicantStatus.Active && a.WaitingListNumber != null && a.RegistrationDate <= asAt)
                .Select(a => new { a.Category, a.MonthlyIncome, a.DateOfBirth }).ToListAsync(ct).ConfigureAwait(false))
            .GroupBy(a => (a.Category, Income: WaitingListAgeing.IncomeBand(a.MonthlyIncome), Age: WaitingListAgeing.AgeBand(a.DateOfBirth, asAt)))
            .OrderBy(g => g.Key.Category).ThenBy(g => g.Key.Income, StringComparer.Ordinal).ThenBy(g => g.Key.Age, StringComparer.Ordinal)
            .Select(g => Row(("Category", g.Key.Category.ToString()), ("IncomeBand", g.Key.Income), ("AgeBand", g.Key.Age), ("Applicants", g.Count())))];

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> ScoreDerivationAsync(long companyId, DateOnly asAt, CancellationToken ct)
    {
        List<string> numbers = await _db.HousingApplicants.AsNoTracking()
            .Where(a => a.CompanyId == companyId && a.Status == ApplicantStatus.Active && a.WaitingListNumber != null)
            .OrderBy(a => a.WaitingListNumber).Select(a => a.WaitingListNumber!).ToListAsync(ct).ConfigureAwait(false);
        List<IReadOnlyDictionary<string, object?>> rows = [];
        foreach (string number in numbers)
        {
            var enquiry = await _waitingList.EnquireAsync(companyId, number, asAt, ct).ConfigureAwait(false);
            if (enquiry.IsFailure)
            {
                continue;
            }

            foreach (ScoreLine line in enquiry.Value.Score.Lines)
            {
                rows.Add(Row(("WaitingListNumber", number), ("Factor", line.Factor), ("Detail", line.Detail), ("Points", line.Points.ToString("0.##", CultureInfo.InvariantCulture)),
                    ("Total", enquiry.Value.Score.Total.ToString("0.##", CultureInfo.InvariantCulture))));
            }
        }

        return rows;
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> ApplicantOwnersAsync(long companyId, CancellationToken ct)
    {
        var applicants = await _db.HousingApplicants.AsNoTracking().Where(a => a.CompanyId == companyId && a.Status == ApplicantStatus.Active && a.WaitingListNumber != null)
            .Select(a => new { a.WaitingListNumber, a.FullName, a.IdNumber }).ToListAsync(ct).ConfigureAwait(false);
        List<string> ids = [.. applicants.Select(a => a.IdNumber)];
        var customers = await _db.Customers.AsNoTracking().Where(c => c.CompanyId == companyId && c.NationalIdNumber != null && ids.Contains(c.NationalIdNumber))
            .Select(c => new { c.Id, IdNumber = c.NationalIdNumber! }).ToListAsync(ct).ConfigureAwait(false);
        List<long> customerIds = [.. customers.Select(c => c.Id)];
        var stands = await _db.HousingStands.AsNoTracking().Where(s => s.CompanyId == companyId && s.CurrentHolderCustomerId != null && customerIds.Contains(s.CurrentHolderCustomerId.Value))
            .Select(s => new { Holder = s.CurrentHolderCustomerId!.Value, s.StandNumber }).ToListAsync(ct).ConfigureAwait(false);
        var properties = await (from o in _db.Set<Core.Municipal.PropertyOwner>().AsNoTracking() join p in _db.Properties.AsNoTracking() on o.PropertyId equals p.Id
                                where o.CompanyId == companyId && o.OwnedTo == null && customerIds.Contains(o.CustomerId)
                                select new { o.CustomerId, p.StandNumber }).ToListAsync(ct).ConfigureAwait(false);
        List<IReadOnlyDictionary<string, object?>> rows = [];
        foreach (var applicant in applicants.OrderBy(a => a.WaitingListNumber, StringComparer.Ordinal))
        {
            HashSet<long> mine = [.. customers.Where(c => c.IdNumber == applicant.IdNumber).Select(c => c.Id)];
            List<string> holds = [.. stands.Where(s => mine.Contains(s.Holder)).Select(s => $"council stand {s.StandNumber}"),
                .. properties.Where(p => mine.Contains(p.CustomerId)).Select(p => $"rated property {p.StandNumber}")];
            if (holds.Count > 0)
            {
                rows.Add(Row(("WaitingListNumber", applicant.WaitingListNumber), ("Name", applicant.FullName), ("Holds", string.Join(", ", holds.Distinct()))));
            }
        }

        return rows;
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> ProposedAllocationsAsync(long companyId, CancellationToken ct)
        => [.. (await (from a in _db.Set<StandAllocation>().AsNoTracking()
                       join r in _db.Set<AllocationRun>().AsNoTracking() on a.AllocationRunId equals r.Id
                       join s in _db.HousingStands.AsNoTracking() on a.StandId equals s.Id
                       join p in _db.HousingApplicants.AsNoTracking() on a.ApplicantId equals p.Id
                       where a.CompanyId == companyId && a.Status == AllocationStatus.Proposed && r.Status == AllocationRunStatus.Proposed
                       orderby r.RunNumber, a.Sequence
                       select new { r.RunNumber, r.Method, a.Sequence, s.StandNumber, p.WaitingListNumber, p.FullName }).ToListAsync(ct).ConfigureAwait(false))
            .Select(a => Row(("Run", a.RunNumber), ("Method", a.Method.ToString()), ("Sequence", a.Sequence), ("Stand", a.StandNumber), ("WaitingListNumber", a.WaitingListNumber), ("Applicant", a.FullName)))];

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> BallotsAsync(long companyId, CancellationToken ct)
    {
        List<AllocationRun> runs = await _db.Set<AllocationRun>().AsNoTracking().Where(r => r.CompanyId == companyId && r.Method == AllocationMethod.Ballot)
            .OrderBy(r => r.RunNumber).ToListAsync(ct).ConfigureAwait(false);
        List<IReadOnlyDictionary<string, object?>> rows = [];
        foreach (AllocationRun run in runs)
        {
            var verified = await _allocations.VerifyDrawAsync(companyId, run.RunNumber, ct).ConfigureAwait(false);
            string outcome = verified.IsFailure ? verified.Error.Message
                : verified.Value.HashMatches && verified.Value.OrderMatches ? "Inputs and draw reproduce" : $"MISMATCH (inputs {(verified.Value.HashMatches ? "ok" : "altered")}, draw {(verified.Value.OrderMatches ? "ok" : "differs")})";
            rows.Add(Row(("Run", run.RunNumber), ("Status", run.Status.ToString()), ("Seed", run.Seed?.ToString(CultureInfo.InvariantCulture) ?? "—"), ("InputHash", run.InputHash), ("Verified", outcome)));
        }

        return rows;
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> AllocationsByMonthAsync(long companyId, CancellationToken ct)
        => [.. (await (from a in _db.Set<StandAllocation>().AsNoTracking()
                       join r in _db.Set<AllocationRun>().AsNoTracking() on a.AllocationRunId equals r.Id
                       join sc in _db.HousingSchemes.AsNoTracking() on r.SchemeId equals sc.Id
                       where a.CompanyId == companyId && r.ApprovedOn != null && a.CertificateNumber != null
                       select new { sc.Code, Approved = r.ApprovedOn!.Value, r.ApprovedByUserId }).ToListAsync(ct).ConfigureAwait(false))
            .GroupBy(a => (a.Code, Month: a.Approved.ToString("yyyy-MM", CultureInfo.InvariantCulture), a.ApprovedByUserId))
            .OrderBy(g => g.Key.Month, StringComparer.Ordinal).ThenBy(g => g.Key.Code, StringComparer.Ordinal)
            .Select(g => Row(("Scheme", g.Key.Code), ("Month", g.Key.Month), ("Approver", g.Key.ApprovedByUserId), ("Allocations", g.Count())))];

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> FailedOffersAsync(long companyId, CancellationToken ct)
        => [.. (await (from a in _db.Set<StandAllocation>().AsNoTracking()
                       join s in _db.HousingStands.AsNoTracking() on a.StandId equals s.Id
                       join p in _db.HousingApplicants.AsNoTracking() on a.ApplicantId equals p.Id
                       where a.CompanyId == companyId && a.CertificateNumber != null
                           && (a.Status == AllocationStatus.Declined || a.Status == AllocationStatus.Lapsed || a.Status == AllocationStatus.Cancelled)
                       orderby a.DecidedOn, a.CertificateNumber
                       select new { a.CertificateNumber, s.StandNumber, p.FullName, a.Status, a.DecidedOn, a.Reason }).ToListAsync(ct).ConfigureAwait(false))
            .Select(a => Row(("Certificate", a.CertificateNumber), ("Stand", a.StandNumber), ("Applicant", a.FullName), ("Status", a.Status.ToString()),
                ("DecidedOn", a.DecidedOn is DateOnly d ? Date(d) : "—"), ("Reason", a.Reason ?? "—")))];

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> MultipleStandsAsync(long companyId, CancellationToken ct)
    {
        List<(string Id, string Name, string Stand, string How)> holdings = [];
        holdings.AddRange((await (from s in _db.HousingStands.AsNoTracking() join c in _db.Customers.AsNoTracking() on s.CurrentHolderCustomerId equals c.Id
                                  where s.CompanyId == companyId && c.NationalIdNumber != null
                                  select new { c.NationalIdNumber, c.Name, s.StandNumber }).ToListAsync(ct).ConfigureAwait(false))
            .Select(x => (x.NationalIdNumber!, x.Name, x.StandNumber, "holds")));
        holdings.AddRange((await (from a in _db.Set<StandAllocation>().AsNoTracking()
                                  join p in _db.HousingApplicants.AsNoTracking() on a.ApplicantId equals p.Id
                                  join s in _db.HousingStands.AsNoTracking() on a.StandId equals s.Id
                                  where a.CompanyId == companyId && Live.Contains(a.Status)
                                  select new { p.IdNumber, p.FullName, s.StandNumber, a.Status }).ToListAsync(ct).ConfigureAwait(false))
            .Select(x => (x.IdNumber, x.FullName, x.StandNumber, $"allocated ({x.Status})")));
        holdings.AddRange((await (from c in _db.Set<CooperativeStand>().AsNoTracking()
                                  join m in _db.Set<CooperativeMember>().AsNoTracking() on c.MemberId equals m.Id
                                  join s in _db.HousingStands.AsNoTracking() on c.StandId equals s.Id
                                  where c.CompanyId == companyId
                                  select new { m.IdNumber, m.FullName, s.StandNumber }).ToListAsync(ct).ConfigureAwait(false))
            .Select(x => (x.IdNumber, x.FullName, x.StandNumber, "cooperative member")));
        return [.. holdings.GroupBy(h => h.Id, StringComparer.OrdinalIgnoreCase)
            .Select(g => (g.Key, g.First().Name, Stands: g.Select(h => h.Stand).Distinct(StringComparer.Ordinal).ToList(), Detail: string.Join("; ", g.Select(h => $"{h.Stand} {h.How}").Distinct(StringComparer.Ordinal))))
            .Where(x => x.Stands.Count > 1).OrderBy(x => x.Key, StringComparer.Ordinal)
            .Select(x => Row(("IdNumber", x.Key), ("Name", x.Name), ("Stands", x.Stands.Count), ("Detail", x.Detail)))];
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> UnconvertedAsync(long companyId, DateOnly asAt, CancellationToken ct)
        => [.. (await (from a in _db.Set<StandAllocation>().AsNoTracking()
                       join s in _db.HousingStands.AsNoTracking() on a.StandId equals s.Id
                       join p in _db.HousingApplicants.AsNoTracking() on a.ApplicantId equals p.Id
                       where a.CompanyId == companyId && a.Status == AllocationStatus.Accepted && s.Status == StandStatus.Allocated
                           && !_db.Set<HousingAgreement>().Any(h => h.StandId == a.StandId)
                           && !_db.Set<InstalmentAgreement>().Any(i => i.StandId == a.StandId)
                       orderby a.DecidedOn
                       select new { a.CertificateNumber, s.StandNumber, p.FullName, a.DecidedOn }).ToListAsync(ct).ConfigureAwait(false))
            .Select(a => Row(("Certificate", a.CertificateNumber), ("Stand", a.StandNumber), ("Beneficiary", a.FullName), ("AcceptedOn", a.DecidedOn is DateOnly d ? Date(d) : "—"),
                ("DaysWaiting", a.DecidedOn is DateOnly accepted ? asAt.DayNumber - accepted.DayNumber : 0)))];

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> AgreementsAsync(long companyId, CancellationToken ct)
    {
        List<HousingAgreement> agreements = await _db.Set<HousingAgreement>().AsNoTracking().Include(a => a.Versions).Where(a => a.CompanyId == companyId)
            .OrderBy(a => a.AgreementNumber).ToListAsync(ct).ConfigureAwait(false);
        Dictionary<long, string> parties = await CustomerNamesAsync(companyId, ct).ConfigureAwait(false);
        Dictionary<long, string> stands = await StandNumbersAsync(companyId, ct).ConfigureAwait(false);
        return [.. agreements.Select(a =>
        {
            AgreementVersion? inForce = a.InForce;
            AgreementVersion latest = a.Latest;
            string status = inForce is null ? "Draft (unsigned)" : latest.SignedOn is null ? $"Signed; v{latest.Version} amendment pending" : $"Signed {Date(inForce.SignedOn!.Value)}";
            return Row(("Agreement", a.AgreementNumber), ("Type", a.AgreementType.ToString()), ("Party", parties.GetValueOrDefault(a.PartyCustomerId, "?")),
                ("Stand", a.StandId is long id ? stands.GetValueOrDefault(id, "?") : "—"), ("Version", inForce?.Version.ToString(CultureInfo.InvariantCulture) ?? "—"), ("Status", status),
                ("Price", (inForce ?? latest).Price));
        })];
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> AmortisationAsync(long companyId, IReadOnlyDictionary<string, object?> parameters, CancellationToken ct)
    {
        string? only = parameters.TryGetValue("AgreementNumber", out object? value) && value is string text && text.Length > 0 ? text.Trim() : null;
        List<InstalmentAgreement> agreements = await _db.Set<InstalmentAgreement>().AsNoTracking().Include(a => a.Lines)
            .Where(a => a.CompanyId == companyId && (only == null || a.AgreementNumber == only)).OrderBy(a => a.AgreementNumber).ToListAsync(ct).ConfigureAwait(false);
        return [.. agreements.SelectMany(a => a.Lines.OrderBy(l => l.Number).Select(l => Row(("Agreement", a.AgreementNumber), ("Number", l.Number), ("Due", Date(l.DueDate)),
            ("Interest", Money(l.Interest)), ("Principal", Money(l.Principal)), ("Balance", Money(l.BalanceAfter)), ("State", l.Rebated ? "Rebated" : l.Raised ? "Raised" : "Future"), ("Amount", l.Amount))))];
    }

    // Tenants' open items, aged by due date as at the date; unallocated credits reduce the current band.
    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> RentAgeingAsync(long companyId, DateOnly asAt, CancellationToken ct)
    {
        Dictionary<long, string> types = (await (from t in _db.Set<Tenancy>().AsNoTracking() join u in _db.Set<RentalUnit>().AsNoTracking() on t.UnitId equals u.Id
                                                 where t.CompanyId == companyId select new { t.TenantCustomerId, t.StartDate, u.UnitType }).ToListAsync(ct).ConfigureAwait(false))
            .GroupBy(t => t.TenantCustomerId).ToDictionary(g => g.Key, g => g.OrderByDescending(t => t.StartDate).First().UnitType.ToString());
        Dictionary<long, string> names = await CustomerNamesAsync(companyId, ct).ConfigureAwait(false);
        List<OpenItemAsAt> items = [.. (await OpenItemsAsAt.LoadAsync(_db, companyId, Core.Trading.TradingLedger.AccountsReceivable, asAt, ct).ConfigureAwait(false)).Where(i => types.ContainsKey(i.Item.TraderId))];
        List<IReadOnlyDictionary<string, object?>> rows = [];
        foreach (IGrouping<long, OpenItemAsAt> tenant in items.GroupBy(i => i.Item.TraderId).OrderBy(g => types[g.Key], StringComparer.Ordinal).ThenBy(g => names.GetValueOrDefault(g.Key), StringComparer.Ordinal))
        {
            decimal[] bands = new decimal[DebtorAgeing.Bands.Count];
            foreach (OpenItemAsAt item in tenant)
            {
                decimal signed = item.Signed(Core.Trading.LedgerSign.Debit);
                bands[signed < 0m ? 0 : DebtorAgeing.Band(item.Item.DueDate, asAt)] += signed;
            }

            if (bands.Sum() != 0m)
            {
                rows.Add(AgeingRow("Type", types[tenant.Key], "Tenant", names.GetValueOrDefault(tenant.Key, "?"), bands));
            }
        }

        return rows;
    }

    // Instalment buyers: the unbilled principal is current; the arrears sit in the latest raised instalments (DebtorAgeing.AgeInstalments).
    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> InstalmentAgeingAsync(long companyId, DateOnly asAt, CancellationToken ct)
    {
        List<InstalmentDebtor> debtors = await InstalmentDebtorsAsync(companyId, asAt, ct).ConfigureAwait(false);
        Dictionary<long, string> names = await CustomerNamesAsync(companyId, ct).ConfigureAwait(false);
        return [.. debtors.Where(d => d.Owing != 0m).OrderBy(d => d.Scheme, StringComparer.Ordinal).ThenBy(d => names.GetValueOrDefault(d.BuyerId), StringComparer.Ordinal)
            .Select(d => AgeingRow("Scheme", d.Scheme, "Debtor", names.GetValueOrDefault(d.BuyerId, "?"), DebtorAgeing.AgeInstalments(d.Owing, d.NotYetBilled, d.Raised, asAt)))];
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> InstalmentArrearsAsync(long companyId, DateOnly asAt, CancellationToken ct)
    {
        List<InstalmentDebtor> debtors = await InstalmentDebtorsAsync(companyId, asAt, ct).ConfigureAwait(false);
        List<long> buyers = [.. debtors.Select(d => d.BuyerId)];
        Dictionary<long, DateOnly> lastPaid = await _db.OpenItems.AsNoTracking()
            .Where(i => i.CompanyId == companyId && i.Ledger == Core.Trading.TradingLedger.AccountsReceivable && i.ItemType == Core.Trading.OpenItemType.Receipt && buyers.Contains(i.TraderId) && i.DocumentDate <= asAt)
            .GroupBy(i => i.TraderId).Select(g => new { g.Key, Last = g.Max(i => i.DocumentDate) }).ToDictionaryAsync(x => x.Key, x => x.Last, ct).ConfigureAwait(false);
        Dictionary<long, string> names = await CustomerNamesAsync(companyId, ct).ConfigureAwait(false);
        return [.. debtors.Select(d => (Debtor: d, Arrears: d.Owing - d.NotYetBilled)).Where(x => x.Arrears > 0m).OrderBy(x => x.Debtor.Agreements, StringComparer.Ordinal)
            .Select(x => Row(("Agreement", x.Debtor.Agreements), ("Stand", x.Debtor.Stands), ("Buyer", names.GetValueOrDefault(x.Debtor.BuyerId, "?")),
                ("Overdue", DebtorAgeing.UnpaidInstalments(x.Arrears, x.Debtor.Raised.OrderByDescending(r => r.Due).Select(r => r.Amount))),
                ("LastPaid", lastPaid.TryGetValue(x.Debtor.BuyerId, out DateOnly paid) ? Date(paid) : "never"), ("Owing", x.Arrears)))];
    }

    // Each buyer with an active instalment agreement: net balance as at the date, principal not yet billed, and the instalments raised by then.
    private async Task<List<InstalmentDebtor>> InstalmentDebtorsAsync(long companyId, DateOnly asAt, CancellationToken ct)
    {
        List<InstalmentAgreement> active = await _db.Set<InstalmentAgreement>().AsNoTracking().Include(a => a.Lines)
            .Where(a => a.CompanyId == companyId && a.Status == InstalmentAgreementStatus.Active && a.SignedOn <= asAt).ToListAsync(ct).ConfigureAwait(false);
        var stands = await (from s in _db.HousingStands.AsNoTracking()
                            join sc in _db.HousingSchemes.AsNoTracking() on s.SchemeId equals sc.Id into schemes from sc in schemes.DefaultIfEmpty()
                            where s.CompanyId == companyId select new { s.Id, s.StandNumber, Scheme = sc == null ? "(no scheme)" : sc.Code }).ToDictionaryAsync(s => s.Id, ct).ConfigureAwait(false);
        Dictionary<long, decimal> owing = (await OpenItemsAsAt.LoadAsync(_db, companyId, Core.Trading.TradingLedger.AccountsReceivable, asAt, ct).ConfigureAwait(false))
            .GroupBy(i => i.Item.TraderId).ToDictionary(g => g.Key, g => g.Sum(i => i.Signed(Core.Trading.LedgerSign.Debit)));
        return [.. active.GroupBy(a => a.BuyerCustomerId).Select(g =>
        {
            List<(DateOnly Due, decimal Amount)> raised = [.. g.SelectMany(a => a.Lines).Where(l => l.Raised && l.DueDate <= asAt).Select(l => (l.DueDate, l.Amount))];
            decimal notYetBilled = g.Sum(a => a.Lines.Where(l => l.Raised && l.DueDate <= asAt).OrderBy(l => l.Number).Select(l => (decimal?)l.BalanceAfter).LastOrDefault() ?? a.Financed);
            return new InstalmentDebtor(g.Key, string.Join(", ", g.Select(a => stands.TryGetValue(a.StandId, out var s) ? s.Scheme : "?").Distinct().Order(StringComparer.Ordinal)),
                string.Join(", ", g.Select(a => a.AgreementNumber).Order(StringComparer.Ordinal)), string.Join(", ", g.Select(a => stands.TryGetValue(a.StandId, out var s) ? s.StandNumber : "?")),
                owing.GetValueOrDefault(g.Key), notYetBilled, raised);
        })];
    }

    private static IReadOnlyDictionary<string, object?> AgeingRow(string groupColumn, string group, string nameColumn, string name, decimal[] bands)
        => Row((groupColumn, group), (nameColumn, name), ("Current", Money(bands[0])), ("D30", Money(bands[1])), ("D60", Money(bands[2])), ("D90", Money(bands[3])),
            ("D120", Money(bands[4])), ("Over120", Money(bands[5])), ("Total", bands.Sum()));

    private sealed record InstalmentDebtor(long BuyerId, string Scheme, string Agreements, string Stands, decimal Owing, decimal NotYetBilled, List<(DateOnly Due, decimal Amount)> Raised);

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> EarlySettlementsAsync(long companyId, CancellationToken ct)
    {
        List<InstalmentAgreement> settled = await _db.Set<InstalmentAgreement>().AsNoTracking().Include(a => a.Lines)
            .Where(a => a.CompanyId == companyId && a.Status == InstalmentAgreementStatus.SettledEarly).OrderBy(a => a.SettledOn).ToListAsync(ct).ConfigureAwait(false);
        Dictionary<long, string> stands = await StandNumbersAsync(companyId, ct).ConfigureAwait(false);
        Dictionary<long, string> names = await CustomerNamesAsync(companyId, ct).ConfigureAwait(false);
        return [.. settled.Select(a => Row(("Agreement", a.AgreementNumber), ("Stand", stands.GetValueOrDefault(a.StandId, "?")), ("Buyer", names.GetValueOrDefault(a.BuyerCustomerId, "?")),
            ("SettledOn", a.SettledOn is DateOnly d ? Date(d) : "—"), ("Rebated", a.Lines.Count(l => l.Rebated)), ("InterestRebated", a.Lines.Where(l => l.Rebated).Sum(l => l.Interest))))];
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> EclAsync(long companyId, DateOnly asAt, CancellationToken ct)
    {
        var assessed = await _services.GetRequiredService<IHousingEclService>().AssessAsync(new HousingEclRequest { CompanyId = companyId, AsAtDate = asAt }, ct).ConfigureAwait(false);
        if (assessed.IsFailure)
        {
            return [];
        }

        List<IReadOnlyDictionary<string, object?>> rows = [.. assessed.Value.Portfolios.SelectMany(p => p.Bands.Where(b => b.Exposure != 0m)
            .Select(b => Row(("Portfolio", p.Portfolio), ("Band", b.Band.ToString()), ("Exposure", Money(b.Exposure)), ("Provision", b.Provision))))];
        rows.Add(Row(("Portfolio", "Allowance already booked"), ("Band", Money(assessed.Value.BookedAllowance)), ("Exposure", string.Empty), ("Provision", 0m)));
        return rows;
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> EstateTransfersAsync(long companyId, CancellationToken ct)
    {
        List<TransferApplication> estates = await _db.Set<TransferApplication>().AsNoTracking().Include(t => t.Checks)
            .Where(t => t.CompanyId == companyId && (t.TransferType == TransferType.Inheritance || t.TransferType == TransferType.DeceasedJointHolder))
            .OrderBy(t => t.LodgedOn).ThenBy(t => t.ApplicationNumber).ToListAsync(ct).ConfigureAwait(false);
        Dictionary<long, string> stands = await StandNumbersAsync(companyId, ct).ConfigureAwait(false);
        Dictionary<long, string> names = await CustomerNamesAsync(companyId, ct).ConfigureAwait(false);
        return [.. estates.Select(t => Row(("Application", t.ApplicationNumber), ("Stand", stands.GetValueOrDefault(t.StandId, "?")), ("Type", t.TransferType.ToString()),
            ("Deceased", names.GetValueOrDefault(t.FromCustomerId, "?")), ("Beneficiary", names.GetValueOrDefault(t.ToCustomerId, "?")), ("Status", t.Status.ToString()),
            ("Documents", t.Checks.FirstOrDefault(c => c.Requirement == TransferRequirement.EstateDocuments)?.DocumentReference ?? "not yet verified")))];
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> LeviesAsync(long companyId, CancellationToken ct)
        => [.. (await (from c in _db.Set<LevyCharge>().AsNoTracking()
                       join l in _db.Set<DevelopmentLevy>().AsNoTracking() on c.LevyId equals l.Id
                       join s in _db.HousingStands.AsNoTracking() on c.StandId equals s.Id
                       join cu in _db.Customers.AsNoTracking() on c.CustomerId equals cu.Id
                       where c.CompanyId == companyId
                       orderby l.Code, c.ChargedOn, c.Id
                       select new { l.Code, c.EventReference, s.StandNumber, cu.Name, c.ChargedOn, c.DocumentNumber, c.Amount }).ToListAsync(ct).ConfigureAwait(false))
            .Select(c => Row(("Levy", c.Code), ("Event", c.EventReference), ("Stand", c.StandNumber), ("Customer", c.Name), ("ChargedOn", Date(c.ChargedOn)), ("Invoice", c.DocumentNumber), ("Amount", c.Amount)))];

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> ConveyancersAsync(long companyId, CancellationToken ct)
        => [.. (await _db.Set<TitleDeed>().AsNoTracking().Include(t => t.Steps).Where(t => t.CompanyId == companyId && t.Conveyancer != null).ToListAsync(ct).ConfigureAwait(false))
            .GroupBy(t => t.Conveyancer!, StringComparer.OrdinalIgnoreCase).OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                List<int> days = [.. g.Where(t => t.RegisteredOn is not null).Select(t => t.RegisteredOn!.Value.DayNumber - t.AppliedOn.DayNumber)];
                return Row(("Conveyancer", g.Key), ("Files", g.Count()), ("Registered", days.Count), ("AverageDays", days.Count == 0 ? "—" : Math.Round(days.Average()).ToString(CultureInfo.InvariantCulture)),
                    ("Queries", g.Sum(t => t.Steps.Count(s => s.Stage == TitleStage.QueryRaised))));
            })];

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> TenantDepositsAsync(long companyId, CancellationToken ct)
        => [.. (await (from t in _db.Set<Tenancy>().AsNoTracking()
                       join u in _db.Set<RentalUnit>().AsNoTracking() on t.UnitId equals u.Id
                       join c in _db.Customers.AsNoTracking() on t.TenantCustomerId equals c.Id
                       where t.CompanyId == companyId && t.Status == TenancyStatus.Active && t.Deposit > 0m
                       orderby u.Code
                       select new { t.TenancyNumber, u.Code, c.Name, t.StartDate, t.Deposit }).ToListAsync(ct).ConfigureAwait(false))
            .Select(t => Row(("Tenancy", t.TenancyNumber), ("Unit", t.Code), ("Tenant", t.Name), ("Since", Date(t.StartDate)), ("Deposit", t.Deposit)))];

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> RepairCostsAsync(long companyId, CancellationToken ct)
        => [.. (await (from r in _db.Set<UnitRepair>().AsNoTracking()
                       join u in _db.Set<RentalUnit>().AsNoTracking() on r.UnitId equals u.Id
                       where r.CompanyId == companyId && r.Status == RepairStatus.Completed
                       select new { u.Code, r.Cost, r.TenantCaused }).ToListAsync(ct).ConfigureAwait(false))
            .GroupBy(r => r.Code).OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => Row(("Unit", g.Key), ("Repairs", g.Count()), ("Recharged", Money(g.Where(r => r.TenantCaused).Sum(r => r.Cost))),
                ("Council", Money(g.Where(r => !r.TenantCaused).Sum(r => r.Cost))), ("Cost", g.Sum(r => r.Cost))))];

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> LeaseScheduleAsync(long companyId, CancellationToken ct)
    {
        IStandLeaseService leases = _services.GetRequiredService<IStandLeaseService>();
        List<string> numbers = await _db.Set<StandLease>().AsNoTracking().Where(l => l.CompanyId == companyId).OrderBy(l => l.LeaseNumber).Select(l => l.LeaseNumber).ToListAsync(ct).ConfigureAwait(false);
        List<IReadOnlyDictionary<string, object?>> rows = [];
        foreach (string number in numbers)
        {
            var schedule = await leases.ScheduleAsync(companyId, number, ct).ConfigureAwait(false);
            if (schedule.IsFailure || schedule.Value.Count == 0)
            {
                continue;
            }

            decimal straight = Math.Round(schedule.Value.Sum(y => y.AnnualRent) / schedule.Value.Count, 2, MidpointRounding.AwayFromZero);
            rows.AddRange(schedule.Value.Select(y => Row(("Lease", number), ("Year", y.Year), ("Contractual", Money(y.AnnualRent)), ("StraightLine", Money(straight)), ("Difference", straight - y.AnnualRent))));
        }

        return rows;
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> AuditTrailAsync(long companyId, DateOnly asAt, CancellationToken ct)
    {
        List<string> housingTypes = [.. typeof(HousingStand).Assembly.GetTypes().Where(t => t.Namespace == typeof(HousingStand).Namespace && t.IsClass).Select(t => t.Name)];
        DateTimeOffset until = new(asAt.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero);
        var entries = await _db.ChangeLog.AsNoTracking().Where(c => c.CompanyId == companyId && housingTypes.Contains(c.EntityType) && c.OccurredOnUtc <= until)
            .OrderByDescending(c => c.Id).Take(5000).ToListAsync(ct).ConfigureAwait(false);
        return [.. entries.OrderBy(c => c.Id).Select(c => Row(("When", c.OccurredOnUtc.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)), ("User", c.UserName), ("Entity", c.EntityType),
            ("Id", c.EntityId ?? "—"), ("Action", c.Action.ToString()), ("Changed", c.ChangedFields.Count == 0 ? "—" : string.Join(", ", c.ChangedFields))))];
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> SodViolationsAsync(CancellationToken ct)
    {
        Application.Abstractions.Security.IPermissionService permissions = _services.GetRequiredService<Application.Abstractions.Security.IPermissionService>();
        var users = await _db.Users.AsNoTracking().Where(u => u.IsActive && !u.IsSystemAdministrator).OrderBy(u => u.Username).Select(u => new { u.Id, u.Username }).ToListAsync(ct).ConfigureAwait(false);
        List<IReadOnlyDictionary<string, object?>> rows = [];
        foreach (var user in users)
        {
            foreach (Core.Security.SodRule rule in (await permissions.GetSodViolationsAsync(user.Id, ct).ConfigureAwait(false)).Where(r => r.Code.StartsWith("SOD-HSE", StringComparison.Ordinal)))
            {
                rows.Add(Row(("User", user.Username), ("Rule", rule.Code), ("Conflict", rule.Name)));
            }
        }

        return rows;
    }

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> DashboardAsync(long companyId, DateOnly asAt, CancellationToken ct)
    {
        DateOnly yearStart = new(asAt.Year, 1, 1);
        decimal hectares = await _db.LandParcels.Where(p => p.CompanyId == companyId).SumAsync(p => p.AreaSquareMetres, ct).ConfigureAwait(false) / 10000m;
        int available = await _db.HousingStands.CountAsync(s => s.CompanyId == companyId && s.Status == StandStatus.Available, ct).ConfigureAwait(false);
        int waiting = await _db.HousingApplicants.CountAsync(a => a.CompanyId == companyId && a.Status == ApplicantStatus.Active && a.WaitingListNumber != null, ct).ConfigureAwait(false);
        int waitingTen = await _db.HousingApplicants.CountAsync(a => a.CompanyId == companyId && a.Status == ApplicantStatus.Active && a.WaitingListNumber != null && a.RegistrationDate <= asAt.AddYears(-10), ct).ConfigureAwait(false);
        int allocated = await _db.Set<StandAllocation>().CountAsync(a => a.CompanyId == companyId && a.CertificateNumber != null
            && _db.Set<AllocationRun>().Any(r => r.Id == a.AllocationRunId && r.ApprovedOn >= yearStart && r.ApprovedOn <= asAt), ct).ConfigureAwait(false);
        int titles = await _db.Set<TitleDeed>().CountAsync(t => t.CompanyId == companyId && t.RegisteredOn >= yearStart && t.RegisteredOn <= asAt, ct).ConfigureAwait(false);
        List<long> buyers = await _db.Set<InstalmentAgreement>().Where(a => a.CompanyId == companyId).Select(a => a.BuyerCustomerId).Distinct().ToListAsync(ct).ConfigureAwait(false);
        List<long> tenants = await _db.Set<Tenancy>().Where(t => t.CompanyId == companyId && t.Status == TenancyStatus.Active).Select(t => t.TenantCustomerId).Distinct().ToListAsync(ct).ConfigureAwait(false);
        decimal debtors = (await NetOwingAsync(companyId, buyers, ct).ConfigureAwait(false)).Values.Where(v => v > 0m).Sum();
        decimal arrears = (await InstalmentDebtorsAsync(companyId, asAt, ct).ConfigureAwait(false)).Sum(d => Math.Max(0m, d.Owing - d.NotYetBilled));
        decimal rent = (await NetOwingAsync(companyId, tenants, ct).ConfigureAwait(false)).Values.Where(v => v > 0m).Sum();
        int voids = await _db.Set<RentalUnit>().CountAsync(u => u.CompanyId == companyId && u.VacantSince != null, ct).ConfigureAwait(false);
        return
        [
            Row(("Measure", "Land bank (hectares)"), ("Value", hectares.ToString("N2", CultureInfo.InvariantCulture))),
            Row(("Measure", "Stands available"), ("Value", available.ToString(CultureInfo.InvariantCulture))),
            Row(("Measure", "Applicants on the waiting list"), ("Value", waiting.ToString(CultureInfo.InvariantCulture))),
            Row(("Measure", "of whom waiting 10 years or more"), ("Value", waitingTen.ToString(CultureInfo.InvariantCulture))),
            Row(("Measure", $"Allocations approved in {asAt.Year}"), ("Value", allocated.ToString(CultureInfo.InvariantCulture))),
            Row(("Measure", $"Title deeds registered in {asAt.Year}"), ("Value", titles.ToString(CultureInfo.InvariantCulture))),
            Row(("Measure", "Housing debtors (instalment sales)"), ("Value", debtors.ToString("N2", CultureInfo.InvariantCulture))),
            Row(("Measure", "of which instalments in arrears"), ("Value", arrears.ToString("N2", CultureInfo.InvariantCulture))),
            Row(("Measure", "Rent arrears"), ("Value", rent.ToString("N2", CultureInfo.InvariantCulture))),
            Row(("Measure", "Void rental units"), ("Value", voids.ToString(CultureInfo.InvariantCulture))),
        ];
    }

    private static string Money(decimal amount) => amount.ToString("N2", CultureInfo.InvariantCulture);

    private static string PerM2(decimal amount, decimal area) => area <= 0m ? "—" : (amount / area).ToString("N2", CultureInfo.InvariantCulture);

    private Task<Dictionary<long, string>> CustomerNamesAsync(long companyId, CancellationToken ct)
        => _db.Customers.AsNoTracking().Where(c => c.CompanyId == companyId).ToDictionaryAsync(c => c.Id, c => c.Name, ct);

    private Task<Dictionary<long, decimal>> NetOwingAsync(long companyId, List<long> customerIds, CancellationToken ct)
        => _db.OpenItems.Where(i => i.CompanyId == companyId && i.Ledger == Core.Trading.TradingLedger.AccountsReceivable && customerIds.Contains(i.TraderId))
            .GroupBy(i => i.TraderId).Select(g => new { g.Key, Net = g.Sum(i => i.Sign == Core.Trading.LedgerSign.Debit ? i.OutstandingBase : -i.OutstandingBase) })
            .ToDictionaryAsync(x => x.Key, x => x.Net, ct);

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
