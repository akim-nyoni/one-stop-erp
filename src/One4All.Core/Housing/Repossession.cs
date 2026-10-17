using One4All.Core.Common;

namespace One4All.Core.Housing;

/// <summary>
/// A stand repossessed from a defaulting instalment buyer (<c>hse.repossession</c>, brief §4.10.5 and the §4.10.9
/// ruling): the receivable derecognised, the stand re-recognised in inventory at the lower of its previous carrying
/// amount and net realisable value, and the difference taken to profit or loss - a sensitive number kept here so it is
/// separately reportable. Requested and approved by different people.
/// </summary>
public sealed class Repossession : AuditableEntity, ICompanyScoped
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private Repossession()
    {
        Reference = string.Empty;
        Reason = string.Empty;
    }

    /// <summary>Records a repossession.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="reference">The repossession reference.</param>
    /// <param name="agreementId">The instalment agreement.</param>
    /// <param name="standId">The stand.</param>
    /// <param name="date">The repossession date.</param>
    /// <param name="receivableDerecognised">The receivable written off.</param>
    /// <param name="previousCarryingAmount">The stand's cost before the sale.</param>
    /// <param name="netRealisableValue">Its net realisable value now.</param>
    /// <param name="inventoryRecognised">The amount it returns to inventory at.</param>
    /// <param name="reason">Why it was repossessed.</param>
    /// <param name="requestedByUserId">Who requested it.</param>
    /// <param name="approvedByUserId">Who approved it.</param>
    public Repossession(long companyId, string reference, long agreementId, long standId, DateOnly date, decimal receivableDerecognised, decimal previousCarryingAmount,
        decimal netRealisableValue, decimal inventoryRecognised, string reason, long requestedByUserId, long approvedByUserId)
    {
        CompanyId = companyId;
        Reference = reference;
        AgreementId = agreementId;
        StandId = standId;
        Date = date;
        ReceivableDerecognised = receivableDerecognised;
        PreviousCarryingAmount = previousCarryingAmount;
        NetRealisableValue = netRealisableValue;
        InventoryRecognised = inventoryRecognised;
        Reason = reason;
        RequestedByUserId = requestedByUserId;
        ApprovedByUserId = approvedByUserId;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The repossession reference.</summary>
    public string Reference { get; private set; }

    /// <summary>The instalment agreement.</summary>
    public long AgreementId { get; private set; }

    /// <summary>The stand.</summary>
    public long StandId { get; private set; }

    /// <summary>The repossession date.</summary>
    public DateOnly Date { get; private set; }

    /// <summary>The receivable written off.</summary>
    public decimal ReceivableDerecognised { get; private set; }

    /// <summary>The stand's cost before the sale.</summary>
    public decimal PreviousCarryingAmount { get; private set; }

    /// <summary>Its net realisable value at repossession.</summary>
    public decimal NetRealisableValue { get; private set; }

    /// <summary>The amount it returned to inventory at.</summary>
    public decimal InventoryRecognised { get; private set; }

    /// <summary>The gain (positive) or loss (negative) on repossession.</summary>
    public decimal GainOrLoss => InventoryRecognised - ReceivableDerecognised;

    /// <summary>Why it was repossessed.</summary>
    public string Reason { get; private set; }

    /// <summary>Who requested it.</summary>
    public long RequestedByUserId { get; private set; }

    /// <summary>Who approved it.</summary>
    public long ApprovedByUserId { get; private set; }
}
