using One4All.Reporting.Model;

namespace One4All.Reporting;

/// <summary>
/// The shipped system report definitions built in code (brief §14), seeded into <c>rpt.report_definition</c>.
/// These are read-only; a customer copies one to a company/user version to customise it.
/// </summary>
public static class SystemReports
{
    /// <summary>The Trial Balance report code.</summary>
    public const string TrialBalanceCode = "GL-TB";

    /// <summary>Builds the Trial Balance report definition (brief §14 report 1).</summary>
    /// <returns>The definition.</returns>
    public static ReportDefinition TrialBalance()
    {
        ReportDefinition def = new()
        {
            Code = TrialBalanceCode,
            Name = "Trial Balance",
            Module = "GeneralLedger",
            Description = "Account balances as at a date; debits must equal credits.",
        };
        def.DataSources.Add(new ReportDataSource { Name = "Main", Kind = DataSourceKind.InMemory, Command = "TrialBalance" });
        def.Parameters.Add(new ReportParameter { Name = "AsAtDate", Type = ParameterType.Date, Prompt = "As at date" });

        def.Bands.Add(new ReportBand
        {
            Type = BandType.ReportHeader,
            Height = 46,
            Elements =
            [
                new ReportElement { Kind = ElementKind.Expression, Name = "company", Expression = "CompanyName", X = 0, Y = 0, Width = 400, Height = 16, Bold = true, FontSize = 13 },
                new ReportElement { Kind = ElementKind.Expression, Name = "title", Expression = "\"Trial Balance as at \" + Format(Param(\"AsAtDate\"), \"yyyy-MM-dd\")", X = 0, Y = 18, Width = 400, Height = 14, Bold = true },
                new ReportElement { Kind = ElementKind.Expression, Name = "curLbl", Expression = "\"Currency: \" + Param(\"Currency\")", X = 0, Y = 32, Width = 200, Height = 12, FontSize = 8 },
            ],
        });

        def.Bands.Add(new ReportBand
        {
            Type = BandType.PageHeader,
            Height = 18,
            Elements =
            [
                Label("hCode", "Account", 0, 2, 80, 12, bold: true),
                Label("hName", "Name", 90, 2, 210, 12, bold: true),
                Label("hDebit", "Debit", 300, 2, 90, 12, bold: true, align: ReportHAlign.Right),
                Label("hCredit", "Credit", 400, 2, 90, 12, bold: true, align: ReportHAlign.Right),
            ],
        });

        def.Bands.Add(new ReportBand
        {
            Type = BandType.Detail,
            Height = 14,
            Elements =
            [
                Field("dCode", "AccountCode", 0, 0, 80, 12),
                Field("dName", "AccountName", 90, 0, 210, 12),
                Field("dDebit", "Debit", 300, 0, 90, 12, format: "N2", align: ReportHAlign.Right),
                Field("dCredit", "Credit", 400, 0, 90, 12, format: "N2", align: ReportHAlign.Right),
            ],
        });

        def.Bands.Add(new ReportBand
        {
            Type = BandType.ReportFooter,
            Height = 20,
            Elements =
            [
                Label("tLabel", "Total", 90, 4, 100, 14, bold: true),
                Aggregate("tDebit", "Debit", 300, 4, 90, 14, bold: true, align: ReportHAlign.Right),
                Aggregate("tCredit", "Credit", 400, 4, 90, 14, bold: true, align: ReportHAlign.Right),
            ],
        });

        return def;
    }

    private static ReportElement Label(string name, string text, double x, double y, double w, double h, bool bold = false, double size = 9, ReportHAlign align = ReportHAlign.Left) =>
        new() { Kind = ElementKind.Label, Name = name, Text = text, X = x, Y = y, Width = w, Height = h, Bold = bold, FontSize = size, HAlign = align };

    private static ReportElement Field(string name, string field, double x, double y, double w, double h, string? format = null, ReportHAlign align = ReportHAlign.Left) =>
        new() { Kind = ElementKind.Field, Name = name, FieldName = field, X = x, Y = y, Width = w, Height = h, Format = format, HAlign = align };

    private static ReportElement Aggregate(string name, string field, double x, double y, double w, double h, bool bold = false, ReportHAlign align = ReportHAlign.Left) =>
        new() { Kind = ElementKind.Aggregate, Name = name, Aggregate = AggregateFunction.Sum, AggregateField = field, X = x, Y = y, Width = w, Height = h, Format = "N2", Bold = bold, HAlign = align };
}
