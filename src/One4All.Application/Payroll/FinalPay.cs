namespace One4All.Application.Payroll;

/// <summary>Why employment ended (brief §4.15.8); it decides which terminal entitlements are paid.</summary>
public enum TerminationType
{
    /// <summary>The employee resigned (notice served or waived).</summary>
    Resignation = 1,

    /// <summary>Dismissed for cause.</summary>
    Dismissal = 2,

    /// <summary>Retrenched: notice, gratuity and severance.</summary>
    Retrenchment = 3,

    /// <summary>Retired: gratuity.</summary>
    Retirement = 4,

    /// <summary>Died in service: gratuity, paid to the estate.</summary>
    Death = 5,

    /// <summary>A fixed-term contract ended: gratuity.</summary>
    EndOfContract = 6,

    /// <summary>Absconded: earned pay and leave only.</summary>
    Absconding = 7,
}

/// <summary>What a final pay is made of, with its working.</summary>
/// <param name="YearsOfService">Completed years.</param>
/// <param name="ProRataSalary">Salary for the days worked in the last month (nil when that month was already paid).</param>
/// <param name="Notice">Notice pay.</param>
/// <param name="LeaveDays">Leave days paid out.</param>
/// <param name="Leave">Leave pay.</param>
/// <param name="Gratuity">Gratuity.</param>
/// <param name="Severance">Severance.</param>
/// <param name="OrdinaryTax">PAYE on the salary, notice and leave.</param>
/// <param name="TerminalTax">Tax on the gratuity and severance.</param>
/// <param name="Recoveries">Loans recovered.</param>
/// <param name="UnrecoveredLoans">Loans the final pay could not cover (still owed).</param>
/// <param name="ProRataBonus">The year's bonus for the part of the year served.</param>
/// <param name="OtherRecoveries">Unreturned company property and overpayments recovered (after the loans).</param>
/// <param name="UnrecoveredOther">What of those the final pay could not cover (still owed).</param>
public sealed record FinalPayStatement(int YearsOfService, decimal ProRataSalary, decimal Notice, decimal LeaveDays, decimal Leave, decimal Gratuity, decimal Severance,
    decimal OrdinaryTax, decimal TerminalTax, decimal Recoveries, decimal UnrecoveredLoans, decimal ProRataBonus = 0m, decimal OtherRecoveries = 0m, decimal UnrecoveredOther = 0m)
{
    /// <summary>Ordinary pay: salary, notice, leave and the pro-rata bonus.</summary>
    public decimal Ordinary => ProRataSalary + Notice + Leave + ProRataBonus;

    /// <summary>Terminal benefits: gratuity and severance.</summary>
    public decimal Terminal => Gratuity + Severance;

    /// <summary>Everything earned.</summary>
    public decimal Gross => Ordinary + Terminal;

    /// <summary>What is paid out.</summary>
    public decimal Net => Gross - OrdinaryTax - TerminalTax - Recoveries - OtherRecoveries;
}

/// <summary>
/// The final pay of a leaver (pure): which entitlements the termination type carries, the last month's salary pro rata, and the
/// recoveries the pay can bear - loans beyond that stay owed.
/// </summary>
public static class FinalPay
{
    /// <summary>Which entitlements a termination type carries (leave pay always does).</summary>
    /// <param name="type">The type.</param>
    /// <returns>Notice, gratuity and severance.</returns>
    public static (bool Notice, bool Gratuity, bool Severance) Entitlements(TerminationType type) => type switch
    {
        TerminationType.Retrenchment => (true, true, true),
        TerminationType.Retirement or TerminationType.Death or TerminationType.EndOfContract => (false, true, false),
        _ => (false, false, false),
    };

    /// <summary>The salary for the days of the last month up to the last day.</summary>
    /// <param name="monthlyBasic">Monthly basic.</param>
    /// <param name="lastDay">The last day of service.</param>
    /// <returns>The pro-rata salary.</returns>
    public static decimal ProRata(decimal monthlyBasic, DateOnly lastDay)
        => Math.Round(monthlyBasic * lastDay.Day / DateTime.DaysInMonth(lastDay.Year, lastDay.Month), 2, MidpointRounding.AwayFromZero);

