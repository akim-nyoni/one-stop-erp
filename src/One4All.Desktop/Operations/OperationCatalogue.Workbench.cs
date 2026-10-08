using System.Globalization;
using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Inventory;
using One4All.Application.Abstractions.Payroll;
using One4All.Application.Abstractions.SalesOrders;
using One4All.Application.Inventory;
using One4All.Core.Accounting;
using One4All.Core.Inventory;
using One4All.Core.Procurement;
using static One4All.Desktop.Operations.Ops;

namespace One4All.Desktop.Operations;

/// <content>Stock, staff and sales workbench operations: landed costs, availability, write-down provisions, store requisitions, staff loans, payslips, quotations and pick lists.</content>
public static partial class OperationCatalogue
{
    private static IEnumerable<Operation> Workbench()
    {
        const string stock = "Stock", people = "HR & Attendance", sales = "Sales & Schools";
        yield return new Operation
        {
            Module = stock, Title = "Landed costs on a receipt",
            Description = "Spreads a freight, duty or clearing charge over a GRN's items by value or quantity: the share of stock still on hand is capitalised, the share already sold goes "
                + "to cost of sales.",
            Fields = [Req("Grn", "GRN number"), Num("Amount", "Charge"), Pick("Basis", "Spread by", Enum.GetNames<LandedCostBasis>()), Req("Clearing", "Clearing (charge) account"), Req("Reference", "Reference"), Today()],
            Run = async (c, i) =>
            {
                long grn = await FindAsync(c.Db.Set<GoodsReceivedNote>(), g => g.CompanyId == c.CompanyId && g.GrnNumber.ToUpper() == i.Text("Grn").ToUpper(), g => g.Id, "GRN", i.Text("Grn")).ConfigureAwait(true);
                LandedCostResult result = Check(await c.Get<ILandedCostService>().ApplyAsync(new LandedCostRequest
                {
                    CompanyId = c.CompanyId, GoodsReceivedNoteId = grn, Date = i.Date("Date"), Amount = i.Number("Amount"), Basis = Enum.Parse<LandedCostBasis>(i.Text("Basis")),
                    ClearingAccountCode = i.Text("Clearing"), Reference = i.Text("Reference"),
                }).ConfigureAwait(true));
                return string.Join(Environment.NewLine, result.Lines.Select(l => $"{l.ItemCode,-12} {M(l.Apportioned),12}  capitalised {M(l.Capitalised),12}  to cost of sales {M(l.ToCostOfSales),12}"))
                    + Environment.NewLine + $"Journal {result.JournalNumber}.";
            },
        };
        yield return new Operation
        {
            Module = stock, Title = "Stock availability and back orders", ListTitle = "Back orders",
            Description = "An item's stock on hand, allocated to sales orders, on order and free — available to promise; the list shows every back-ordered sales order line and whether "
                + "stock now covers it.",
            Fields = [Req("Item", "Item code"), Opt("Warehouse", "Warehouse code (blank: all)")],
            Run = async (c, i) =>
            {
                long? warehouse = i.Optional("Warehouse") is string code ? await WarehouseAsync(c, code).ConfigureAwait(true) : null;
                StockAvailability a = Check(await c.Get<IStockAvailabilityService>().AvailabilityAsync(c.CompanyId, await ItemAsync(c, i.Text("Item")).ConfigureAwait(true), warehouse).ConfigureAwait(true));
                return $"{a.ItemCode}: on hand {Q(a.OnHand)}, allocated {Q(a.AllocatedToOrders)}, free {Q(a.Free)}, on order {Q(a.OnOrder)} — available to promise {Q(a.AvailableToPromise)}.";
            },
            List = async c => Table(await c.Get<IStockAvailabilityService>().BackOrdersAsync(c.CompanyId).ConfigureAwait(true),
                ("Order", b => b.OrderNumber), ("Date", b => D(b.OrderDate)), ("Item", b => b.ItemCode), ("Outstanding", b => Q(b.Outstanding)), ("On hand", b => Q(b.OnHand)), ("Can fulfil", b => b.CanFulfil),
                ("Short", b => Q(b.Shortfall))),
        };
        yield return new Operation
        {
            Module = stock, Title = "Stock write-down provision",
            Description = "Assesses the IAS 2 provision on stock: down to net realisable value (selling price less cost to sell), slow-moving and obsolete stock by days since last issued; "
                + "Post books the change against the provision account.",
            Fields = [Pick("Step", "Step", ["Assess", "Post"]), Today("Date", "As at"), Num("CostToSell", "Cost to sell %", "10"), Num("SlowDays", "Slow-moving after (days)", "180"),
                Num("SlowPercent", "Slow-moving provision %", "25"), Num("ObsoleteDays", "Obsolete after (days)", "365"), Num("ObsoletePercent", "Obsolete provision %", "100"),
                Req("Provision", "Stock provision account"), Opt("Expense", "Write-down expense account (post)")],
            Run = async (c, i) =>
            {
                IStockProvisionService provisions = c.Get<IStockProvisionService>();
                StockProvisionPolicy policy = new(i.Number("CostToSell"), i.Whole("SlowDays"), i.Number("SlowPercent"), i.Whole("ObsoleteDays"), i.Number("ObsoletePercent"));
                StockProvisionReport report;
                string? journal = null;
                if (i.Text("Step") == "Post")
                {
                    StockProvisionPostingResult posted = Check(await provisions.PostAsync(c.CompanyId, i.Date("Date"), policy, i.Text("Provision"), i.Optional("Expense") ?? string.Empty).ConfigureAwait(true));
                    (report, journal) = (posted.Report, posted.JournalNumber);
                }
                else
                {
                    report = Check(await provisions.AssessAsync(c.CompanyId, i.Date("Date"), policy, i.Text("Provision")).ConfigureAwait(true));
                }

                return string.Join(Environment.NewLine, report.Lines.Where(l => l.Provision > 0m).Select(l =>
                        $"{l.ItemCode,-12} {l.WarehouseCode,-8} cost {M(l.Cost),12}  {l.Basis,-12} idle {l.DaysSinceLastIssue,4} days  provision {M(l.Provision),12}"))
                    + Environment.NewLine + $"Needed {M(report.TargetProvision)}, booked {M(report.BookedProvision)}, adjustment {M(report.Adjustment)}{(journal is null ? string.Empty : $" (posted {journal})")}.";
            },
        };
        yield return new Operation
        {
            Module = stock, Title = "Store requisitions", ListTitle = "Requisitions",
            Description = "A department requisitions stock from the store (ITEM:quantity; …) charged to an account and cost centre; another user approves it; the store issues it, in "
                + "full or part (lines to issue now, blank for everything outstanding).",
            Fields = [Pick("Step", "Step", ["Raise", "Approve", "Issue"]), Opt("Requisition", "Requisition number (approve / issue)"), Opt("Warehouse", "Store (warehouse) code"),
                Opt("Charge", "Charge account (raise)"), Opt("CostCentre", "Cost centre"), Opt("Lines", "Lines: ITEM:quantity; …"), Today()],
            Run = async (c, i) =>
            {
                IStoreRequisitionService requisitions = c.Get<IStoreRequisitionService>();
                async Task<List<StoreRequisitionLineRequest>> Lines()
                {
                    List<StoreRequisitionLineRequest> lines = [];
                    foreach (string part in (i.Optional("Lines") ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    {
                        string[] bits = part.Split(':', StringSplitOptions.TrimEntries);
                        lines.Add(bits.Length == 2 && decimal.TryParse(bits[1], NumberStyles.Number, CultureInfo.InvariantCulture, out decimal quantity)
                            ? new StoreRequisitionLineRequest(await ItemAsync(c, bits[0]).ConfigureAwait(true), quantity) : throw new OperationException($"Write each line as ITEM:quantity (\"{part}\")."));
                    }

                    return lines;
                }

                async Task<long> Id() => await FindAsync(c.Db.Set<StoreRequisition>(), r => r.CompanyId == c.CompanyId && r.Number.ToUpper() == (i.Optional("Requisition") ?? string.Empty).ToUpper(), r => r.Id,
                    "requisition", i.Optional("Requisition") ?? string.Empty).ConfigureAwait(true);
                switch (i.Text("Step"))
                {
                    case "Raise":
                        (long _, string number) = Check(await requisitions.RaiseAsync(c.CompanyId, new StoreRequisitionRequest(i.Date("Date"), await WarehouseAsync(c, i.Optional("Warehouse") ?? string.Empty).ConfigureAwait(true),
                            i.Optional("Charge") ?? string.Empty, i.Optional("CostCentre"), c.UserId, await Lines().ConfigureAwait(true))).ConfigureAwait(true));
                        return $"Requisition {number} raised; another user approves it.";
                    case "Approve":
                        return Check(await requisitions.ApproveAsync(c.CompanyId, await Id().ConfigureAwait(true), c.UserId).ConfigureAwait(true), "Approved.");
                    default:
                        StoreIssueResult issued = Check(await requisitions.IssueAsync(c.CompanyId, await Id().ConfigureAwait(true), i.Date("Date"), await Lines().ConfigureAwait(true)).ConfigureAwait(true));
                        return $"Issue note {issued.IssueNoteNumber}: {M(issued.Cost)}{(issued.FullyIssued ? "; requisition complete." : "; more to issue.")}";
                }
            },
            List = async c => Table(await c.Db.Set<StoreRequisition>().AsNoTracking().Where(r => r.CompanyId == c.CompanyId).OrderByDescending(r => r.Id).Take(200).ToListAsync().ConfigureAwait(true),
                ("Requisition", r => r.Number), ("Status", r => r.Status)),
        };
        yield return new Operation
        {
            Module = people, Title = "Staff loans", ListTitle = "Staff loans",
            Description = "Grants a staff loan (paid out, recovered through payroll by the instalment) or records a repayment made outside payroll.",
            Fields = [Pick("Step", "Step", ["Grant", "Repayment"]), Opt("Employee", "Employee number (grant)"), Opt("Reference", "Loan reference"), Num("Amount", "Principal / repayment"),
                Num("Instalment", "Monthly instalment (grant)", "0"), Opt("Receivable", "Loan receivable account (grant)"), Req("Funding", "Bank / funding account"), Req("Document", "Document number"), Today()],
            Run = async (c, i) =>
            {
                IEmployeeLoanService loans = c.Get<IEmployeeLoanService>();
                if (i.Text("Step") == "Grant")
                {
                    return Check(await loans.GrantAsync(c.CompanyId, await EmployeeAsync(c, i.Optional("Employee") ?? string.Empty).ConfigureAwait(true), i.Optional("Reference") ?? string.Empty, i.Number("Amount"),
                        i.Number("Instalment"), i.Date("Date"), i.Optional("Receivable") ?? string.Empty, i.Text("Funding"), i.Text("Document")).ConfigureAwait(true), id => $"Loan {id} granted.");
                }

                long loan = await FindAsync(c.Db.Set<Core.Payroll.EmployeeLoan>(), l => l.CompanyId == c.CompanyId && l.Reference == (i.Optional("Reference") ?? string.Empty), l => l.Id, "loan",
                    i.Optional("Reference") ?? string.Empty).ConfigureAwait(true);
                return $"Repaid; {M(Check(await loans.RecordRepaymentAsync(c.CompanyId, loan, i.Number("Amount"), i.Date("Date"), i.Text("Funding"), i.Text("Document")).ConfigureAwait(true)))} outstanding.";
            },
            List = async c => Table(await c.Db.Set<Core.Payroll.EmployeeLoan>().AsNoTracking().Where(l => l.CompanyId == c.CompanyId).OrderByDescending(l => l.Id).Take(300).ToListAsync().ConfigureAwait(true),
                ("Loan", l => l.Reference), ("Employee", l => l.EmployeeId), ("Principal", l => M(l.Principal)), ("Outstanding", l => M(l.Outstanding))),
        };
        yield return new Operation
        {
            Module = people, Title = "Payslips: save or email",
            Description = "Saves one employee's payslip for a period as a (password-protected) PDF, or emails every payslip of a payroll run to the employees, listing who has no "
                + "email address or payslip password.",
            Fields = [Pick("Step", "Step", ["Save PDF", "Email a run"]), Opt("Employee", "Employee number (PDF)"), Opt("Period", "Period, e.g. 2026-09 (PDF)"), Opt("File", "Save to file path (PDF)"),
                Opt("Run", "Payroll run number (email)")],
            Run = async (c, i) =>
            {
                IPayslipDistributionService payslips = c.Get<IPayslipDistributionService>();
                if (i.Text("Step") == "Save PDF")
                {
                    byte[] pdf = Check(await payslips.PayslipPdfAsync(c.CompanyId, await EmployeeAsync(c, i.Optional("Employee") ?? string.Empty).ConfigureAwait(true), i.Optional("Period") ?? string.Empty).ConfigureAwait(true));
                    string path = i.Optional("File") ?? throw new OperationException("Give the file path to save the payslip to.");
                    await System.IO.File.WriteAllBytesAsync(path, pdf).ConfigureAwait(true);
                    return $"Saved to {path}.";
                }

                PayslipEmailRun run = Check(await payslips.EmailRunAsync(c.CompanyId, i.Optional("Run") ?? string.Empty).ConfigureAwait(true));
                return $"{run.Queued} payslip(s) queued."
                    + (run.NoEmail.Count == 0 ? string.Empty : $" No email: {string.Join(", ", run.NoEmail)}.")
                    + (run.NoPassword.Count == 0 ? string.Empty : $" No payslip password: {string.Join(", ", run.NoPassword)}.");
            },
        };
        yield return new Operation
        {
            Module = sales, Title = "Quotations", ListTitle = "Quotations",
            Description = "Quotes a customer (ITEM:quantity@price; …, valid until a date), records it sent, accepted or declined, and converts an accepted quotation into a sales order.",
            Fields = [Pick("Step", "Step", ["Create", "Sent", "Accepted", "Declined", "Convert to order"]), Req("Quote", "Quotation number"), Opt("Customer", "Customer code (create)"),
                Opt("Lines", "Lines: ITEM:quantity@price; … (create)"), Opt("Revenue", "Revenue account (create)", "4000"), Opt("TaxCode", "Tax code (create)"), Today("Valid", "Valid until (create)"),
                Opt("Order", "Sales order number (convert)"), Today()],
            Run = async (c, i) =>
            {
                IQuotationService quotations = c.Get<IQuotationService>();
                async Task<long> Id() => await FindAsync(c.Db.Set<Core.SalesOrders.Quotation>(), q => q.CompanyId == c.CompanyId && q.QuoteNumber.ToUpper() == i.Text("Quote").ToUpper(), q => q.Id,
                    "quotation", i.Text("Quote")).ConfigureAwait(true);
                switch (i.Text("Step"))
                {
                    case "Create":
                        long customer = await CustomerAsync(c, i.Optional("Customer") ?? string.Empty).ConfigureAwait(true);
                        CurrencyCode currency = await c.Db.Customers.AsNoTracking().Where(x => x.Id == customer).Select(x => x.Currency).FirstAsync().ConfigureAwait(true);
                        List<QuotationLineRequest> lines = [];
                        foreach (string part in (i.Optional("Lines") ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                        {
                            string[] main = part.Split('@', StringSplitOptions.TrimEntries);
                            string[] itemQty = main[0].Split(':', StringSplitOptions.TrimEntries);
                            if (main.Length != 2 || itemQty.Length != 2 || !decimal.TryParse(itemQty[1], NumberStyles.Number, CultureInfo.InvariantCulture, out decimal quantity)
                                || !decimal.TryParse(main[1], NumberStyles.Number, CultureInfo.InvariantCulture, out decimal price))
                            {
                                throw new OperationException($"Write each line as ITEM:quantity@price (\"{part}\").");
                            }

                            long item = await ItemAsync(c, itemQty[0]).ConfigureAwait(true);
                            string description = await c.Db.Items.AsNoTracking().Where(x => x.Id == item).Select(x => x.Description).FirstAsync().ConfigureAwait(true);
                            lines.Add(new QuotationLineRequest(item, description, quantity, price, i.Optional("TaxCode"), i.Optional("Revenue") ?? "4000"));
                        }

                        return Check(await quotations.CreateAsync(c.CompanyId, customer, i.Text("Quote"), i.Date("Date"), i.Date("Valid"), currency, lines).ConfigureAwait(true), _ => "Quotation created.");
                    case "Sent":
                        return Check(await quotations.SendAsync(c.CompanyId, await Id().ConfigureAwait(true)).ConfigureAwait(true), "Marked sent.");
                    case "Accepted":
                        return Check(await quotations.AcceptAsync(c.CompanyId, await Id().ConfigureAwait(true)).ConfigureAwait(true), "Accepted.");
                    case "Declined":
                        return Check(await quotations.DeclineAsync(c.CompanyId, await Id().ConfigureAwait(true)).ConfigureAwait(true), "Declined.");
                    default:
                        return Check(await quotations.ConvertToOrderAsync(c.CompanyId, await Id().ConfigureAwait(true), i.Optional("Order") ?? string.Empty, i.Date("Date")).ConfigureAwait(true),
                            _ => "Converted into a sales order.");
                }
            },
            List = async c => Table(await c.Db.Set<Core.SalesOrders.Quotation>().AsNoTracking().Where(q => q.CompanyId == c.CompanyId).OrderByDescending(q => q.Id).Take(200).ToListAsync().ConfigureAwait(true),
                ("Quotation", q => q.QuoteNumber), ("Status", q => q.Status)),
        };
        yield return new Operation
        {
            Module = sales, Title = "Pick list for a sales order",
            Description = "The pick list for what is still to despatch on a sales order from a warehouse, in bin order, as text to print.",
            Fields = [Req("Order", "Sales order number"), Req("Warehouse", "Warehouse code")],
            Run = async (c, i) =>
            {
                long order = await FindAsync(c.Db.SalesOrders, o => o.CompanyId == c.CompanyId && o.OrderNumber == i.Text("Order").ToUpper(), o => o.Id, "sales order", i.Text("Order")).ConfigureAwait(true);
                return Check(await c.Get<IPickListService>().RenderTextAsync(c.CompanyId, order, await WarehouseAsync(c, i.Text("Warehouse")).ConfigureAwait(true)).ConfigureAwait(true));
            },
        };
    }
}
