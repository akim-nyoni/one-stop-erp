using System.Data;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Accounting;
using One4All.Application.Abstractions.Backup;
using One4All.Application.Abstractions.Integration;
using One4All.Application.Abstractions.MasterData;
using One4All.Application.Abstractions.Onboarding;
using One4All.Application.Abstractions.Security;
using One4All.Application.MasterData;
using One4All.Core.Security;
using One4All.Core.Accounting;
using One4All.Core.Budgeting;
using One4All.Core.Inventory;
using One4All.Core.System;
using static One4All.Desktop.Operations.Ops;

namespace One4All.Desktop.Operations;

/// <content>System Manager operations: creating companies, the chart of accounts and the other master data.</content>
public static partial class OperationCatalogue
{
    private const string NoControl = "(not a control account)";

    private static IEnumerable<Operation> MasterData()
    {
        const string module = "System Manager";
        yield return new Operation
        {
            Module = module, Title = "Back up or restore the database", ListTitle = "Backups",
            Description = "Takes a backup now (pg_dump, SHA-256 recorded, copied off-site when configured), checks one, or restores a checked backup into a NEW database "
                + "(type RESTORE <name> to confirm) — the live database is never overwritten.",
            Fields = [Pick("Do", "Action", ["Back up now", "Check a backup", "Restore into a new database"]), OptNum("Backup", "Backup id (check / restore)"), Opt("Database", "New database name"),
                Opt("Confirm", "Confirmation (RESTORE name)")],
            Run = async (c, i) =>
            {
                IBackupService backups = c.Get<IBackupService>();
                switch (i.Text("Do"))
                {
                    case "Back up now":
                        Core.System.BackupRecord taken = Check(await backups.BackupAsync("manual").ConfigureAwait(true));
                        if (taken.Status == Core.System.BackupStatus.Failed)
                        {
                            return taken.Message ?? "Backup failed.";
                        }

                        Core.System.BackupStatus status = Check(await backups.VerifyAsync(taken.Id).ConfigureAwait(true));
                        return $"Backup {taken.Id}: {taken.FileName} ({taken.SizeBytes:N0} bytes), {status}.";
                    case "Check a backup":
                        return $"Backup is {Check(await backups.VerifyAsync((long)i.Number("Backup")).ConfigureAwait(true))}.";
                    default:
                        return Check(await backups.RestoreToNewDatabaseAsync((long)i.Number("Backup"), i.Text("Database"), i.Optional("Confirm") ?? string.Empty).ConfigureAwait(true));
                }
            },
            List = async c => Table(await c.Get<IBackupService>().ListAsync().ConfigureAwait(true),
                ("Id", b => b.Id), ("Started", b => b.StartedOnUtc.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)), ("Reason", b => b.Reason), ("Status", b => b.Status),
                ("Size", b => b.SizeBytes.ToString("N0", CultureInfo.InvariantCulture)), ("File", b => b.FileName), ("Off-site", b => b.OffsiteCopy), ("Message", b => b.Message)),
        };
        yield return new Operation
        {
            Module = module, Title = "Manage webhooks", ListTitle = "Webhook deliveries (latest 200)",
            Description = "Subscribe an endpoint to sale.completed, invoice.posted, payment.received, bill.generated or *; deliveries are signed (X-One4All-Signature: sha256=HMAC). "
                + "Give a subscription id to stop it, a failed delivery id to retry it, or tick Send now to dispatch what is due.",
            Fields = [Opt("Event", "Event (to subscribe)"), Opt("Url", "Endpoint URL"), Opt("Secret", "Signing secret (16+ characters)"), OptNum("Stop", "Subscription id to stop"),
                OptNum("Retry", "Failed delivery id to retry"), Tick("Send", "Send now")],
            Run = async (c, i) =>
            {
                IWebhookService hooks = c.Get<IWebhookService>();
                List<string> done = [];
                if (i.Optional("Event") is string evt)
                {
                    done.Add($"Subscription {Check(await hooks.SubscribeAsync(c.CompanyId, evt, i.Text("Url"), i.Text("Secret")).ConfigureAwait(true))} created.");
                }

                if (i.OptionalNumber("Stop") is decimal stop)
                {
                    done.Add(Check(await hooks.UnsubscribeAsync(c.CompanyId, (long)stop).ConfigureAwait(true), "Subscription stopped."));
                }

                if (i.OptionalNumber("Retry") is decimal retry)
                {
                    done.Add(Check(await hooks.RetryAsync(c.CompanyId, (long)retry).ConfigureAwait(true), "Delivery queued again."));
                }

                if (i.Flag("Send"))
                {
                    WebhookDispatchResult pass = await hooks.DispatchDueAsync(DateTimeOffset.UtcNow).ConfigureAwait(true);
                    done.Add($"{pass.Delivered} delivered, {pass.Retrying} to retry, {pass.Failed} given up.");
                }

                return done.Count == 0 ? "Nothing to do." : string.Join(" ", done);
            },
            List = async c => Table(await c.Db.Set<Core.Integration.WebhookDelivery>().AsNoTracking().Where(d => d.CompanyId == c.CompanyId).OrderByDescending(d => d.Id).Take(200).ToListAsync().ConfigureAwait(true),
                ("Id", d => d.Id), ("Subscription", d => d.SubscriptionId), ("Event", d => d.EventType), ("Status", d => d.Status), ("Attempts", d => d.Attempts),
                ("Next attempt", d => d.NextAttemptOnUtc.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)), ("Last status", d => d.LastStatusCode), ("Last error", d => d.LastError)),
        };
        yield return new Operation
        {
            Module = module, Title = "Review direct database changes", ListTitle = "Sensitive-table changes made outside One4All (last 30 days)",
            Description = "Database triggers record every change to users, roles, permissions, prices, tariffs, meters and the licence — including changes made with SQL tools, listed here.",
            Fields = [Num("Days", "Look back (days)", "30"), Tick("All", "Include the application's own changes")],
            Run = async (c, i) =>
            {
                IReadOnlyList<DatabaseChangeView> changes = await c.Get<IAuditService>().DatabaseChangesAsync(DateTimeOffset.UtcNow.AddDays(-i.Whole("Days")), DateTimeOffset.UtcNow, !i.Flag("All"))
                    .ConfigureAwait(true);
                return string.Join(Environment.NewLine, [$"{changes.Count} change(s).", .. changes.Take(50).Select(x => $"{x.OccurredOnUtc:yyyy-MM-dd HH:mm} {x.Operation} {x.TableName} #{x.RowId} by {x.DbUser} via {x.ApplicationName ?? "(unnamed)"} from {x.ClientAddress ?? "local"}")]);
            },
            List = async c => Table(await c.Get<IAuditService>().DatabaseChangesAsync(DateTimeOffset.UtcNow.AddDays(-30), DateTimeOffset.UtcNow, true).ConfigureAwait(true),
                ("When", x => x.OccurredOnUtc.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)), ("Operation", x => x.Operation), ("Table", x => x.TableName), ("Row", x => x.RowId),
                ("Database user", x => x.DbUser), ("Application", x => x.ApplicationName), ("Client", x => x.ClientAddress)),
        };
        yield return new Operation
        {
            Module = module, Title = "Attachments", ListTitle = "Recent attachments",
            Description = "Scans, contracts and delivery notes on a record (journal, customer, supplier, ledger document, purchase order, asset, employee, property, student, "
                + "contract): PDF, images, Word / Excel, CSV, text or e-mail up to 10 MB. Each file is checksummed and checked on opening; removing one keeps the file with the "
                + "reason, and files on posted journals and ledger documents cannot be removed.",
            Fields = [Pick("Step", "Step", ["List", "Attach", "Save a copy", "Remove"]), Pick("Target", "Record type", Enum.GetNames<AttachmentTarget>()), Opt("Key", "Record number / code"),
                Opt("File", "File path (attach) or folder (save a copy)"), Opt("Description", "Description / removal reason"), OptNum("Id", "Attachment id (save / remove)")],
            Run = async (c, i) =>
            {
                Application.Abstractions.System.IAttachmentService attachments = c.Get<Application.Abstractions.System.IAttachmentService>();
                AttachmentTarget target = Enum.Parse<AttachmentTarget>(i.Text("Target"));
                switch (i.Text("Step"))
                {
                    case "Attach":
                        string path = i.Optional("File") ?? throw new OperationException("Give the file path.");
                        long id = Check(await attachments.AttachAsync(c.CompanyId, target, i.Optional("Key") ?? string.Empty, System.IO.Path.GetFileName(path),
                            await System.IO.File.ReadAllBytesAsync(path).ConfigureAwait(true), c.UserId, i.Optional("Description")).ConfigureAwait(true));
                        return $"Attached as {id}.";
                    case "Save a copy":
                        Application.Abstractions.System.AttachmentFile file = Check(await attachments.OpenAsync(c.CompanyId, (long)i.Number("Id")).ConfigureAwait(true));
                        string to = System.IO.Path.Combine(i.Optional("File") ?? System.IO.Path.GetTempPath(), file.FileName);
                        await System.IO.File.WriteAllBytesAsync(to, file.Bytes).ConfigureAwait(true);
                        return $"Saved to {to} (checksum verified).";
                    case "Remove":
                        return Check(await attachments.RemoveAsync(c.CompanyId, (long)i.Number("Id"), c.UserId, i.Optional("Description") ?? string.Empty).ConfigureAwait(true), "Removed (the file is kept).");
                    default:
                        IReadOnlyList<Application.Abstractions.System.AttachmentInfo> list = await attachments.ListAsync(c.CompanyId, target, i.Optional("Key") ?? string.Empty).ConfigureAwait(true);
                        return list.Count == 0 ? "No attachments." : string.Join(Environment.NewLine, list.Select(a => $"{a.Id,6}  {a.FileName}  {a.SizeBytes / 1024} KB  {a.UploadedOnUtc:yyyy-MM-dd}  {a.Description}"));
                }
            },
            List = async c => Table(await c.Db.Set<Attachment>().AsNoTracking().Where(a => a.CompanyId == c.CompanyId && a.RemovedOnUtc == null).OrderByDescending(a => a.Id).Take(200).ToListAsync()
                    .ConfigureAwait(true), ("Id", a => a.Id), ("Record", a => a.Target), ("Number", a => a.TargetKey), ("File", a => a.FileName), ("KB", a => a.SizeBytes / 1024),
                ("Uploaded", a => a.UploadedOnUtc.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)), ("Description", a => a.Description)),
        };
        string[] imports = ["Accounts", "Warehouses", "Customers", "Suppliers", "Items", "Stock", "Receivables", "Payables", "TrialBalance", "FixedAssets", "Employees", "StaffLoans", "PayrollYtd",
            "Properties", "Meters", "Students", "Stands", "RentalUnits", "Budget"];
        yield return new Operation
        {
            Module = module, Title = "Import master data or opening balances", ListTitle = "Opening balance reconciliation",
            Description = "Loads a CSV template (header row; dates yyyy-MM-dd). Untick Commit to preview: nothing is written while any row has an error. "
                + "Accounts: code,name,type[,category,manual,budget]. Warehouses: code,name. "
                + "RentalUnits: code,description,type,rooms,rent,rent_account[,vacant_since,suburb,max_occupants] (no vacant date: let). "
                + "Customers/Suppliers: code,name,currency[,credit_limit]. Items: code,description[,type,revenue_account,cost_account,category]. Stock: item,warehouse,quantity,unit_cost. "
                + "Receivables/Payables: trader,document,date,amount (negative = credit). TrialBalance: account,debit,credit. "
                + "FixedAssets: asset_number,description,class,acquired,cost,accumulated,life_months,cost_account,accum_account,expense_account[,residual,method,rate]. "
                + "Employees: employee_number,name,basic_salary,engaged[,national_id,department,cost_centre,grade]. StaffLoans: employee_number,reference,outstanding,instalment,loan_account. "
                + "PayrollYtd: employee_number,tax_year,code,amount (TAXABLE for taxable pay so far, or a pay element such as PAYE for tax already deducted). "
                + "Properties: stand,ward,ratepayer[,valuation,size,connected,services e.g. Water:WAT1;Refuse:REF1]. "
                + "Meters: serial,stand,service,reading,date[,digits,multiplier]. Students: admission,name,grade,guardian[,boarder,admitted]. "
                + "Stands: stand,parcel,size[,cost,scheme,status] (a parcel not yet registered is created from its stands). Budget: account,period,amount[,cost_centre,quantity] for the year of the date. "
                + "The list also shows the fixed asset and staff loan registers against their accounts.",
            Fields = [Pick("Kind", "What", imports), Req("File", "CSV or Excel (.xlsx, first sheet) file path"), Today("Date", "Opening date"), Req("Suspense", "Migration suspense account", "2900"),
                Opt("Label", "Budget label (budget)"), Pick("BudgetType", "Budget type (budget)", Enum.GetNames<BudgetType>()), Opt("Department", "Budget department (budget; blank: whole company)"),
                Tick("Commit", "Commit")],
            Run = async (c, i) =>
            {
                byte[] file = await System.IO.File.ReadAllBytesAsync(i.Text("File")).ConfigureAwait(true);
                string csv = Application.Onboarding.XlsxReader.IsWorkbook(file) ? Application.Onboarding.XlsxReader.ToCsv(file) : System.Text.Encoding.UTF8.GetString(file);
                IOpeningBalanceService ob = c.Get<IOpeningBalanceService>();
                IVerticalImportService registers = c.Get<IVerticalImportService>();
                bool commit = i.Flag("Commit");
                ImportReport report = i.Text("Kind") switch
                {
                    "Accounts" => await registers.ImportAccountsAsync(c.CompanyId, csv, commit).ConfigureAwait(true),
                    "Warehouses" => await registers.ImportWarehousesAsync(c.CompanyId, csv, commit).ConfigureAwait(true),
                    "RentalUnits" => await registers.ImportRentalUnitsAsync(c.CompanyId, csv, i.Date("Date"), commit).ConfigureAwait(true),
                    "Customers" => await ob.ImportCustomersAsync(c.CompanyId, csv, commit).ConfigureAwait(true),
                    "Suppliers" => await ob.ImportSuppliersAsync(c.CompanyId, csv, commit).ConfigureAwait(true),
                    "Items" => await ob.ImportItemsAsync(c.CompanyId, csv, commit).ConfigureAwait(true),
                    "Stock" => await ob.ImportStockAsync(c.CompanyId, csv, i.Date("Date"), i.Text("Suspense"), commit).ConfigureAwait(true),
                    "Receivables" => await ob.ImportOpenItemsAsync(c.CompanyId, Core.Trading.TradingLedger.AccountsReceivable, csv, i.Text("Suspense"), commit).ConfigureAwait(true),
                    "Payables" => await ob.ImportOpenItemsAsync(c.CompanyId, Core.Trading.TradingLedger.AccountsPayable, csv, i.Text("Suspense"), commit).ConfigureAwait(true),
                    "FixedAssets" => await registers.ImportFixedAssetsAsync(c.CompanyId, csv, i.Date("Date"), i.Text("Suspense"), commit).ConfigureAwait(true),
                    "Employees" => await registers.ImportEmployeesAsync(c.CompanyId, csv, commit).ConfigureAwait(true),
                    "StaffLoans" => await registers.ImportEmployeeLoansAsync(c.CompanyId, csv, i.Date("Date"), i.Text("Suspense"), commit).ConfigureAwait(true),
                    "PayrollYtd" => await registers.ImportPayrollYtdAsync(c.CompanyId, csv, commit).ConfigureAwait(true),
                    "Properties" => await registers.ImportPropertiesAsync(c.CompanyId, csv, commit).ConfigureAwait(true),
                    "Meters" => await registers.ImportMetersAsync(c.CompanyId, csv, commit).ConfigureAwait(true),
                    "Students" => await registers.ImportStudentsAsync(c.CompanyId, csv, commit).ConfigureAwait(true),
                    "Stands" => await registers.ImportStandsAsync(c.CompanyId, csv, commit).ConfigureAwait(true),
                    "Budget" => await registers.ImportBudgetAsync(c.CompanyId, csv, i.Date("Date"), i.Optional("Label") ?? string.Empty, Enum.Parse<BudgetType>(i.Text("BudgetType")),
                        i.Optional("Department"), commit).ConfigureAwait(true),
                    _ => await ob.ImportTrialBalanceAsync(c.CompanyId, csv, i.Date("Date"), i.Text("Suspense"), commit).ConfigureAwait(true),
                };
                string head = $"{report.Rows} row(s): {(report.Committed ? $"{report.Created} written" : "nothing written")}, {report.Skipped} already there, {report.Errors.Count} error(s).";
                return report.Errors.Count == 0 ? head
                    : head + Environment.NewLine + string.Join(Environment.NewLine, report.Errors.Take(30).Select(e => e.LineNumber > 0 ? $"Line {e.LineNumber}: {e.Message}" : e.Message));
            },
            List = OpeningReconciliationAsync,
        };
        yield return new Operation
        {
            Module = module, Title = "Set up a company for an industry", ListTitle = "Companies",
            Description = "The Industry Setup Wizard: a new company with the chart of accounts, tax codes, warehouses, an open fiscal year and starter roles for the industries chosen (they add up).",
            Fields = [Req("Code", "Company code"), Req("Name", "Name"), Req("Currency", "Base currency", "USD"), Req("Industries", "Industries (" + string.Join(", ", Enum.GetNames<Vertical>()) + ")", "Retail"),
                Today("Start", "Fiscal year starts"), Num("Vat", "VAT rate %", "15"), Tick("Demo", "Add demonstration data")],
            Run = async (c, i) =>
            {
                List<Vertical> verticals = [];
                foreach (string name in i.Text("Industries").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    verticals.Add(Enum.TryParse(name, true, out Vertical v) ? v : throw new OperationException($"Unknown industry {name}."));
                }

                IndustrySetupResult done = Check(await c.Get<IIndustrySetupService>().SetUpCompanyAsync(new IndustrySetupRequest
                {
                    Code = i.Text("Code"), Name = i.Text("Name"), BaseCurrency = CurrencyCode.Parse(i.Text("Currency")), Verticals = verticals, FiscalYearStart = i.Date("Start"),
                    VatRatePercent = i.Number("Vat"), WithDemoData = i.Flag("Demo"),
                }).ConfigureAwait(true));
                return $"Company {done.CompanyId} set up: {done.Accounts} accounts, {done.TaxCodes} tax codes, {done.Warehouses} warehouses, {done.Roles} new roles, {done.DemoRecords} demo records, "
                    + $"{done.ItemCategories} item categories, {done.Terminals} POS terminal(s), {done.FeaturedReports} featured reports.";
            },
            List = async c => Table(await c.Db.Companies.AsNoTracking().OrderBy(x => x.Code).ToListAsync().ConfigureAwait(true),
                ("Id", x => x.Id), ("Code", x => x.Code), ("Name", x => x.Name), ("Base currency", x => x.BaseCurrency.Value)),
        };
        yield return new Operation
        {
            Module = module, Title = "Add an industry to this company", ListTitle = "Chart of accounts",
            Description = "Adds an industry's accounts, taxes, warehouses, roles, item categories, POS terminal and report set to the current company, keeping everything already there.",
            Fields = [Pick("Industry", "Industry", Enum.GetNames<Vertical>()), Num("Vat", "VAT rate %", "15"), Tick("Demo", "Add demonstration data")],
            Run = async (c, i) =>
            {
                IndustrySetupResult done = Check(await c.Get<IIndustrySetupService>().AddVerticalAsync(c.CompanyId, Enum.Parse<Vertical>(i.Text("Industry")), i.Number("Vat"), i.Flag("Demo"))
                    .ConfigureAwait(true));
                return $"Added {done.Accounts} accounts, {done.TaxCodes} tax codes, {done.Warehouses} warehouses, {done.Roles} roles, {done.DemoRecords} demo records, "
                    + $"{done.ItemCategories} item categories, {done.Terminals} POS terminal(s), {done.FeaturedReports} featured reports.";
            },
            List = async c => Table(await c.Db.Accounts.AsNoTracking().Where(x => x.CompanyId == c.CompanyId).OrderBy(x => x.AccountCode).ToListAsync().ConfigureAwait(true),
                ("Code", x => x.AccountCode), ("Name", x => x.Name), ("Type", x => x.Type), ("Control", x => x.ControlAccountType)),
        };
        yield return new Operation
        {
            Module = module, Title = "Create a company", ListTitle = "Companies", Description = "A new company with a head-office branch, held to the licence's company limit.",
            Fields = [Req("Code", "Company code"), Req("Name", "Name"), Req("Currency", "Base currency", "USD"), Req("TimeZone", "Time zone", "UTC")],
            Run = async (c, i) => Check(await c.Get<IMasterDataService>().CreateCompanyAsync(i.Text("Code"), i.Text("Name"), CurrencyCode.Parse(i.Text("Currency")), i.Text("TimeZone"))
                .ConfigureAwait(true), id => $"Company {id} created."),
            List = async c => Table(await c.Db.Companies.AsNoTracking().OrderBy(x => x.Code).ToListAsync().ConfigureAwait(true),
                ("Id", x => x.Id), ("Code", x => x.Code), ("Name", x => x.Name), ("Base currency", x => x.BaseCurrency.Value), ("Time zone", x => x.TimeZoneId)),
        };
        yield return new Operation
        {
            Module = module, Title = "Add a branch", ListTitle = "Branches", Description = "A branch or site of the current company.",
            Fields = [Req("Code", "Code"), Req("Name", "Name")],
            Run = async (c, i) => Check(await c.Get<IMasterDataService>().CreateBranchAsync(c.CompanyId, i.Text("Code"), i.Text("Name")).ConfigureAwait(true), id => "Branch added."),
            List = async c => Table(await c.Db.Branches.AsNoTracking().Where(x => x.CompanyId == c.CompanyId).OrderBy(x => x.Code).ToListAsync().ConfigureAwait(true),
                ("Code", x => x.Code), ("Name", x => x.Name), ("Head office", x => x.IsHeadOffice)),
        };
        yield return new Operation
        {
            Module = module, Title = "Burst a report by email", ListTitle = "Report subscriptions",
            Description = "Run a report once, split it by a field (e.g. Customer code) and email each part as its own PDF — to the address in a field of the rows, or to the contacts of the "
                + "customer or supplier the key names. Parameters as Name=value; ... (dates as yyyy-MM-dd). Parts with nobody to send to are listed.",
            Fields = [Req("Report", "Report code"), Req("Key", "Split by field"), Opt("EmailField", "Field holding the email (optional)"), Pick("Ledger", "Or send to the contacts of", ["(email field)", "Customers", "Suppliers"]),
                Opt("Parameters", "Parameters")],
            Run = async (c, i) =>
            {
                Dictionary<string, object?> parameters = [];
                foreach (string pair in (i.Optional("Parameters") ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    string[] parts = pair.Split('=', 2, StringSplitOptions.TrimEntries);
                    parameters[parts[0]] = parts.Length < 2 ? null : DateOnly.TryParseExact(parts[1], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly date) ? date : parts[1];
                }

                Core.Trading.TradingLedger? ledger = i.Text("Ledger") switch
                {
                    "Customers" => Core.Trading.TradingLedger.AccountsReceivable,
                    "Suppliers" => Core.Trading.TradingLedger.AccountsPayable,
                    _ => null,
                };
                IReadOnlyList<Application.Abstractions.Reporting.BurstDelivery> sent = Check(await c.Get<Application.Abstractions.Reporting.IReportBurstService>().BurstAsync(
                    new Application.Abstractions.Reporting.BurstRequest(c.CompanyId, i.Text("Report"), i.Text("Key"))
                    {
                        Parameters = parameters, RecipientField = i.Optional("EmailField"), Ledger = ledger, UserId = c.UserId,
                    }).ConfigureAwait(true));
                List<string> missing = [.. sent.Where(d => d.Recipients.Count == 0).Select(d => d.Key)];
                return $"{sent.Count(d => d.Recipients.Count > 0)} part(s) queued by email."
                    + (missing.Count == 0 ? string.Empty : $" No email address for: {string.Join(", ", missing.Take(20))}{(missing.Count > 20 ? " ..." : string.Empty)}.");
            },
            List = async c => Table(await c.Db.Set<Core.Reporting.ReportSubscription>().AsNoTracking().Where(x => x.CompanyId == c.CompanyId).OrderBy(x => x.Code).ToListAsync().ConfigureAwait(true),
                ("Code", x => x.Code), ("Report", x => x.ReportCode), ("Burst by", x => x.BurstKeyField ?? "—"), ("Recipients", x => x.BurstKeyField is null ? string.Join(", ", x.Recipients) : x.BurstRecipientField ?? (x.BurstLedger == 2 ? "supplier contacts" : "customer contacts")),
                ("Active", x => x.IsActive)),
        };
        yield return new Operation
        {
            Module = module, Title = "Report subscription", ListTitle = "Report subscriptions",
            Description = "A report emailed every time the report-subscriptions job runs: to a list of addresses, or burst by a field to each customer's or supplier's contacts (or the address in "
                + "a field). Parameters may use date tokens (@Today, @MonthStart, @PrevMonthEnd ...).",
            Fields = [Req("Code", "Subscription code"), Req("Report", "Report code"), Pick("Format", "Format", ["Pdf", "Xlsx", "Csv", "Html"]), Opt("Recipients", "Email addresses (; separated)"),
                Opt("Key", "Burst by field (optional)"), Opt("EmailField", "Field holding the email (burst)"), Pick("Ledger", "Or burst to the contacts of", ["(email field)", "Customers", "Suppliers"]),
                Opt("Parameters", "Parameters (Name=value; ...)")],
            Run = async (c, i) =>
            {
                Dictionary<string, string> parameters = [];
                foreach (string pair in (i.Optional("Parameters") ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    string[] parts = pair.Split('=', 2, StringSplitOptions.TrimEntries);
                    parameters[parts[0]] = parts.Length < 2 ? string.Empty : parts[1];
                }

                _ = Check(await c.Get<Application.Abstractions.Reporting.IReportSubscriptionService>().SubscribeAsync(new Application.Abstractions.Reporting.SubscriptionRequest
                {
                    CompanyId = c.CompanyId, Code = i.Text("Code"), ReportCode = i.Text("Report"), Format = Enum.Parse<Application.Abstractions.Reporting.ReportFormat>(i.Text("Format")),
                    Recipients = [.. (i.Optional("Recipients") ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)], Parameters = parameters,
                    BurstKeyField = i.Optional("Key"), BurstRecipientField = i.Optional("EmailField"),
                    BurstLedger = i.Text("Ledger") switch { "Customers" => Core.Trading.TradingLedger.AccountsReceivable, "Suppliers" => Core.Trading.TradingLedger.AccountsPayable, _ => null },
                }).ConfigureAwait(true), string.Empty);
                return $"Subscription {i.Text("Code").ToUpperInvariant()} created.";
            },
            List = async c => Table(await c.Db.Set<Core.Reporting.ReportSubscription>().AsNoTracking().Where(x => x.CompanyId == c.CompanyId).OrderBy(x => x.Code).ToListAsync().ConfigureAwait(true),
                ("Code", x => x.Code), ("Report", x => x.ReportCode), ("Burst by", x => x.BurstKeyField ?? "—"), ("Active", x => x.IsActive)),
        };
        yield return new Operation
        {
            Module = module, Title = "Set the company logo", ListTitle = "Company",
            Description = "A PNG or JPEG (up to 512 KB) that report layouts show wherever they place the Logo (an image element with expression CompanyLogo). Leave the path blank to remove it.",
            Fields = [Opt("Path", "Picture file path")],
            Run = async (c, i) =>
            {
                Company company = await c.Db.Companies.SingleAsync(x => x.Id == c.CompanyId).ConfigureAwait(true);
                if (i.Optional("Path") is not string path)
                {
                    company.Logo = null;
                    await c.Db.SaveChangesAsync().ConfigureAwait(true);
                    return "Logo removed.";
                }

                byte[] bytes = await System.IO.File.ReadAllBytesAsync(path.Trim('"')).ConfigureAwait(true);
                bool png = bytes.Length > 4 && bytes[0] == 0x89 && bytes[1] == 0x50, jpeg = bytes.Length > 3 && bytes[0] == 0xFF && bytes[1] == 0xD8;
                if (!png && !jpeg || bytes.Length > 512 * 1024)
                {
                    throw new OperationException("The logo must be a PNG or JPEG of at most 512 KB.");
                }

                company.Logo = bytes;
                await c.Db.SaveChangesAsync().ConfigureAwait(true);
                return $"Logo set ({bytes.Length / 1024} KB).";
            },
            List = async c => Table(await c.Db.Companies.AsNoTracking().Where(x => x.Id == c.CompanyId).Select(x => new { x.Name, Logo = x.Logo != null }).ToListAsync().ConfigureAwait(true),
                ("Company", x => x.Name), ("Logo", x => x.Logo ? "set" : "none")),
        };
        yield return new Operation
        {
            Module = module, Title = "Set the reporting currency", ListTitle = "Company currencies",
            Description = "A second currency every posting is also kept in (e.g. USD beside ZWG), each at the base→reporting rate of its day, so statements can be drawn in it "
                + "(General Ledger report GL2R). It applies to postings from now on — posted journals cannot change — so set it before go-live. Leave blank to stop keeping it.",
            Fields = [Opt("Currency", "Reporting currency (blank: none)")],
            Run = async (c, i) =>
            {
                Company company = await c.Db.Companies.SingleAsync(x => x.Id == c.CompanyId).ConfigureAwait(true);
                CurrencyCode? currency = i.Optional("Currency") is string code ? CurrencyCode.Parse(code) : null;
                company.SetReportingCurrency(currency);
                await c.Db.SaveChangesAsync().ConfigureAwait(true);
                return currency is CurrencyCode set ? $"Postings are now also kept in {set.Value}." : "No reporting currency is kept.";
            },
            List = async c => Table(await c.Db.Companies.AsNoTracking().Where(x => x.Id == c.CompanyId).ToListAsync().ConfigureAwait(true),
                ("Company", x => x.Name), ("Base", x => x.BaseCurrency.Value), ("Reporting", x => x.ReportingCurrency?.Value ?? "—")),
        };
        yield return new Operation
        {
            Module = module, Title = "Create a fiscal year", ListTitle = "Fiscal years",
            Description = "Monthly periods from the start date (any month — e.g. July to June), or a 4-4-5 week calendar; optionally an adjustment (13th) period on the last day for audit "
                + "and year-end adjustments. Tick to open the periods for every ledger at once.",
            Fields = [Req("Code", "Year code"), Today("Start", "First day"), Num("Months", "Periods (monthly)", "12"), Pick("Calendar", "Calendar", ["Monthly", "4-4-5 weeks"]),
                Tick("Adjust", "Add an adjustment period"), Tick("Open", "Open all periods now")],
            Run = async (c, i) =>
            {
                long year = Check(await c.Get<IMasterDataService>().CreateFiscalYearAsync(c.CompanyId, i.Text("Code"), i.Date("Start"),
                    i.Text("Calendar") == "Monthly" ? FiscalCalendar.Monthly : FiscalCalendar.FourFourFive, i.Whole("Months"), i.Flag("Adjust")).ConfigureAwait(true));
                if (i.Flag("Open"))
                {
                    _ = Check(await c.Get<IPeriodService>().OpenYearAsync(c.CompanyId, year, ["GL", "AR", "AP", "CB", "INV", "POP", "SOP", "POS", "VOU", "FA", "PAY", "JOB", "MUNI", "HSE", "SCH", "BUD"])
                        .ConfigureAwait(true), string.Empty);
                }

                return $"Fiscal year {i.Text("Code").ToUpperInvariant()} created{(i.Flag("Open") ? " and opened" : string.Empty)}.";
            },
            List = async c => Table(await c.Db.FiscalYears.AsNoTracking().Where(x => x.CompanyId == c.CompanyId).OrderBy(x => x.StartDate).ToListAsync().ConfigureAwait(true),
                ("Id", x => x.Id), ("Code", x => x.Code), ("From", x => D(x.StartDate)), ("To", x => D(x.EndDate)), ("Closed", x => x.IsClosed), ("Hard-locked", x => x.IsHardLocked)),
        };
        yield return new Operation
        {
            Module = module, Title = "Add a ledger account", ListTitle = "Chart of accounts", Description = "A ledger account; a control account is posted only by its own ledger.",
            Fields = [Req("Code", "Account code (up to 8)"), Req("Name", "Name"), Pick("Type", "Type", Enum.GetNames<AccountType>()),
                Pick("Control", "Control account", [NoControl, .. Enum.GetNames<ControlAccountType>()]), Opt("Category", "Report category")],
            Run = async (c, i) => Check(await c.Get<IMasterDataService>().CreateAccountAsync(new NewAccount(c.CompanyId, i.Text("Code"), i.Text("Name"), Enum.Parse<AccountType>(i.Text("Type")),
                i.Text("Control") == NoControl ? null : Enum.Parse<ControlAccountType>(i.Text("Control")), i.Optional("Category"))).ConfigureAwait(true), id => "Account added."),
            List = async c => Table(await c.Db.Accounts.AsNoTracking().Where(x => x.CompanyId == c.CompanyId).OrderBy(x => x.AccountCode).ToListAsync().ConfigureAwait(true),
                ("Code", x => x.AccountCode), ("Name", x => x.Name), ("Type", x => x.Type), ("Control", x => x.ControlAccountType), ("Active", x => x.IsActive)),
        };
        yield return new Operation
        {
            Module = module, Title = "Add a tax code", ListTitle = "Tax codes", Description = "A tax code with its first rate and the accounts it posts to.",
            Fields = [Req("Code", "Code"), Req("Name", "Name"), Pick("Type", "Type", Enum.GetNames<TaxType>()), Num("Rate", "Rate %"), Today("From", "Rate from"),
                Opt("Output", "Output / payable account"), Opt("Input", "Input account")],
            Run = async (c, i) => Check(await c.Get<IMasterDataService>().CreateTaxCodeAsync(new NewTaxCode(c.CompanyId, i.Text("Code"), i.Text("Name"), Enum.Parse<TaxType>(i.Text("Type")),
                i.Number("Rate"), i.Date("From"), i.Optional("Output"), i.Optional("Input"))).ConfigureAwait(true), id => "Tax code added."),
            List = async c => Table(await c.Db.TaxCodes.AsNoTracking().Where(x => x.CompanyId == c.CompanyId).OrderBy(x => x.Code).ToListAsync().ConfigureAwait(true),
                ("Code", x => x.Code), ("Name", x => x.Name), ("Type", x => x.Type), ("Output", x => x.OutputAccountCode), ("Input", x => x.InputAccountCode), ("Active", x => x.IsActive)),
        };
        yield return new Operation
        {
            Module = module, Title = "Add a currency", ListTitle = "Currencies", Description = "An ISO currency the system may trade or hold balances in.",
            Fields = [Req("Code", "ISO code"), Req("Name", "Name"), Opt("Symbol", "Symbol"), Num("Places", "Decimal places", "2")],
            Run = async (c, i) => Check(await c.Get<IMasterDataService>().CreateCurrencyAsync(CurrencyCode.Parse(i.Text("Code")), i.Text("Name"), i.Optional("Symbol") ?? string.Empty, i.Whole("Places"))
                .ConfigureAwait(true), "Currency added."),
            List = async c => Table(await c.Db.Currencies.AsNoTracking().ToListAsync().ConfigureAwait(true), ("Code", x => x.Code.Value), ("Name", x => x.Name), ("Symbol", x => x.Symbol),
                ("Places", x => x.DecimalPlaces)),
        };
        yield return new Operation
        {
            Module = module, Title = "Set an exchange rate", ListTitle = "Exchange rates (latest 200)", Description = "A rate from a date; one already set for that day and type is replaced.",
            Fields = [Req("From", "From currency"), Req("To", "To currency"), Pick("Type", "Rate type", Enum.GetNames<ExchangeRateType>()), Today(), Num("Rate", "Rate")],
            Run = async (c, i) => Check(await c.Get<IMasterDataService>().SetExchangeRateAsync(CurrencyCode.Parse(i.Text("From")), CurrencyCode.Parse(i.Text("To")),
                Enum.Parse<ExchangeRateType>(i.Text("Type")), i.Date("Date"), i.Number("Rate")).ConfigureAwait(true), "Rate set."),
            List = async c => Table(await c.Db.ExchangeRates.AsNoTracking().OrderByDescending(x => x.EffectiveFrom).Take(200).ToListAsync().ConfigureAwait(true),
                ("From", x => x.FromCurrency.Value), ("To", x => x.ToCurrency.Value), ("Type", x => x.RateType), ("Effective", x => D(x.EffectiveFrom)), ("Rate", x => x.Rate.ToString("0.######", CultureInfo.InvariantCulture))),
        };
        yield return new Operation
        {
            Module = module, Title = "Add a warehouse", ListTitle = "Warehouses", Description = "A stock location of the current company.",
            Fields = [Req("Code", "Code"), Req("Name", "Name")],
            Run = async (c, i) => Check(await c.Get<IMasterDataService>().CreateWarehouseAsync(c.CompanyId, i.Text("Code"), i.Text("Name")).ConfigureAwait(true), id => "Warehouse added."),
            List = async c => Table(await c.Db.Warehouses.AsNoTracking().Where(x => x.CompanyId == c.CompanyId).OrderBy(x => x.Code).ToListAsync().ConfigureAwait(true),
                ("Code", x => x.Code), ("Name", x => x.Name), ("Active", x => x.IsActive)),
        };
        yield return new Operation
        {
            Module = module, Title = "Add a cost centre or department", ListTitle = "Cost centres and departments", Description = "GL analysis codes of up to three characters.",
            Fields = [Pick("Kind", "Kind", ["Cost centre", "Department"]), Req("Code", "Code (up to 3)"), Req("Name", "Name")],
            Run = async (c, i) => Check(i.Text("Kind") == "Department"
                ? await c.Get<IMasterDataService>().CreateDepartmentAsync(c.CompanyId, i.Text("Code"), i.Text("Name")).ConfigureAwait(true)
                : await c.Get<IMasterDataService>().CreateCostCentreAsync(c.CompanyId, i.Text("Code"), i.Text("Name")).ConfigureAwait(true), id => $"{i.Text("Kind")} added."),
            List = async c =>
            {
                var centres = (await c.Db.CostCentres.AsNoTracking().Where(x => x.CompanyId == c.CompanyId).ToListAsync().ConfigureAwait(true)).Select(x => (Kind: "Cost centre", x.Code, x.Name));
                var departments = (await c.Db.Departments.AsNoTracking().Where(x => x.CompanyId == c.CompanyId).ToListAsync().ConfigureAwait(true)).Select(x => (Kind: "Department", x.Code, x.Name));
                return Table(centres.Concat(departments).OrderBy(x => x.Kind).ThenBy(x => x.Code), ("Kind", x => x.Kind), ("Code", x => x.Code), ("Name", x => x.Name));
            },
        };
        yield return new Operation
        {
            Module = module, Title = "Add a customer", ListTitle = "Customers", Description = "A customer account; raise the credit limit later through approval.",
            Fields = [Req("Code", "Account code"), Req("Name", "Name"), Req("Currency", "Currency", "USD"), Num("Limit", "Credit limit", "0")],
            Run = async (c, i) => Check(await c.Get<IMasterDataService>().CreateCustomerAsync(c.CompanyId, i.Text("Code"), i.Text("Name"), CurrencyCode.Parse(i.Text("Currency")), i.Number("Limit"))
                .ConfigureAwait(true), id => "Customer added."),
            List = async c => Table(await c.Db.Customers.AsNoTracking().Where(x => x.CompanyId == c.CompanyId).OrderBy(x => x.Code).ToListAsync().ConfigureAwait(true),
                ("Code", x => x.Code), ("Name", x => x.Name), ("Currency", x => x.Currency.Value), ("Credit limit", x => M(x.CreditLimit))),
        };
        yield return new Operation
        {
            Module = module, Title = "Add a supplier", ListTitle = "Suppliers", Description = "A supplier account.",
            Fields = [Req("Code", "Account code"), Req("Name", "Name"), Req("Currency", "Currency", "USD")],
            Run = async (c, i) => Check(await c.Get<IMasterDataService>().CreateSupplierAsync(c.CompanyId, i.Text("Code"), i.Text("Name"), CurrencyCode.Parse(i.Text("Currency"))).ConfigureAwait(true),
                id => "Supplier added."),
            List = async c => Table(await c.Db.Suppliers.AsNoTracking().Where(x => x.CompanyId == c.CompanyId).OrderBy(x => x.Code).ToListAsync().ConfigureAwait(true),
                ("Code", x => x.Code), ("Name", x => x.Name), ("Currency", x => x.Currency.Value)),
        };
        yield return new Operation
        {
            Module = module, Title = "Add an item", ListTitle = "Items", Description = "A stock, service or other item with the accounts its sales and cost post to.",
            Fields = [Req("Code", "Item code"), Req("Description", "Description"), Pick("Type", "Type", Enum.GetNames<ItemType>()), Opt("Revenue", "Revenue account"), Opt("Cost", "Cost of sales account"),
                OptNum("Standard", "Standard cost")],
            Run = async (c, i) => Check(await c.Get<IMasterDataService>().CreateItemAsync(new NewItem(c.CompanyId, i.Text("Code"), i.Text("Description"), Enum.Parse<ItemType>(i.Text("Type")),
                i.Optional("Revenue"), i.Optional("Cost"), i.OptionalNumber("Standard"))).ConfigureAwait(true), id => "Item added."),
            List = async c => Table(await c.Db.Items.AsNoTracking().Where(x => x.CompanyId == c.CompanyId).OrderBy(x => x.Code).Take(500).ToListAsync().ConfigureAwait(true),
                ("Code", x => x.Code), ("Description", x => x.Description), ("Type", x => x.ItemType), ("Revenue", x => x.RevenueAccountCode), ("Cost of sales", x => x.CostOfSalesAccountCode)),
        };
        yield return new Operation
        {
            Module = module, Title = "Create a user", ListTitle = "Users and their roles",
            Description = "A login who must change the initial password at first sign-in (8+ characters, letters and digits, not the user name).",
            Fields = [Req("Username", "Login name"), Req("Name", "Display name"), Opt("Email", "E-mail"), Req("Password", "Initial password")],
            Run = async (c, i) => Check(await c.Get<IUserAdminService>().CreateUserAsync(i.Text("Username"), i.Text("Name"), i.Optional("Email"), i.Text("Password")).ConfigureAwait(true),
                id => $"User {i.Text("Username")} created."),
            List = UsersAsync,
        };
        yield return new Operation
        {
            Module = module, Title = "API keys for integrations", ListTitle = "API clients",
            Description = "An integration signs in with the X-Api-Key header and acts with the permissions of the service user named (create one with only what it needs). "
                + "The key is shown once — copy it then; rotating issues a new one and the old stops working; revoking stops the client.",
            Fields = [Pick("Step", "Step", ["Create", "Rotate", "Revoke"]), Opt("Name", "Client name (create)"), Opt("User", "Service user it acts as (create)"),
                new("Expires", "Expires on (create)", FieldKind.Date, Required: false), OptNum("Rate", "Requests per minute (create; blank: the server's)"), Opt("Client", "Client id (rotate / revoke)")],
            Run = async (c, i) =>
            {
                IApiClientService clients = c.Get<IApiClientService>();
                string client = i.Optional("Client") ?? string.Empty;
                return i.Text("Step") switch
                {
                    "Create" => Check(await clients.CreateAsync(i.Optional("Name") ?? string.Empty, i.Optional("User") ?? string.Empty, i.OptionalDate("Expires"),
                        i.OptionalNumber("Rate") is decimal rate ? (int)rate : null).ConfigureAwait(true), k => $"Client {k.ClientId} created. Its API key (shown only now): {k.ApiKey}"),
                    "Rotate" => Check(await clients.RotateAsync(client).ConfigureAwait(true), k => $"New API key for {k.ClientId} (shown only now): {k.ApiKey}"),
                    _ => Check(await clients.RevokeAsync(client).ConfigureAwait(true), $"Client {client} revoked."),
                };
            },
            List = async c => Table(await c.Get<IApiClientService>().ListAsync().ConfigureAwait(true), ("Client", r => r.ClientId), ("Name", r => r.Name), ("Acts as", r => r.ActsAs),
                ("Active", r => r.IsActive), ("Expires", r => r.ExpiresOn is DateOnly d ? D(d) : "—"), ("Rate/min", r => r.RequestsPerMinute?.ToString(CultureInfo.CurrentCulture) ?? "server"),
                ("Last used", r => r.LastUsedOnUtc?.ToLocalTime().ToString("g", CultureInfo.CurrentCulture) ?? "never")),
        };
        yield return new Operation
        {
            Module = module, Title = "Reset a password", ListTitle = "Users and their roles", Description = "Sets a new password the user must change at next sign-in, and clears any lockout.",
            Fields = [Req("Username", "Login name"), Req("Password", "New password")],
            Run = async (c, i) => Check(await c.Get<IUserAdminService>().ResetPasswordAsync(await UserAsync(c, i.Text("Username")).ConfigureAwait(true), i.Text("Password")).ConfigureAwait(true), "Password reset."),
            List = UsersAsync,
        };
        yield return new Operation
        {
            Module = module, Title = "Activate or deactivate a user", ListTitle = "Users and their roles", Description = "A deactivated user cannot sign in; the last system administrator stays active.",
            Fields = [Req("Username", "Login name"), Tick("Active", "Active")],
            Run = async (c, i) => Check(await c.Get<IUserAdminService>().SetActiveAsync(await UserAsync(c, i.Text("Username")).ConfigureAwait(true), i.Flag("Active")).ConfigureAwait(true), "Updated."),
            List = UsersAsync,
        };
        yield return new Operation
        {
            Module = module, Title = "Create a role", ListTitle = "Roles", Description = "A role, optionally starting from a copy of another role's permissions (edit grants in the permission grid).",
            Fields = [Req("Name", "Role name"), Opt("Description", "Description"), Opt("Copy", "Copy permissions from role")],
            Run = async (c, i) => Check(await c.Get<IUserAdminService>().CreateRoleAsync(i.Text("Name"), i.Optional("Description"),
                i.Optional("Copy") is string copy ? await RoleAsync(c, copy).ConfigureAwait(true) : null).ConfigureAwait(true), id => $"Role {i.Text("Name")} created."),
            List = async c => Table(await c.Db.Roles.AsNoTracking().Include(r => r.Permissions).OrderBy(r => r.Name).ToListAsync().ConfigureAwait(true),
                ("Role", r => r.Name), ("Description", r => r.Description), ("Permissions", r => r.Permissions.Count)),
        };
        yield return new Operation
        {
            Module = module, Title = "Cashier card and fingerprint", ListTitle = "Users and their roles",
            Description = "Issues a user the cashier card they swipe or scan to sign in at the till and to authorise (swipe it into the card box; only its hash is kept), or links "
                + "them to their fingerprint reader enrolment. Blank removes either.",
            Fields = [Req("Username", "Login name"), Pick("What", "Set", ["Card", "Fingerprint"]), Opt("Value", "Card swipe / enrolment id")],
            Run = async (c, i) =>
            {
                long user = await UserAsync(c, i.Text("Username")).ConfigureAwait(true);
                IUserAdminService admin = c.Get<IUserAdminService>();
                return i.Text("What") == "Fingerprint"
                    ? Check(await admin.SetBiometricIdAsync(user, i.Optional("Value")).ConfigureAwait(true), i.Optional("Value") is null ? "Fingerprint enrolment removed." : "Fingerprint linked.")
                    : Check(await admin.SetCashierCardAsync(user, i.Optional("Value")).ConfigureAwait(true), i.Optional("Value") is null ? "Card removed." : "Card issued.");
            },
            List = UsersAsync,
        };
        yield return new Operation
        {
            Module = module, Title = "Compare roles or change many at once", ListTitle = "Roles",
            Description = "Compare shows where two roles differ, permission by permission. Bulk change adds, removes or sets operations (View, Create, Edit, Delete, Print, Export, Post, "
                + "Approve, VoidReverse — comma separated) on a permission code or a pattern such as Payroll.* across several roles (comma separated) at once. System roles are left alone.",
            Fields = [Pick("Step", "Step", ["Compare", "Bulk change"]), Req("Roles", "Role(s) (compare: two)"), Opt("Pattern", "Permission code or pattern (bulk)"),
                Opt("Operations", "Operations (bulk)"), Pick("Mode", "Bulk change", Enum.GetNames<BulkGrantMode>())],
            Run = async (c, i) =>
            {
                List<long> roles = [];
                foreach (string name in i.Text("Roles").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    roles.Add(await RoleAsync(c, name).ConfigureAwait(true));
                }

                if (i.Text("Step") == "Compare")
                {
                    if (roles.Count != 2)
                    {
                        throw new OperationException("Name exactly two roles to compare.");
                    }

                    IReadOnlyList<RoleDifference> differences = Check(await c.Get<IUserAdminService>().CompareRolesAsync(roles[0], roles[1]).ConfigureAwait(true));
                    return differences.Count == 0 ? "The two roles grant exactly the same." : string.Join(Environment.NewLine, differences.Select(d =>
                        $"{d.Code,-32} only first: {d.OnlyFirst,-24} only second: {d.OnlySecond,-24} both: {d.Both}"));
                }

                OperationFlags operations = OperationFlags.None;
                foreach (string op in (i.Optional("Operations") ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    operations |= Enum.TryParse(op, true, out OperationFlags flag) ? flag : throw new OperationException($"'{op}' is not an operation.");
                }

                int changed = Check(await c.Get<IUserAdminService>().BulkGrantAsync(roles, i.Optional("Pattern") ?? string.Empty, operations, Enum.Parse<BulkGrantMode>(i.Text("Mode")))
                    .ConfigureAwait(true));
                return $"{changed} grant(s) changed.";
            },
            List = async c => Table(await c.Db.Roles.AsNoTracking().Include(r => r.Permissions).OrderBy(r => r.Name).ToListAsync().ConfigureAwait(true),
                ("Role", r => r.Name), ("System", r => r.IsSystemRole ? "yes" : string.Empty), ("Permissions", r => r.Permissions.Count)),
        };
        yield return new Operation
        {
            Module = module, Title = "Assign a role", ListTitle = "Users and their roles",
            Description = "Gives a user a role, limited by scopes such as Company:ACME;Branch:HO,NORTH (blank: unrestricted). Warns of segregation-of-duties conflicts.",
            Fields = [Req("Username", "Login name"), Req("Role", "Role name"), Opt("Scopes", "Scopes (Dimension:values;...)"), Tick("Remove", "Remove the role instead")],
            Run = async (c, i) =>
            {
                long user = await UserAsync(c, i.Text("Username")).ConfigureAwait(true);
                long role = await RoleAsync(c, i.Text("Role")).ConfigureAwait(true);
                if (i.Flag("Remove"))
                {
                    return Check(await c.Get<IUserAdminService>().RemoveRoleAsync(user, role).ConfigureAwait(true), "Role removed.");
                }

                Dictionary<ScopeDimension, IReadOnlyList<string>> scopes = [];
                foreach (string part in (i.Optional("Scopes") ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    string[] bits = part.Split(':', 2, StringSplitOptions.TrimEntries);
                    if (bits.Length != 2 || !Enum.TryParse(bits[0], true, out ScopeDimension dimension))
                    {
                        throw new OperationException($"'{part}' should look like Company:CODE1,CODE2 (dimensions: {string.Join(", ", Enum.GetNames<ScopeDimension>())}).");
                    }

                    scopes[dimension] = [.. bits[1].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
                }

                IReadOnlyList<string> conflicts = Check(await c.Get<IUserAdminService>().AssignRoleAsync(user, role, scopes).ConfigureAwait(true));
                return conflicts.Count == 0 ? "Role assigned." : $"Role assigned. Segregation-of-duties conflicts: {string.Join("; ", conflicts)}.";
            },
            List = UsersAsync,
        };
        yield return new Operation
        {
            Module = module, Title = "Create a group or add a role to it", ListTitle = "Groups",
            Description = "Groups bundle roles; users who join inherit them. Give a role to add it to the group (creating the group if new).",
            Fields = [Req("Group", "Group name"), Opt("Role", "Role to add")],
            Run = async (c, i) =>
            {
                IUserAdminService admin = c.Get<IUserAdminService>();
                string name = i.Text("Group").Trim();
                string normalized = name.ToUpperInvariant();
                long? existing = await c.Db.Groups.Where(g => g.NormalizedName == normalized).Select(g => (long?)g.Id).FirstOrDefaultAsync().ConfigureAwait(true);
                long group = existing ?? Check(await admin.CreateGroupAsync(name, null).ConfigureAwait(true));
                if (i.Optional("Role") is string roleName)
                {
                    _ = Check(await admin.AddRoleToGroupAsync(group, await RoleAsync(c, roleName).ConfigureAwait(true)).ConfigureAwait(true), string.Empty);
                }

                return existing is null ? $"Group {name} created." : $"Group {name} updated.";
            },
            List = async c =>
            {
                Dictionary<long, string> roles = await c.Db.Roles.AsNoTracking().ToDictionaryAsync(r => r.Id, r => r.Name).ConfigureAwait(true);
                return Table(await c.Db.Groups.AsNoTracking().Include(g => g.Roles).OrderBy(g => g.Name).ToListAsync().ConfigureAwait(true),
                    ("Group", g => g.Name), ("Roles", g => string.Join(", ", g.Roles.Select(r => roles.GetValueOrDefault(r.RoleId)))));
            },
        };
        yield return new Operation
        {
            Module = module, Title = "Join or leave a group", ListTitle = "Users and their roles", Description = "Puts a user in a group (inheriting its roles) or takes them out.",
            Fields = [Req("Username", "Login name"), Req("Group", "Group name"), Tick("Member", "Member")],
            Run = async (c, i) =>
            {
                string normalized = i.Text("Group").Trim().ToUpperInvariant();
                long group = await FindAsync(c.Db.Groups, g => g.NormalizedName == normalized, g => g.Id, "group", i.Text("Group")).ConfigureAwait(true);
                IReadOnlyList<string> conflicts = Check(await c.Get<IUserAdminService>().SetGroupMembershipAsync(await UserAsync(c, i.Text("Username")).ConfigureAwait(true), group, i.Flag("Member"))
                    .ConfigureAwait(true));
                return conflicts.Count == 0 ? "Membership updated." : $"Membership updated. Segregation-of-duties conflicts: {string.Join("; ", conflicts)}.";
            },
            List = UsersAsync,
        };
    }

    private static Task<long> RoleAsync(OperationContext c, string name)
    {
        string normalized = name.Trim().ToUpperInvariant();
        return FindAsync(c.Db.Roles, r => r.NormalizedName == normalized, r => r.Id, "role", name);
    }

    private static async Task<DataTable> UsersAsync(OperationContext c)
    {
        Dictionary<long, string> roles = await c.Db.Roles.AsNoTracking().ToDictionaryAsync(r => r.Id, r => r.Name).ConfigureAwait(true);
        return Table(await c.Db.Users.AsNoTracking().Include(u => u.Roles).ThenInclude(r => r.Scopes).OrderBy(u => u.Username).ToListAsync().ConfigureAwait(true),
            ("User", u => u.Username), ("Name", u => u.DisplayName), ("Active", u => u.IsActive), ("Administrator", u => u.IsSystemAdministrator),
            ("Roles", u => string.Join("; ", u.Roles.Select(r => roles.GetValueOrDefault(r.RoleId) + (r.Scopes.Count == 0 ? string.Empty
                : " (" + string.Join(", ", r.Scopes.Select(s => $"{s.Dimension}: {string.Join("/", s.Values)}")) + ")")))));
    }

    private static async Task<DataTable> OpeningReconciliationAsync(OperationContext c)
    {
        string suspense = await c.Db.Accounts.Where(a => a.CompanyId == c.CompanyId && a.ControlAccountType == ControlAccountType.Suspense).Select(a => a.AccountCode).FirstOrDefaultAsync().ConfigureAwait(true) ?? "2900";
        Core.Common.Result<OpeningBalanceReconciliation> result = await c.Get<IOpeningBalanceService>().ReconcileAsync(c.CompanyId, suspense).ConfigureAwait(true);
        if (result.IsFailure)
        {
            return Table(new[] { result.Error.Message }, ("Problem", m => m));
        }

        OpeningBalanceReconciliation r = result.Value;
        List<(string Check, string Ledger, string Sub, string Status)> rows =
        [
            ("Trial balance loaded", r.TrialBalanceLoaded ? "yes" : "no", string.Empty, r.TrialBalanceLoaded ? "OK" : "Load it"),
            ($"Suspense {r.SuspenseAccountCode}", M(r.SuspenseBalance), string.Empty, Math.Abs(r.SuspenseBalance) < 0.01m ? "OK" : "Investigate"),
            .. r.Controls.Select(x => (x.Ledger, M(x.ControlBalance), M(x.SubLedgerBalance), x.Agrees ? "OK" : "Differs")),
        ];
        IReadOnlyList<RegisterCheck> registers = (await c.Get<IVerticalImportService>().ReconcileRegistersAsync(c.CompanyId).ConfigureAwait(true)).Value;
        bool registersAgree = registers.All(x => x.Agrees);
        rows =
        [
            .. rows,
            .. registers.Select(x => (x.Register, M(x.LedgerBalance), M(x.RegisterBalance), x.Agrees ? "OK" : "Differs")),
            ("Ready to go live", r.Ready && registersAgree ? "yes" : "no", string.Empty, string.Empty),
        ];
        return Table(rows, ("Check", x => x.Check), ("Ledger / balance", x => x.Ledger), ("Sub-ledger", x => x.Sub), ("Status", x => x.Status));
    }
}
