using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using One4All.Core.Common;

namespace One4All.Core.Housing;

/// <summary>The lifecycle of a housing stand (brief §4.10.5/4.10.6).</summary>
public enum StandStatus
{
    /// <summary>Serviced and available for sale.</summary>
    Available = 1,

    /// <summary>Allocated/offered to a beneficiary but control not yet transferred.</summary>
    Allocated = 2,

    /// <summary>Sold — control transferred, cost of sale released.</summary>
    Sold = 3,

    /// <summary>Ownership transferred/ceded to a subsequent holder.</summary>
    Transferred = 4,

    /// <summary>Raw land, not yet planned.</summary>
    RawLand = 10,

    /// <summary>On an approved layout plan.</summary>
    Planned = 11,

    /// <summary>Surveyed and pegged.</summary>
    Surveyed = 12,

    /// <summary>Being serviced.</summary>
    UnderServicing = 13,

    /// <summary>Serviced, not yet released for sale.</summary>
    Serviced = 14,

    /// <summary>Held back for a purpose (e.g. a cooperative or an institution) while still free.</summary>
    Reserved = 15,

    /// <summary>Offered to a beneficiary outside an allocation run, awaiting acceptance.</summary>
    Offered = 16,

    /// <summary>Paid in full by its holder.</summary>
    FullyPaid = 18,

    /// <summary>Title applied for.</summary>
    TitleApplied = 19,

    /// <summary>Title registered.</summary>
    TitleRegistered = 20,

    /// <summary>Title deed handed over (the end of the line).</summary>
    TitleHandedOver = 21,

    /// <summary>Withheld or blocked by a dispute or caveat; released back to where it was.</summary>
    Withheld = 30,

    /// <summary>Surrendered by its holder to council.</summary>
    Surrendered = 32,

    /// <summary>An offer was cancelled.</summary>
    OfferCancelled = 33,

    /// <summary>Merged into another stand (consolidation); no longer exists on its own.</summary>
    ConsolidatedAway = 34,

    /// <summary>Split into new stands (re-subdivision); no longer exists on its own.</summary>
    ReSubdivided = 35,

    /// <summary>Taken back by council from a defaulting buyer; released to available once it may be re-allocated.</summary>
    Repossessed = 36,
}

/// <summary>
/// The stand status state machine (brief §4.10.2): which moves are allowed, so no state is jumped. The main line runs raw land → planned →
/// surveyed → under servicing → serviced → available → (reserved / offered) → allocated → sold (under agreement; transferred after a cession)
/// → fully paid → title applied → registered → handed over, with side branches for a hold, surrender, a cancelled offer, consolidation and
/// re-subdivision. Repossession (its own document) takes a sold stand to repossessed, from where it is released to available for re-allocation.
/// </summary>
public static class StandLifecycle
{
    private static readonly Dictionary<StandStatus, StandStatus[]> Moves = new()
    {
        [StandStatus.RawLand] = [StandStatus.Planned],
        [StandStatus.Planned] = [StandStatus.Surveyed],
        [StandStatus.Surveyed] = [StandStatus.UnderServicing],
        [StandStatus.UnderServicing] = [StandStatus.Serviced],
        [StandStatus.Serviced] = [StandStatus.Available],
        [StandStatus.Available] = [StandStatus.Reserved, StandStatus.Offered, StandStatus.Allocated],
        [StandStatus.Reserved] = [StandStatus.Available, StandStatus.Offered, StandStatus.Allocated],
        [StandStatus.Offered] = [StandStatus.Allocated, StandStatus.OfferCancelled],
        [StandStatus.OfferCancelled] = [StandStatus.Available],
        [StandStatus.Allocated] = [StandStatus.Sold, StandStatus.Available, StandStatus.Surrendered],
        [StandStatus.FullyPaid] = [StandStatus.TitleApplied, StandStatus.Transferred],
        [StandStatus.TitleApplied] = [StandStatus.TitleRegistered],
        [StandStatus.TitleRegistered] = [StandStatus.TitleHandedOver],
        [StandStatus.Surrendered] = [StandStatus.Available],
        [StandStatus.Sold] = [StandStatus.Transferred, StandStatus.FullyPaid, StandStatus.Available, StandStatus.Surrendered, StandStatus.Repossessed],
        [StandStatus.Transferred] = [StandStatus.Transferred, StandStatus.FullyPaid, StandStatus.Available, StandStatus.Surrendered, StandStatus.Repossessed],
        [StandStatus.Repossessed] = [StandStatus.Available],
    };

