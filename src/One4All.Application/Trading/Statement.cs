using System.Globalization;
using System.Text;
using One4All.Application.Abstractions.Trading;

namespace One4All.Application.Trading;

/// <summary>A movement on a balance-forward statement.</summary>
/// <param name="Date">The document date.</param>
/// <param name="DocumentType">The kind of document.</param>
/// <param name="DocumentNumber">The document number.</param>
/// <param name="Amount">The amount (positive increases what the customer owes, or what we owe the supplier).</param>
/// <param name="Balance">The running balance after it.</param>
public sealed record BalanceForwardLine(DateOnly Date, Core.Trading.OpenItemType DocumentType, string DocumentNumber, decimal Amount, decimal Balance);

/// <summary>
/// A balance-forward statement (brief §4.5): the balance brought forward at the start of the period, the period's documents with a running
/// balance, and the balance carried forward; the ageing still comes from the open items.
/// </summary>
/// <param name="TraderCode">The trader code.</param>
/// <param name="TraderName">The trader name.</param>
/// <param name="Currency">The statement currency.</param>
/// <param name="From">The first day of the period.</param>
/// <param name="To">The last day of the period.</param>
/// <param name="Opening">The balance brought forward.</param>
/// <param name="Lines">The period's movements.</param>
/// <param name="Closing">The balance carried forward.</param>
/// <param name="Ageing">The ageing of what is outstanding at the period end.</param>
public sealed record BalanceForwardStatement(string TraderCode, string TraderName, string Currency, DateOnly From, DateOnly To, decimal Opening, IReadOnlyList<BalanceForwardLine> Lines,
    decimal Closing, StatementAgeing Ageing);

/// <summary>The outstanding balance of a statement split by days past due as at a date.</summary>
/// <param name="Current">Not yet due.</param>
/// <param name="Days1To30">1–30 days past due.</param>
/// <param name="Days31To60">31–60 days past due.</param>
/// <param name="Days61To90">61–90 days past due.</param>
/// <param name="Over90">More than 90 days past due.</param>
public sealed record StatementAgeing(decimal Current, decimal Days1To30, decimal Days31To60, decimal Days61To90, decimal Over90)
{
    /// <summary>The total outstanding.</summary>
    public decimal Total => Current + Days1To30 + Days31To60 + Days61To90 + Over90;

    /// <summary>Ages a statement's outstanding lines as at a date (credits reduce the bucket their due date falls in).</summary>
    /// <param name="statement">The statement.</param>
    /// <param name="asAt">The ageing date.</param>
    /// <returns>The ageing.</returns>
    public static StatementAgeing Of(TraderStatement statement, DateOnly asAt)
    {
        decimal current = 0m, d30 = 0m, d60 = 0m, d90 = 0m, over = 0m;
        foreach (StatementLineRow line in statement.Lines)
        {
            if (line.Outstanding == 0m)
            {
                continue;
            }

            int overdue = asAt.DayNumber - line.DueDate.DayNumber;
            if (overdue <= 0)
            {
                current += line.Outstanding;
            }
            else if (overdue <= 30)
            {
                d30 += line.Outstanding;
            }
            else if (overdue <= 60)
            {
                d60 += line.Outstanding;
            }
            else if (overdue <= 90)
            {
                d90 += line.Outstanding;
            }
            else
            {
                over += line.Outstanding;
            }
        }

        return new StatementAgeing(current, d30, d60, d90, over);
    }
}

/// <summary>
/// Renders the existing <see cref="TraderStatement"/> report data (a customer's or supplier's open items)
/// to fixed-width plain text for printing/emailing, with an ageing footer. Pure and unit-tested; the trading
/// reports supply the data.
/// </summary>
public static class StatementFormatter
{
    /// <summary>The default width in characters.</summary>
    public const int DefaultWidth = 64;

