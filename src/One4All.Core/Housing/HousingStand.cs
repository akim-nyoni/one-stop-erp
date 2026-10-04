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
}

/// <summary>
/// A housing stand (brief §4.10.2, <c>hse.stand</c>) carrying its apportioned cost and a tamper-evident
/// chain of ownership from origination to the current holder — the module's primary job (brief §4.10).
/// </summary>
public sealed class HousingStand : SoftDeletableEntity, ICompanyScoped
{
    private readonly List<OwnershipRecord> _ownership = [];

    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private HousingStand() => StandNumber = string.Empty;

    /// <summary>Creates a stand from a parcel with its apportioned cost.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="landParcelId">The originating parcel.</param>
    /// <param name="schemeId">The housing scheme, if any.</param>
    /// <param name="standNumber">The stand/erf number.</param>
    /// <param name="sizeSquareMetres">The size in square metres.</param>
    /// <param name="allocatedCost">The cost apportioned from the parcel.</param>
    public HousingStand(long companyId, long landParcelId, long? schemeId, string standNumber, decimal sizeSquareMetres, decimal allocatedCost)
    {
        CompanyId = companyId;
        LandParcelId = landParcelId;
        SchemeId = schemeId;
        StandNumber = standNumber.Trim().ToUpperInvariant();
        SizeSquareMetres = sizeSquareMetres;
        AllocatedCost = allocatedCost;
        Status = StandStatus.Available;
        AppendOwnership(OwnershipChangeType.Origination, null, "Municipality/Developer", DateOnly.FromDateTime(DateTime.UtcNow), 0m);
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
    public void MarkAllocated() => Status = StandStatus.Allocated;

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
        Status = StandStatus.Available;
        AppendOwnership(OwnershipChangeType.Repossession, null, "Council (repossessed)", date, 0m);
    }

    /// <summary>Returns an allocated stand to the available pool (the offer was declined or lapsed).</summary>
    public void ReleaseAllocation()
    {
        if (Status == StandStatus.Allocated)
        {
            Status = StandStatus.Available;
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
        Status = StandStatus.Sold;
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
        Status = StandStatus.Transferred;
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