    /// <summary>States a stand can be consolidated or re-subdivided from (nobody holds or is promised it).</summary>
    public static readonly IReadOnlySet<StandStatus> Unencumbered = new HashSet<StandStatus>
    {
        StandStatus.RawLand, StandStatus.Planned, StandStatus.Surveyed, StandStatus.UnderServicing, StandStatus.Serviced, StandStatus.Available,
    };

    /// <summary>States that end a stand's life.</summary>
    public static readonly IReadOnlySet<StandStatus> Final = new HashSet<StandStatus> { StandStatus.TitleHandedOver, StandStatus.ConsolidatedAway, StandStatus.ReSubdivided };

    /// <summary>Whether a stand may move from one state to another (a hold is possible from any live state and returns where it was).</summary>
    /// <param name="from">Where it is.</param>
    /// <param name="to">Where it would go.</param>
    /// <returns>True when allowed.</returns>
    public static bool CanMove(StandStatus from, StandStatus to)
        => to == StandStatus.Withheld ? from != StandStatus.Withheld && !Final.Contains(from)
            : to is StandStatus.ConsolidatedAway or StandStatus.ReSubdivided ? Unencumbered.Contains(from)
            : Moves.TryGetValue(from, out StandStatus[]? next) && next.Contains(to);

    /// <summary>Where a stand may go from here.</summary>
    /// <param name="from">Where it is.</param>
    /// <returns>The allowed next states.</returns>
    public static IReadOnlyList<StandStatus> Next(StandStatus from)
        => [.. Enum.GetValues<StandStatus>().Where(to => to != from && CanMove(from, to))];
}

/// <summary>One audited move of a stand's status (<c>hse.stand_status_change</c>): who, when and on what authority.</summary>
public sealed class StandStatusChange : Entity
{
    private StandStatusChange()
    {
        Authority = string.Empty;
    }

    /// <summary>Records the move.</summary>
    /// <param name="from">Where it was.</param>
    /// <param name="to">Where it went.</param>
    /// <param name="date">The date.</param>
    /// <param name="userId">Who moved it (null when a document did, e.g. an approved allocation run).</param>
    /// <param name="authority">The authority (resolution, minute, document number or process).</param>
    /// <param name="reason">Why.</param>
    public StandStatusChange(StandStatus from, StandStatus to, DateOnly date, long? userId, string authority, string? reason)
    {
        From = from;
        To = to;
        Date = date;
        UserId = userId;
        Authority = authority;
        Reason = reason;
    }

    /// <summary>The stand.</summary>
    public long HousingStandId { get; private set; }

    /// <summary>Where it was.</summary>
    public StandStatus From { get; private set; }

    /// <summary>Where it went.</summary>
    public StandStatus To { get; private set; }

    /// <summary>The date.</summary>
    public DateOnly Date { get; private set; }

    /// <summary>Who moved it.</summary>
    public long? UserId { get; private set; }

    /// <summary>The authority.</summary>
    public string Authority { get; private set; }

    /// <summary>Why.</summary>
    public string? Reason { get; private set; }
}

/// <summary>The kind of ownership event recorded on a stand's chain (brief §4.10.6).</summary>
public enum OwnershipChangeType
{
    /// <summary>The stand was created from a subdivided parcel.</summary>
    Origination = 0,

    /// <summary>Allocated/offered to a beneficiary.</summary>
    Allocation = 1,

    /// <summary>First sale (transfer of control) to a beneficiary.</summary>
    Sale = 2,

    /// <summary>Cession / change of ownership to a subsequent holder.</summary>
    Cession = 3,

    /// <summary>Repossessed by the council from a defaulting holder (brief §4.10.5 / §4.10.9).</summary>
    Repossession = 4,

    /// <summary>Surrendered by its holder to council.</summary>
    Surrender = 5,
}

/// <summary>
/// A housing stand (brief §4.10.2, <c>hse.stand</c>) carrying its apportioned cost and a tamper-evident
/// chain of ownership from origination to the current holder — the module's primary job (brief §4.10).
/// </summary>
public sealed class HousingStand : SoftDeletableEntity, ICompanyScoped
{
    private readonly List<OwnershipRecord> _ownership = [];
    private readonly List<StandStatusChange> _statusChanges = [];

    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private HousingStand() => StandNumber = string.Empty;

