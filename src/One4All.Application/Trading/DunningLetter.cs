using System.Globalization;
using System.Text;

namespace One4All.Application.Trading;

/// <summary>One overdue invoice listed on a reminder letter.</summary>
/// <param name="DocumentNumber">The invoice number.</param>
/// <param name="DueDate">When it fell due.</param>
/// <param name="DaysOverdue">Days past due as at the letter date.</param>
/// <param name="Outstanding">The outstanding balance.</param>
public sealed record DunningReminderItem(string DocumentNumber, DateOnly DueDate, int DaysOverdue, decimal Outstanding);

/// <summary>
/// The data for a customer payment reminder at a given dunning level. Built from the customer's overdue
/// open items; the <see cref="DunningLetterFormatter"/> turns it into the letter text.
/// </summary>
public sealed class DunningReminder
{
    /// <summary>The issuing company's name.</summary>
    public string CompanyName { get; init; } = string.Empty;

    /// <summary>The customer's name.</summary>
    public string CustomerName { get; init; } = string.Empty;

    /// <summary>The letter date.</summary>
    public DateOnly AsAtDate { get; init; }

    /// <summary>The dunning level (1 first reminder, 2 second, 3 final notice).</summary>
    public int Level { get; init; }

    /// <summary>The level label.</summary>
    public string LevelLabel { get; init; } = string.Empty;

    /// <summary>The overdue invoices.</summary>
    public IReadOnlyList<DunningReminderItem> Items { get; init; } = [];

    /// <summary>The total overdue.</summary>
    public decimal TotalOverdue { get; init; }
}

/// <summary>
/// Renders a <see cref="DunningReminder"/> to fixed-width plain text, with wording that escalates by level
/// (friendly reminder → second reminder → final notice). Pure and unit-tested.
/// </summary>
public static class DunningLetterFormatter
{
    /// <summary>The default width in characters.</summary>
    public const int DefaultWidth = 60;

    /// <summary>Renders the reminder letter to plain text.</summary>
    /// <param name="reminder">The reminder data.</param>
    /// <param name="width">The character width (default 60).</param>
    /// <returns>The formatted letter.</returns>
    public static string ToPlainText(DunningReminder reminder, int width = DefaultWidth)
    {
        if (width < 40)
        {
            width = 40;
        }

        StringBuilder sb = new();
        string rule = new('-', width);

        AppendCentered(sb, reminder.CompanyName, width);
        AppendCentered(sb, reminder.LevelLabel.ToUpperInvariant(), width);
        sb.AppendLine(rule);
        sb.AppendLine($"To: {reminder.CustomerName}");
        sb.AppendLine($"Date: {reminder.AsAtDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}");
        sb.AppendLine();
        sb.AppendLine(MessageFor(reminder.Level));
        sb.AppendLine();
        sb.AppendLine(TwoColumn("Invoice            Due         Days", "Outstanding", width));
        sb.AppendLine(rule);
        foreach (DunningReminderItem item in reminder.Items)
        {
            string left = $"{Fit(item.DocumentNumber, 18)} {item.DueDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}  {item.DaysOverdue,4}";
            sb.AppendLine(TwoColumn(left, Money(item.Outstanding), width));
        }

        sb.AppendLine(rule);
        sb.AppendLine(TwoColumn("TOTAL OVERDUE", Money(reminder.TotalOverdue), width));
        sb.AppendLine(rule);
        sb.AppendLine("Please quote the invoice numbers when paying.");

        return sb.ToString();
    }

    private static string MessageFor(int level) => level switch
    {
        1 => "This is a friendly reminder that the invoices below are overdue. Please arrange payment at your earliest convenience.",
        2 => "Despite our previous reminder, the invoices below remain unpaid. Please settle them promptly to avoid further action.",
        3 => "FINAL NOTICE: the invoices below are seriously overdue. Unless payment is received within 7 days, the account will be referred for collection.",
        _ => "The invoices below are outstanding.",
    };

    private static string Fit(string value, int width) => value.Length > width ? value[..width] : value.PadRight(width);

    private static string Money(decimal value) => value.ToString("N2", CultureInfo.InvariantCulture);

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