    /// <summary>Renders the statement to plain text under a company heading.</summary>
    /// <param name="statement">The statement data (from <c>ITradingReports.CustomerStatementAsync</c> / <c>SupplierStatementAsync</c>).</param>
    /// <param name="companyName">The issuing company's name (heading).</param>
    /// <param name="asAt">The statement date (also the ageing date).</param>
    /// <param name="width">The character width (default 64).</param>
    /// <returns>The formatted statement.</returns>
    /// <param name="address">The trader's correspondence address lines.</param>
    /// <param name="remittanceSlip">Whether to end with a tear-off remittance slip for the customer to return with the payment.</param>
    public static string ToPlainText(TraderStatement statement, string companyName, DateOnly asAt, int width = DefaultWidth, IReadOnlyList<string>? address = null, bool remittanceSlip = false)
    {
        if (width < 40)
        {
            width = 40;
        }

        StringBuilder sb = new();
        string rule = new('-', width);

        AppendCentered(sb, companyName, width);
        AppendCentered(sb, "STATEMENT OF ACCOUNT", width);
        sb.AppendLine(rule);
        sb.AppendLine(TwoColumn($"To: {statement.TraderName} ({statement.TraderCode})", string.Empty, width));

        foreach (string addressLine in address ?? [])
        {
            sb.AppendLine(TwoColumn("    " + addressLine, string.Empty, width));
        }
        sb.AppendLine(TwoColumn($"Statement date: {asAt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}", $"Currency: {statement.Currency}", width));
        sb.AppendLine(rule);
        sb.AppendLine(ThreeColumn("Date       Document", "Amount", "Outstanding", width));
        foreach (StatementLineRow line in statement.Lines)
        {
            string left = $"{line.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)} {Abbrev(line.DocumentType)} {line.DocumentNumber}";
            sb.AppendLine(ThreeColumn(left, Money(line.Amount), Money(line.Outstanding), width));
        }

        sb.AppendLine(rule);
        sb.AppendLine(TwoColumn($"BALANCE DUE ({statement.Currency})", Money(statement.Balance), width));
        sb.AppendLine(rule);
        AppendFooter(sb, statement.TraderCode, statement.TraderName, statement.Currency, statement.Balance, StatementAgeing.Of(statement, asAt), companyName, asAt, width, remittanceSlip);
        return sb.ToString();
    }

    /// <summary>Renders a balance-forward statement: brought forward, the period's documents with a running balance, carried forward and the ageing.</summary>
    /// <param name="statement">The statement data.</param>
    /// <param name="companyName">The issuing company's name (heading).</param>
    /// <param name="width">The character width (default 64).</param>
    /// <param name="address">The trader's correspondence address lines.</param>
    /// <param name="remittanceSlip">Whether to end with a tear-off remittance slip.</param>
    /// <returns>The formatted statement.</returns>
    public static string ToBalanceForwardText(BalanceForwardStatement statement, string companyName, int width = DefaultWidth, IReadOnlyList<string>? address = null, bool remittanceSlip = false)
    {
        width = Math.Max(width, 40);
        StringBuilder sb = new();
        string rule = new('-', width);

        AppendCentered(sb, companyName, width);
        AppendCentered(sb, "STATEMENT OF ACCOUNT", width);
        sb.AppendLine(rule);
        sb.AppendLine(TwoColumn($"To: {statement.TraderName} ({statement.TraderCode})", string.Empty, width));
        foreach (string addressLine in address ?? [])
        {
            sb.AppendLine(TwoColumn("    " + addressLine, string.Empty, width));
        }

        sb.AppendLine(TwoColumn($"Period: {Iso(statement.From)} to {Iso(statement.To)}", $"Currency: {statement.Currency}", width));
        sb.AppendLine(rule);
        sb.AppendLine(ThreeColumn("Date       Document", "Amount", "Balance", width));
        sb.AppendLine(ThreeColumn($"{Iso(statement.From)} Balance brought forward", string.Empty, Money(statement.Opening), width));
        foreach (BalanceForwardLine line in statement.Lines)
        {
            sb.AppendLine(ThreeColumn($"{Iso(line.Date)} {Abbrev(line.DocumentType)} {line.DocumentNumber}", Money(line.Amount), Money(line.Balance), width));
        }

        sb.AppendLine(rule);
        sb.AppendLine(TwoColumn($"BALANCE CARRIED FORWARD ({statement.Currency})", Money(statement.Closing), width));
        sb.AppendLine(rule);
        AppendFooter(sb, statement.TraderCode, statement.TraderName, statement.Currency, statement.Closing, statement.Ageing, companyName, statement.To, width, remittanceSlip);
        return sb.ToString();
    }