    /// <summary>Creates a stand from a parcel with its apportioned cost.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="landParcelId">The originating parcel.</param>
    /// <param name="schemeId">The housing scheme, if any.</param>
    /// <param name="standNumber">The stand/erf number.</param>
    /// <param name="sizeSquareMetres">The size in square metres.</param>
    /// <param name="allocatedCost">The cost apportioned from the parcel.</param>
    /// <param name="createdOn">When the stand was created (the subdivision date; today when not given).</param>
    /// <param name="initialStatus">Where it starts: available, or an earlier planning / survey / servicing state.</param>
    public HousingStand(long companyId, long landParcelId, long? schemeId, string standNumber, decimal sizeSquareMetres, decimal allocatedCost, DateOnly? createdOn = null,
        StandStatus initialStatus = StandStatus.Available)
    {
        if (!StandLifecycle.Unencumbered.Contains(initialStatus))
        {
            throw new ArgumentOutOfRangeException(nameof(initialStatus), initialStatus, "A new stand starts as raw land, planned, surveyed, under servicing, serviced or available.");
        }

        CompanyId = companyId;
        LandParcelId = landParcelId;
        SchemeId = schemeId;
        StandNumber = standNumber.Trim().ToUpperInvariant();
        SizeSquareMetres = sizeSquareMetres;
        AllocatedCost = allocatedCost;
        Status = initialStatus;
        AppendOwnership(OwnershipChangeType.Origination, null, "Municipality/Developer", createdOn ?? DateOnly.FromDateTime(DateTime.UtcNow), 0m);
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The originating parcel.</summary>
    public long LandParcelId { get; private set; }

    /// <summary>The housing scheme, if any.</summary>
    public long? SchemeId { get; private set; }

    /// <summary>The stand/erf number.</summary>
    public string StandNumber { get; private set; }

    /// <summary>The size in square metres.</summary>
    public decimal SizeSquareMetres { get; private set; }

    /// <summary>The cost apportioned from the parcel (released to COGS on transfer of control).</summary>
    public decimal AllocatedCost { get; private set; }

    /// <summary>The selling price, once set.</summary>
    public decimal SellingPrice { get; private set; }

    /// <summary>The stand status.</summary>
    public StandStatus Status { get; private set; }

    /// <summary>Where a withheld stand was before the hold (it returns there on release).</summary>
    public StandStatus? StatusBeforeHold { get; private set; }

    /// <summary>The stand this one was re-subdivided from.</summary>
    public long? ParentStandId { get; private set; }

    /// <summary>The stand this one was consolidated into.</summary>
    public long? ConsolidatedIntoStandId { get; private set; }

    /// <summary>Every status move, as audited.</summary>
    public IReadOnlyList<StandStatusChange> StatusChanges => _statusChanges.AsReadOnly();

    /// <summary>The current holder (customer) account, once allocated/sold.</summary>
    public long? CurrentHolderCustomerId { get; private set; }

    /// <summary>The tamper-evident chain of ownership, in sequence.</summary>
    public IReadOnlyList<OwnershipRecord> Ownership => _ownership.OrderBy(x => x.Sequence).ToList();

    /// <summary>The mapped boundary, once surveyed onto the map.</summary>
    public MappedBoundary? Boundary { get; private set; }

    /// <summary>The road frontage, for apportioning servicing cost.</summary>
    public decimal? FrontageMetres { get; private set; }

    /// <summary>The zoning weight, for apportioning servicing cost.</summary>
    public decimal? ZoningWeight { get; private set; }

    /// <summary>The street address.</summary>
    public string? StreetAddress { get; set; }

    /// <summary>The ward.</summary>
    public string? Ward { get; set; }

    /// <summary>The suburb.</summary>
    public string? Suburb { get; set; }

    /// <summary>The zone.</summary>
    public string? Zone { get; set; }

    /// <summary>The intended use (residential, commercial, industrial, institutional…).</summary>
    public string? IntendedUse { get; set; }

    /// <summary>The zoning.</summary>
    public string? Zoning { get; set; }

    /// <summary>The servicing phase.</summary>
    public string? ServicingPhase { get; set; }

    /// <summary>Whether water is available to the stand.</summary>
    public bool HasWater { get; set; }

    /// <summary>Whether sewer is available.</summary>
    public bool HasSewer { get; set; }

    /// <summary>Whether a road reaches it.</summary>
    public bool HasRoad { get; set; }

    /// <summary>Whether electricity is available.</summary>
    public bool HasElectricity { get; set; }

    /// <summary>Whether storm-water drainage is available.</summary>
    public bool HasStormWater { get; set; }

    /// <summary>Records the measures servicing cost is apportioned on.</summary>
    /// <param name="frontageMetres">The road frontage.</param>
    /// <param name="zoningWeight">The zoning weight.</param>
    public void SetServicingMeasures(decimal? frontageMetres, decimal? zoningWeight)
    {
        FrontageMetres = frontageMetres;
        ZoningWeight = zoningWeight;
    }

    /// <summary>Capitalises a share of servicing cost into the stand (it is still in stock).</summary>
    /// <param name="amount">The share.</param>
    public void AddServicingCost(decimal amount) => AllocatedCost += amount;

    /// <summary>Records the mapped boundary.</summary>
    /// <param name="boundary">The boundary.</param>
    public void Map(MappedBoundary boundary) => Boundary = boundary;

    /// <summary>Marks the stand allocated to a beneficiary under an approved allocation (brief §4.10.4).</summary>
    public void MarkAllocated() => SetStatus(StandStatus.Allocated, Today, null, "Allocation", null);

    /// <summary>Moves the stand along its lifecycle, refusing a jump (brief §4.10.2).</summary>
    /// <param name="to">Where it goes.</param>
    /// <param name="date">The date.</param>
    /// <param name="userId">Who moves it.</param>
    /// <param name="authority">The authority (resolution, minute or document).</param>
    /// <param name="reason">Why.</param>
    /// <returns>Success, or why not.</returns>
    public Result MoveTo(StandStatus to, DateOnly date, long userId, string authority, string? reason)
    {
        if (string.IsNullOrWhiteSpace(authority))
        {
            return Result.Failure(Error.Validation("Hse.StandAuthority", $"Moving stand {StandNumber} needs the authority for it."));
        }

        if (Status == StandStatus.Withheld)
        {
            if (to != StatusBeforeHold)
            {
                return Result.Failure(Error.Rule("Hse.StandWithheld", $"Stand {StandNumber} is withheld: release it (back to {StatusBeforeHold}) first."));
            }

            StatusBeforeHold = null;
            SetStatus(to, date, userId, authority.Trim(), reason);
            return Result.Success();
        }

        if (!StandLifecycle.CanMove(Status, to))
        {
            return Result.Failure(Error.Rule("Hse.StandMove", $"Stand {StandNumber} cannot go from {Status} to {to}; next can be: {string.Join(", ", StandLifecycle.Next(Status))}."));
        }

        if (to == StandStatus.Withheld)
        {
            StatusBeforeHold = Status;
        }

        if (to == StandStatus.Surrendered)
        {
            CurrentHolderCustomerId = null;
            AppendOwnership(OwnershipChangeType.Surrender, null, "Council (surrendered)", date, 0m);
        }

        SetStatus(to, date, userId, authority.Trim(), reason);
        return Result.Success();
    }

    /// <summary>Retires the stand into a consolidated stand, its cost moving with it.</summary>
    /// <param name="intoStandId">The consolidated stand.</param>
    /// <param name="date">The date.</param>
    /// <param name="userId">Who did it.</param>
    /// <param name="authority">The authority.</param>
    /// <returns>The cost moved.</returns>
    public decimal ConsolidateInto(long intoStandId, DateOnly date, long userId, string authority)
    {
        decimal cost = AllocatedCost;
        ConsolidatedIntoStandId = intoStandId;
        AllocatedCost = 0m;
        SetStatus(StandStatus.ConsolidatedAway, date, userId, authority, FormattableString.Invariant($"Cost {cost:0.00} moved to the consolidated stand"));
        return cost;
    }

    /// <summary>Retires the stand into the stands it was re-subdivided into, its cost moving with them.</summary>
    /// <param name="date">The date.</param>
    /// <param name="userId">Who did it.</param>
    /// <param name="authority">The authority.</param>
    /// <param name="children">The new stand numbers.</param>
    public void Resubdivide(DateOnly date, long userId, string authority, IEnumerable<string> children)
    {
        decimal cost = AllocatedCost;
        AllocatedCost = 0m;
        SetStatus(StandStatus.ReSubdivided, date, userId, authority, FormattableString.Invariant($"Cost {cost:0.00} split into {string.Join(", ", children)}"));
    }

    /// <summary>Records that this stand came from re-subdividing another.</summary>
    /// <param name="parentStandId">The parent stand.</param>
    public void SetParent(long parentStandId) => ParentStandId = parentStandId;

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    private void SetStatus(StandStatus to, DateOnly date, long? userId, string authority, string? reason)
    {
        if (to != Status)
        {
            _statusChanges.Add(new StandStatusChange(Status, to, date, userId, authority, reason));
            Status = to;
        }
    }

    /// <summary>
    /// Takes the stand back from a defaulting holder: it returns to the available pool at its new carrying amount (the
    /// lower of its previous cost and net realisable value), and the repossession is recorded on the ownership chain.
    /// </summary>
    /// <param name="date">The repossession date.</param>
    /// <param name="carryingAmount">The amount the stand is re-recognised at in inventory.</param>
    public void Repossess(DateOnly date, decimal carryingAmount)
    {
        AllocatedCost = carryingAmount;
        CurrentHolderCustomerId = null;
        SetStatus(StandStatus.Repossessed, date, null, "Repossession", "Repossessed and back in stock, to be released for re-allocation");
        AppendOwnership(OwnershipChangeType.Repossession, null, "Council (repossessed)", date, 0m);
    }

    /// <summary>Returns an allocated stand to the available pool (the offer was declined or lapsed).</summary>
    public void ReleaseAllocation()
    {
        if (Status == StandStatus.Allocated)
        {
            SetStatus(StandStatus.Available, Today, null, "Allocation released", "Offer declined or lapsed");
        }
    }

    /// <summary>Records the first sale (transfer of control) to a beneficiary.</summary>
    /// <param name="buyerCustomerId">The buyer's account.</param>
    /// <param name="buyerName">The buyer's name.</param>
    /// <param name="sellingPrice">The agreed price.</param>
    /// <param name="date">The transfer date.</param>
    public void Sell(long buyerCustomerId, string buyerName, decimal sellingPrice, DateOnly date)
    {
        SellingPrice = sellingPrice;
        CurrentHolderCustomerId = buyerCustomerId;
        SetStatus(StandStatus.Sold, date, null, "Sale", null);
        AppendOwnership(OwnershipChangeType.Sale, buyerCustomerId, buyerName, date, sellingPrice);
    }

    /// <summary>Records a cession / change of ownership to a subsequent holder.</summary>
    /// <param name="newHolderCustomerId">The new holder's account.</param>
    /// <param name="newHolderName">The new holder's name.</param>
    /// <param name="consideration">The consideration.</param>
    /// <param name="date">The cession date.</param>
    public void Cede(long newHolderCustomerId, string newHolderName, decimal consideration, DateOnly date)
    {
        CurrentHolderCustomerId = newHolderCustomerId;
        SetStatus(StandStatus.Transferred, date, null, "Cession", null);
        AppendOwnership(OwnershipChangeType.Cession, newHolderCustomerId, newHolderName, date, consideration);
    }

    /// <summary>Verifies the ownership hash chain is intact (tamper-evident, brief §4.10).</summary>
    /// <returns>True when every record's hash matches its content and predecessor.</returns>
    public bool VerifyOwnershipChain()
    {
        string? previous = null;
        foreach (OwnershipRecord record in _ownership.OrderBy(x => x.Sequence))
        {
            if (record.PreviousHash != previous || record.Hash != ComputeHash(record, previous))
            {
                return false;
            }

            previous = record.Hash;
        }

        return true;
    }

    /// <summary>Whether the stand's chain holds only its origination record (so migrated history can be laid onto it).</summary>
    public bool HasNoOwnershipHistory => _ownership.Count <= 1;

    /// <summary>
    /// Appends a migrated ownership change (opening-balance take-on, brief §10): the record joins the hash chain like any other, so the history is
    /// tamper-evident from the day it is loaded. Dates must not run backwards.
    /// </summary>
    /// <param name="changeType">The kind of change (not origination, which every stand already has).</param>
    /// <param name="holderCustomerId">The holder's account, if any.</param>
    /// <param name="holderName">The holder's name.</param>
    /// <param name="date">The effective date.</param>
    /// <param name="consideration">The consideration.</param>
    public void ImportOwnership(OwnershipChangeType changeType, long? holderCustomerId, string holderName, DateOnly date, decimal consideration)
    {
        if (changeType == OwnershipChangeType.Origination)
        {
            throw new InvalidOperationException("A stand's origination is recorded when it is created; history starts from the first change after it.");
        }

        if (_ownership.Count > 1 && date < _ownership[^1].EffectiveDate)
        {
            throw new InvalidOperationException($"Ownership history for stand {StandNumber} must be in date order.");
        }

        AppendOwnership(changeType, holderCustomerId, holderName.Trim(), date, consideration);
    }

    private void AppendOwnership(OwnershipChangeType changeType, long? holderId, string holderName, DateOnly date, decimal consideration)
    {
        int sequence = _ownership.Count + 1;
        string? previous = _ownership.Count == 0 ? null : _ownership[^1].Hash;
        OwnershipRecord record = new(sequence, changeType, holderId, holderName, date, consideration, previous);
        record.SetHash(ComputeHash(record, previous));
        _ownership.Add(record);
    }

    private static string ComputeHash(OwnershipRecord record, string? previousHash)
    {
        string payload = string.Join(
            '|',
            record.Sequence.ToString(CultureInfo.InvariantCulture),
            ((int)record.ChangeType).ToString(CultureInfo.InvariantCulture),
            record.HolderCustomerId?.ToString(CultureInfo.InvariantCulture) ?? "-",
            record.HolderName,
            record.EffectiveDate.ToString("O", CultureInfo.InvariantCulture),
            record.Consideration.ToString("F4", CultureInfo.InvariantCulture),
            previousHash ?? "genesis");
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
    }
}

/// <summary>One tamper-evident link in a stand's chain of ownership (<c>hse.ownership_record</c>).</summary>
public sealed class OwnershipRecord : Entity
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private OwnershipRecord()
    {
        HolderName = string.Empty;
        Hash = string.Empty;
    }

