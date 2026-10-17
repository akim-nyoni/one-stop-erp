using System.Globalization;
using System.Text;

namespace One4All.Application.Payroll;

/// <summary>A single earning or deduction printed on a payslip.</summary>
/// <param name="Label">The pay-element name.</param>
/// <param name="Amount">The amount.</param>
public sealed record PayslipItem(string Label, decimal Amount);

/// <summary>
/// The data for an employee payslip, independent of rendering. Built from a computed pay run; the
/// <see cref="PayslipFormatter"/> turns it into fixed-width printable text.
/// </summary>
public sealed class PayslipDocument
{
    /// <summary>The employing company name.</summary>
    public string CompanyName { get; init; } = string.Empty;

    /// <summary>The employee's name.</summary>
    public string EmployeeName { get; init; } = string.Empty;

    /// <summary>The employee number.</summary>
    public string EmployeeNumber { get; init; } = string.Empty;

    /// <summary>The pay-period label.</summary>
    public string PeriodLabel { get; init; } = string.Empty;

    /// <summary>The pay date.</summary>
    public DateOnly PayDate { get; init; }

    /// <summary>The earnings.</summary>
    public IReadOnlyList<PayslipItem> Earnings { get; init; } = [];

    /// <summary>The deductions.</summary>
    public IReadOnlyList<PayslipItem> Deductions { get; init; } = [];

    /// <summary>The gross earnings.</summary>
    public decimal GrossEarnings { get; init; }

    /// <summary>The total deductions.</summary>
    public decimal TotalDeductions { get; init; }

    /// <summary>The net pay.</summary>
    public decimal NetPay { get; init; }

    /// <summary>What the employer contributes on top (pension, medical aid, levies) - shown, not deducted.</summary>
    public IReadOnlyList<PayslipItem> EmployerContributions { get; init; } = [];

    /// <summary>Taxable benefits and other memo items - taxed, not paid.</summary>
    public IReadOnlyList<PayslipItem> MemoItems { get; init; } = [];

    /// <summary>How the net pay is paid (e.g. "Bank transfer to …4321"), or null.</summary>
    public string? PaidBy { get; init; }

    /// <summary>The period's taxable pay, or null when not shown.</summary>
    public decimal? TaxablePay { get; init; }

    /// <summary>The period's tax (the tax-table deductions), or null when not shown.</summary>
    public decimal? TaxDeducted { get; init; }

    /// <summary>The tax year to date: gross, taxable, tax and net (null when not shown).</summary>
    public (decimal Gross, decimal Taxable, decimal Tax, decimal Net)? YearToDate { get; init; }

    /// <summary>The leave balances (type, days).</summary>
    public IReadOnlyList<(string LeaveType, decimal Days)> LeaveBalances { get; init; } = [];

    /// <summary>The loan balances outstanding after this pay (reference, balance).</summary>
    public IReadOnlyList<(string Reference, decimal Balance)> LoanBalances { get; init; } = [];

    /// <summary>
    /// How the net pay goes out when some of it is paid in another currency (brief §4.15.7): each currency's amount, with the base-currency value
    /// and the rate used for a foreign one (rate 1 for the base currency). Empty when all of it is in the base currency.
    /// </summary>
    public IReadOnlyList<(string Currency, decimal Amount, decimal BaseAmount, decimal Rate)> NetPayByCurrency { get; init; } = [];
}

/// <summary>
/// Renders a <see cref="PayslipDocument"/> to fixed-width plain text (for a printer or a monospace preview).
/// Pure and dependency-free, so the layout is unit-tested; the payroll service builds the document from a
/// computed payslip.
/// </summary>
public static class PayslipFormatter
{
    /// <summary>The default payslip width in characters.</summary>
    public const int DefaultWidth = 44;

    /// <summary>Renders the payslip to plain text.</summary>
    /// <param name="doc">The payslip data.</param>
    /// <param name="width">The character width (default 44).</param>
    /// <returns>The formatted payslip.</returns>
    public static string ToPlainText(PayslipDocument doc, int width = DefaultWidth)
    {
        if (width < 28)
        {
            width = 28;
        }

        StringBuilder sb = new();
        string rule = new('-', width);

        AppendCentered(sb, doc.CompanyName, width);
        AppendCentered(sb, "PAYSLIP", width);
        sb.AppendLine(rule);

        sb.AppendLine(TwoColumn($"Employee: {doc.EmployeeName}", doc.EmployeeNumber, width));
        sb.AppendLine(TwoColumn($"Period: {doc.PeriodLabel}", doc.PayDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), width));
        sb.AppendLine(rule);

        sb.AppendLine("EARNINGS");
        foreach (PayslipItem item in doc.Earnings)
        {
            sb.AppendLine(TwoColumn(item.Label, Money(item.Amount), width));
        }

        sb.AppendLine(rule);
        sb.AppendLine("DEDUCTIONS");
        foreach (PayslipItem item in doc.Deductions)
        {
            sb.AppendLine(TwoColumn(item.Label, Money(item.Amount), width));
        }

        sb.AppendLine(rule);
        sb.AppendLine(TwoColumn("Gross earnings", Money(doc.GrossEarnings), width));
        sb.AppendLine(TwoColumn("Total deductions", Money(doc.TotalDeductions), width));
        sb.AppendLine(TwoColumn("NET PAY", Money(doc.NetPay), width));
        foreach ((string currency, decimal amount, decimal baseAmount, decimal rate) in doc.NetPayByCurrency)
        {
            sb.AppendLine(TwoColumn(rate == 1m ? $"  in {currency}" : $"  in {currency} at {rate.ToString("0.####", CultureInfo.InvariantCulture)} ({Money(baseAmount)})", Money(amount), width));
        }

        if (doc.PaidBy is string paidBy)
        {
            sb.AppendLine(paidBy.Length > width ? paidBy[..width] : paidBy);
        }

        sb.AppendLine(rule);

        // The further sections (brief §4.15.7 payslip content), each only when there is something to show.
        void Section(string title, IEnumerable<(string Label, string Value)> rows)
        {
            List<(string Label, string Value)> list = [.. rows];
            if (list.Count == 0)
            {
                return;
            }

            sb.AppendLine(title);
            foreach ((string label, string value) in list)
            {
                sb.AppendLine(TwoColumn(label, value, width));
            }

            sb.AppendLine(rule);
        }

        Section("EMPLOYER CONTRIBUTIONS", doc.EmployerContributions.Select(i => (i.Label, Money(i.Amount))));
        Section("TAXABLE BENEFITS (NOT PAID)", doc.MemoItems.Select(i => (i.Label, Money(i.Amount))));
        Section("TAX THIS PERIOD", doc.TaxablePay is decimal taxable ? [("Taxable pay", Money(taxable)), ("Tax deducted", Money(doc.TaxDeducted ?? 0m))] : []);
        Section("TAX YEAR TO DATE", doc.YearToDate is { } ytd
            ? [("Gross", Money(ytd.Gross)), ("Taxable", Money(ytd.Taxable)), ("Tax", Money(ytd.Tax)), ("Net", Money(ytd.Net))]
            : []);
        Section("LEAVE BALANCES (DAYS)", doc.LeaveBalances.Select(l => (l.LeaveType, l.Days.ToString("0.##", CultureInfo.InvariantCulture))));
        Section("LOAN BALANCES", doc.LoanBalances.Select(l => (l.Reference, Money(l.Balance))));

        return sb.ToString();
    }

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
