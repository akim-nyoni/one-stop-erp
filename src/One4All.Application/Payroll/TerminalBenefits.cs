namespace One4All.Application.Payroll;

/// <summary>The policy terms terminal benefits are computed on.</summary>
/// <param name="NoticeMonths">Months of notice paid.</param>
/// <param name="WorkingDaysPerMonth">Working days in a month (for the daily rate).</param>
/// <param name="GratuityWeeksPerYear">Weeks of pay per completed year of service as gratuity.</param>
/// <param name="SeveranceWeeksPerYear">Weeks of pay per completed year as severance on retrenchment.</param>
/// <param name="TerminalTaxRatePercent">The tax rate on gratuity and severance.</param>
public readonly record struct TerminalPolicy(decimal NoticeMonths, decimal WorkingDaysPerMonth, decimal GratuityWeeksPerYear, decimal SeveranceWeeksPerYear, decimal TerminalTaxRatePercent);

/// <summary>A final settlement, line by line.</summary>
/// <param name="Years">Completed years of service.</param>
/// <param name="Notice">Notice pay.</param>
/// <param name="Leave">Leave paid out.</param>
/// <param name="Gratuity">Gratuity.</param>
/// <param name="Severance">Severance.</param>
/// <param name="Recoveries">Amounts recovered (loans).</param>
/// <param name="Tax">Tax on terminal benefits.</param>
/// <param name="Net">What is paid.</param>
public readonly record struct FinalSettlement(int Years, decimal Notice, decimal Leave, decimal Gratuity, decimal Severance, decimal Recoveries, decimal Tax, decimal Net);

/// <summary>Terminal benefits: the final settlement on leaving and the long-service provision while in service.</summary>
public static class TerminalBenefits
{
    /// <summary>Completed years between two dates.</summary>
    /// <param name="from">The start.</param>
    /// <param name="to">The end.</param>
    /// <returns>The whole years.</returns>
    public static int CompletedYears(DateOnly from, DateOnly to)
    {
        int years = to.Year - from.Year;
        return Math.Max(0, from.AddYears(years) > to ? years - 1 : years);
    }

    /// <summary>A week's pay from a monthly salary.</summary>
    /// <param name="monthly">The monthly basic.</param>
    /// <returns>The weekly pay.</returns>
    public static decimal Weekly(decimal monthly) => monthly * 12m / 52m;

    /// <summary>Computes a final settlement.</summary>
    /// <param name="monthlyBasic">The monthly basic salary.</param>
    /// <param name="engaged">The engagement date.</param>
    /// <param name="left">The termination date.</param>
    /// <param name="leaveDays">Leave days still owed.</param>
    /// <param name="retrenched">Whether the termination is a retrenchment (severance applies).</param>
    /// <param name="recoveries">Amounts owed to the employer (loans).</param>
    /// <param name="policy">The policy terms.</param>
    /// <returns>The settlement, each line to the cent.</returns>
    public static FinalSettlement Settle(decimal monthlyBasic, DateOnly engaged, DateOnly left, decimal leaveDays, bool retrenched, decimal recoveries, TerminalPolicy policy)
    {
        static decimal R(decimal v) => Math.Round(v, 2, MidpointRounding.AwayFromZero);
        int years = CompletedYears(engaged, left);
        decimal notice = R(monthlyBasic * policy.NoticeMonths);
        decimal leave = policy.WorkingDaysPerMonth <= 0m ? 0m : R(Math.Max(0m, leaveDays) * monthlyBasic / policy.WorkingDaysPerMonth);
        decimal gratuity = R(years * policy.GratuityWeeksPerYear * Weekly(monthlyBasic));
        decimal severance = retrenched ? R(years * policy.SeveranceWeeksPerYear * Weekly(monthlyBasic)) : 0m;
        decimal tax = R((gratuity + severance) * policy.TerminalTaxRatePercent / 100m);
        return new FinalSettlement(years, notice, leave, gratuity, severance, R(recoveries), tax, notice + leave + gratuity + severance - R(recoveries) - tax);
    }

    /// <summary>The long-service provision for an employee: the gratuity earned to date, discounted from retirement to today.</summary>
    /// <param name="monthlyBasic">The monthly basic.</param>
    /// <param name="engaged">The engagement date.</param>
    /// <param name="asAt">The valuation date.</param>
    /// <param name="dateOfBirth">The date of birth (null: no discounting).</param>
    /// <param name="weeksPerYear">Weeks of pay per year of service.</param>
    /// <param name="retirementAge">The normal retirement age.</param>
    /// <param name="discountRatePercent">The annual discount rate.</param>
    /// <returns>The accrued entitlement, the whole years to retirement and the discounted provision.</returns>
    public static (decimal Accrued, int YearsToRetirement, decimal Provision) Provision(decimal monthlyBasic, DateOnly engaged, DateOnly asAt, DateOnly? dateOfBirth, decimal weeksPerYear,
        int retirementAge, decimal discountRatePercent)
    {
        decimal accrued = Math.Round(CompletedYears(engaged, asAt) * weeksPerYear * Weekly(monthlyBasic), 2, MidpointRounding.AwayFromZero);
        int toRetirement = dateOfBirth is DateOnly born ? Math.Max(0, retirementAge - CompletedYears(born, asAt)) : 0;
        decimal factor = 1m;
        for (int i = 0; i < toRetirement; i++)
        {
            factor /= 1m + (discountRatePercent / 100m);
        }

        return (accrued, toRetirement, Math.Round(accrued * factor, 2, MidpointRounding.AwayFromZero));
    }
}