    /// <summary>Creates an ownership record.</summary>
    /// <param name="sequence">The 1-based position in the chain.</param>
    /// <param name="changeType">The kind of change.</param>
    /// <param name="holderCustomerId">The holder's account, if any.</param>
    /// <param name="holderName">The holder's name.</param>
    /// <param name="effectiveDate">The effective date.</param>
    /// <param name="consideration">The consideration.</param>
    /// <param name="previousHash">The predecessor's hash, or null for the genesis record.</param>
    public OwnershipRecord(int sequence, OwnershipChangeType changeType, long? holderCustomerId, string holderName, DateOnly effectiveDate, decimal consideration, string? previousHash)
    {
        Sequence = sequence;
        ChangeType = changeType;
        HolderCustomerId = holderCustomerId;
        HolderName = holderName;
        EffectiveDate = effectiveDate;
        Consideration = consideration;
        PreviousHash = previousHash;
        Hash = string.Empty;
    }

    /// <summary>The owning stand id.</summary>
    public long HousingStandId { get; private set; }

    /// <summary>The 1-based position in the chain.</summary>
    public int Sequence { get; private set; }

    /// <summary>The kind of change.</summary>
    public OwnershipChangeType ChangeType { get; private set; }

    /// <summary>The holder's account, if any.</summary>
    public long? HolderCustomerId { get; private set; }