    /// <summary>Works out the final pay.</summary>
    /// <param name="type">Why employment ended.</param>
    /// <param name="monthlyBasic">Monthly basic.</param>
    /// <param name="engaged">Engagement date.</param>
    /// <param name="lastDay">Last day of service.</param>
    /// <param name="lastMonthPaid">Whether the last month's salary was already paid in a run.</param>
    /// <param name="leaveDays">Leave days owed.</param>
    /// <param name="loans">Outstanding loans.</param>
    /// <param name="policy">The company's terminal policy.</param>
    /// <param name="ordinaryTax">PAYE for an amount of ordinary pay (the tax table in force).</param>
    /// <param name="annualBonus">The year's bonus, paid for the part of the calendar year served (nil: none).</param>
    /// <param name="otherDeductions">Unreturned company property and overpayments to recover after the loans.</param>
    /// <returns>The statement.</returns>
    public static FinalPayStatement Calculate(TerminationType type, decimal monthlyBasic, DateOnly engaged, DateOnly lastDay, bool lastMonthPaid, decimal leaveDays, decimal loans,
        TerminalPolicy policy, Func<decimal, decimal> ordinaryTax, decimal annualBonus = 0m, decimal otherDeductions = 0m)
    {
        (bool notice, bool gratuity, bool severance) = Entitlements(type);
        FinalSettlement settled = TerminalBenefits.Settle(monthlyBasic, engaged, lastDay, leaveDays, severance, 0m, policy);
        decimal proRata = lastMonthPaid ? 0m : ProRata(monthlyBasic, lastDay);
        decimal noticePay = notice ? settled.Notice : 0m, gratuityPay = gratuity ? settled.Gratuity : 0m, severancePay = severance ? settled.Severance : 0m;
        decimal bonus = ProRataBonus(annualBonus, engaged, lastDay);
        decimal ordinary = proRata + noticePay + settled.Leave + bonus;
        decimal taxOnOrdinary = Math.Round(Math.Max(0m, ordinaryTax(ordinary)), 2, MidpointRounding.AwayFromZero);
        decimal taxOnTerminal = Math.Round((gratuityPay + severancePay) * policy.TerminalTaxRatePercent / 100m, 2, MidpointRounding.AwayFromZero);
        decimal available = Math.Max(0m, ordinary + gratuityPay + severancePay - taxOnOrdinary - taxOnTerminal);
        decimal recovered = Math.Min(Math.Max(0m, loans), available);
        decimal other = Math.Min(Math.Max(0m, otherDeductions), available - recovered);
        return new FinalPayStatement(settled.Years, proRata, noticePay, Math.Max(0m, leaveDays), settled.Leave, gratuityPay, severancePay, taxOnOrdinary, taxOnTerminal, recovered,
            Math.Max(0m, loans) - recovered, bonus, other, Math.Max(0m, otherDeductions) - other);
    }

    /// <summary>The year's bonus for the days of the calendar year served up to the last day (from the engagement date when engaged that year).</summary>
    /// <param name="annualBonus">The full year's bonus.</param>
    /// <param name="engaged">The engagement date.</param>
    /// <param name="lastDay">The last day of service.</param>
    /// <returns>The pro-rata bonus.</returns>
    public static decimal ProRataBonus(decimal annualBonus, DateOnly engaged, DateOnly lastDay)
    {
        if (annualBonus <= 0m)
        {
            return 0m;
        }

        DateOnly start = engaged.Year == lastDay.Year ? engaged : new DateOnly(lastDay.Year, 1, 1);
        int served = Math.Max(0, lastDay.DayNumber - start.DayNumber + 1);
        int yearDays = DateTime.IsLeapYear(lastDay.Year) ? 366 : 365;
        return Math.Round(annualBonus * served / yearDays, 2, MidpointRounding.AwayFromZero);
    }
}
