using One4All.Application.Abstractions.Pos;
using One4All.Core.Common;

namespace One4All.Application.Pos;

/// <summary>One basket line's share in a part of a split bill.</summary>
/// <param name="LineIndex">The line's position in the basket (from 0).</param>
/// <param name="Quantity">How much of it this part takes.</param>
public readonly record struct SplitShare(int LineIndex, decimal Quantity);

/// <summary>
/// Splitting one basket into several sales, each paid on its own (brief §4.1 split bill): by items — each part takes whole lines or
/// part quantities — or evenly. Amounts follow the quantity taken; the last share of a line takes any rounding so every line's net,
/// tax and discount add back exactly.
/// </summary>
public static class SplitBill
{
    /// <summary>Divides the basket into its parts' lines.</summary>
    /// <param name="lines">The basket.</param>
    /// <param name="parts">Each part's shares of the basket's lines.</param>
    /// <returns>Each part's sale lines, or why the split does not work.</returns>
    public static Result<IReadOnlyList<IReadOnlyList<PosSaleLineRequest>>> Plan(IReadOnlyList<PosSaleLineRequest> lines, IReadOnlyList<IReadOnlyList<SplitShare>> parts)
    {
        if (parts.Count < 2)
        {
            return Result<IReadOnlyList<IReadOnlyList<PosSaleLineRequest>>>.Failure(Error.Validation("Pos.SplitParts", "A split bill needs at least two parts."));
        }

        if (parts.Any(p => p.Count == 0))
        {
            return Result<IReadOnlyList<IReadOnlyList<PosSaleLineRequest>>>.Failure(Error.Validation("Pos.SplitEmptyPart", "Every part of a split bill must take something."));
        }

        if (parts.SelectMany(p => p).Any(s => s.LineIndex < 0 || s.LineIndex >= lines.Count || s.Quantity <= 0m))
        {
            return Result<IReadOnlyList<IReadOnlyList<PosSaleLineRequest>>>.Failure(Error.Validation("Pos.SplitShare", "Each share must name a basket line and take a positive quantity."));
        }

        for (int i = 0; i < lines.Count; i++)
        {
            decimal taken = parts.SelectMany(p => p).Where(s => s.LineIndex == i).Sum(s => s.Quantity);
            if (taken != lines[i].Quantity)
            {
                return Result<IReadOnlyList<IReadOnlyList<PosSaleLineRequest>>>.Failure(Error.Validation("Pos.SplitUnbalanced",
                    $"Line {i + 1} ({lines[i].Description}) has {lines[i].Quantity} but the parts take {taken}."));
            }
        }

        // The last part taking a line absorbs its rounding.
        Dictionary<int, (int Part, int Share)> last = [];
        for (int p = 0; p < parts.Count; p++)
        {
            for (int s = 0; s < parts[p].Count; s++)
            {
                last[parts[p][s].LineIndex] = (p, s);
            }
        }

        Dictionary<int, (decimal Net, decimal Tax, decimal Discount)> given = [];
        List<IReadOnlyList<PosSaleLineRequest>> planned = [];
        for (int p = 0; p < parts.Count; p++)
        {
            List<PosSaleLineRequest> partLines = [];
            for (int s = 0; s < parts[p].Count; s++)
            {
                SplitShare share = parts[p][s];
                PosSaleLineRequest line = lines[share.LineIndex];
                (decimal net, decimal tax, decimal discount) = given.GetValueOrDefault(share.LineIndex);
                bool isLast = last[share.LineIndex] == (p, s);
                decimal fraction = share.Quantity / line.Quantity;
                decimal shareNet = isLast ? line.LineNet - net : Math.Round(line.LineNet * fraction, 2, MidpointRounding.AwayFromZero);
                decimal shareTax = isLast ? line.TaxAmount - tax : Math.Round(line.TaxAmount * fraction, 2, MidpointRounding.AwayFromZero);
                decimal shareDiscount = isLast ? line.PromotionDiscount - discount : Math.Round(line.PromotionDiscount * fraction, 2, MidpointRounding.AwayFromZero);
                given[share.LineIndex] = (net + shareNet, tax + shareTax, discount + shareDiscount);
                partLines.Add(line with { Quantity = share.Quantity, LineNet = shareNet, TaxAmount = shareTax, PromotionDiscount = shareDiscount });
            }

            planned.Add(partLines);
        }

        return Result<IReadOnlyList<IReadOnlyList<PosSaleLineRequest>>>.Success(planned);
    }

    /// <summary>Shares for splitting every line evenly between a number of people (quantities to four places, the last share takes the rest).</summary>
    /// <param name="lines">The basket.</param>
    /// <param name="ways">How many ways to split it.</param>
    /// <returns>The parts' shares.</returns>
    public static IReadOnlyList<IReadOnlyList<SplitShare>> Evenly(IReadOnlyList<PosSaleLineRequest> lines, int ways)
    {
        List<List<SplitShare>> parts = [.. Enumerable.Range(0, Math.Max(ways, 0)).Select(_ => new List<SplitShare>())];
        for (int i = 0; i < lines.Count && ways > 0; i++)
        {
            decimal each = Math.Round(lines[i].Quantity / ways, 4, MidpointRounding.ToZero);
            for (int p = 0; p < ways; p++)
            {
                parts[p].Add(new SplitShare(i, p == ways - 1 ? lines[i].Quantity - (each * (ways - 1)) : each));
            }
        }

        return parts;
    }

    /// <summary>A part's gross (net plus tax) — what its payer must tender.</summary>
    /// <param name="lines">The part's lines.</param>
    /// <returns>The gross.</returns>
    public static decimal Gross(IEnumerable<PosSaleLineRequest> lines) => lines.Sum(l => l.LineNet + l.TaxAmount);
}