    /// <summary>The holder's name.</summary>
    public string HolderName { get; private set; }

    /// <summary>The effective date.</summary>
    public DateOnly EffectiveDate { get; private set; }

    /// <summary>The consideration.</summary>
    public decimal Consideration { get; private set; }

    /// <summary>The predecessor's hash, or null for the genesis record.</summary>
    public string? PreviousHash { get; private set; }

    /// <summary>This record's hash.</summary>
    public string Hash { get; private set; }

    /// <summary>Sets the computed hash (during construction / append).</summary>
    /// <param name="hash">The hash.</param>
    internal void SetHash(string hash) => Hash = hash;
}

/// <summary>A dated remark on a stand (<c>hse.stand_remark</c>, brief §4.10.2 "a free-form remarks history"): added, never edited.</summary>
public sealed class StandRemark : Entity, ICompanyScoped
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private StandRemark()
    {
        Text = string.Empty;
        RecordedBy = string.Empty;
    }

    /// <summary>Records a remark.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="standId">The stand.</param>
    /// <param name="text">The remark.</param>
    /// <param name="recordedBy">Who recorded it.</param>
    /// <param name="at">When.</param>
    public StandRemark(long companyId, long standId, string text, string recordedBy, DateTimeOffset at)
    {
        CompanyId = companyId;
        StandId = standId;
        Text = text;
        RecordedBy = recordedBy;
        At = at;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The stand.</summary>
    public long StandId { get; private set; }

    /// <summary>The remark.</summary>
    public string Text { get; private set; }

    /// <summary>Who recorded it.</summary>
    public string RecordedBy { get; private set; }

    /// <summary>When.</summary>
    public DateTimeOffset At { get; private set; }
}