    private static string Iso(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    // The ageing and (for customers) the tear-off remittance slip, shared by both statement styles.
    private static void AppendFooter(StringBuilder sb, string traderCode, string traderName, string currency, decimal balance, StatementAgeing ageing, string companyName, DateOnly asAt, int width,
        bool remittanceSlip)
    {
        string rule = new('-', width);
        sb.AppendLine(TwoColumn("Current", Money(ageing.Current), width));
        sb.AppendLine(TwoColumn("1-30 days", Money(ageing.Days1To30), width));
        sb.AppendLine(TwoColumn("31-60 days", Money(ageing.Days31To60), width));
        sb.AppendLine(TwoColumn("61-90 days", Money(ageing.Days61To90), width));
        sb.AppendLine(TwoColumn("Over 90 days", Money(ageing.Over90), width));
        sb.AppendLine(rule);
        if (remittanceSlip)
        {
            // The tear-off slip (brief §4.5 statements with remittance slip): what the customer sends back with the payment.
            sb.AppendLine();
            sb.AppendLine(Fit("- - - cut here - - - " + new string('-', width), width));
            AppendCentered(sb, "REMITTANCE SLIP", width);
            sb.AppendLine(Fit($"Return with your payment to {companyName}", width));
            sb.AppendLine(TwoColumn($"Account: {traderCode}", $"Statement: {Iso(asAt)}", width));
            sb.AppendLine(Fit(traderName, width));
            sb.AppendLine(TwoColumn($"Balance due ({currency})", Money(balance), width));
            sb.AppendLine(TwoColumn("Overdue now", Money(ageing.Total - ageing.Current), width));
            sb.AppendLine(TwoColumn("Amount paid", "______________", width));
            sb.AppendLine(TwoColumn("Date paid", "______________", width));
            sb.AppendLine(Fit($"Quote reference {traderCode} on your payment.", width));
            sb.AppendLine(rule);
        }
    }

    private static string Fit(string text, int width) => text.Length > width ? text[..width] : text;

    private static string Abbrev(Core.Trading.OpenItemType type) => type switch
    {
        Core.Trading.OpenItemType.Invoice => "INV",
        Core.Trading.OpenItemType.CreditNote => "CRN",
        Core.Trading.OpenItemType.DebitNote => "DBN",
        Core.Trading.OpenItemType.Receipt => "RCT",
        Core.Trading.OpenItemType.Payment => "PAY",
        Core.Trading.OpenItemType.Journal => "JNL",
        Core.Trading.OpenItemType.WriteOff => "W/O",
        Core.Trading.OpenItemType.Interest => "INT",
        _ => "OTH",
    };

    private static string Money(decimal value) => value.ToString("N2", CultureInfo.InvariantCulture);

    private static string ThreeColumn(string left, string middle, string right, int width)
    {
        const int MoneyWidth = 14;
        string tail = middle.PadLeft(MoneyWidth) + right.PadLeft(MoneyWidth);
        int max = width - tail.Length - 1;
        if (max < 1)
        {
            return tail.PadLeft(width);
        }

        if (left.Length > max)
        {
            left = left[..max];
        }

        return left + tail.PadLeft(width - left.Length);
    }

    private static string TwoColumn(string left, string right, int width)
    {
        int max = width - right.Length - 1;
        if (max < 1)
        {
            return right.PadLeft(width);
        }

        if (left.Length > max)
        {
            left = left[..max];
        }

        return left + right.PadLeft(width - left.Length);
    }

    private static void AppendCentered(StringBuilder sb, string text, int width)
    {
        if (text.Length >= width)
        {
            sb.AppendLine(text[..width]);
            return;
        }

        sb.AppendLine(new string(' ', (width - text.Length) / 2) + text);
    }
}
