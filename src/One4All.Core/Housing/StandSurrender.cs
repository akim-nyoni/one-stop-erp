using One4All.Core.Common;

namespace One4All.Core.Housing;

/// <summary>
/// A stand surrendered by its holder to council (<c>hse.stand_surrender</c>, brief §4.10.2/§4.10.5): requested and approved by different people, the
/// holder's instalment receivable derecognised, a sold stand back in inventory at the lower of cost and net realisable value, and any refund credited
/// to the holder's account.
/// </summary>
public sealed class StandSurrender : AuditableEntity, ICompanyScoped
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private StandSurrender()
    {
        Reference = string.Empty;
        Reason = string.Empty;
    }

    /// <summary>Records a surrender.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="reference">The surrender reference.</param>
    /// <param name="standId">The stand.</param>
    /// <param name="holderCustomerId">The holder's customer account (none for an allocatee without one).</param>
    /// <param name="agreementId">The instalment agreement closed, if any.</param>
    /// <param name="date">The date.</param>
    /// <param name="receivableDerecognised">The receivable written off.</param>
    /// <param name="inventoryRecognised">The amount a sold stand came back into inventory at.</param>
    /// <param name="refund">The amount credited to the holder.</param>
    /// <param name="reason">Why.</param>
    /// <param name="requestedByUserId">Who requested it.</param>
    /// <param name="approvedByUserId">Who approved it.</param>
    public StandSurrender(long companyId, string reference, long standId, long? holderCustomerId, long? agreementId, DateOnly date, decimal receivableDerecognised,
        decimal inventoryRecognised, decimal refund, string reason, long requestedByUserId, long approvedByUserId)
    {
        CompanyId = companyId;
        Reference = reference;
        StandId = standId;
        HolderCustomerId = holderCustomerId;
        AgreementId = agreementId;
        Date = date;
        ReceivableDerecognised = receivableDerecognised;
        InventoryRecognised = inventoryRecognised;
        Refund = refund;
        Reason = reason;
        RequestedByUserId = requestedByUserId;
        ApprovedByUserId = approvedByUserId;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The surrender reference.</summary>
    public string Reference { get; private set; }

    /// <summary>The stand.</summary>
    public long StandId { get; private set; }

    /// <summary>The holder's customer account (none for an allocatee without one).</summary>
    public long? HolderCustomerId { get; private set; }

    /// <summary>The holder as named on the register (customer code, or the allocatee's name).</summary>
    public string Holder { get; private set; } = string.Empty;

    /// <summary>The allocation it ended, if any.</summary>
    public long? AllocationId { get; private set; }

    /// <summary>Names the holder and the allocation the surrender ended.</summary>
    /// <param name="holder">The holder as named on the register.</param>
    /// <param name="allocationId">The allocation ended, if any.</param>
    public void Describe(string holder, long? allocationId)
    {
        Holder = holder;
        AllocationId = allocationId;
    }

    /// <summary>The instalment agreement closed, if any.</summary>
    public long? AgreementId { get; private set; }

    /// <summary>The date.</summary>
    public DateOnly Date { get; private set; }

    /// <summary>The receivable written off.</summary>
    public decimal ReceivableDerecognised { get; private set; }

    /// <summary>The amount a sold stand came back into inventory at.</summary>
    public decimal InventoryRecognised { get; private set; }

    /// <summary>The amount credited to the holder.</summary>
    public decimal Refund { get; private set; }

    /// <summary>Why.</summary>
    public string Reason { get; private set; }

    /// <summary>Who requested it.</summary>
    public long RequestedByUserId { get; private set; }

    /// <summary>Who approved it.</summary>
    public long ApprovedByUserId { get; private set; }
}
