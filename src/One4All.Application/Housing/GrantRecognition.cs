namespace One4All.Application.Housing;

/// <summary>
/// IAS 20 grant recognition (brief §4.10.9: recognise in line with the expenditure the grant compensates; deferred while
/// conditions are outstanding): the income recognised to date is the grant's share of the budgeted expenditure incurred
/// so far, capped at the grant, and nothing until the conditions are reasonably assured. Pure and unit-tested.
/// </summary>
public static class GrantRecognition
{
    /// <summary>The income that should be recognised to date.</summary>
    /// <param name="grant">The grant received.</param>
    /// <param name="budgetedExpenditure">The expenditure it funds.</param>
    /// <param name="expenditureToDate">The expenditure incurred so far.</param>
    /// <param name="conditionsAssured">Whether the conditions are reasonably assured.</param>
    /// <returns>The income to date, to the cent.</returns>
    public static decimal TargetToDate(decimal grant, decimal budgetedExpenditure, decimal expenditureToDate, bool conditionsAssured)
    {
        if (!conditionsAssured || budgetedExpenditure <= 0m || expenditureToDate <= 0m)
        {
            return 0m;
        }

        decimal share = Math.Min(1m, expenditureToDate / budgetedExpenditure);
        return decimal.Round(grant * share, 2, MidpointRounding.AwayFromZero);
    }
}
