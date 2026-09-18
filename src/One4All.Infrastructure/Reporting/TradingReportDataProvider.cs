using One4All.Application.Abstractions.Reporting;
using One4All.Application.Abstractions.Trading;

namespace One4All.Infrastructure.Reporting;

/// <summary>
/// Supplies live rows for the trader-ledger reports (aged debtors/creditors and the open-item customer
/// statement) from <see cref="ITradingReports"/>.
/// </summary>
public sealed class TradingReportDataProvider : IReportDataProvider
{
    private static readonly HashSet<string> Commands = new(StringComparer.OrdinalIgnoreCase)
    {
        "AgedDebtors", "AgedCreditors", "CustomerStatement",
    };

    private readonly ITradingReports _reports;

    /// <summary>Creates the provider.</summary>
    /// <param name="reports">The trading report services.</param>
    public TradingReportDataProvider(ITradingReports reports) => _reports = reports;

    /// <inheritdoc />
    public bool CanHandle(string command) => Commands.Contains(command);

    /// <inheritdoc />
    public async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> GetRowsAsync(long companyId, string command, IReadOnlyDictionary<string, object?> parameters, CancellationToken ct = default)
    {
        DateOnly asAt = ReportParameters.Date(parameters, "AsAtDate");
        return command.ToUpperInvariant() switch
        {
            "AGEDDEBTORS" => Ageing(await _reports.AgedDebtorsAsync(companyId, asAt, ct).ConfigureAwait(false)),
            "AGEDCREDITORS" => Ageing(await _reports.AgedCreditorsAsync(companyId, asAt, ct).ConfigureAwait(false)),
            "CUSTOMERSTATEMENT" => await CustomerStatementAsync(companyId, parameters, ct).ConfigureAwait(false),
            _ => [],
        };
    }

    private static IReadOnlyList<IReadOnlyDictionary<string, object?>> Ageing(AgeingReport report) =>
        report.Rows.Select(r => (IReadOnlyDictionary<string, object?>)new Dictionary<string, object?>
        {
            ["TraderCode"] = r.TraderCode,
            ["TraderName"] = r.TraderName,
            ["Current"] = r.Current,
            ["Days1To30"] = r.Days1To30,
            ["Days31To60"] = r.Days31To60,
            ["Days61To90"] = r.Days61To90,
            ["Days91Plus"] = r.Days91Plus,
            ["Total"] = r.Total,
        }).ToList();

    private async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> CustomerStatementAsync(long companyId, IReadOnlyDictionary<string, object?> parameters, CancellationToken ct)
    {
        long customerId = ReportParameters.Long(parameters, "CustomerId");
        TraderStatement? statement = customerId == 0 ? null : await _reports.CustomerStatementAsync(companyId, customerId, ct).ConfigureAwait(false);
        if (statement is null)
        {
            return [];
        }

        return statement.Lines.Select(l => (IReadOnlyDictionary<string, object?>)new Dictionary<string, object?>
        {
            ["Date"] = l.Date.ToDateTime(TimeOnly.MinValue),
            ["Type"] = l.DocumentType.ToString(),
            ["Number"] = l.DocumentNumber,
            ["Due"] = l.DueDate.ToDateTime(TimeOnly.MinValue),
            ["Amount"] = l.Amount,
            ["Outstanding"] = l.Outstanding,
            ["TraderCode"] = statement.TraderCode,
        }).ToList();
    }
}
