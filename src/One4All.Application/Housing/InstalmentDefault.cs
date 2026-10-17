using One4All.Core.Housing;

namespace One4All.Application.Housing;

/// <summary>
/// Decides the next default notice on an instalment agreement (brief §4.10.5): the stage the arrears' age calls for - reminder from the first unpaid instalment,
/// demand after 30 days, breach after 60, cancellation after 90 - never skipping a stage, each after the previous one has been served, and the cancellation
/// notice only once the breach notice has stood for 30 days. Pure.
/// </summary>
public static class InstalmentDefault
{
    /// <summary>Days overdue at which each stage falls due.</summary>
    public static readonly IReadOnlyDictionary<DefaultStage, int> StageDays = new Dictionary<DefaultStage, int>
    {
        [DefaultStage.Reminder] = 1, [DefaultStage.Demand] = 30, [DefaultStage.Breach] = 60, [DefaultStage.Cancellation] = 90,
    };

    /// <summary>Days a served breach notice must stand before the cancellation notice.</summary>
    public const int BreachNoticeDays = 30;

    /// <summary>How long the oldest unpaid instalment has been due: the raised instalments, newest first, absorb the arrears.</summary>
    /// <param name="arrears">The arrears.</param>
    /// <param name="raised">The raised instalments (due date, amount).</param>
    /// <param name="asAt">The date.</param>
    /// <returns>The days overdue (0 when nothing is in arrears).</returns>
    public static int DaysOverdue(decimal arrears, IEnumerable<(DateOnly Due, decimal Amount)> raised, DateOnly asAt)
    {
        if (arrears <= 0m)
        {
            return 0;
        }

        decimal left = arrears;
        DateOnly? oldest = null;
        foreach ((DateOnly due, decimal amount) in raised.OrderByDescending(r => r.Due))
        {
            oldest = due;
            left -= amount;
            if (left <= 0m)
            {
                break;
            }
        }

        return oldest is DateOnly d ? Math.Max(0, asAt.DayNumber - d.DayNumber) : 0;
    }

    /// <summary>The next notice due, or null with the reason none is.</summary>
    /// <param name="daysOverdue">How long the oldest unpaid instalment has been due.</param>
    /// <param name="last">The last notice (stage, issued, served), or null.</param>
    /// <param name="asAt">The date.</param>
    /// <returns>The stage to issue, or null and why not.</returns>
    public static (DefaultStage? Next, string? Why) Next(int daysOverdue, (DefaultStage Stage, DateOnly IssuedOn, DateOnly? ServedOn)? last, DateOnly asAt)
    {
        if (daysOverdue < StageDays[DefaultStage.Reminder])
        {
            return (null, "Nothing is overdue.");
        }

        if (last is { } previous && previous.ServedOn is null)
        {
            return (null, $"The {previous.Stage} notice of {previous.IssuedOn:yyyy-MM-dd} has not been served yet.");
        }

        DefaultStage next = last is { } done ? done.Stage + 1 : DefaultStage.Reminder;
        if (next > DefaultStage.Cancellation)
        {
            return (null, "The cancellation notice has been served; cancel the agreement.");
        }

        if (daysOverdue < StageDays[next])
        {
            return (null, $"The {next} notice is due after {StageDays[next]} days overdue ({daysOverdue} now).");
        }

        if (next == DefaultStage.Cancellation && last is { ServedOn: DateOnly served } && asAt.DayNumber - served.DayNumber < BreachNoticeDays)
        {
            return (null, $"The breach notice served on {served:yyyy-MM-dd} must stand {BreachNoticeDays} days first.");
        }

        return (next, null);
    }

    /// <summary>The penalty interest for a month on arrears at an annual rate.</summary>
    /// <param name="arrears">The arrears.</param>
    /// <param name="annualRatePercent">The annual rate (%).</param>
    /// <returns>The penalty, rounded to cents (0 for none).</returns>
    public static decimal MonthlyPenalty(decimal arrears, decimal annualRatePercent)
        => arrears <= 0m || annualRatePercent <= 0m ? 0m : decimal.Round(arrears * annualRatePercent / 100m / 12m, 2, MidpointRounding.AwayFromZero);

    /// <summary>The notice text.</summary>
    /// <param name="stage">The stage.</param>
    /// <param name="noticeNumber">The notice number.</param>
    /// <param name="agreementNumber">The agreement.</param>
    /// <param name="buyer">The buyer.</param>
    /// <param name="stand">The stand.</param>
    /// <param name="arrears">The arrears.</param>
    /// <param name="daysOverdue">How long overdue.</param>
    /// <param name="issuedOn">The date.</param>
    /// <returns>The text.</returns>
    public static string Text(DefaultStage stage, string noticeNumber, string agreementNumber, string buyer, string stand, decimal arrears, int daysOverdue, DateOnly issuedOn)
    {
        string title = stage switch
        {
            DefaultStage.Reminder => "REMINDER: INSTALMENT ARREARS",
            DefaultStage.Demand => "LETTER OF DEMAND",
            DefaultStage.Breach => "NOTICE OF BREACH",
            _ => "NOTICE OF CANCELLATION",
        };
        string body = stage switch
        {
            DefaultStage.Reminder => "Our records show instalments unpaid. Please pay the arrears below, or contact the housing office to arrange payment.",
            DefaultStage.Demand => "Despite our reminder the arrears below remain unpaid. We demand payment in full within 14 days of this letter.",
            DefaultStage.Breach => "You are in breach of the agreement through failure to pay. Unless the arrears below are paid within 30 days, the council may cancel the agreement.",
            _ => "The breach has not been remedied. The council gives notice that the agreement is cancelled; the stand reverts to the council and any refund due will be computed under the agreement.",
        };
        return $"{title}\nNotice {noticeNumber}    Date {issuedOn:yyyy-MM-dd}\n\nTo: {buyer}\nAgreement {agreementNumber}, stand {stand}\n\n{body}\n\n"
            + $"Arrears: {arrears:N2} (oldest instalment {daysOverdue} days overdue)\n";
    }
}
