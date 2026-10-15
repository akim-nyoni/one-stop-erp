using System.Globalization;
using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Accounting;
using One4All.Application.Abstractions.Budgeting;
using One4All.Application.Abstractions.Inventory;
using One4All.Application.Abstractions.Procurement;
using One4All.Application.Abstractions.Trading;
using One4All.Core.Accounting;
using One4All.Core.System;
using One4All.Core.Trading;
using static One4All.Desktop.Operations.Ops;

namespace One4All.Desktop.Operations;

public static partial class OperationCatalogue
{
    // Services the brief asks for that had no way in from the desktop: period close, price lists, supplier returns, debit notes,
    // framework agreements, actual volumes, asset insurance and the customer's ageing basis and group.
    private static IEnumerable<Operation> Wiring()
    {
        yield return new Operation
        {
            Module = "General Ledger", Title = "Close or reopen a period", ListTitle = "Periods of the latest fiscal year",
            Description = "Sets one period's status for one posting module (GL, AR, AP, INV, POS…): Closed stops postings (a soft close can be reopened), PermanentlyClosed "
                + "can never take another posting (draft or deferred journals dated in it must be posted or deleted first), Open reopens it.",
            Fields = [Req("Year", "Fiscal year code"), Num("Period", "Period number", "1"), Req("Module", "Posting module", "GL"), Pick("Status", "Status", Enum.GetNames<PeriodStatus>())],
            Run = async (c, i) =>
            {
                string code = i.Text("Year").Trim();
                long year = await FindAsync(c.Db.FiscalYears, y => y.CompanyId == c.CompanyId && y.Code == code, y => y.Id, "fiscal year", code).ConfigureAwait(true);
                PeriodStatus status = Enum.Parse<PeriodStatus>(i.Text("Status"));
                return Check(await c.Get<IPeriodService>().SetStatusAsync(c.CompanyId, year, i.Whole("Period"), i.Text("Module").Trim().ToUpperInvariant(), status).ConfigureAwait(true),
                    $"Period {i.Whole("Period")} of {code} is {status} for {i.Text("Module").Trim().ToUpperInvariant()}.");
            },
            List = async c =>
            {
                FiscalYear? year = await c.Db.FiscalYears.AsNoTracking().Include(y => y.Periods).ThenInclude(p => p.ModuleStatuses).Where(y => y.CompanyId == c.CompanyId)
                    .OrderByDescending(y => y.StartDate).FirstOrDefaultAsync().ConfigureAwait(true);
                return Table(year?.Periods.OrderBy(p => p.PeriodNumber).ToList() ?? [],
                    ("Year", p => year!.Code), ("Period", p => p.PeriodNumber), ("From", p => D(p.StartDate)), ("To", p => D(p.EndDate)),
                    ("Module statuses", p => p.ModuleStatuses.Count == 0 ? "Open (all)" : string.Join(", ", p.ModuleStatuses.OrderBy(s => s.ModuleCode).Select(s => $"{s.ModuleCode} {s.Status}"))));
            },
        };
        yield return new Operation
        {
            Module = "Customers", Title = "Price lists", ListTitle = "Price lists",
            Description = "Create a price list in a currency (one may be the default), set an item's price on it (with a minimum quantity for a quantity break), "
                + "or put a customer on a list (blank list: back to the default).",
            Fields = [Pick("Step", "Step", ["Create", "Set price", "Assign customer"]), Opt("List", "Price list code"), Opt("Name", "Name (create)"), Opt("Currency", "Currency (create; blank: base)"),
                Tick("Default", "The default list (create)"), Opt("Item", "Item code (set price)"), OptNum("MinQty", "From quantity (set price)"), OptNum("Price", "Unit price (set price)"),
                Opt("Customer", "Customer code (assign)")],
            Run = async (c, i) =>
            {
                IPricingService pricing = c.Get<IPricingService>();
                string? code = i.Optional("List")?.Trim().ToUpperInvariant();
                async Task<long> ListAsync() => await FindAsync(c.Db.PriceLists, l => l.CompanyId == c.CompanyId && l.Code == code, l => l.Id, "price list", code ?? "?").ConfigureAwait(true);
                switch (i.Text("Step"))
                {
                    case "Create":
                        CurrencyCode currency = i.Optional("Currency") is string given ? CurrencyCode.Parse(given)
                            : await c.Db.Companies.Where(x => x.Id == c.CompanyId).Select(x => x.BaseCurrency).SingleAsync().ConfigureAwait(true);
                        _ = Check(await pricing.CreatePriceListAsync(c.CompanyId, code ?? throw new OperationException("Give the list's code."), i.Optional("Name") ?? code, currency, i.Flag("Default"))
                            .ConfigureAwait(true));
                        return $"Price list {code} created in {currency.Value}.";
                    case "Set price":
                        long item = await ItemAsync(c, i.Optional("Item") ?? throw new OperationException("Give the item.")).ConfigureAwait(true);
                        decimal price = i.OptionalNumber("Price") ?? throw new OperationException("Give the unit price.");
                        return Check(await pricing.SetPriceAsync(c.CompanyId, await ListAsync().ConfigureAwait(true), item, i.OptionalNumber("MinQty") ?? 0m, price).ConfigureAwait(true),
                            $"{i.Optional("Item")!.ToUpperInvariant()} on {code} at {M(price)}.");
                    default:
                        long customer = await CustomerAsync(c, i.Optional("Customer") ?? throw new OperationException("Give the customer.")).ConfigureAwait(true);
                        long? list = code is null ? null : await ListAsync().ConfigureAwait(true);
                        return Check(await pricing.AssignCustomerAsync(c.CompanyId, customer, list).ConfigureAwait(true),
                            code is null ? "The customer is back on the default price list." : $"The customer buys from {code}.");
                }
            },
            List = async c => Table(await c.Db.PriceLists.AsNoTracking().Where(l => l.CompanyId == c.CompanyId).OrderBy(l => l.Code).Page(c, 200).ToListAsync().ConfigureAwait(true),
                ("List", l => l.Code), ("Name", l => l.Name), ("Currency", l => l.Currency.Value), ("Default", l => l.IsDefault ? "yes" : string.Empty), ("Active", l => l.IsActive ? "yes" : "no")),
        };
        yield return new Operation
        {
            Module = "Customers", Title = "Customer debit note", ListTitle = "Open customer items",
            Description = "A further charge on an invoice already raised (under-billing, a price correction, a charge omitted): posted as a debit note that refers to the invoice.",
            Fields = [CustomerPick("Customer", "Customer"), Req("Number", "Debit note number"), Req("Invoice", "Invoice it adds to"), Today(), Num("Net", "Net amount"), Num("Tax", "Tax amount", "0"),
                Req("Account", "Sales account"), Opt("TaxAccount", "Tax account"), Opt("Narrative", "Narrative")],
            Run = async (c, i) =>
            {
                long customer = await CustomerAsync(c, i.Text("Customer")).ConfigureAwait(true);
                CurrencyCode currency = await c.Db.Customers.AsNoTracking().Where(x => x.Id == customer).Select(x => x.Currency).SingleAsync().ConfigureAwait(true);
                TradingPostingResult posted = Check(await c.Get<ISalesLedgerService>().PostDebitNoteAsync(new SalesInvoiceRequest
                {
                    CompanyId = c.CompanyId, CustomerId = customer, DocumentNumber = i.Text("Number"), DocumentDate = i.Date("Date"), Currency = currency, NetAmount = i.Number("Net"),
                    TaxAmount = i.Number("Tax"), SalesAccountCode = i.Text("Account"), TaxAccountCode = i.Optional("TaxAccount"), Narrative = i.Optional("Narrative"), EnteredByUserId = c.UserId,
                }, i.Text("Invoice")).ConfigureAwait(true));
                return $"Debit note {i.Text("Number")} on {i.Text("Invoice")} posted (journal {posted.JournalNumber}).";
            },
        };
        yield return new Operation
        {
            Module = "Customers", Title = "Customer ageing basis and group", ListTitle = "Customers in groups or aged by invoice date",
            Description = "How a customer's debts are aged (from the invoice date or the due date), and the parent account it belongs to for consolidated group statements (blank: none).",
            Fields = [CustomerPick("Customer", "Customer"), Pick("Ageing", "Age by", Enum.GetNames<AgeingBasis>()), Opt("Parent", "Parent customer code (blank: none)")],
            Run = async (c, i) =>
            {
                long id = await CustomerAsync(c, i.Text("Customer")).ConfigureAwait(true);
                long? parent = i.Optional("Parent") is string p ? await CustomerAsync(c, p).ConfigureAwait(true) : null;
                if (parent == id)
                {
                    throw new OperationException("A customer cannot be its own parent.");
                }

                Customer customer = await c.Db.Customers.FirstAsync(x => x.Id == id).ConfigureAwait(true);
                customer.AgeingBasis = Enum.Parse<AgeingBasis>(i.Text("Ageing"));
                customer.ParentCustomerId = parent;
                await c.Db.SaveChangesAsync().ConfigureAwait(true);
                return $"{customer.Code} is aged by {customer.AgeingBasis}" + (parent is null ? "." : $", under {i.Optional("Parent")!.ToUpperInvariant()}.");
            },
            List = async c => Table(await c.Db.Customers.AsNoTracking().Where(x => x.CompanyId == c.CompanyId && (x.ParentCustomerId != null || x.AgeingBasis == AgeingBasis.InvoiceDate))
                    .OrderBy(x => x.Code).Page(c, 300).ToListAsync().ConfigureAwait(true),
                ("Customer", x => x.Code), ("Name", x => x.Name), ("Age by", x => x.AgeingBasis.ToString()), ("Parent id", x => x.ParentCustomerId)),
        };
        yield return new Operation
        {
            Module = "Purchasing", Title = "Return stock to a supplier", ListTitle = "Recent supplier returns",
            Description = "Sends accepted stock back to the supplier: the stock leaves the warehouse at average cost and the supplier's account is debited at the agreed return price "
                + "(a debit note), any difference to the price variance account.",
            Fields = [SupplierPick("Supplier", "Supplier"), ItemPick("Item", "Product"), WarehousePick("Warehouse", "Warehouse"), Num("Quantity", "Quantity"), Num("Price", "Return unit price"), Today(),
                Req("Document", "Return document number"), Req("Variance", "Price variance account", "5010")],
            Run = async (c, i) =>
            {
                SupplierReturnResult returned = Check(await c.Get<IProcurementService>().ReturnToSupplierAsync(c.CompanyId, await SupplierAsync(c, i.Text("Supplier")).ConfigureAwait(true),
                    await ItemAsync(c, i.Text("Item")).ConfigureAwait(true), await WarehouseAsync(c, i.Text("Warehouse")).ConfigureAwait(true), i.Number("Quantity"), i.Number("Price"), i.Date("Date"),
                    i.Text("Document"), i.Text("Variance")).ConfigureAwait(true));
                return $"Returned {Q(i.Number("Quantity"))} to {i.Text("Supplier").ToUpperInvariant()}: credit {M(returned.CreditValue)} (stock {M(returned.StockValue)}, variance {M(returned.PriceVariance)}; journal {returned.JournalNumber}).";
            },
            List = async c => Table(await c.Db.StockMovements.AsNoTracking().Where(m => m.CompanyId == c.CompanyId && m.Type == Core.Inventory.StockMovementType.SupplierReturn)
                    .OrderByDescending(m => m.Id).Page(c, 100).ToListAsync().ConfigureAwait(true),
                ("Date", m => D(m.Date)), ("Item id", m => m.ItemId), ("Quantity", m => Q(m.Quantity)), ("Value", m => M(m.Value)), ("Reference", m => m.Reference ?? string.Empty)),
        };
        yield return new Operation
        {
            Module = "Purchasing", Title = "Framework agreements", ListTitle = "Framework agreements",
            Description = "A supplier contract with a term and a ceiling: orders placed under it are checked against both, and report PO133 shows how much of it is used.",
            Fields = [SupplierPick("Supplier", "Supplier"), Req("Reference", "Agreement reference"), Req("Description", "Description"), Today("From", "Starts"), Today("To", "Ends"), Num("Ceiling", "Ceiling value")],
            Run = async (c, i) =>
            {
                long supplier = await SupplierAsync(c, i.Text("Supplier")).ConfigureAwait(true);
                string reference = i.Text("Reference").Trim().ToUpperInvariant();
                if (i.Date("To") < i.Date("From") || i.Number("Ceiling") <= 0m)
                {
                    throw new OperationException("An agreement needs an end on or after its start and a ceiling above nil.");
                }

                if (await c.Db.FrameworkAgreements.AnyAsync(f => f.CompanyId == c.CompanyId && f.Reference == reference).ConfigureAwait(true))
                {
                    throw new OperationException($"Agreement {reference} already exists.");
                }

                c.Db.FrameworkAgreements.Add(new Core.Procurement.FrameworkAgreement(c.CompanyId, supplier, reference, i.Text("Description"), i.Date("From"), i.Date("To"), i.Number("Ceiling")));
                await c.Db.SaveChangesAsync().ConfigureAwait(true);
                return $"Agreement {reference} with {i.Text("Supplier").ToUpperInvariant()} to {D(i.Date("To"))}, ceiling {M(i.Number("Ceiling"))}.";
            },
            List = async c => Table(await c.Db.FrameworkAgreements.AsNoTracking().Where(f => f.CompanyId == c.CompanyId).OrderByDescending(f => f.EndDate).Page(c, 200).ToListAsync().ConfigureAwait(true),
                ("Agreement", f => f.Reference), ("Supplier id", f => f.SupplierId), ("Ends", f => D(f.EndDate)), ("Ceiling", f => M(f.CeilingValue))),
        };
        yield return new Operation
        {
            Module = "Budgeting", Title = "Record actual volumes", ListTitle = "Recorded volumes",
            Description = "The quantity behind an actual (units sold, kilolitres billed, hours worked) on a budget line's account, so budget variances split into volume and price (report B35).",
            Fields = [Req("Account", "Account"), Opt("Department", "Department"), Today(), Num("Quantity", "Quantity"), Req("Reference", "Reference")],
            Run = async (c, i) => Check(await c.Get<IBudgetingService>().RecordActualVolumeAsync(c.CompanyId, i.Text("Account"), i.Optional("Department"), i.Date("Date"), i.Number("Quantity"),
                i.Text("Reference")).ConfigureAwait(true), id => $"Volume of {Q(i.Number("Quantity"))} recorded on {i.Text("Account")}."),
        };
        yield return new Operation
        {
            Module = "System Manager", Title = "Report version history", ListTitle = "Saved report versions",
            Description = "Every saved version of a company's report layout: list them, see what changed between two versions (field by field), or roll back to an earlier "
                + "one (saved as a new version, so nothing is lost).",
            Fields = [Pick("Step", "Step", ["History", "Compare", "Roll back"]), Req("Report", "Report code"), OptNum("From", "Earlier version (compare) / version to restore"), OptNum("To", "Later version (compare)")],
            Run = async (c, i) =>
            {
                Application.Abstractions.Reporting.IReportDefinitionService reports = c.Get<Application.Abstractions.Reporting.IReportDefinitionService>();
                string code = i.Text("Report").Trim().ToUpperInvariant();
                switch (i.Text("Step"))
                {
                    case "History":
                        IReadOnlyList<Application.Abstractions.Reporting.ReportVersionEntry> versions = Check(await reports.HistoryAsync(c.CompanyId, code).ConfigureAwait(true));
                        return versions.Count == 0 ? $"{code} has no saved versions (it is the system layout)."
                            : string.Join(Environment.NewLine, versions.Select(v => $"v{v.Version}{(v.IsCurrent ? " (current)" : string.Empty)}  {v.SavedAt:yyyy-MM-dd HH:mm}  user {v.SavedByUserId?.ToString(CultureInfo.InvariantCulture) ?? "-"}  {v.Note}"));
                    case "Compare":
                        int from = (int)(i.OptionalNumber("From") ?? throw new OperationException("Give the earlier version.")), to = (int)(i.OptionalNumber("To") ?? throw new OperationException("Give the later version."));
                        IReadOnlyList<Application.Reporting.JsonChange> changes = Check(await reports.DiffAsync(c.CompanyId, code, from, to).ConfigureAwait(true));
                        return changes.Count == 0 ? $"v{from} and v{to} are the same."
                            : string.Join(Environment.NewLine, changes.Select(ch => $"{ch.Kind,-8} {ch.Path}: {ch.Before ?? "—"} → {ch.After ?? "—"}"));
                    default:
                        int version = (int)(i.OptionalNumber("From") ?? throw new OperationException("Give the version to restore."));
                        int saved = Check(await reports.RollbackAsync(c.CompanyId, code, version, c.UserId).ConfigureAwait(true));
                        return $"{code} rolled back to v{version} (saved as v{saved}).";
                }
            },
        };
        yield return new Operation
        {
            Module = "Fixed Assets", Title = "Asset insurance and warranty", ListTitle = "Insurance and warranty",
            Description = "Who insures an asset, the policy and the sum insured with its expiry, and when the manufacturer's warranty ends (report M213 and the expiry alerts read them).",
            Fields = [Req("Asset", "Asset number"), Opt("Insurer", "Insurer"), Opt("Policy", "Policy number"), OptNum("Insured", "Sum insured"), Opt("InsuredTo", "Insurance expires (yyyy-MM-dd)"),
                Opt("WarrantyTo", "Warranty ends (yyyy-MM-dd)")],
            Run = async (c, i) =>
            {
                string number = i.Text("Asset").Trim().ToUpperInvariant();
                Core.FixedAssets.FixedAsset asset = await c.Db.FixedAssets.FirstOrDefaultAsync(a => a.CompanyId == c.CompanyId && a.AssetNumber.ToUpper() == number).ConfigureAwait(true)
                    ?? throw new OperationException($"No asset {number}.");
                static DateOnly? When(string? text) => text is null ? null
                    : DateOnly.TryParse(text, CultureInfo.InvariantCulture, out DateOnly date) ? date : throw new OperationException($"'{text}' is not a date (yyyy-MM-dd).");
                asset.Insurer = i.Optional("Insurer") ?? asset.Insurer;
                asset.PolicyNumber = i.Optional("Policy") ?? asset.PolicyNumber;
                asset.InsuredValue = i.OptionalNumber("Insured") ?? asset.InsuredValue;
                asset.InsuranceExpiry = When(i.Optional("InsuredTo")) ?? asset.InsuranceExpiry;
                asset.WarrantyExpiry = When(i.Optional("WarrantyTo")) ?? asset.WarrantyExpiry;
                await c.Db.SaveChangesAsync().ConfigureAwait(true);
                return $"{number}: insured by {asset.Insurer ?? "-"} ({asset.PolicyNumber ?? "-"}) for {(asset.InsuredValue is decimal v ? M(v) : "-")} to {(asset.InsuranceExpiry is DateOnly e ? D(e) : "-")}; "
                    + $"warranty to {(asset.WarrantyExpiry is DateOnly w ? D(w) : "-")}.";
            },
            List = async c => Table(await c.Db.FixedAssets.AsNoTracking().Where(a => a.CompanyId == c.CompanyId).OrderBy(a => a.AssetNumber).Page(c, 300).ToListAsync().ConfigureAwait(true),
                ("Asset", a => a.AssetNumber), ("Insurer", a => a.Insurer ?? "-"), ("Policy", a => a.PolicyNumber ?? "-"), ("Insured", a => a.InsuredValue is decimal v ? M(v) : "-"),
                ("Insurance to", a => a.InsuranceExpiry is DateOnly e ? D(e) : "-"), ("Warranty to", a => a.WarrantyExpiry is DateOnly w ? D(w) : "-")),
        };
    }
}
