using One4All.Application.Abstractions.Receipting;
using One4All.Core.Receipting;

namespace One4All.Application.Receipting;

/// <summary>
/// A payment split across tenders at the teller (brief §RCP-3): part cash, part card, part mobile money. Each part is positive, every part but cash
/// carries its reference, and parts of the same tender and reference are merged; the receipt is for the total of the parts.
/// </summary>
public static class TenderSplit
{
    /// <summary>Checks the parts.</summary>
    /// <param name="parts">The parts taken so far.</param>
    /// <returns>Null when they can be posted; otherwise why not, in words for the teller.</returns>
    public static string? Problem(IReadOnlyCollection<ReceiptTenderRequest> parts)
    {
        ArgumentNullException.ThrowIfNull(parts);
        if (parts.Count == 0)
        {
            return "Add at least one part: type its amount and press its tender.";
        }

        if (parts.Any(p => p.Amount <= 0m))
        {
            return "Each part must be more than zero.";
        }

        return parts.FirstOrDefault(p => p.Type != TenderType.Cash && string.IsNullOrWhiteSpace(p.Reference)) is { } missing && missing.Amount > 0m
            ? $"The {missing.Type} part needs its reference (card slip, mobile money or cheque number)."
            : null;
    }

    /// <summary>The parts with the same tender and reference merged, in the order first taken.</summary>
    /// <param name="parts">The parts.</param>
    /// <returns>The tenders to post.</returns>
    public static IReadOnlyList<ReceiptTenderRequest> Merge(IEnumerable<ReceiptTenderRequest> parts)
    {
        ArgumentNullException.ThrowIfNull(parts);
        List<ReceiptTenderRequest> merged = [];
        foreach (ReceiptTenderRequest part in parts)
        {
            string? reference = string.IsNullOrWhiteSpace(part.Reference) ? null : part.Reference.Trim();
            int at = merged.FindIndex(m => m.Type == part.Type && string.Equals(m.Reference, reference, StringComparison.OrdinalIgnoreCase));
            if (at >= 0)
            {
                merged[at] = merged[at] with { Amount = merged[at].Amount + part.Amount };
            }
            else
            {
                merged.Add(part with { Reference = reference });
            }
        }

        return merged;
    }

    /// <summary>The receipt total: the sum of the parts.</summary>
    /// <param name="parts">The parts.</param>
    /// <returns>The total.</returns>
    public static decimal Total(IEnumerable<ReceiptTenderRequest> parts) => parts.Sum(p => p.Amount);
}
