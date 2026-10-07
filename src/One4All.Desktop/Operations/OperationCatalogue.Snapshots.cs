using System.Globalization;
using One4All.Application.Abstractions.Reporting;
using static One4All.Desktop.Operations.Ops;

namespace One4All.Desktop.Operations;

/// <content>Reporting snapshots and the quick search.</content>
public static partial class OperationCatalogue
{
    private static IEnumerable<Operation> Snapshots()
    {
        const string module = "System Manager";
        yield return new Operation
        {
            Module = module, Title = "Refresh reporting snapshots", ListTitle = "Snapshot refreshes",
            Description = "Re-takes the aged debt, stock valuation and sales-by-day snapshots now (normally the scheduled task reporting.refresh-snapshots does this); reports keep reading the previous snapshot meanwhile.",
            Fields = [],
            Run = async (c, _) => string.Join(Environment.NewLine, (await c.Get<IReportingSnapshotService>().RefreshAsync().ConfigureAwait(true))
                .Select(r => $"{r.Name}: {r.Rows} row(s) in {r.DurationMs} ms")),
            List = async c => Table(await c.Get<IReportingSnapshotService>().LastRefreshesAsync().ConfigureAwait(true), ("Snapshot", r => r.Name),
                ("Refreshed", r => r.RefreshedOnUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)), ("Took (ms)", r => r.DurationMs), ("Rows", r => r.Rows)),
        };
        yield return new Operation
        {
            Module = module, Title = "Language and terminology", ListTitle = "The company's terms",
            Description = "The company's locale (screen language where translated, and the date and number formats of its reports — e.g. en-ZW, pt-MZ) and its own words for standard terms "
                + "(Stand → Erf) on screens and report labels. Sign in again to see the screens change.",
            Fields = [Opt("Locale", "Locale (blank: keep; '-' for English / invariant)"), Opt("Term", "Standard word"), Opt("Replacement", "The company's word (blank removes it)")],
            Run = async (c, i) =>
            {
                One4All.Application.Abstractions.System.ILocalizationService localization = c.Get<One4All.Application.Abstractions.System.ILocalizationService>();
                List<string> done = [];
                if (i.Optional("Locale") is string locale)
                {
                    _ = Check(await localization.SetLocaleAsync(c.CompanyId, locale == "-" ? null : locale).ConfigureAwait(true), string.Empty);
                    done.Add(locale == "-" ? "Locale cleared." : $"Locale set to {locale}.");
                }

                if (i.Optional("Term") is string term)
                {
                    _ = Check(await localization.SetTermAsync(c.CompanyId, term, i.Optional("Replacement")).ConfigureAwait(true), string.Empty);
                    done.Add(i.Optional("Replacement") is string word ? $"'{term}' now reads '{word}'." : $"'{term}' is back to standard.");
                }

                IReadOnlyList<string> cultures = One4All.Application.Localization.TextCatalog.Cultures;
                return string.Join(" ", done.DefaultIfEmpty("Nothing changed.")) + $" Screen translations shipped: en, {string.Join(", ", cultures)}.";
            },
            List = async c => Table((await c.Get<One4All.Application.Abstractions.System.ILocalizationService>().TermsAsync(c.CompanyId).ConfigureAwait(true)).ToList(),
                ("Standard word", t => t.Key), ("Company's word", t => t.Value)),
        };
        yield return new Operation
        {
            Module = module, Title = "Payroll encryption", ListTitle = "Values not yet under the active key",
            Description = "Salary, bank details, national ID and tax numbers are encrypted at rest. This shows what is still in plaintext (written before encryption) or under a retired key, "
                + "and Encrypt now rewrites it with the active key (the scheduled task security.reencrypt-fields does the same).",
            Fields = [Tick("Encrypt", "Encrypt now")],
            Run = async (c, i) =>
            {
                One4All.Infrastructure.Security.IFieldEncryptionService encryption = c.Get<One4All.Infrastructure.Security.IFieldEncryptionService>();
                int rewritten = i.Flag("Encrypt") ? await encryption.ReencryptAsync().ConfigureAwait(true) : 0;
                One4All.Infrastructure.Security.FieldEncryptionReport report = await encryption.StatusAsync().ConfigureAwait(true);
                return $"Active key {report.ActiveKeyId}{(report.DevelopmentKey ? " (DEVELOPMENT key file — configure FieldEncryption keys before going live)" : string.Empty)}. "
                    + (i.Flag("Encrypt") ? $"{rewritten} row(s) rewritten. " : string.Empty) + (report.Complete ? "Everything is encrypted with the active key." : $"{report.Pending.Values.Sum()} value(s) still to encrypt.");
            },
            List = async c => Table((await c.Get<One4All.Infrastructure.Security.IFieldEncryptionService>().StatusAsync().ConfigureAwait(true)).Pending.ToList(),
                ("Column", p => p.Key), ("Values to encrypt", p => p.Value)),
        };
        yield return new Operation
        {
            Module = module, Title = "Quick search", ListTitle = "Aged debt (snapshot)",
            Description = "Finds items, customers, suppliers, leads and accounts by name or code, forgiving a mistyped letter or two.",
            Fields = [Req("Term", "Search for")],
            Run = async (c, i) =>
            {
                IReadOnlyList<QuickSearchHit> hits = await c.Get<IReportingSnapshotService>().SearchAsync(c.CompanyId, i.Text("Term"), 40).ConfigureAwait(true);
                return hits.Count == 0 ? "Nothing found." : string.Join(Environment.NewLine, hits.Select(h => $"{h.Kind,-9} {h.Code,-14} {h.Name}  ({h.Score:0.00})"));
            },
            List = async c =>
            {
                Snapshot<AgedDebtSnapshotRow> aged = await c.Get<IReportingSnapshotService>().AgedDebtAsync(c.CompanyId).ConfigureAwait(true);
                return Table(aged.Rows, ("Customer", r => r.CustomerCode), ("Name", r => r.CustomerName), ("Current", r => M(r.Current)), ("1–30", r => M(r.Days1To30)),
                    ("31–60", r => M(r.Days31To60)), ("61–90", r => M(r.Days61To90)), ("90+", r => M(r.Over90)), ("Total", r => M(r.Total)),
                    ("As at", r => $"{r.AsAt:yyyy-MM-dd} (refreshed {aged.RefreshedOnUtc?.ToLocalTime():yyyy-MM-dd HH:mm})"));
            },
        };
        yield return new Operation
        {
            Module = module, Title = "Stock valuation and sales snapshots", ListTitle = "Stock valuation (snapshot)",
            Description = "Revenue by day and department between two dates from the sales snapshot (the list shows stock value by warehouse and category).",
            Fields = [Today("From", "From"), Today("To", "To")],
            Run = async (c, i) =>
            {
                Snapshot<SalesByDaySnapshotRow> sales = await c.Get<IReportingSnapshotService>().SalesByDayAsync(c.CompanyId, i.Date("From"), i.Date("To")).ConfigureAwait(true);
                return string.Join(Environment.NewLine, sales.Rows.Select(r => $"{r.Date:yyyy-MM-dd} {(r.Department.Length == 0 ? "-" : r.Department),-8} {M(r.Revenue)}")
                    .Prepend($"Sales snapshot refreshed {sales.RefreshedOnUtc?.ToLocalTime():yyyy-MM-dd HH:mm}; total {M(sales.Rows.Sum(r => r.Revenue))}"));
            },
            List = async c =>
            {
                Snapshot<StockValuationSnapshotRow> stock = await c.Get<IReportingSnapshotService>().StockValuationAsync(c.CompanyId).ConfigureAwait(true);
                return Table(stock.Rows, ("Warehouse", r => r.WarehouseCode), ("Category", r => r.Category), ("Items", r => r.Items), ("Quantity", r => Q(r.Quantity)), ("Value", r => M(r.Value)));
            },
        };
    }
}
