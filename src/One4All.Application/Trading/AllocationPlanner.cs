namespace One4All.Application.Trading;

/// <summary>An open item offered to the allocation planner.</summary>
/// <param name="Id">The open item.</param>
/// <param name="Date">The document date.</param>
/// <param name="DueDate">The due date (what "oldest" means for the items being settled).</param>
/// <param name="Outstanding">The outstanding transaction amount (positive).</param>
public sealed record AllocatableItem(long Id, DateOnly Date, DateOnly DueDate, decimal Outstanding);

/// <summary>A proposed allocation of part of a credit item against a debit item.</summary>
/// <param name="DebitItemId">The debit item.</param>
/// <param name="CreditItemId">The credit item.</param>
/// <param name="Amount">The amount to allocate.</param>
/// <param name="ExactMatch">True when paired because the two outstanding amounts were identical.</param>
public sealed record PlannedAllocation(long DebitItemId, long CreditItemId, decimal Amount, bool ExactMatch);

/// <summary>
/// Plans automatic allocation of one trader's open items (brief §4.5/§4.6), in one currency. Pass one pairs a
/// debit and credit whose outstanding amounts are identical (a receipt or payment for exactly one document),
/// earliest first on both sides. Pass two applies what is left oldest-first: credits in date order against
/// debits in due-date order (then date, then id), splitting as needed. Symmetric, so it serves AR (invoices are
/// debits) and AP (invoices are credits) alike; never allocates more than either side has outstanding. Pure.
/// </summary>
public static class AllocationPlanner
{
    /// <summary>Plans the allocations.</summary>
    /// <param name="debits">The trader's outstanding debit items.</param>
    /// <param name="credits">The trader's outstanding credit items.</param>
    /// <returns>The allocations, exact matches first.</returns>
    public static IReadOnlyList<PlannedAllocation> Plan(IReadOnlyList<AllocatableItem> debits, IReadOnlyList<AllocatableItem> credits)
    {
        Dictionary<long, decimal> debitLeft = debits.Where(d => d.Outstanding > 0m).ToDictionary(d => d.Id, d => d.Outstanding);
        Dictionary<long, decimal> creditLeft = credits.Where(c => c.Outstanding > 0m).ToDictionary(c => c.Id, c => c.Outstanding);

        List<AllocatableItem> debitOrder = [.. debits.Where(d => debitLeft.ContainsKey(d.Id)).OrderBy(d => d.DueDate).ThenBy(d => d.Date).ThenBy(d => d.Id)];
        List<AllocatableItem> creditOrder = [.. credits.Where(c => creditLeft.ContainsKey(c.Id)).OrderBy(c => c.DueDate).ThenBy(c => c.Date).ThenBy(c => c.Id)];

        List<PlannedAllocation> plan = [];

        // Pass 1: exact amounts.
        foreach (AllocatableItem credit in creditOrder)
        {
            AllocatableItem? debit = debitOrder.FirstOrDefault(d => debitLeft[d.Id] > 0m && debitLeft[d.Id] == creditLeft[credit.Id]);
            if (debit is null)
            {
                continue;
            }

            decimal amount = creditLeft[credit.Id];
            plan.Add(new PlannedAllocation(debit.Id, credit.Id, amount, true));
            debitLeft[debit.Id] = 0m;
            creditLeft[credit.Id] = 0m;
        }

        // Pass 2: oldest first.
        foreach (AllocatableItem credit in creditOrder)
        {
            foreach (AllocatableItem debit in debitOrder)
            {
                if (creditLeft[credit.Id] == 0m)
                {
                    break;
                }

                if (debitLeft[debit.Id] == 0m)
                {
                    continue;
                }

                decimal amount = Math.Min(creditLeft[credit.Id], debitLeft[debit.Id]);
                plan.Add(new PlannedAllocation(debit.Id, credit.Id, amount, false));
                debitLeft[debit.Id] -= amount;
                creditLeft[credit.Id] -= amount;
            }
        }

        return plan;
    }
}
