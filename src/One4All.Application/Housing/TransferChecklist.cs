using One4All.Core.Housing;

namespace One4All.Application.Housing;

/// <summary>
/// The pre-conditions a change of ownership must meet, by transfer type (brief §4.10.6 defaults): municipal clearance and
/// a clear housing account wherever the holding changes hands; identity always; spousal consent where a spouse's rights
/// are affected; the original documents (or a loss affidavit) whenever the holder changes; estate documents on a death;
/// fees receipted always; and tax withholding where there is consideration on a sale or cession. Pure and unit-tested.
/// </summary>
public static class TransferChecklist
{
    /// <summary>The checklist for a transfer.</summary>
    /// <param name="type">The kind of transfer.</param>
    /// <param name="consideration">The consideration.</param>
    /// <returns>The requirements, in checklist order.</returns>
    public static IReadOnlyList<TransferRequirement> Required(TransferType type, decimal consideration)
    {
        bool changesHands = type is not (TransferType.ChangeOfName or TransferType.Correction);
        List<TransferRequirement> items = [];
        if (changesHands)
        {
            items.Add(TransferRequirement.MunicipalClearance);
            items.Add(TransferRequirement.HousingAccountClear);
        }

        items.Add(TransferRequirement.Identity);
        if (type is TransferType.Sale or TransferType.Cession or TransferType.Donation or TransferType.Matrimonial)
        {
            items.Add(TransferRequirement.SpousalConsent);
        }

        if (changesHands)
        {
            items.Add(TransferRequirement.OriginalDocuments);
        }

        if (type is TransferType.Inheritance or TransferType.DeceasedJointHolder)
        {
            items.Add(TransferRequirement.EstateDocuments);
        }

        items.Add(TransferRequirement.FeesReceipted);
        if (type is TransferType.Sale or TransferType.Cession && consideration > 0m)
        {
            items.Add(TransferRequirement.TaxWithholding);
        }

        return items;
    }

    /// <summary>Whether the system checks an item itself (an officer cannot tick it).</summary>
    /// <param name="requirement">The item.</param>
    /// <returns>True for the live checks.</returns>
    public static bool IsLive(TransferRequirement requirement) => requirement is TransferRequirement.MunicipalClearance or TransferRequirement.HousingAccountClear;
}
