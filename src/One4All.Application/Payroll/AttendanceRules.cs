namespace One4All.Application.Payroll;

/// <summary>Attendance arithmetic: hours worked, overtime cost and the sick-pay tier.</summary>
public static class AttendanceRules
{
    /// <summary>Hours between clock-in and clock-out (an overnight shift wraps past midnight).</summary>
    /// <param name="clockIn">Clock-in.</param>
    /// <param name="clockOut">Clock-out.</param>
    /// <returns>The hours, or null when either is missing.</returns>
    public static decimal? HoursWorked(TimeOnly? clockIn, TimeOnly? clockOut)
    {
        if (clockIn is not TimeOnly start || clockOut is not TimeOnly end)
        {
            return null;
        }

        double minutes = (end - start).TotalMinutes; // TimeOnly subtraction wraps around midnight
        return Math.Round((decimal)minutes / 60m, 2, MidpointRounding.AwayFromZero);
    }

    /// <summary>The cost of overtime: hours × the hourly rate of basic × the multiplier.</summary>
    /// <param name="hours">The overtime hours.</param>
    /// <param name="monthlyBasic">The monthly basic salary.</param>
    /// <param name="hoursPerMonth">The standard hours in a month.</param>
    /// <param name="multiplier">The overtime multiplier.</param>
    /// <returns>The cost, to the cent.</returns>
    public static decimal OvertimeCost(decimal hours, decimal monthlyBasic, decimal hoursPerMonth, decimal multiplier)
        => hoursPerMonth <= 0m ? 0m : Math.Round(hours * monthlyBasic / hoursPerMonth * multiplier, 2, MidpointRounding.AwayFromZero);

    /// <summary>Where an employee stands on sick pay after a number of sick days in the last 12 months.</summary>
    /// <param name="sickDays">Sick days in the last 12 months.</param>
    /// <param name="fullPayDays">Days paid in full.</param>
    /// <param name="halfPayDays">Further days paid at half.</param>
    /// <returns>Full pay, Half pay or Exhausted.</returns>
    public static string SickTier(decimal sickDays, decimal fullPayDays, decimal halfPayDays)
        => sickDays <= fullPayDays ? "Full pay" : sickDays <= fullPayDays + halfPayDays ? "Half pay" : "Exhausted";
}
