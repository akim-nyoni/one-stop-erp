namespace One4All.Application.Trading;

/// <summary>One open item on a supplier's statement, as the supplier shows it.</summary>
/// <param name="DocumentNumber">The supplier's document number (their invoice / credit number, or their payment description).</param>
/// <param name="Date">The document date.</param>
/// <param name="Amount">The outstanding amount from the supplier's point of view: invoices positive, credits and payments negative.</param>
public sealed record SupplierStatementLine(string DocumentNumber, DateOnly Date, decimal Amount);

/// <summary>One outstanding item on our purchase ledger for the supplier, signed the supplier's way.</summary>
/// <param name="OpenItemId">The AP open item.</param>
/// <param name="DocumentNumber">The document number we recorded.</param>
/// <param name="Date">The document date.</param>
/// <param name="Amount">Outstanding amount: what we owe positive (invoices), what reduces it negative (payments, debit notes).</param>
public sealed record SupplierLedgerItem(long OpenItemId, string DocumentNumber, DateOnly Date, decimal Amount);

/// <summary>How a line of the reconciliation resolved.</summary>
public enum SupplierReconStatus
{
    /// <summary>On both sides at the same amount.</summary>
    Agreed = 1,

    /// <summary>On both sides (same document) at different amounts.</summary>
    AmountDiffers = 2,

    /// <summary>On the supplier's statement but not outstanding on our ledger (unposted invoice, or a payment they have not applied).</summary>
    MissingFromLedger = 3,

    /// <summary>Outstanding on our ledger but not on the supplier's statement (in transit, or posted to the wrong supplier).</summary>
    NotOnStatement = 4,
}

/// <summary>One line of the reconciliation.</summary>
/// <param name="Status">How it resolved.</param>
/// <param name="DocumentNumber">The document number (the supplier's when they have it, else ours).</param>
/// <param name="Date">The document date.</param>
/// <param name="StatementAmount">The supplier's amount, or null when not on their statement.</param>
/// <param name="LedgerAmount">Our amount, or null when not on our ledger.</param>
/// <param name="OpenItemId">Our open item, when there is one.</param>
/// <param name="MatchedByAmount">True when paired on amount and date rather than document number.</param>
public sealed record SupplierReconLine(SupplierReconStatus Status, string DocumentNumber, DateOnly Date, decimal? StatementAmount, decimal? LedgerAmount, long? OpenItemId, bool MatchedByAmount)
{
    /// <summary>Statement amount less ledger amount (missing sides count as zero).</summary>
    public decimal Difference => (StatementAmount ?? 0m) - (LedgerAmount ?? 0m);
}

/// <summary>The reconciliation of a supplier's statement to our purchase ledger.</summary>
/// <param name="Lines">Every line, mismatches first then agreed, each group by date.</param>
/// <param name="StatementBalance">The total of the supplier's statement.</param>
/// <param name="LedgerBalance">Our outstanding balance with the supplier.</param>
public sealed record SupplierReconciliation(IReadOnlyList<SupplierReconLine> Lines, decimal StatementBalance, decimal LedgerBalance)
{
    /// <summary>Statement balance less ledger balance; equals the sum of the line differences.</summary>
    public decimal Difference => StatementBalance - LedgerBalance;

    /// <summary>True when every line agrees.</summary>
    public bool IsAgreed => Lines.All(l => l.Status == SupplierReconStatus.Agreed);
}

/// <summary>
/// Reconciles a supplier's open-item statement to our outstanding purchase-ledger items (brief §4.6). First
/// pass pairs by document number (case, space and punctuation insensitive); a second pass pairs what is left
/// by equal amount within a date window — which is how payments, described in the supplier's own words,
/// are agreed. Every line resolves to Agreed, AmountDiffers, MissingFromLedger or NotOnStatement, and the line
/// differences always sum to the balance difference. Pure and unit-tested.
/// </summary>
public static class SupplierStatementReconciler
{
    /// <summary>The default days either side a same-amount item may fall in the second pass.</summary>
    public const int DefaultDateToleranceDays = 7;

    /// <summary>Reconciles the statement to the ledger.</summary>
    /// <param name="statement">The supplier's statement lines.</param>
    /// <param name="ledger">Our outstanding items for the supplier.</param>
    /// <param name="dateToleranceDays">Days either side for the amount pass.</param>
    /// <returns>The reconciliation.</returns>
    public static SupplierReconciliation Reconcile(IReadOnlyList<SupplierStatementLine> statement, IReadOnlyList<SupplierLedgerItem> ledger, int dateToleranceDays = DefaultDateToleranceDays)
    {
        List<SupplierReconLine> lines = [];
        List<SupplierStatementLine> theirs = [.. statement.OrderBy(s => s.Date)];
        List<SupplierLedgerItem> ours = [.. ledger.OrderBy(l => l.Date).ThenBy(l => l.OpenItemId)];

        // Pass 1: by document number.
        foreach (SupplierStatementLine line in theirs.ToList())
        {
            string key = Key(line.DocumentNumber);
            if (key.Length == 0)
            {
                continue;
            }

            SupplierLedgerItem? item = ours.FirstOrDefault(o => Key(o.DocumentNumber) == key);
            if (item is null)
            {
                continue;
            }

            lines.Add(new SupplierReconLine(item.Amount == line.Amount ? SupplierReconStatus.Agreed : SupplierReconStatus.AmountDiffers,
                line.DocumentNumber, line.Date, line.Amount, item.Amount, item.OpenItemId, false));
            theirs.Remove(line);
            ours.Remove(item);
        }

        // Pass 2: what is left, by equal amount within the date window (closest date first).
        foreach (SupplierStatementLine line in theirs.ToList())
        {
            SupplierLedgerItem? item = ours
                .Where(o => o.Amount == line.Amount && Math.Abs(o.Date.DayNumber - line.Date.DayNumber) <= dateToleranceDays)
                .OrderBy(o => Math.Abs(o.Date.DayNumber - line.Date.DayNumber))
                .ThenBy(o => o.OpenItemId)
                .FirstOrDefault();
            if (item is null)
            {
                continue;
            }

            lines.Add(new SupplierReconLine(SupplierReconStatus.Agreed, line.DocumentNumber, line.Date, line.Amount, item.Amount, item.OpenItemId, true));
            theirs.Remove(line);
            ours.Remove(item);
        }

        lines.AddRange(theirs.Select(s => new SupplierReconLine(SupplierReconStatus.MissingFromLedger, s.DocumentNumber, s.Date, s.Amount, null, null, false)));
        lines.AddRange(ours.Select(o => new SupplierReconLine(SupplierReconStatus.NotOnStatement, o.DocumentNumber, o.Date, null, o.Amount, o.OpenItemId, false)));

        List<SupplierReconLine> ordered = [.. lines
            .OrderBy(l => l.Status == SupplierReconStatus.Agreed ? 1 : 0)
            .ThenBy(l => l.Date)
            .ThenBy(l => l.DocumentNumber, StringComparer.Ordinal)];

        return new SupplierReconciliation(ordered, statement.Sum(s => s.Amount), ledger.Sum(l => l.Amount));
    }

    // Document numbers compare on letters and digits only: "INV-0042", "inv 0042" and "INV0042" are the same.
    private static string Key(string documentNumber) =>
        new([.. documentNumber.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant)]);
}
