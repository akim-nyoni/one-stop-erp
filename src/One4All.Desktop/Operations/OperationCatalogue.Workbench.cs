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
            Description = "An item's stock on hand, allocated to sales orders, on order and free - available to promise; the list shows every back-ordered sales order line and whether "
                + "stock now covers it.",
            Fields = [ItemPick("Item", "Product"), Opt("Warehouse", "Warehouse code (blank: all)")],
            Run = async (c, i) =>
            {
                long? warehouse = i.Optional("Warehouse") is string code ? await WarehouseAsync(c, code).ConfigureAwait(true) : null;
                StockAvailability a = Check(await c.Get<IStockAvailabilityService>().AvailabilityAsync(c.CompanyId, await ItemAsync(c, i.Text("Item")).ConfigureAwait(true), warehouse).ConfigureAwait(true));
                return $"{a.ItemCode}: on hand {Q(a.OnHand)}, allocated {Q(a.AllocatedToOrders)}, free {Q(a.Free)}, on order {Q(a.OnOrder)} - available to promise {Q(a.AvailableToPromise)}.";
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
            List = async c => Table(await c.Db.Set<StoreRequisition>().AsNoTracking().Where(r => r.CompanyId == c.CompanyId).OrderByDescending(r => r.Id).Page(c, 200).ToListAsync().ConfigureAwait(true),
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
            List = async c => Table(await c.Db.Set<Core.Payroll.EmployeeLoan>().AsNoTracking().Where(l => l.CompanyId == c.CompanyId).OrderByDescending(l => l.Id).Page(c, 300).ToListAsync().ConfigureAwait(true),
                ("Loan", l => l.Reference), ("Employee", l => l.EmployeeId), ("Principal", l => M(l.Principal)), ("Outstanding", l => M(l.Outstanding))),
        };
        yield return new Operation
        {
            Module = people, Title = "Payslips: save or email",
            Description = "Saves one employee's payslip for a period as a (password-protected) PDF, or emails every payslip of a payroll run to the employees, listing who has no "
                + "email address or payslip password. Payslip settings choose what opens the PDF (national ID, date of birth, employee number, a password the employee sets, or none) "
                + "and the report whose layout prints it (the company's own design in the report designer, e.g. a copy of PR74; blank for the standard text payslip). "
                + "Set password keeps a password for an employee who cannot set their own in self-service.",
            Fields = [Pick("Step", "Step", ["Save PDF", "Email a run", "Payslip settings", "Set password"]), Opt("Employee", "Employee number (PDF / password)"), Opt("Period", "Period, e.g. 2026-09 (PDF)"),
                Opt("File", "Save to file path (PDF)"), Opt("Run", "Payroll run number (email)"),
                Pick("Rule", "Opens the PDF with (settings)", ["National ID", "Date of birth", "Employee number", "Password the employee sets", "No password"]),
                Opt("Layout", "Payslip layout report code (settings; blank: standard text)"), Opt("Password", "Payslip password (set password; 8+ characters)")],
            Run = async (c, i) =>
            {
                IPayslipDistributionService payslips = c.Get<IPayslipDistributionService>();
                if (i.Text("Step") == "Payslip settings")
                {
                    Core.System.Company company = await c.Db.Companies.FirstAsync(x => x.Id == c.CompanyId).ConfigureAwait(true);
                    company.PayslipPasswordRule = i.Text("Rule") switch
                    {
                        "Date of birth" => Core.Payroll.PayslipPasswordRule.DateOfBirth,
                        "Employee number" => Core.Payroll.PayslipPasswordRule.EmployeeNumber,
                        "Password the employee sets" => Core.Payroll.PayslipPasswordRule.EmployeeSet,
                        "No password" => Core.Payroll.PayslipPasswordRule.None,
                        _ => Core.Payroll.PayslipPasswordRule.NationalId,
                    };
                    string? layout = i.Optional("Layout")?.Trim().ToUpperInvariant();
                    if (layout is not null && !await c.Db.ReportDefinitions.AnyAsync(r => r.Code == layout).ConfigureAwait(true) && One4All.Reporting.SystemReportCatalogue.All().All(r => r.Code != layout))
                    {
                        throw new OperationException($"No report {layout}. Design the payslip in the report designer (start from PR74) and give its code here.");
                    }

                    company.PayslipLayoutCode = layout;
                    await c.Db.SaveChangesAsync().ConfigureAwait(true);
                    return $"Payslips open with {i.Text("Rule").ToLowerInvariant()}; layout: {layout ?? "standard text payslip"}.";
                }

                if (i.Text("Step") == "Set password")
                {
                    string password = i.Optional("Password")?.Trim() ?? string.Empty;
                    if (password.Length < 8)
                    {
                        throw new OperationException("The payslip password needs at least 8 characters.");
                    }

                    long employeeId = await EmployeeAsync(c, i.Optional("Employee") ?? string.Empty).ConfigureAwait(true);
                    Core.Payroll.Employee employee = await c.Db.Employees.FirstAsync(e => e.Id == employeeId).ConfigureAwait(true);
                    employee.PayslipPassword = password;
                    await c.Db.SaveChangesAsync().ConfigureAwait(true);
                    return $"Payslip password set for {employee.EmployeeNumber} (it is stored encrypted and not shown again).";
                }

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
            List = async c => Table(await c.Db.Set<Core.SalesOrders.Quotation>().AsNoTracking().Where(q => q.CompanyId == c.CompanyId).OrderByDescending(q => q.Id).Page(c, 200).ToListAsync().ConfigureAwait(true),
                ("Quotation", q => q.QuoteNumber), ("Status", q => q.Status)),
        };
        yield return new Operation
        {
            Module = sales, Title = "Pick list for a sales order",
            Description = "The pick list for what is still to despatch on a sales order from a warehouse, in bin order, as text to print.",
            Fields = [Req("Order", "Sales order number"), WarehousePick("Warehouse", "Warehouse")],
            Run = async (c, i) =>
            {
                long order = await FindAsync(c.Db.SalesOrders, o => o.CompanyId == c.CompanyId && o.OrderNumber == i.Text("Order").ToUpper(), o => o.Id, "sales order", i.Text("Order")).ConfigureAwait(true);
                return Check(await c.Get<IPickListService>().RenderTextAsync(c.CompanyId, order, await WarehouseAsync(c, i.Text("Warehouse")).ConfigureAwait(true)).ConfigureAwait(true));
            },
        };
        const string customers = "Customers", suppliers = "Suppliers";
        yield return new Operation
        {
            Module = customers, Title = "Sales commissions", ListTitle = "Sales reps",
            Description = "A sales rep's commission statement for a period (by customer, on the rep's basis: invoiced, collected or gross margin), or the month-end accrual of "
                + "every rep's commission earned. Save a rep sets up or changes one; Assign customer puts a customer with a rep. Link employee makes a rep an employee; "
                + "To payroll sends every linked rep's commission from From to To to the pay period's commission element (instead of accruing it). Delete takes a rep out of use "
                + "once no customer is assigned to it (Save rep brings it back).",
            KeyColumn = "Rep",
            Delete = async (c, code) =>
            {
                Core.Trading.SalesRep rep = await ExistingAsync<Core.Trading.SalesRep>(c, code, r => r.Code).ConfigureAwait(true) ?? throw new OperationException($"No sales rep {code}.");
                int assigned = await c.Db.Customers.CountAsync(x => x.CompanyId == c.CompanyId && x.SalesRepId == rep.Id).ConfigureAwait(true);
                if (assigned > 0)
                {
                    throw new OperationException($"{assigned} customer(s) are assigned to {rep.Code}; assign them to another rep first.");
                }

                rep.IsActive = false;
                await c.Db.SaveChangesAsync().ConfigureAwait(true);
                return $"Sales rep {rep.Code} taken out of use (kept in history).";
            },
            Fields = [Pick("Step", "Step", ["Statement", "Accrue", "Save rep", "Assign customer", "Link employee", "To payroll"]), Opt("Rep", "Sales rep code"), Today("From", "From"), Today("To", "To / month end"),
                Opt("Expense", "Commission expense account (accrue)"), Opt("Payable", "Commission payable account (accrue)"), Opt("Name", "Rep name (save rep)"),
                Pick("Basis", "Commission basis", Enum.GetNames<Core.Trading.CommissionBasis>()), Num("Rate", "Commission %", "0"), Opt("Customer", "Customer code (assign)"),
                Opt("Employee", "Employee number (link)"), Opt("Period", "Pay period (to payroll)"), Opt("Element", "Commission element", "COMMISSION")],
            Run = async (c, i) =>
            {
                Application.Abstractions.Trading.ICommissionService commissions = c.Get<Application.Abstractions.Trading.ICommissionService>();
                string repCode = (i.Optional("Rep") ?? string.Empty).Trim().ToUpperInvariant();
                if (i.Text("Step") == "Save rep")
                {
                    if (repCode.Length == 0 || i.Number("Rate") < 0m || i.Number("Rate") > 100m)
                    {
                        throw new OperationException("A rep needs a code and a commission rate from 0 to 100%.");
                    }

                    Core.Trading.SalesRep? saved = await c.Db.Set<Core.Trading.SalesRep>().FirstOrDefaultAsync(r => r.CompanyId == c.CompanyId && r.Code == repCode).ConfigureAwait(true);
                    Core.Trading.CommissionBasis basis = Enum.Parse<Core.Trading.CommissionBasis>(i.Text("Basis"));
                    if (saved is null)
                    {
                        c.Db.Add(new Core.Trading.SalesRep(c.CompanyId, repCode, i.Optional("Name") ?? repCode, basis, i.Number("Rate")));
                    }
                    else
                    {
                        (saved.Name, saved.Basis, saved.RatePercent, saved.IsActive) = (i.Optional("Name") ?? saved.Name, basis, i.Number("Rate"), true);
                    }

                    await c.Db.SaveChangesAsync().ConfigureAwait(true);
                    return $"Sales rep {repCode} saved: {basis} at {Q(i.Number("Rate"))}%.";
                }

                if (i.Text("Step") == "Assign customer")
                {
                    long repId = await FindAsync(c.Db.Set<Core.Trading.SalesRep>(), r => r.CompanyId == c.CompanyId && r.Code == repCode, r => r.Id, "sales rep", repCode).ConfigureAwait(true);
                    long customerId = await CustomerAsync(c, i.Optional("Customer") ?? string.Empty).ConfigureAwait(true);
                    (await c.Db.Customers.SingleAsync(x => x.Id == customerId).ConfigureAwait(true)).SalesRepId = repId;
                    await c.Db.SaveChangesAsync().ConfigureAwait(true);
                    return $"{i.Optional("Customer")} now earns {repCode} commission.";
                }

                if (i.Text("Step") == "Link employee")
                {
                    Core.Trading.SalesRep rep = await c.Db.Set<Core.Trading.SalesRep>().FirstOrDefaultAsync(r => r.CompanyId == c.CompanyId && r.Code == repCode).ConfigureAwait(true)
                        ?? throw new OperationException($"Sales rep {repCode} not found.");
                    string number = (i.Optional("Employee") ?? string.Empty).Trim().ToUpperInvariant();
                    rep.EmployeeId = number.Length == 0 ? null
                        : await FindAsync(c.Db.Employees, e => e.CompanyId == c.CompanyId && e.EmployeeNumber == number, e => e.Id, "employee", number).ConfigureAwait(true);
                    await c.Db.SaveChangesAsync().ConfigureAwait(true);
                    return rep.EmployeeId is null ? $"{repCode} is no longer paid through payroll." : $"{repCode} is employee {number}; commission can go to payroll.";
                }

                if (i.Text("Step") == "To payroll")
                {
                    IReadOnlyList<Application.Abstractions.Trading.CommissionPayrollLine> sent = Check(await commissions.ToPayrollAsync(c.CompanyId, i.Date("From"), i.Date("To"),
                        i.Optional("Period") ?? string.Empty, i.Optional("Element") ?? "COMMISSION").ConfigureAwait(true));
                    return sent.Count == 0 ? "No commission to send." : string.Join(Environment.NewLine, sent.Select(l => $"{l.RepCode,-10} → {l.EmployeeNumber,-10} {M(l.Amount),12}"));
                }

                if (i.Text("Step") == "Statement")
                {
                    long rep = await FindAsync(c.Db.Set<Core.Trading.SalesRep>(), r => r.CompanyId == c.CompanyId && r.Code == (i.Optional("Rep") ?? string.Empty).ToUpper(), r => r.Id, "sales rep",
                        i.Optional("Rep") ?? string.Empty).ConfigureAwait(true);
                    Application.Abstractions.Trading.CommissionStatement statement = Check(await commissions.StatementAsync(c.CompanyId, rep, i.Date("From"), i.Date("To")).ConfigureAwait(true));
                    return string.Join(Environment.NewLine, statement.Lines.Select(l => $"{l.CustomerCode,-12} base {M(l.Base),14}  commission {M(l.Commission),12}"))
                        + Environment.NewLine + $"{statement.RepCode} total {M(statement.Total)}.";
                }

                IReadOnlyList<Application.Abstractions.Trading.CommissionAccrualLine> accrued = Check(await commissions.AccrueAsync(c.CompanyId, i.Date("To"), i.Optional("Expense") ?? string.Empty,
                    i.Optional("Payable") ?? string.Empty).ConfigureAwait(true));
                return accrued.Count == 0 ? "Nothing to accrue." : string.Join(Environment.NewLine, accrued.Select(a => $"{a.RepCode,-10} earned {M(a.Earned),12}  booked now {M(a.Booked),12}"));
            },
            List = async c => Table(await c.Db.Set<Core.Trading.SalesRep>().AsNoTracking().Where(r => r.CompanyId == c.CompanyId).OrderBy(r => r.Code).ToListAsync().ConfigureAwait(true),
                ("Step", _ => "Save rep"), ("Rep", r => r.Code), ("Name", r => r.Name), ("Basis", r => r.Basis), ("Rate %", r => Q(r.RatePercent)), ("Active", r => r.IsActive)),
        };
        yield return new Operation
        {
            Module = customers, Title = "Lay-bys", ListTitle = "Lay-by plans",
            Description = "Opens a lay-by (goods ITEM:quantity@price; … reserved against a deposit, paid off by a final date), takes payments (completed when paid up), cancels one "
                + "(the forfeit % kept, the rest refunded) or defaults every overdue plan.",
            Fields = [Pick("Step", "Step", ["Open", "Pay", "Cancel", "Default overdue"]), Opt("Plan", "Plan number"), Opt("Customer", "Customer code (open)"), Opt("Warehouse", "Warehouse (open)"),
                Opt("Lines", "Goods: ITEM:quantity@price; … (open)"), Num("Amount", "Deposit / payment", "0"), Num("MinDeposit", "Minimum deposit %", "10"), Num("Forfeit", "Forfeit % on cancel", "10"),
                Today("Final", "Final payment date (open)"), Opt("Bank", "Bank / refund account"), Opt("Deposits", "Lay-by deposits account"), Opt("Revenue", "Revenue account"), Opt("Forfeits", "Forfeiture income account"),
                Opt("TaxCode", "Tax code the prices include (open)"), Today()],
            Run = async (c, i) =>
            {
                Application.Abstractions.Trading.ILaybyService laybys = c.Get<Application.Abstractions.Trading.ILaybyService>();
                async Task<long> PlanId() => await FindAsync(c.Db.Set<Core.Trading.LaybyPlan>(), l => l.CompanyId == c.CompanyId && l.PlanNumber.ToUpper() == (i.Optional("Plan") ?? string.Empty).ToUpper(),
                    l => l.Id, "lay-by", i.Optional("Plan") ?? string.Empty).ConfigureAwait(true);
                switch (i.Text("Step"))
                {
                    case "Open":
                        List<Application.Abstractions.Trading.LaybyLineRequest> lines = [];
                        foreach (string part in (i.Optional("Lines") ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                        {
                            string[] main = part.Split('@', StringSplitOptions.TrimEntries);
                            string[] itemQty = main[0].Split(':', StringSplitOptions.TrimEntries);
                            lines.Add(main.Length == 2 && itemQty.Length == 2 && decimal.TryParse(itemQty[1], NumberStyles.Number, CultureInfo.InvariantCulture, out decimal quantity)
                                && decimal.TryParse(main[1], NumberStyles.Number, CultureInfo.InvariantCulture, out decimal price)
                                ? new Application.Abstractions.Trading.LaybyLineRequest(await ItemAsync(c, itemQty[0]).ConfigureAwait(true), quantity, price)
                                : throw new OperationException($"Write each line as ITEM:quantity@price (\"{part}\")."));
                        }

                        return Check(await laybys.OpenAsync(new Application.Abstractions.Trading.LaybyRequest
                        {
                            CompanyId = c.CompanyId, CustomerId = await CustomerAsync(c, i.Optional("Customer") ?? string.Empty).ConfigureAwait(true), PlanNumber = i.Optional("Plan") ?? string.Empty,
                            WarehouseId = await WarehouseAsync(c, i.Optional("Warehouse") ?? string.Empty).ConfigureAwait(true), OpenedOn = i.Date("Date"), FinalDate = i.Date("Final"), Deposit = i.Number("Amount"),
                            MinimumDepositPercent = i.Number("MinDeposit"), ForfeitPercent = i.Number("Forfeit"), BankAccountCode = i.Optional("Bank") ?? string.Empty,
                            DepositAccountCode = i.Optional("Deposits") ?? string.Empty, RevenueAccountCode = i.Optional("Revenue") ?? string.Empty, ForfeitureAccountCode = i.Optional("Forfeits") ?? string.Empty,
                            Lines = lines, TaxCode = i.Optional("TaxCode"),
                        }).ConfigureAwait(true), _ => "Lay-by opened; the goods are reserved.");
                    case "Pay":
                        Application.Abstractions.Trading.LaybyPayment paid = Check(await laybys.PayAsync(c.CompanyId, await PlanId().ConfigureAwait(true), i.Date("Date"), i.Number("Amount"), i.Optional("Bank") ?? string.Empty)
                            .ConfigureAwait(true));
                        return paid.Completed ? $"Paid up ({M(paid.Paid)}); the goods can be collected." : $"Paid {M(paid.Paid)}; balance {M(paid.Balance)}.";
                    case "Cancel":
                        Application.Abstractions.Trading.LaybyCancellation cancelled = Check(await laybys.CancelAsync(c.CompanyId, await PlanId().ConfigureAwait(true), i.Date("Date"), i.Optional("Bank") ?? string.Empty)
                            .ConfigureAwait(true));
                        return $"Cancelled: paid {M(cancelled.Paid)}, forfeited {M(cancelled.Forfeited)}, refunded {M(cancelled.Refunded)}.";
                    default:
                        IReadOnlyList<Application.Abstractions.Trading.LaybyCancellation> defaulted = Check(await laybys.DefaultOverdueAsync(c.CompanyId, i.Date("Date"), i.Optional("Bank") ?? string.Empty).ConfigureAwait(true));
                        return defaulted.Count == 0 ? "No overdue lay-bys." : string.Join(Environment.NewLine, defaulted.Select(d => $"{d.PlanNumber}: forfeited {M(d.Forfeited)}, refunded {M(d.Refunded)}"));
                }
            },
            List = async c => Table(await c.Db.Set<Core.Trading.LaybyPlan>().AsNoTracking().Where(l => l.CompanyId == c.CompanyId).OrderByDescending(l => l.Id).Page(c, 200).ToListAsync().ConfigureAwait(true),
                ("Plan", l => l.PlanNumber), ("Status", l => l.Status)),
        };
        foreach (string ledger in new[] { customers, suppliers })
        {
            bool selling = ledger == customers;
            yield return new Operation
            {
                Module = ledger, Title = selling ? "Recurring sales invoices" : "Recurring purchase invoices", ListTitle = "Recurring invoices",
                Description = $"An invoice {(selling ? "to a customer" : "from a supplier")} raised every so many months from a first date; Generate raises all that are due; Stop / restart one. "
                    + "Create with an existing reference changes that template's amounts, accounts, interval and narrative from its next run (its trader and next run date stay); "
                    + "Delete stops it (kept in history; Restart brings it back).",
                KeyColumn = "Reference",
                Delete = async (c, reference) =>
                {
                    long id = selling
                        ? await FindAsync(c.Db.Set<Core.Trading.RecurringSalesInvoice>(), t => t.CompanyId == c.CompanyId && t.Reference.ToUpper() == reference.ToUpper(), t => t.Id, "recurring invoice", reference).ConfigureAwait(true)
                        : await FindAsync(c.Db.Set<Core.Trading.RecurringPurchaseInvoice>(), t => t.CompanyId == c.CompanyId && t.Reference.ToUpper() == reference.ToUpper(), t => t.Id, "recurring invoice", reference).ConfigureAwait(true);
                    return Check(selling
                        ? await c.Get<Application.Abstractions.Trading.IRecurringSalesInvoiceService>().SetActiveAsync(c.CompanyId, id, false).ConfigureAwait(true)
                        : await c.Get<Application.Abstractions.Trading.IRecurringPurchaseInvoiceService>().SetActiveAsync(c.CompanyId, id, false).ConfigureAwait(true),
                        $"Recurring invoice {reference} stopped (kept in history).");
                },
                Fields = [Pick("Step", "Step", ["Create", "Generate due", "Stop", "Restart"]), Opt("Trader", selling ? "Customer code (create)" : "Supplier code (create)"), Opt("Reference", "Reference"),
                    Num("Net", "Net amount", "0"), Num("Tax", "Tax amount", "0"), Opt("Account", selling ? "Sales account" : "Expense account"), Opt("TaxAccount", "Tax account"), Num("Interval", "Every (months)", "1"),
                    Today("First", "First run date"), Opt("Narrative", "Narrative"), Today("Date", "Generate as of")],
                Run = async (c, i) =>
                {
                    string reference = i.Optional("Reference") ?? string.Empty;
                    async Task<long> TemplateId() => selling
                        ? await FindAsync(c.Db.Set<Core.Trading.RecurringSalesInvoice>(), t => t.CompanyId == c.CompanyId && t.Reference.ToUpper() == reference.ToUpper(), t => t.Id, "recurring invoice", reference).ConfigureAwait(true)
                        : await FindAsync(c.Db.Set<Core.Trading.RecurringPurchaseInvoice>(), t => t.CompanyId == c.CompanyId && t.Reference.ToUpper() == reference.ToUpper(), t => t.Id, "recurring invoice", reference).ConfigureAwait(true);
                    switch (i.Text("Step"))
                    {
                        case "Create":
                            if (i.Number("Net") <= 0m || i.Number("Tax") < 0m || i.Whole("Interval") <= 0)
                            {
                                throw new OperationException("A recurring invoice needs a positive net amount, a tax amount of nil or more and an interval of at least one month.");
                            }

                            if (selling && await ExistingAsync<Core.Trading.RecurringSalesInvoice>(c, reference, t => t.Reference).ConfigureAwait(true) is { } sale)
                            {
                                sale.Amend(i.Number("Net"), i.Number("Tax"), i.Optional("Account") ?? sale.SalesAccountCode, i.Optional("TaxAccount"), i.Whole("Interval"), i.Optional("Narrative"));
                                await c.Db.SaveChangesAsync().ConfigureAwait(true);
                                return $"Recurring invoice {sale.Reference} changed; the next run on {D(sale.NextRunDate)} bills the new amounts.";
                            }

                            if (!selling && await ExistingAsync<Core.Trading.RecurringPurchaseInvoice>(c, reference, t => t.Reference).ConfigureAwait(true) is { } purchase)
                            {
                                purchase.Amend(i.Number("Net"), i.Number("Tax"), i.Optional("Account") ?? purchase.ExpenseAccountCode, i.Optional("TaxAccount"), i.Whole("Interval"), i.Optional("Narrative"));
                                await c.Db.SaveChangesAsync().ConfigureAwait(true);
                                return $"Recurring invoice {purchase.Reference} changed; the next run on {D(purchase.NextRunDate)} bills the new amounts.";
                            }

                            if (selling)
                            {
                                long customer = await CustomerAsync(c, i.Optional("Trader") ?? string.Empty).ConfigureAwait(true);
                                CurrencyCode currency = await c.Db.Customers.AsNoTracking().Where(x => x.Id == customer).Select(x => x.Currency).FirstAsync().ConfigureAwait(true);
                                return Check(await c.Get<Application.Abstractions.Trading.IRecurringSalesInvoiceService>().CreateAsync(c.CompanyId, customer, reference, currency, i.Number("Net"), i.Number("Tax"),
                                    i.Optional("Account") ?? string.Empty, i.Optional("TaxAccount"), i.Whole("Interval"), i.Date("First"), i.Optional("Narrative")).ConfigureAwait(true), _ => "Recurring invoice created.");
                            }

                            long supplier = await SupplierAsync(c, i.Optional("Trader") ?? string.Empty).ConfigureAwait(true);
                            CurrencyCode supplierCurrency = await c.Db.Suppliers.AsNoTracking().Where(x => x.Id == supplier).Select(x => x.Currency).FirstAsync().ConfigureAwait(true);
                            return Check(await c.Get<Application.Abstractions.Trading.IRecurringPurchaseInvoiceService>().CreateAsync(c.CompanyId, supplier, reference, supplierCurrency, i.Number("Net"), i.Number("Tax"),
                                i.Optional("Account") ?? string.Empty, i.Optional("TaxAccount"), i.Whole("Interval"), i.Date("First"), i.Optional("Narrative")).ConfigureAwait(true), _ => "Recurring invoice created.");
                        case "Generate due":
                            int raised = selling
                                ? Check(await c.Get<Application.Abstractions.Trading.IRecurringSalesInvoiceService>().GenerateDueAsync(c.CompanyId, i.Date("Date")).ConfigureAwait(true))
                                : Check(await c.Get<Application.Abstractions.Trading.IRecurringPurchaseInvoiceService>().GenerateDueAsync(c.CompanyId, i.Date("Date")).ConfigureAwait(true));
                            return $"{raised} invoice(s) raised.";
                        default:
                            bool active = i.Text("Step") == "Restart";
                            long id = await TemplateId().ConfigureAwait(true);
                            return Check(selling
                                ? await c.Get<Application.Abstractions.Trading.IRecurringSalesInvoiceService>().SetActiveAsync(c.CompanyId, id, active).ConfigureAwait(true)
                                : await c.Get<Application.Abstractions.Trading.IRecurringPurchaseInvoiceService>().SetActiveAsync(c.CompanyId, id, active).ConfigureAwait(true), active ? "Restarted." : "Stopped.");
                    }
                },
                List = selling
                    ? async c => Table(await (from t in c.Db.Set<Core.Trading.RecurringSalesInvoice>().AsNoTracking()
                                              join x in c.Db.Customers.AsNoTracking() on t.CustomerId equals x.Id
                                              where t.CompanyId == c.CompanyId
                                              orderby t.Reference
                                              select new { Trader = x.Code, t.Reference, t.NetAmount, t.TaxAmount, Account = t.SalesAccountCode, t.TaxAccountCode, t.IntervalMonths, t.NextRunDate, t.Narrative, t.IsActive })
                            .ToListAsync().ConfigureAwait(true),
                        ("Step", _ => "Create"), ("Reference", t => t.Reference), ("Trader", t => t.Trader), ("Net", t => M(t.NetAmount)), ("Tax", t => M(t.TaxAmount)), ("Account", t => t.Account),
                        ("Tax account", t => t.TaxAccountCode), ("Interval", t => t.IntervalMonths), ("Next run", t => D(t.NextRunDate)), ("Narrative", t => t.Narrative), ("Active", t => t.IsActive))
                    : async c => Table(await (from t in c.Db.Set<Core.Trading.RecurringPurchaseInvoice>().AsNoTracking()
                                              join x in c.Db.Suppliers.AsNoTracking() on t.SupplierId equals x.Id
                                              where t.CompanyId == c.CompanyId
                                              orderby t.Reference
                                              select new { Trader = x.Code, t.Reference, t.NetAmount, t.TaxAmount, Account = t.ExpenseAccountCode, t.TaxAccountCode, t.IntervalMonths, t.NextRunDate, t.Narrative, t.IsActive })
                            .ToListAsync().ConfigureAwait(true),
                        ("Step", _ => "Create"), ("Reference", t => t.Reference), ("Trader", t => t.Trader), ("Net", t => M(t.NetAmount)), ("Tax", t => M(t.TaxAmount)), ("Account", t => t.Account),
                        ("Tax account", t => t.TaxAccountCode), ("Interval", t => t.IntervalMonths), ("Next run", t => D(t.NextRunDate)), ("Narrative", t => t.Narrative), ("Active", t => t.IsActive)),
            };
        }

        yield return new Operation
        {
            Module = suppliers, Title = "Remittance advice",
            Description = "The remittance advice for a supplier payment (by its document number), as text to print or send, listing the invoices it settled.",
            Fields = [Req("Payment", "Payment document number")],
            Run = async (c, i) =>
            {
                long payment = await FindAsync(c.Db.OpenItems, o => o.CompanyId == c.CompanyId && o.Ledger == Core.Trading.TradingLedger.AccountsPayable && o.DocumentNumber == i.Text("Payment"), o => o.Id,
                    "supplier payment", i.Text("Payment")).ConfigureAwait(true);
                return Check(await c.Get<Application.Abstractions.Trading.IRemittanceService>().RenderRemittanceTextAsync(c.CompanyId, payment).ConfigureAwait(true));
            },
        };
        yield return new Operation
        {
            Module = suppliers, Title = "Supplier bank details", ListTitle = "Changes awaiting approval",
            Description = "A change to a supplier's bank details (or mobile-money wallet) is requested by one user and approved (or rejected) by another; until then the supplier is held "
                + "out of payment runs. Payment priority (1 paid first … 9 last) decides who is paid first when a run's funds are short.",
            Fields = [Pick("Step", "Step", ["Request change", "Approve", "Reject", "Payment priority"]), Opt("Supplier", "Supplier code (request)"), Opt("AccountName", "Account name"),
                Opt("AccountNumber", "Account number"), Opt("Bank", "Bank"), Opt("Wallet", "Mobile-money wallet"), OptNum("Change", "Change id (approve / reject)"), Opt("Reason", "Reason (reject)"),
                Num("Priority", "Payment priority (1–9)", "5")],
            Run = async (c, i) =>
            {
                Application.Abstractions.Trading.ISupplierBankDetailsService bank = c.Get<Application.Abstractions.Trading.ISupplierBankDetailsService>();
                long change = (long)(i.OptionalNumber("Change") ?? 0m);
                async Task<string> SetPriorityAsync()
                {
                    int priority = (int)i.Number("Priority");
                    if (priority is < 1 or > 9)
                    {
                        throw new OperationException("Payment priority is 1 (paid first) to 9 (paid last).");
                    }

                    long supplierId = await SupplierAsync(c, i.Optional("Supplier") ?? string.Empty).ConfigureAwait(true);
                    Core.Trading.Supplier supplier = await c.Db.Suppliers.SingleAsync(s => s.Id == supplierId).ConfigureAwait(true);
                    supplier.PaymentPriority = priority;
                    await c.Db.SaveChangesAsync().ConfigureAwait(true);
                    return $"{supplier.Code} is now paid at priority {priority}.";
                }

                return i.Text("Step") switch
                {
                    "Request change" => Check(await bank.RequestChangeAsync(c.CompanyId, await SupplierAsync(c, i.Optional("Supplier") ?? string.Empty).ConfigureAwait(true),
                        new Application.Abstractions.Trading.SupplierBankDetails(i.Optional("AccountName"), i.Optional("AccountNumber"), i.Optional("Bank")) { MobileMoneyNumber = i.Optional("Wallet") },
                        c.UserId).ConfigureAwait(true),
                        id => $"Change {id} awaits another user's approval; the supplier is held out of payment runs meanwhile."),
                    "Approve" => Check(await bank.ApproveAsync(c.CompanyId, change, c.UserId).ConfigureAwait(true), "Bank details changed."),
                    "Payment priority" => await SetPriorityAsync().ConfigureAwait(true),
                    _ => Check(await bank.RejectAsync(c.CompanyId, change, c.UserId, i.Optional("Reason") ?? string.Empty).ConfigureAwait(true), "Change rejected."),
                };
            },
            List = async c => Table(await c.Get<Application.Abstractions.Trading.ISupplierBankDetailsService>().PendingAsync(c.CompanyId).ConfigureAwait(true),
                ("Change", x => x.ChangeId), ("Supplier", x => x.SupplierCode), ("Current account", x => x.Current.AccountNumber), ("Requested account", x => x.Requested.AccountNumber),
                ("Requested bank", x => x.Requested.BankName)),
        };
        yield return new Operation
        {
            Module = suppliers, Title = "Withholding tax returns",
            Description = "The return of tax withheld from supplier payments in a period (by certificate), its remittance to the revenue authority, or the text of one certificate.",
            Fields = [Pick("Step", "Step", ["Return", "Remit", "Certificate"]), Today("From", "From"), Today("To", "To"), Today("Date", "Remittance date"), Opt("Bank", "Bank account (remit)"),
                OptNum("Certificate", "Certificate id")],
            Run = async (c, i) =>
            {
                Application.Abstractions.Trading.IWithholdingTaxService wht = c.Get<Application.Abstractions.Trading.IWithholdingTaxService>();
                switch (i.Text("Step"))
                {
                    case "Return":
                        Application.Abstractions.Trading.WithholdingReturn ret = Check(await wht.ReturnAsync(c.CompanyId, i.Date("From"), i.Date("To")).ConfigureAwait(true));
                        return string.Join(Environment.NewLine, ret.Lines.Select(l => $"{l.CertificateNumber,-12} {l.SupplierCode,-10} {D(l.PaymentDate)} {l.TaxCode,-8} withheld {M(l.Withheld),12}"))
                            + Environment.NewLine + $"Withheld {M(ret.TotalWithheld)}; not yet remitted {M(ret.Unremitted)}.";
                    case "Remit":
                        Application.Abstractions.Trading.WithholdingRemittance remitted = Check(await wht.RemitAsync(c.CompanyId, i.Date("From"), i.Date("To"), i.Date("Date"), i.Optional("Bank") ?? string.Empty).ConfigureAwait(true));
                        return $"{remitted.Certificates} certificate(s), {M(remitted.Amount)} remitted{(remitted.JournalNumber is null ? string.Empty : $" ({remitted.JournalNumber})")}.";
                    default:
                        return Check(await wht.CertificateTextAsync(c.CompanyId, (long)(i.OptionalNumber("Certificate") ?? 0m)).ConfigureAwait(true));
                }
            },
        };
    }
}
