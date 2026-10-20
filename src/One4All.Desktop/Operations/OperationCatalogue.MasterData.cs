using System.Data;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Accounting;
using One4All.Application.Abstractions.Backup;
using One4All.Application.Abstractions.Integration;
using One4All.Application.Abstractions.MasterData;
using One4All.Application.Abstractions.Communications;
using One4All.Application.Abstractions.Onboarding;
using One4All.Application.Abstractions.Security;
using One4All.Application.Communications;
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
                + "(type RESTORE <name> to confirm) - the live database is never overwritten. Restore a file: an off-site copy or another server's backup with no record here, into a new database. "
                + "Switch over: the restored database takes the live name and the old live one is kept as <live>_retired_<stamp> (type SWITCH TO <name>); restart the server and every desktop afterwards.",
            Fields = [Pick("Do", "Action", ["Back up now", "Check a backup", "Restore into a new database", "Restore a file into a new database", "Switch over to a restored database"]),
                OptNum("Backup", "Backup id (check / restore)"), Opt("File", "Backup file path (restore a file)"), Opt("Database", "New / restored database name"),
                Opt("Confirm", "Confirmation (RESTORE name / SWITCH TO name)")],
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
                    case "Restore a file into a new database":
                        return Check(await backups.RestoreFileToNewDatabaseAsync(i.Text("File"), i.Text("Database"), i.Optional("Confirm") ?? string.Empty).ConfigureAwait(true));
                    case "Switch over to a restored database":
                        return Check(await backups.SwitchOverAsync(i.Text("Database"), i.Optional("Confirm") ?? string.Empty).ConfigureAwait(true));
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
            List = async c => Table(await c.Db.Set<Core.Integration.WebhookDelivery>().AsNoTracking().Where(d => d.CompanyId == c.CompanyId).OrderByDescending(d => d.Id).Page(c, 200).ToListAsync().ConfigureAwait(true),
                ("Id", d => d.Id), ("Subscription", d => d.SubscriptionId), ("Event", d => d.EventType), ("Status", d => d.Status), ("Attempts", d => d.Attempts),
                ("Next attempt", d => d.NextAttemptOnUtc.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)), ("Last status", d => d.LastStatusCode), ("Last error", d => d.LastError)),
        };
        yield return new Operation
        {
            Module = module, Title = "Review direct database changes", ListTitle = "Sensitive-table changes made outside One4All (last 30 days)",
            Description = "Database triggers record every change to users, roles, permissions, prices, tariffs, meters and the licence - including changes made with SQL tools, listed here.",
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
            List = async c => Table(await c.Db.Set<Attachment>().AsNoTracking().Where(a => a.CompanyId == c.CompanyId && a.RemovedOnUtc == null).OrderByDescending(a => a.Id).Page(c, 200).ToListAsync()
                    .ConfigureAwait(true), ("Id", a => a.Id), ("Record", a => a.Target), ("Number", a => a.TargetKey), ("File", a => a.FileName), ("KB", a => a.SizeBytes / 1024),
                ("Uploaded", a => a.UploadedOnUtc.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)), ("Description", a => a.Description)),
        };
        string[] imports = ["Accounts", "Warehouses", "Customers", "Suppliers", "Items", "Stock", "Receivables", "Payables", "TrialBalance", "FixedAssets", "Employees", "StaffLoans", "PayrollYtd", "LeaveBalances", "CostCentres", "Departments", "TaxCodes",
            "Properties", "Meters", "Students", "Stands", "OwnershipHistory", "Applicants", "Tariffs", "PriceLists", "RentalUnits", "Budget", "InstalmentAgreements", "MunicipalArrears"];
        yield return new Operation
        {
            Module = module, Title = "Company email and SMS settings", ListTitle = "Import templates",
            Description = "The company's own email delivery (its SMTP server, or Microsoft 365 through Microsoft Graph with an app registration allowed Mail.Send) and its SMS "
                + "provider, used instead of the server's settings. Passwords, client secrets and API keys are stored encrypted and never shown; leave them blank to keep the stored "
                + "ones. Show: what is set.",
            Fields = [Pick("Step", "Step", ["Show", "Email", "SMS"]), Pick("Provider", "Email provider", Enum.GetNames<Core.Communications.EmailProvider>()),
                Opt("From", "Sender address / Graph mailbox"), Opt("FromName", "Sender name"), Opt("Host", "SMTP host"), Num("Port", "SMTP port", "587"), Tick("Tls", "SMTP uses TLS", true),
                Opt("User", "SMTP login"), Opt("Password", "SMTP password (blank: keep)"), Opt("Tenant", "Graph tenant id"), Opt("Client", "Graph client id"),
                Opt("Secret", "Graph client secret (blank: keep)"), Opt("SmsUrl", "SMS endpoint (blank: the server's)"), Opt("SmsTemplate", "SMS body template with {to} {body} {from}"),
                Opt("SmsHeader", "SMS auth header"), Opt("SmsKey", "SMS auth value (blank: keep)"), Opt("SmsFrom", "SMS sender id")],
            Run = async (c, i) =>
            {
                ICompanyMessagingService messaging = c.Get<ICompanyMessagingService>();
                switch (i.Text("Step"))
                {
                    case "Email":
                        _ = Check(await messaging.SaveEmailAsync(c.CompanyId, new CompanyEmailSettings(Enum.Parse<Core.Communications.EmailProvider>(i.Text("Provider")), i.Optional("From"),
                            i.Optional("FromName"), i.Optional("Host"), i.Whole("Port"), i.Flag("Tls"), i.Optional("User"), i.Optional("Password"), i.Optional("Tenant"), i.Optional("Client"),
                            i.Optional("Secret"))).ConfigureAwait(true), string.Empty);
                        break;
                    case "SMS":
                        _ = Check(await messaging.SaveSmsAsync(c.CompanyId, new CompanySmsSettings(i.Optional("SmsUrl"), i.Optional("SmsTemplate"), null, i.Optional("SmsHeader"),
                            i.Optional("SmsKey"), i.Optional("SmsFrom"))).ConfigureAwait(true), string.Empty);
                        break;
                }

                return await messaging.DescribeAsync(c.CompanyId).ConfigureAwait(true);
            },
        };
        yield return new Operation
        {
            Module = module, Title = "Message templates", ListTitle = "Message templates",
            Description = "The email, SMS and WhatsApp templates bulk sends use (statements STATEMENT, fee statements FEES, reminders REMINDER, bills BILL), with {{Field}} merge fields. "
                + "Save: creates or rewords a template (\\n in the body starts a new line). Preview: renders it with sample values \"Name=Thandi;Account=C001\" and lists fields still blank. "
                + "Seed standard: adds the standard templates this company is missing without touching reworded ones. An existing code, channel and language rewords that template; "
                + "templates are kept rather than deleted (sent messages name them).",
            Fields = [Pick("Step", "Step", ["Save", "Preview", "Seed standard", "List"]), Opt("Code", "Template code"), Pick("Channel", "Channel", ["Email", "Sms", "WhatsApp", "InApp"]), Opt("Language", "Language", "en"),
                Opt("DocumentType", "Document type (save)"), Opt("Subject", "Subject (email)"), Opt("Body", "Body (save; \\n for a new line)"), Opt("Values", "Sample values (preview), Field=value; …")],
            Run = async (c, i) =>
            {
                INotificationService messages = c.Get<INotificationService>();
                Core.Communications.MessageChannel channel = Enum.Parse<Core.Communications.MessageChannel>(i.Text("Channel"));
                string language = i.Optional("Language") ?? "en";
                switch (i.Text("Step"))
                {
                    case "Save":
                        IReadOnlyList<string> fields = Check(await messages.SaveTemplateAsync(c.CompanyId, i.Optional("Code") ?? string.Empty, i.Optional("DocumentType") ?? string.Empty, language, channel,
                            i.Optional("Subject") ?? string.Empty, (i.Optional("Body") ?? string.Empty).Replace("\\n", Environment.NewLine, StringComparison.Ordinal)).ConfigureAwait(true));
                        return $"Template {i.Optional("Code")?.ToUpperInvariant()} ({channel}, {language}) saved; merge fields: {(fields.Count == 0 ? "none" : string.Join(", ", fields))}.";
                    case "Preview":
                        Dictionary<string, string> values = (i.Optional("Values") ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                            .Select(pair => pair.Split('=', 2)).Where(parts => parts.Length == 2).ToDictionary(parts => parts[0].Trim(), parts => parts[1].Trim(), StringComparer.OrdinalIgnoreCase);
                        RenderedMessage rendered = Check(await messages.PreviewAsync(c.CompanyId, i.Optional("Code") ?? string.Empty, language, channel, values).ConfigureAwait(true));
                        return (rendered.Subject.Length == 0 ? string.Empty : $"Subject: {rendered.Subject}{Environment.NewLine}") + rendered.Body
                            + (rendered.MissingFields.Count == 0 ? string.Empty : $"{Environment.NewLine}Fields without a value: {string.Join(", ", rendered.MissingFields)}");
                    case "Seed standard":
                        int added = await c.Get<Infrastructure.Persistence.Seeding.MessageTemplateSeeder>().SeedAsync(c.CompanyId).ConfigureAwait(true);
                        return added == 0 ? "Every standard template is already there." : $"{added} standard template(s) added.";
                    default:
                        List<Core.Communications.MessageTemplate> all = await c.Db.Set<Core.Communications.MessageTemplate>().AsNoTracking().Where(x => x.CompanyId == c.CompanyId)
                            .OrderBy(x => x.Code).ThenBy(x => x.Channel).ThenBy(x => x.Language).ToListAsync().ConfigureAwait(true);
                        return all.Count == 0 ? "No templates; Seed standard adds the standard ones."
                            : string.Join(Environment.NewLine, all.Select(x => $"{x.Code,-12} {x.Channel,-8} {x.Language,-4} {x.DocumentType,-14} {(x.Subject.Length == 0 ? x.Body : x.Subject)[..Math.Min(70, (x.Subject.Length == 0 ? x.Body : x.Subject).Length)]}"));
                }
            },
            List = async c => Table(await c.Db.Set<Core.Communications.MessageTemplate>().AsNoTracking().Where(x => x.CompanyId == c.CompanyId).OrderBy(x => x.Code).ThenBy(x => x.Channel).ToListAsync().ConfigureAwait(true),
                ("Code", x => x.Code), ("Channel", x => x.Channel), ("Language", x => x.Language), ("Document type", x => x.DocumentType), ("Subject", x => x.Subject),
                ("Body", x => x.Body.Replace("\r\n", "\\n", StringComparison.Ordinal).Replace("\n", "\\n", StringComparison.Ordinal))),

            // A template has no retired state and sent messages name it, so it is reworded rather than removed (no Delete).
            Editable = true,
        };
        yield return new Operation
        {
            Module = module, Title = "Message batches", ListTitle = "Message batches",
            Description = "Every bulk send (statements, bills, reminders, fee statements, payslips) with how many messages are queued, sent and failed. Status: one batch's failures by recipient. "
                + "Despatch now: sends everything queued (retrying failures up to the attempts given).",
            Fields = [Pick("Step", "Step", ["List", "Status", "Despatch now"]), OptNum("Batch", "Batch id (status)"), Num("Attempts", "Attempts before a message is failed (despatch)", "3")],
            Run = async (c, i) =>
            {
                INotificationService messages = c.Get<INotificationService>();
                switch (i.Text("Step"))
                {
                    case "Status":
                        BatchStatus status = Check(await messages.BatchStatusAsync(c.CompanyId, (long)(i.OptionalNumber("Batch") ?? 0m)).ConfigureAwait(true));
                        return $"{status.Name}: queued {status.Queued}, sent {status.Sent}, failed {status.Failed}" + (status.Failures.Count == 0 ? string.Empty : Environment.NewLine + string.Join(Environment.NewLine, status.Failures));
                    case "Despatch now":
                        DespatchRun run = await messages.DespatchAsync(c.CompanyId, (int)(i.OptionalNumber("Attempts") ?? 3m)).ConfigureAwait(true);
                        return $"Sent {run.Sent}, retrying {run.Retrying}, failed {run.Failed}.";
                    default:
                        var batches = await (from b in c.Db.Set<Core.Communications.DespatchBatch>().AsNoTracking()
                                             where b.CompanyId == c.CompanyId
                                             orderby b.QueuedOn descending
                                             select new
                                             {
                                                 b.Id, b.Name, b.TemplateCode, b.QueuedOn,
                                                 Queued = c.Db.Set<Core.Communications.OutboundMessage>().Count(m => m.BatchId == b.Id && m.Status == Core.Communications.MessageStatus.Queued),
                                                 Sent = c.Db.Set<Core.Communications.OutboundMessage>().Count(m => m.BatchId == b.Id && m.Status == Core.Communications.MessageStatus.Sent),
                                                 Failed = c.Db.Set<Core.Communications.OutboundMessage>().Count(m => m.BatchId == b.Id && m.Status == Core.Communications.MessageStatus.Failed),
                                             }).Take(200).ToListAsync().ConfigureAwait(true);
                        return batches.Count == 0 ? "No message batches yet."
                            : string.Join(Environment.NewLine, batches.Select(b => $"{b.Id,6} {b.QueuedOn:yyyy-MM-dd HH:mm} {b.TemplateCode,-10} {b.Name,-40} queued {b.Queued,5} sent {b.Sent,5} failed {b.Failed,5}"));
                }
            },
            List = async c => Table(await c.Db.Set<Core.Communications.DespatchBatch>().AsNoTracking().Where(x => x.CompanyId == c.CompanyId).OrderByDescending(x => x.QueuedOn).Take(200).ToListAsync().ConfigureAwait(true),
                ("Id", x => x.Id), ("Queued", x => x.QueuedOn.ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture)), ("Template", x => x.TemplateCode), ("Name", x => x.Name)),
        };
        yield return new Operation
        {
            Module = module, Title = "Import templates and master data export", ListTitle = "Import templates",
            Description = "Template: saves the CSV template for an import (its header row and an example row) to fill in. Export: saves a register (accounts, warehouses, customers, "
                + "suppliers, items, employees, stock on hand, fixed assets, staff loans, cost centres, departments, students, stands) in its import layout, to correct in a spreadsheet "
                + "or load into another company.",
            Fields = [Pick("Action", "Action", ["Template", "Export"]), Pick("Kind", "What", imports.Concat(["Employees"]).Distinct().ToArray()), Req("File", "Save to (.csv path)")],
            Run = async (c, i) =>
            {
                string kind = i.Text("Kind");
                string csv = i.Text("Action") == "Template"
                    ? Application.Onboarding.ImportTemplates.For(kind) ?? throw new OperationException($"No template for {kind}.")
                    : Check(await c.Get<IMasterDataExportService>().ExportAsync(c.CompanyId, kind).ConfigureAwait(true));
                await System.IO.File.WriteAllTextAsync(i.Text("File"), csv, new System.Text.UTF8Encoding(false)).ConfigureAwait(true);
                int lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length - 1;
                return i.Text("Action") == "Template" ? $"{kind} template saved to {i.Text("File")}." : $"{lines} {kind.ToLowerInvariant()} exported to {i.Text("File")}.";
            },
            List = c => Task.FromResult(Table(Application.Onboarding.ImportTemplates.Kinds.Select(k => (Kind: k, Header: Application.Onboarding.ImportTemplates.For(k)!.Split('\r')[0])).ToList(),
                ("Import", t => t.Kind), ("Columns", t => t.Header))),
        };
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
                + "LeaveBalances: employee_number,leave_type,days[,taken] (the leave owed at take-on). CostCentres/Departments: code,name. "
                + "TaxCodes: code,name,type,rate,effective_from[,output_account,input_account]. "
                + "Properties: stand,ward,ratepayer[,valuation,size,connected,services e.g. Water:WAT1;Refuse:REF1]. "
                + "Meters: serial,stand,service,reading,date[,digits,multiplier]. Students: admission,name,grade,guardian[,boarder,admitted]. "
                + "Stands: stand,parcel,size[,cost,scheme,status] (a parcel not yet registered is created from its stands). "
                + "OwnershipHistory: stand,date,change,holder[,customer,consideration] (change: Allocation, Sale, Cession, Surrender, Repossession; joins the stand's hash chain in date order). "
                + "Applicants: id_number,name,number,registered[,category,date_of_birth,resident_since,dependants,disability,income,tenure,status,marital,regime,spouse_id,spouse_name,verified] (legacy waiting-list numbers kept). "
                + "Tariffs: code,name,service,type,effective,revenue_account[,fixed,rate,minimum,rate_per_valuation_cents,rate_per_sqm,category,ward,blocks e.g. 0-10:0.5;10-30:0.8;30-:1.2]. "
                + "PriceLists: price_list,item,price[,name,currency,min_qty,effective] (a list not yet on file is created). Budget: account,period,amount[,cost_centre,quantity] for the year of the date. "
                + "InstalmentAgreements: agreement,stand,buyer,cash_price,deposit,rate,method,term_months,signed,first_due,interest_account[,tenure,escalation,arrears] (instalments due by the opening date are history; arrears become an open item against the suspense account). "
                + "MunicipalArrears: stand,service,invoice,amount[,date,narrative] (an unpaid bill per property and service against the suspense account, on an opening billing run for the service). "
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
                    "LeaveBalances" => await registers.ImportLeaveBalancesAsync(c.CompanyId, csv, commit).ConfigureAwait(true),
                    "CostCentres" => await registers.ImportDimensionsAsync(c.CompanyId, csv, departments: false, commit).ConfigureAwait(true),
                    "Departments" => await registers.ImportDimensionsAsync(c.CompanyId, csv, departments: true, commit).ConfigureAwait(true),
                    "TaxCodes" => await registers.ImportTaxCodesAsync(c.CompanyId, csv, commit).ConfigureAwait(true),
                    "Properties" => await registers.ImportPropertiesAsync(c.CompanyId, csv, commit).ConfigureAwait(true),
                    "Meters" => await registers.ImportMetersAsync(c.CompanyId, csv, commit).ConfigureAwait(true),
                    "Students" => await registers.ImportStudentsAsync(c.CompanyId, csv, commit).ConfigureAwait(true),
                    "Stands" => await registers.ImportStandsAsync(c.CompanyId, csv, commit).ConfigureAwait(true),
                    "OwnershipHistory" => await registers.ImportOwnershipHistoryAsync(c.CompanyId, csv, commit).ConfigureAwait(true),
                    "Applicants" => await registers.ImportApplicantsAsync(c.CompanyId, csv, commit).ConfigureAwait(true),
                    "Tariffs" => await registers.ImportTariffsAsync(c.CompanyId, csv, commit).ConfigureAwait(true),
                    "PriceLists" => await registers.ImportPriceListsAsync(c.CompanyId, csv, commit).ConfigureAwait(true),
                    "InstalmentAgreements" => await registers.ImportInstalmentAgreementsAsync(c.CompanyId, csv, i.Date("Date"), i.Text("Suspense"), commit).ConfigureAwait(true),
                    "MunicipalArrears" => await registers.ImportMunicipalArrearsAsync(c.CompanyId, csv, i.Date("Date"), i.Text("Suspense"), commit).ConfigureAwait(true),
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
            Module = module, Title = "Load sample data for testing", ListTitle = "Companies",
            Description = "Fills a company with realistic sample records so every screen has something to show while you try the system: 8 products with stock, 10 customers with "
                + "invoices (some overdue) and receipts, 5 suppliers with invoices and a payment, 8 employees on the payroll with a PAYE table, 8 rated properties with water meters and "
                + "readings, a housing scheme with 12 stands and 8 applicants, a bank account, a cashier station and 3 assets. All codes start with SMP-. "
                + "Tick \"Create a new DEMO company\" to set one up first (every industry, standard chart of accounts, fiscal year) and load into it; otherwise it loads into this company.",
            Fields = [Tick("New", "Create a new DEMO company first", true), Opt("Code", "New company code", "DEMO"), Opt("Name", "New company name", "Demo Council & Trading"),
                Opt("Currency", "Base currency (new company)", "USD"), Today("Start", "Fiscal year starts (new company)")],
            Run = async (c, i) =>
            {
                long target = c.CompanyId;
                string created = string.Empty;
                if (i.Flag("New"))
                {
                    DateOnly start = new(i.Date("Start").Year, 1, 1);
                    IndustrySetupResult done = Check(await c.Get<IIndustrySetupService>().SetUpCompanyAsync(new IndustrySetupRequest
                    {
                        Code = (i.Optional("Code") ?? "DEMO").Trim().ToUpperInvariant(), Name = i.Optional("Name") ?? "Demo Council & Trading",
                        BaseCurrency = CurrencyCode.Parse(i.Optional("Currency") ?? "USD"), Verticals = Enum.GetValues<Vertical>(), FiscalYearStart = start, VatRatePercent = 15m, WithDemoData = false,
                    }).ConfigureAwait(true));
                    target = done.CompanyId;
                    created = $"Company {i.Optional("Code") ?? "DEMO"} (id {done.CompanyId}) set up with {done.Accounts} accounts, {done.TaxCodes} tax codes, {done.Warehouses} warehouses and {done.Roles} roles; "
                        + "sign out and in again to see it." + Environment.NewLine;
                }

                SampleDataSummary summary = Check(await c.Get<ISampleDataService>().LoadAsync(target).ConfigureAwait(true));
                return created + $"Sample data loaded into company {summary.CompanyId}:" + Environment.NewLine + string.Join(Environment.NewLine, summary.Loaded.Select(l => "  • " + l));
            },
            List = async c => Table(await c.Db.Companies.AsNoTracking().OrderBy(x => x.Id).ToListAsync().ConfigureAwait(true), ("Id", x => x.Id), ("Code", x => x.Code), ("Name", x => x.Name),
                ("Currency", x => x.BaseCurrency.Value)),
        };
        yield return new Operation
        {
            Module = module, Title = "Create a company", ListTitle = "Companies",
            Description = "A new company with a head-office branch, held to the licence's company limit. An existing code changes that company's name and time zone (its base currency "
                + "stays: its books are kept in it). Companies are kept rather than deleted.",
            Fields = [Req("Code", "Company code"), Req("Name", "Name"), Req("Currency", "Base currency", "USD"), Req("TimeZone", "Time zone", "UTC")],
            Run = async (c, i) =>
            {
                string code = i.Text("Code").Trim().ToUpperInvariant();
                if (await c.Db.Companies.FirstOrDefaultAsync(x => x.Code == code).ConfigureAwait(true) is { } existing)
                {
                    if (!string.Equals(existing.BaseCurrency.Value, i.Text("Currency").Trim(), StringComparison.OrdinalIgnoreCase))
                    {
                        throw new OperationException($"Company {existing.Code} keeps its books in {existing.BaseCurrency.Value}; its base currency cannot change.");
                    }

                    existing.Name = i.Text("Name");
                    existing.TimeZoneId = i.Text("TimeZone");
                    await c.Db.SaveChangesAsync().ConfigureAwait(true);
                    return $"Company {existing.Code} changed.";
                }

                return Check(await c.Get<IMasterDataService>().CreateCompanyAsync(i.Text("Code"), i.Text("Name"), CurrencyCode.Parse(i.Text("Currency")), i.Text("TimeZone"))
                    .ConfigureAwait(true), id => $"Company {id} created.");
            },
            List = async c => Table(await c.Db.Companies.AsNoTracking().OrderBy(x => x.Code).ToListAsync().ConfigureAwait(true),
                ("Id", x => x.Id), ("Code", x => x.Code), ("Name", x => x.Name), ("Currency", x => x.BaseCurrency.Value), ("Time zone", x => x.TimeZoneId)),

            // A company holds its own books, users' access and licence count: it is changed, never removed from here.
            Editable = true,
        };
        yield return new Operation
        {
            Module = module, Title = "Add a branch", ListTitle = "Branches",
            Description = "A branch or site of the current company. An existing code changes that branch's name (a deleted branch with the code is brought back). The head office cannot be deleted.",
            Fields = [Req("Code", "Code"), Req("Name", "Name")],
            Run = async (c, i) =>
            {
                string code = i.Text("Code").Trim();
                List<Core.System.Branch> all = await c.Db.Branches.IgnoreQueryFilters([Infrastructure.Persistence.One4AllDbContext.SoftDeleteFilter]).Where(x => x.CompanyId == c.CompanyId).ToListAsync().ConfigureAwait(true);
                if (all.FirstOrDefault(x => string.Equals(x.Code, code, StringComparison.OrdinalIgnoreCase)) is { } existing)
                {
                    bool restored = existing.IsDeleted;
                    existing.Name = i.Text("Name");
                    BringBackDeleted(existing);
                    await c.Db.SaveChangesAsync().ConfigureAwait(true);
                    return restored ? $"Branch {existing.Code} brought back into use." : $"Branch {existing.Code} changed.";
                }

                return Check(await c.Get<IMasterDataService>().CreateBranchAsync(c.CompanyId, code, i.Text("Name")).ConfigureAwait(true), id => "Branch added.");
            },
            List = async c => Table(await c.Db.Branches.AsNoTracking().Where(x => x.CompanyId == c.CompanyId).OrderBy(x => x.Code).ToListAsync().ConfigureAwait(true),
                ("Code", x => x.Code), ("Name", x => x.Name), ("Head office", x => x.IsHeadOffice)),
            Delete = async (c, code) =>
            {
                Core.System.Branch branch = await ExistingAsync<Core.System.Branch>(c, code, x => x.Code).ConfigureAwait(true) ?? throw new OperationException($"No branch {code}.");
                if (branch.IsHeadOffice)
                {
                    throw new OperationException($"Branch {branch.Code} is the head office and stays; make another branch the head office first.");
                }

                return await SoftDelete<Core.System.Branch>(x => x.Code, "branch")(c, code).ConfigureAwait(true);
            },
        };
        yield return new Operation
        {
            Module = module, Title = "Burst a report by email", ListTitle = "Report subscriptions",
            Description = "Run a report once, split it by a field (e.g. Customer code) and email each part as its own PDF - to the address in a field of the rows, or to the contacts of the "
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
                ("Code", x => x.Code), ("Report", x => x.ReportCode), ("Burst by", x => x.BurstKeyField ?? "-"), ("Recipients", x => x.BurstKeyField is null ? string.Join(", ", x.Recipients) : x.BurstRecipientField ?? (x.BurstLedger == 2 ? "supplier contacts" : "customer contacts")),
                ("Active", x => x.IsActive)),
        };
        yield return new Operation
        {
            Module = module, Title = "Report subscription", ListTitle = "Report subscriptions",
            Description = "A report emailed every time the report-subscriptions job runs: to a list of addresses, or burst by a field to each customer's or supplier's contacts (or the address in "
                + "a field); and/or saved to a folder or printed on a printer. Parameters may use date tokens (@Today, @MonthStart, @PrevMonthEnd ...). An existing code changes that "
                + "subscription (and starts it again if it was stopped); Delete stops it, keeping its delivery log.",
            Fields = [Req("Code", "Subscription code"), Req("Report", "Report code"), Pick("Format", "Format", ["Pdf", "Xlsx", "Csv", "Html"]), Opt("Recipients", "Email addresses (; separated)"),
                Opt("Key", "Burst by field"), Opt("EmailField", "Field holding the email (burst)"), Pick("Ledger", "Or burst to the contacts of", ["(email field)", "Customers", "Suppliers"]),
                Opt("Parameters", "Parameters (Name=value; ...)"), Opt("Folder", "Also save to this folder (inside the delivery root)"), Opt("Printer", "Also print on this printer")],
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
                    BurstKeyField = i.Optional("Key"), BurstRecipientField = i.Optional("EmailField"), DeliveryFolder = i.Optional("Folder"), DeliveryPrinter = i.Optional("Printer"),
                    BurstLedger = i.Text("Ledger") switch { "Customers" => Core.Trading.TradingLedger.AccountsReceivable, "Suppliers" => Core.Trading.TradingLedger.AccountsPayable, _ => null },
                }).ConfigureAwait(true), string.Empty);
                return $"Subscription {i.Text("Code").ToUpperInvariant()} saved.";
            },
            List = async c => Table(await c.Db.Set<Core.Reporting.ReportSubscription>().AsNoTracking().Where(x => x.CompanyId == c.CompanyId).OrderBy(x => x.Code).ToListAsync().ConfigureAwait(true),
                ("Code", x => x.Code), ("Report", x => x.ReportCode), ("Format", x => ((Application.Abstractions.Reporting.ReportFormat)x.Format).ToString()),
                ("Recipients", x => string.Join("; ", x.Recipients)), ("Burst by field", x => x.BurstKeyField), ("Email field", x => x.BurstRecipientField),
                ("Ledger", x => x.BurstLedger switch { 1 => "Customers", 2 => "Suppliers", _ => "(email field)" }),
                ("Parameters", x => SubscriptionParameters(x.ParametersJson)), ("Folder", x => x.DeliveryFolder), ("Printer", x => x.DeliveryPrinter), ("Active", x => x.IsActive)),
            Delete = Retire<Core.Reporting.ReportSubscription>(x => x.Code, x => x.SetActive(false), "report subscription"),
        };
        yield return new Operation
        {
            Module = module, Title = "Application log", ListTitle = "Application log (latest 300)",
            Description = "What the desktop, server and scheduler logged (information and above) - one table for every machine, beside each host's daily log file. "
                + "The list shows the latest 300 lines, newest first; Run removes lines older than the days kept.",
            Fields = [Num("Keep", "Keep (days)", "90")],
            Run = async (c, i) =>
            {
                DateTimeOffset cutoff = DateTimeOffset.UtcNow.AddDays(-Math.Max(1, i.Whole("Keep")));
                int removed = await c.Db.AppLog.Where(l => l.LoggedOnUtc < cutoff).ExecuteDeleteAsync().ConfigureAwait(true);
                return $"{removed} log line(s) older than {i.Whole("Keep")} days removed.";
            },
            List = async c =>
            {
                return Table(await c.Db.AppLog.AsNoTracking().OrderByDescending(l => l.Id).Take(300).ToListAsync().ConfigureAwait(true),
                    ("When", l => l.LoggedOnUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)), ("Level", l => l.Level), ("Host", l => l.Host), ("Machine", l => l.Machine),
                    ("Source", l => l.Source.Contains('.') ? l.Source[(l.Source.LastIndexOf('.') + 1)..] : l.Source), ("Message", l => l.Message.Length > 200 ? l.Message[..200] + "…" : l.Message),
                    ("Exception", l => l.Exception is null ? string.Empty : l.Exception.Split('\n')[0]));
            },
        };
        yield return new Operation
        {
            Module = module, Title = "Number sequences", ListTitle = "Number sequences",
            Description = "How each document type is numbered (brief §2.4): the prefix, the digits the number is padded to, whether the counter restarts every year (the year goes into "
                + "the number, INV-2026-000001), whether it must be gapless, and per branch when a branch numbers its own. A sequence not set up here is created on first use as "
                + "'<type>-' padded to 5 digits. A starting number moves the counter forward, never back. An existing type (and branch) changes that sequence; sequences are "
                + "kept rather than deleted, since documents carry their numbers.",
            Fields = [Req("Type", "Document type (e.g. AR.Invoice, PO, AC, ALR)"), Opt("Branch", "Branch code (blank: company-wide)"), Req("Prefix", "Prefix"), Num("Padding", "Padding (digits)", "5"),
                Tick("Yearly", "Restart each year"), Tick("Gapless", "Gapless", true), OptNum("StartAt", "Next number (move forward)")],
            Run = async (c, i) =>
            {
                long? branch = null;
                if (i.Optional("Branch") is string code && code.Trim().Length > 0)
                {
                    branch = await c.Db.Branches.AsNoTracking().Where(b => b.CompanyId == c.CompanyId && b.Code == code.Trim().ToUpper()).Select(b => (long?)b.Id).FirstOrDefaultAsync().ConfigureAwait(true)
                        ?? throw new OperationException($"No branch {code}.");
                }

                Application.Abstractions.Numbering.NumberSequenceRow row = Check(await c.Get<Application.Abstractions.Numbering.INumberSequenceService>().ConfigureAsync(c.CompanyId, i.Text("Type"), branch,
                    new Application.Abstractions.Numbering.NumberSequenceSetup(i.Text("Prefix"), i.Whole("Padding"), i.Flag("Yearly"), i.Flag("Gapless"), (long?)i.OptionalNumber("StartAt"))).ConfigureAwait(true));
                return $"{row.DocumentType}: next number {row.Prefix}{(row.ResetYearly ? "<year>-" : string.Empty)}{row.NextValue.ToString(CultureInfo.InvariantCulture).PadLeft(row.Padding, '0')}{(row.Gapless ? " (gapless)" : string.Empty)}.";
            },
            List = async c =>
            {
                // The branch is shown by its code (blank: company-wide), so Edit fills the form's branch code.
                Dictionary<long, string> branches = await c.Db.Branches.AsNoTracking().Where(b => b.CompanyId == c.CompanyId).ToDictionaryAsync(b => b.Id, b => b.Code).ConfigureAwait(true);
                return Table(await c.Get<Application.Abstractions.Numbering.INumberSequenceService>().ListAsync(c.CompanyId).ConfigureAwait(true),
                    ("Type", s => s.DocumentType), ("Branch", s => s.BranchId is long b ? branches.GetValueOrDefault(b, b.ToString(CultureInfo.InvariantCulture)) : string.Empty), ("Prefix", s => s.Prefix),
                    ("Padding", s => s.Padding), ("Yearly", s => s.ResetYearly), ("Gapless", s => s.Gapless), ("Next", s => s.NextValue),
                    ("Year", s => s.CurrentYear == 0 ? string.Empty : s.CurrentYear.ToString(CultureInfo.InvariantCulture)));
            },
            KeyColumn = "Type",

            // Issued documents carry the sequence's numbers, so a sequence is changed, never removed (no Delete).
            Editable = true,
        };
        yield return new Operation
        {
            Module = module, Title = "Document layouts", ListTitle = "Document layouts",
            Description = "Which report lays out each business document. Copy the document's designable report (DOC01–DOC50, listed here) in the report designer, lay it out as your own, "
                + "and name your copy's code here; a document with no layout prints as the standard text document. A blank code clears the layout; so does Delete (the report itself is kept).",
            Fields = [Pick("Document", "Document", [.. Application.Documents.DocumentKinds.All.Select(k => k.Name)]), Opt("Layout", "Layout report code (blank: standard text document)")],
            Run = async (c, i) =>
            {
                Application.Documents.DocumentKind kind = Application.Documents.DocumentKinds.Find(i.Text("Document")) ?? throw new OperationException("Choose a document.");
                string? layout = i.Optional("Layout")?.Trim().ToUpperInvariant();
                if (layout is not null && !await c.Db.ReportDefinitions.AnyAsync(r => r.Code == layout).ConfigureAwait(true) && One4All.Reporting.SystemReportCatalogue.All().All(r => r.Code != layout))
                {
                    throw new OperationException($"No report {layout}. Design the {kind.Name.ToLowerInvariant()} in the report designer (start from {kind.DesignableReportCode}) and give its code here.");
                }

                Company company = await c.Db.Companies.SingleAsync(x => x.Id == c.CompanyId).ConfigureAwait(true);
                Application.Documents.DocumentLayoutSettings settings = Application.Documents.DocumentLayoutSettings.Parse(company.DocumentLayoutsJson);
                settings.Set(kind.Kind, layout);
                company.DocumentLayoutsJson = settings.ToJson();
                await c.Db.SaveChangesAsync().ConfigureAwait(true);
                return layout is null ? $"{kind.Name}: prints as the standard text document." : $"{kind.Name}: prints through report {layout}.";
            },
            List = async c =>
            {
                string? json = await c.Db.Companies.Where(x => x.Id == c.CompanyId).Select(x => x.DocumentLayoutsJson).FirstOrDefaultAsync().ConfigureAwait(true);
                Application.Documents.DocumentLayoutSettings settings = Application.Documents.DocumentLayoutSettings.Parse(json);
                return Table(Application.Documents.DocumentKinds.All, ("Document", k => k.Name), ("Layout", k => settings.LayoutFor(k.Kind)),
                    ("Prints as", k => settings.LayoutFor(k.Kind) is null ? "standard text document" : "its layout report"), ("Start from", k => k.DesignableReportCode), ("Identified by", k => k.NumberLabel));
            },
            KeyColumn = "Document",
            Delete = async (c, name) =>
            {
                // Deleting a document's layout returns it to the standard text document; the layout report stays in the report designer.
                Application.Documents.DocumentKind kind = Application.Documents.DocumentKinds.Find(name) ?? throw new OperationException($"No document {name}.");
                Company company = await c.Db.Companies.SingleAsync(x => x.Id == c.CompanyId).ConfigureAwait(true);
                Application.Documents.DocumentLayoutSettings settings = Application.Documents.DocumentLayoutSettings.Parse(company.DocumentLayoutsJson);
                settings.Set(kind.Kind, null);
                company.DocumentLayoutsJson = settings.ToJson();
                await c.Db.SaveChangesAsync().ConfigureAwait(true);
                return $"{kind.Name}: layout cleared; it prints as the standard text document.";
            },
        };
        yield return new Operation
        {
            Module = module, Title = "Report house style", ListTitle = "House style",
            Description = "The company's house style for every report in one click: the font and size for standard text, the colour of report titles, the colour of column-heading strips, "
                + "the colour of lines, a footer line on every page, and whether the logo is placed top-right on reports that do not place it themselves. Colours are #RRGGBB. "
                + "A report's own choices stay; Clear returns every report to its own design.",
            Fields = [Pick("Step", "Step", ["Apply", "Clear"]), Opt("Font", "Font family (e.g. Calibri)"), OptNum("Size", "Standard text size (pt)"), Opt("Title", "Title colour"),
                Opt("HeadBack", "Column-heading background"), Opt("HeadText", "Column-heading text colour"), Opt("Accent", "Line colour"), Opt("Footer", "Footer line on every page"), Tick("Logo", "Place the logo top-right")],
            Run = async (c, i) =>
            {
                Company company = await c.Db.Companies.SingleAsync(x => x.Id == c.CompanyId).ConfigureAwait(true);
                if (i.Text("Step") == "Clear")
                {
                    company.ReportThemeJson = null;
                    await c.Db.SaveChangesAsync().ConfigureAwait(true);
                    return "House style cleared: every report prints with its own design.";
                }

                One4All.Reporting.Model.ReportHouseStyle theme = new()
                {
                    FontFamily = i.Optional("Font"), BaseFontSize = i.OptionalNumber("Size") is decimal size ? (double)size : null, TitleColour = i.Optional("Title")?.Trim(),
                    HeadingBackground = i.Optional("HeadBack")?.Trim(), HeadingForeground = i.Optional("HeadText")?.Trim(), AccentColour = i.Optional("Accent")?.Trim(),
                    FooterText = i.Optional("Footer"), ShowLogo = i.Flag("Logo"),
                };
                if (theme.BadColours() is { Count: > 0 } bad)
                {
                    throw new OperationException($"Colours are #RRGGBB (e.g. #1B5E20): check {string.Join(", ", bad)}.");
                }

                if (theme.BaseFontSize is < 6 or > 24)
                {
                    throw new OperationException("The standard text size is between 6 and 24 pt.");
                }

                if (theme.IsEmpty)
                {
                    throw new OperationException("Fill in at least one setting, or choose Clear.");
                }

                company.ReportThemeJson = theme.ToJson();
                await c.Db.SaveChangesAsync().ConfigureAwait(true);
                return "House style applied to every report.";
            },
            List = async c =>
            {
                string? json = await c.Db.Companies.Where(x => x.Id == c.CompanyId).Select(x => x.ReportThemeJson).FirstOrDefaultAsync().ConfigureAwait(true);
                One4All.Reporting.Model.ReportHouseStyle theme = One4All.Reporting.Model.ReportHouseStyle.Parse(json) ?? new();
                return Table(new[] { theme }, ("Font", t => t.FontFamily ?? "(report's own)"), ("Size", t => t.BaseFontSize?.ToString(CultureInfo.InvariantCulture) ?? "(report's own)"),
                    ("Title colour", t => t.TitleColour ?? "-"), ("Heading background", t => t.HeadingBackground ?? "-"), ("Heading text", t => t.HeadingForeground ?? "-"),
                    ("Line colour", t => t.AccentColour ?? "-"), ("Footer", t => t.FooterText ?? "-"), ("Logo top-right", t => t.ShowLogo));
            },
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
                + "(General Ledger report GL2R). It applies to postings from now on - posted journals cannot change - so set it before go-live. Leave blank to stop keeping it.",
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
                ("Company", x => x.Name), ("Base", x => x.BaseCurrency.Value), ("Reporting", x => x.ReportingCurrency?.Value ?? "-")),
        };
        yield return new Operation
        {
            Module = module, Title = "Create a fiscal year", ListTitle = "Fiscal years",
            Description = "Monthly periods from the start date (any month - e.g. July to June), or a week calendar: 4-4-5, 4-5-4 or 5-4-4 weeks a quarter, or 13 periods of 4 weeks, "
                + "with a 53rd week in the last period when the year needs one (the year-end adds it on its own when the calendar would drift from its year-end date); optionally "
                + "an adjustment period on the last day for audit and year-end adjustments. Tick to open the periods for every ledger at once.",
            Fields = [Req("Code", "Year code"), Today("Start", "First day"), Num("Months", "Periods (monthly)", "12"),
                Pick("Calendar", "Calendar", ["Monthly", "4-4-5 weeks", "4-5-4 weeks", "5-4-4 weeks", "13 x 4 weeks"]), Tick("Week53", "53 weeks (week calendars)"),
                Tick("Adjust", "Add an adjustment period"), Tick("Open", "Open all periods now")],
            Run = async (c, i) =>
            {
                FiscalCalendar calendar = i.Text("Calendar") switch
                {
                    "4-4-5 weeks" => FiscalCalendar.FourFourFive,
                    "4-5-4 weeks" => FiscalCalendar.FourFiveFour,
                    "5-4-4 weeks" => FiscalCalendar.FiveFourFour,
                    "13 x 4 weeks" => FiscalCalendar.ThirteenFourWeek,
                    _ => FiscalCalendar.Monthly,
                };
                long year = Check(await c.Get<IMasterDataService>().CreateFiscalYearAsync(c.CompanyId, i.Text("Code"), i.Date("Start"), calendar, i.Whole("Months"), i.Flag("Adjust"),
                    week53: i.Flag("Week53")).ConfigureAwait(true));
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
            Module = module, Title = "Add a ledger account", ListTitle = "Chart of accounts",
            Description = "A ledger account; a control account is posted only by its own ledger. An existing code changes that account (and puts it back in use); its type and control "
                + "account change only while nothing has been posted to it. Delete takes an account out of use when it has no balance and is not a control account.",
            Fields = [Req("Code", "Account code (up to 8)"), Req("Name", "Name"), Pick("Type", "Type", Enum.GetNames<AccountType>()),
                Pick("Control", "Control account", [NoControl, .. Enum.GetNames<ControlAccountType>()]), Opt("Category", "Report category"), Opt("TaxCode", "Default tax code"),
                Tick("Manual", "Manual journals allowed (untick for an account the system alone posts)", true), Tick("Monetary", "Monetary item (revalued at closing rates when currency-restricted)", true),
                Tick("Oci", "Equity reserve whose movement is other comprehensive income")],
            Run = async (c, i) =>
            {
                if (await ExistingAsync<LedgerAccount>(c, i.Text("Code"), x => x.AccountCode).ConfigureAwait(true) is { } existing)
                {
                    AccountType type = Enum.Parse<AccountType>(i.Text("Type"));
                    ControlAccountType control = i.Text("Control") == NoControl ? ControlAccountType.None : Enum.Parse<ControlAccountType>(i.Text("Control"));
                    if (type != existing.Type || control != existing.ControlAccountType)
                    {
                        // Posted history was classified by the type and control account it was posted under, so these are fixed once there are postings.
                        if (await AccountHasPostingsAsync(c, existing.AccountCode).ConfigureAwait(true))
                        {
                            throw new OperationException($"Account {existing.AccountCode} has postings, so its type and control account cannot change. Open a new account and move the balance by journal.");
                        }

                        existing.ChangeType(type);
                        existing.MakeControlAccount(control);
                    }

                    existing.Name = i.Text("Name");
                    existing.ReportCategory = i.Optional("Category") ?? existing.ReportCategory;
                    existing.DefaultTaxCode = i.Optional("TaxCode")?.Trim().ToUpperInvariant();
                    existing.AllowManualJournal = !existing.IsControlAccount && i.Flag("Manual");
                    existing.IsMonetary = i.Flag("Monetary");
                    existing.IsOtherComprehensiveIncome = i.Flag("Oci") && existing.Type == AccountType.Equity;
                    existing.IsActive = true;
                    await c.Db.SaveChangesAsync().ConfigureAwait(true);
                    return $"Account {existing.AccountCode} changed.";
                }

                return Check(await c.Get<IMasterDataService>().CreateAccountAsync(new NewAccount(c.CompanyId, i.Text("Code"), i.Text("Name"), Enum.Parse<AccountType>(i.Text("Type")),
                    i.Text("Control") == NoControl ? null : Enum.Parse<ControlAccountType>(i.Text("Control")), i.Optional("Category"), DefaultTaxCode: i.Optional("TaxCode"), AllowManualJournal: i.Flag("Manual"), IsMonetary: i.Flag("Monetary"),
                    IsOtherComprehensiveIncome: i.Flag("Oci"))).ConfigureAwait(true), id => "Account added.");
            },
            List = async c => Table(await c.Db.Accounts.AsNoTracking().Where(x => x.CompanyId == c.CompanyId).OrderBy(x => x.AccountCode).ToListAsync().ConfigureAwait(true),
                ("Code", x => x.AccountCode), ("Name", x => x.Name), ("Type", x => x.Type), ("Control", x => x.IsControlAccount ? x.ControlAccountType.ToString() : NoControl),
                ("Category", x => x.ReportCategory), ("Tax code", x => x.DefaultTaxCode), ("Manual", x => x.AllowManualJournal), ("Monetary", x => x.IsMonetary), ("OCI", x => x.IsOtherComprehensiveIncome),
                ("Active", x => x.IsActive)),
            Delete = async (c, code) =>
            {
                LedgerAccount account = await ExistingAsync<LedgerAccount>(c, code, x => x.AccountCode).ConfigureAwait(true) ?? throw new OperationException($"No account {code}.");
                if (account.IsControlAccount)
                {
                    throw new OperationException($"Account {account.AccountCode} is the {account.ControlAccountType} control account; its ledger posts to it, so it stays in use.");
                }

                decimal balance = await c.Db.Set<Core.Accounting.JournalLine>().Where(l => l.CompanyId == c.CompanyId && l.AccountCode == account.AccountCode)
                    .SumAsync(l => l.BaseDebit - l.BaseCredit).ConfigureAwait(true);
                if (balance != 0m)
                {
                    throw new OperationException($"Account {account.AccountCode} has a balance of {M(balance)}; move it to another account by journal before taking the account out of use.");
                }

                account.IsActive = false;
                await c.Db.SaveChangesAsync().ConfigureAwait(true);
                return $"Account {account.AccountCode} taken out of use (kept in history).";
            },
        };
        yield return new Operation
        {
            Module = module, Title = "Add a tax code", ListTitle = "Tax codes",
            Description = "A tax code with its first rate and the accounts it posts to; what it applies to (a goods-only code is refused on a service line and the other way round), "
                + "its jurisdiction and the tax-return box it reports in (TX226 VAT return by box). An existing code changes that tax code (and puts it back in use): a different "
                + "rate is added from the date given, so earlier documents keep the rate they were taxed at; the type stays. Delete takes a code out of use.",
            Fields = [Req("Code", "Code"), Req("Name", "Name"), Pick("Type", "Type", Enum.GetNames<TaxType>()), Num("Rate", "Rate %"), Today("From", "Rate from"),
                Opt("Output", "Output / payable account"), Opt("Input", "Input account"), Pick("AppliesTo", "Applies to", Enum.GetNames<TaxAppliesTo>()), Opt("Jurisdiction", "Jurisdiction"),
                Opt("ReturnBox", "Return box")],
            Run = async (c, i) =>
            {
                string code = i.Text("Code").Trim().ToUpperInvariant();
                if (await c.Db.TaxCodes.Include(x => x.Rates).FirstOrDefaultAsync(x => x.CompanyId == c.CompanyId && x.Code == code).ConfigureAwait(true) is { } existing)
                {
                    if (Enum.Parse<TaxType>(i.Text("Type")) != existing.Type)
                    {
                        throw new OperationException($"Tax code {existing.Code} is {existing.Type}; its type cannot change. Add a new tax code for the other type.");
                    }

                    foreach (string? account in new[] { i.Optional("Output"), i.Optional("Input") })
                    {
                        string? key = account?.Trim().ToUpperInvariant();
                        if (key is not null && !await c.Db.Accounts.AnyAsync(a => a.CompanyId == c.CompanyId && a.AccountCode == key).ConfigureAwait(true))
                        {
                            throw new OperationException($"No account {key}.");
                        }
                    }

                    decimal rate = i.Number("Rate");
                    DateOnly from = i.Date("From");
                    decimal current = existing.Rates.Where(r => r.EffectiveFrom <= from).OrderByDescending(r => r.EffectiveFrom).Select(r => r.RatePercent).FirstOrDefault();
                    string rateNote = string.Empty;
                    if (rate != current)
                    {
                        if (rate is < 0m or > 100m)
                        {
                            throw new OperationException("A rate is between 0 and 100%.");
                        }

                        if (existing.Rates.Any(r => r.EffectiveFrom == from))
                        {
                            throw new OperationException($"Tax code {existing.Code} already has a rate from {D(from)}; give the new rate a later date.");
                        }

                        existing.AddRate(from, rate);
                        rateNote = $" Rate {rate:0.####}% applies from {D(from)}.";
                    }

                    existing.Name = i.Text("Name");
                    existing.OutputAccountCode = i.Optional("Output")?.Trim().ToUpperInvariant();
                    existing.InputAccountCode = i.Optional("Input")?.Trim().ToUpperInvariant();
                    existing.AppliesTo = Enum.Parse<TaxAppliesTo>(i.Text("AppliesTo"));
                    existing.Jurisdiction = i.Optional("Jurisdiction")?.Trim().ToUpperInvariant();
                    existing.ReturnBox = i.Optional("ReturnBox")?.Trim();
                    existing.IsActive = true;
                    await c.Db.SaveChangesAsync().ConfigureAwait(true);
                    return $"Tax code {existing.Code} changed.{rateNote}";
                }

                return Check(await c.Get<IMasterDataService>().CreateTaxCodeAsync(new NewTaxCode(c.CompanyId, i.Text("Code"), i.Text("Name"), Enum.Parse<TaxType>(i.Text("Type")),
                    i.Number("Rate"), i.Date("From"), i.Optional("Output"), i.Optional("Input"), Enum.Parse<TaxAppliesTo>(i.Text("AppliesTo")), i.Optional("Jurisdiction"), i.Optional("ReturnBox")))
                    .ConfigureAwait(true), id => "Tax code added.");
            },
            List = async c => Table(await c.Db.TaxCodes.AsNoTracking().Include(x => x.Rates).Where(x => x.CompanyId == c.CompanyId).OrderBy(x => x.Code).ToListAsync().ConfigureAwait(true),
                ("Code", x => x.Code), ("Name", x => x.Name), ("Type", x => x.Type),
                ("Rate", x => x.Rates.Where(r => r.EffectiveFrom <= DateOnly.FromDateTime(DateTime.Today)).OrderByDescending(r => r.EffectiveFrom).Select(r => (decimal?)r.RatePercent).FirstOrDefault()?.ToString("0.####", CultureInfo.InvariantCulture)),
                ("Output", x => x.OutputAccountCode), ("Input", x => x.InputAccountCode), ("Applies to", x => x.AppliesTo),
                ("Jurisdiction", x => x.Jurisdiction), ("Return box", x => x.ReturnBox), ("Active", x => x.IsActive)),
            Delete = Retire<TaxCode>(x => x.Code, x => x.IsActive = false, "tax code"),
        };
        yield return new Operation
        {
            Module = module, Title = "Add a currency", ListTitle = "Currencies",
            Description = "An ISO currency the system may trade or hold balances in. An existing code changes its name and symbol (its decimal places only while nothing has been "
                + "posted in it); a deleted currency with the code is brought back. Delete is refused for a company's base or reporting currency and for one in use.",
            Fields = [Req("Code", "ISO code"), Req("Name", "Name"), Opt("Symbol", "Symbol"), Num("Places", "Decimal places", "2")],
            Run = async (c, i) =>
            {
                CurrencyCode code = CurrencyCode.Parse(i.Text("Code").Trim().ToUpperInvariant());
                if (await c.Db.Currencies.IgnoreQueryFilters([Infrastructure.Persistence.One4AllDbContext.SoftDeleteFilter]).FirstOrDefaultAsync(x => x.Code == code).ConfigureAwait(true) is { } existing)
                {
                    int places = i.Whole("Places");
                    if (places != existing.DecimalPlaces)
                    {
                        if (places is < 0 or > 4)
                        {
                            throw new OperationException("A currency has 0 to 4 decimal places.");
                        }

                        if (await c.Db.Journals.IgnoreQueryFilters([Infrastructure.Persistence.One4AllDbContext.CompanyFilter]).AnyAsync(j => j.TransactionCurrency == code).ConfigureAwait(true))
                        {
                            throw new OperationException($"Amounts have been posted in {code.Value}, so its decimal places stay {existing.DecimalPlaces}.");
                        }

                        existing.DecimalPlaces = places;
                        existing.RoundingIncrement = places == 0 ? 1m : 1m / (decimal)Math.Pow(10, places);
                    }

                    bool restored = existing.IsDeleted;
                    existing.Name = i.Text("Name");
                    existing.Symbol = i.Optional("Symbol") ?? existing.Symbol;
                    BringBackDeleted(existing);
                    await c.Db.SaveChangesAsync().ConfigureAwait(true);
                    return restored ? $"Currency {code.Value} brought back into use." : $"Currency {code.Value} changed.";
                }

                return Check(await c.Get<IMasterDataService>().CreateCurrencyAsync(code, i.Text("Name"), i.Optional("Symbol") ?? string.Empty, i.Whole("Places")).ConfigureAwait(true), "Currency added.");
            },
            List = async c => Table(await c.Db.Currencies.AsNoTracking().ToListAsync().ConfigureAwait(true), ("Code", x => x.Code.Value), ("Name", x => x.Name), ("Symbol", x => x.Symbol),
                ("Places", x => x.DecimalPlaces)),
            Delete = async (c, text) =>
            {
                CurrencyCode code = CurrencyCode.Parse(text.Trim().ToUpperInvariant());
                Currency currency = await c.Db.Currencies.FirstOrDefaultAsync(x => x.Code == code).ConfigureAwait(true) ?? throw new OperationException($"No currency {text}.");
                string? company = await c.Db.Companies.Where(x => x.BaseCurrency == code || x.ReportingCurrency == code).Select(x => x.Code).FirstOrDefaultAsync().ConfigureAwait(true);
                if (company is not null)
                {
                    throw new OperationException($"{code.Value} is company {company}'s base or reporting currency, so it stays.");
                }

                bool inUse = await c.Db.Customers.IgnoreQueryFilters().AnyAsync(x => x.Currency == code).ConfigureAwait(true)
                    || await c.Db.Suppliers.IgnoreQueryFilters().AnyAsync(x => x.Currency == code).ConfigureAwait(true)
                    || await c.Db.Journals.IgnoreQueryFilters([Infrastructure.Persistence.One4AllDbContext.CompanyFilter]).AnyAsync(j => j.TransactionCurrency == code).ConfigureAwait(true);
                if (inUse)
                {
                    throw new OperationException($"{code.Value} is in use (customer or supplier accounts, or postings), so it stays; it is kept for their history.");
                }

                currency.IsDeleted = true;
                currency.DeletedOnUtc = DateTimeOffset.UtcNow;
                currency.DeletedByUserId = c.UserId;
                await c.Db.SaveChangesAsync().ConfigureAwait(true);
                return $"Currency {code.Value} deleted (kept in history).";
            },
        };
        yield return new Operation
        {
            Module = module, Title = "Set an exchange rate", ListTitle = "Exchange rates (latest 200)", Description = "A rate from a date; one already set for that day and type is replaced.",
            Fields = [Req("From", "From currency"), Req("To", "To currency"), Pick("Type", "Rate type", Enum.GetNames<ExchangeRateType>()), Today(), Num("Rate", "Rate")],
            Run = async (c, i) => Check(await c.Get<IMasterDataService>().SetExchangeRateAsync(CurrencyCode.Parse(i.Text("From")), CurrencyCode.Parse(i.Text("To")),
                Enum.Parse<ExchangeRateType>(i.Text("Type")), i.Date("Date"), i.Number("Rate")).ConfigureAwait(true), "Rate set."),
            List = async c => Table(await c.Db.ExchangeRates.AsNoTracking().OrderByDescending(x => x.EffectiveFrom).Page(c, 200).ToListAsync().ConfigureAwait(true),
                ("From", x => x.FromCurrency.Value), ("To", x => x.ToCurrency.Value), ("Type", x => x.RateType), ("Effective", x => D(x.EffectiveFrom)), ("Rate", x => x.Rate.ToString("0.######", CultureInfo.InvariantCulture))),
        };
        yield return new Operation
        {
            Module = module, Title = "Add a warehouse", ListTitle = "Warehouses", Delete = SoftDelete<Core.Inventory.Warehouse>(x => x.Code, "warehouse"),
            Description = "A stock location of the current company. An existing code changes that warehouse.",
            Fields = [Req("Code", "Code"), Req("Name", "Name")],
            Run = async (c, i) =>
            {
                if (await ExistingAsync<Core.Inventory.Warehouse>(c, i.Text("Code"), x => x.Code).ConfigureAwait(true) is { } existing)
                {
                    existing.Name = i.Text("Name");
                    await c.Db.SaveChangesAsync().ConfigureAwait(true);
                    return $"Warehouse {existing.Code} changed.";
                }

                return Check(await c.Get<IMasterDataService>().CreateWarehouseAsync(c.CompanyId, i.Text("Code"), i.Text("Name")).ConfigureAwait(true), id => "Warehouse added.");
            },
            List = async c => Table(await c.Db.Warehouses.AsNoTracking().Where(x => x.CompanyId == c.CompanyId).OrderBy(x => x.Code).ToListAsync().ConfigureAwait(true),
                ("Code", x => x.Code), ("Name", x => x.Name), ("Active", x => x.IsActive)),
        };
        yield return new Operation
        {
            Module = module, Title = "Add a cost centre or department", ListTitle = "Cost centres and departments",
            Description = "GL analysis codes of up to three characters. An existing code of the kind changes its name (and puts it back in use); Delete takes one out of use, "
                + "keeping what was posted to it.",
            Fields = [Pick("Kind", "Kind", ["Cost centre", "Department"]), Req("Code", "Code (up to 3)"), Req("Name", "Name")],
            Run = async (c, i) =>
            {
                bool department = i.Text("Kind") == "Department";
                Core.Common.ICompanyScoped? found = department
                    ? (Core.Common.ICompanyScoped?)await ExistingAsync<Department>(c, i.Text("Code"), x => x.Code).ConfigureAwait(true)
                    : (Core.Common.ICompanyScoped?)await ExistingAsync<CostCentre>(c, i.Text("Code"), x => x.Code).ConfigureAwait(true);
                switch (found)
                {
                    case Department d:
                        (d.Name, d.IsActive) = (i.Text("Name"), true);
                        await c.Db.SaveChangesAsync().ConfigureAwait(true);
                        return $"Department {d.Code} changed.";
                    case CostCentre cc:
                        (cc.Name, cc.IsActive) = (i.Text("Name"), true);
                        await c.Db.SaveChangesAsync().ConfigureAwait(true);
                        return $"Cost centre {cc.Code} changed.";
                }

                return Check(department
                    ? await c.Get<IMasterDataService>().CreateDepartmentAsync(c.CompanyId, i.Text("Code"), i.Text("Name")).ConfigureAwait(true)
                    : await c.Get<IMasterDataService>().CreateCostCentreAsync(c.CompanyId, i.Text("Code"), i.Text("Name")).ConfigureAwait(true), id => $"{i.Text("Kind")} added.");
            },
            List = async c =>
            {
                var centres = (await c.Db.CostCentres.AsNoTracking().Where(x => x.CompanyId == c.CompanyId).ToListAsync().ConfigureAwait(true)).Select(x => (Kind: "Cost centre", x.Code, x.Name, x.IsActive));
                var departments = (await c.Db.Departments.AsNoTracking().Where(x => x.CompanyId == c.CompanyId).ToListAsync().ConfigureAwait(true)).Select(x => (Kind: "Department", x.Code, x.Name, x.IsActive));
                return Table(centres.Concat(departments).OrderBy(x => x.Kind).ThenBy(x => x.Code), ("Kind", x => x.Kind), ("Code", x => x.Code), ("Name", x => x.Name), ("Active", x => x.IsActive),
                    ("Record", x => $"{x.Kind} {x.Code}"));
            },

            // A cost centre and a department may share a code, so a row is identified by its kind and code ("Department FIN").
            KeyColumn = "Record",
            Delete = async (c, row) =>
            {
                if (row.StartsWith("Department ", StringComparison.Ordinal))
                {
                    return await Retire<Department>(x => x.Code, x => x.IsActive = false, "department")(c, row["Department ".Length..]).ConfigureAwait(true);
                }

                return row.StartsWith("Cost centre ", StringComparison.Ordinal)
                    ? await Retire<CostCentre>(x => x.Code, x => x.IsActive = false, "cost centre")(c, row["Cost centre ".Length..]).ConfigureAwait(true)
                    : throw new OperationException($"No cost centre or department {row}.");
            },
        };
        yield return new Operation
        {
            Module = module, Title = "Add a customer", ListTitle = "Customers", Delete = SoftDelete<Core.Trading.Customer>(x => x.Code, "customer"),
            Description = "A customer account; raise the credit limit later through approval. An existing code changes that customer's name and analysis codes (the currency and limit stay). "
                + "The three analysis codes (e.g. region, salesperson, sector) are how the account's transactions are analysed in reports.",
            Fields = [Req("Code", "Account code"), Req("Name", "Name"), Req("Currency", "Currency", "USD"), Num("Limit", "Credit limit", "0"), Opt("Analysis1", "Analysis code 1"),
                Opt("Analysis2", "Analysis code 2"), Opt("Analysis3", "Analysis code 3")],
            Run = async (c, i) =>
            {
                static string? Code(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToUpperInvariant();
                if (await ExistingAsync<Core.Trading.Customer>(c, i.Text("Code"), x => x.Code).ConfigureAwait(true) is { } existing)
                {
                    existing.Name = i.Text("Name");
                    (existing.AnalysisCode1, existing.AnalysisCode2, existing.AnalysisCode3) = (Code(i.Optional("Analysis1")), Code(i.Optional("Analysis2")), Code(i.Optional("Analysis3")));
                    existing.DefaultTaxCode = Code(i.Optional("TaxCode"));
                    await c.Db.SaveChangesAsync().ConfigureAwait(true);
                    return $"Customer {existing.Code} changed.";
                }

                long id = Check(await c.Get<IMasterDataService>().CreateCustomerAsync(c.CompanyId, i.Text("Code"), i.Text("Name"), CurrencyCode.Parse(i.Text("Currency")), i.Number("Limit")).ConfigureAwait(true));
                Core.Trading.Customer added = await c.Db.Customers.SingleAsync(x => x.Id == id).ConfigureAwait(true);
                (added.AnalysisCode1, added.AnalysisCode2, added.AnalysisCode3) = (Code(i.Optional("Analysis1")), Code(i.Optional("Analysis2")), Code(i.Optional("Analysis3")));
                added.DefaultTaxCode = Code(i.Optional("TaxCode"));
                await c.Db.SaveChangesAsync().ConfigureAwait(true);
                return "Customer added.";
            },
            List = async c => Table(await c.Db.Customers.AsNoTracking().Where(x => x.CompanyId == c.CompanyId).OrderBy(x => x.Code).Page(c, 500).ToListAsync().ConfigureAwait(true),
                ("Code", x => x.Code), ("Name", x => x.Name), ("Currency", x => x.Currency.Value), ("Credit limit", x => M(x.CreditLimit)),
                ("Analysis", x => string.Join(" / ", new[] { x.AnalysisCode1, x.AnalysisCode2, x.AnalysisCode3 }.Where(a => !string.IsNullOrEmpty(a))))),
        };
        yield return new Operation
        {
            Module = module, Title = "Add a supplier", ListTitle = "Suppliers", Delete = SoftDelete<Core.Trading.Supplier>(x => x.Code, "supplier"),
            Description = "A supplier account. An existing code changes that supplier's name and analysis codes (the currency stays). The three analysis codes (e.g. region, buyer, commodity) "
                + "are how the account's transactions are analysed in reports.",
            Fields = [Req("Code", "Account code"), Req("Name", "Name"), Req("Currency", "Currency", "USD"), Opt("Analysis1", "Analysis code 1"), Opt("Analysis2", "Analysis code 2"), Opt("Analysis3", "Analysis code 3"),
                Opt("TaxCode", "Default tax code")],
            Run = async (c, i) =>
            {
                static string? Code(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToUpperInvariant();
                if (await ExistingAsync<Core.Trading.Supplier>(c, i.Text("Code"), x => x.Code).ConfigureAwait(true) is { } existing)
                {
                    existing.Name = i.Text("Name");
                    (existing.AnalysisCode1, existing.AnalysisCode2, existing.AnalysisCode3) = (Code(i.Optional("Analysis1")), Code(i.Optional("Analysis2")), Code(i.Optional("Analysis3")));
                    await c.Db.SaveChangesAsync().ConfigureAwait(true);
                    return $"Supplier {existing.Code} changed.";
                }

                long id = Check(await c.Get<IMasterDataService>().CreateSupplierAsync(c.CompanyId, i.Text("Code"), i.Text("Name"), CurrencyCode.Parse(i.Text("Currency"))).ConfigureAwait(true));
                Core.Trading.Supplier added = await c.Db.Suppliers.SingleAsync(x => x.Id == id).ConfigureAwait(true);
                (added.AnalysisCode1, added.AnalysisCode2, added.AnalysisCode3) = (Code(i.Optional("Analysis1")), Code(i.Optional("Analysis2")), Code(i.Optional("Analysis3")));
                await c.Db.SaveChangesAsync().ConfigureAwait(true);
                return "Supplier added.";
            },
            List = async c => Table(await c.Db.Suppliers.AsNoTracking().Where(x => x.CompanyId == c.CompanyId).OrderBy(x => x.Code).Page(c, 500).ToListAsync().ConfigureAwait(true),
                ("Code", x => x.Code), ("Name", x => x.Name), ("Currency", x => x.Currency.Value),
                ("Analysis", x => string.Join(" / ", new[] { x.AnalysisCode1, x.AnalysisCode2, x.AnalysisCode3 }.Where(a => !string.IsNullOrEmpty(a))))),
        };
        yield return new Operation
        {
            Module = module, Title = "Add an item", ListTitle = "Items", Delete = SoftDelete<Core.Inventory.Item>(x => x.Code, "item"),
            Description = "A stock, service or other item: its unit, barcode and selling price (on the default price list), and the accounts its sales and cost post to - "
                + "left blank, they are taken from another item in the same category. How its stock is costed (weighted average, FIFO, standard with its "
                + "variance accounts, or specific identification), batch or serial tracking and a shelf life are further down.",
            Fields = [Req("Code", "Item code"), Req("Description", "Description"), Pick("Type", "Type", Enum.GetNames<ItemType>()), Opt("Category", "Category"),
                Opt("Unit", "Unit (EA, KG, L; blank is EA)"), Opt("Barcode", "Barcode"), OptNum("Price", "Selling price (default price list)"),
                Opt("Revenue", "Revenue account (blank: as the category)"), Opt("Cost", "Cost of sales account (blank: as the category)"),
                Pick("Costing", "Costing method", Enum.GetNames<CostingMethod>()), OptNum("Standard", "Standard cost"), Opt("PriceVariance", "Price variance account (standard)"),
                Opt("UsageVariance", "Usage variance account (standard)"), Pick("Tracking", "Tracking", Enum.GetNames<ItemTracking>()), OptNum("ShelfLife", "Shelf life (days)")],
            Run = async (c, i) =>
            {
                if (await ExistingAsync<Core.Inventory.Item>(c, i.Text("Code"), x => x.Code).ConfigureAwait(true) is { } existing)
                {
                    // The description, accounts, standard cost and category can change; the type, costing method and tracking are fixed once there is stock history,
                    // and a standard cost on stock on hand changes only by revaluation.
                    _ = Check(await c.Get<IMasterDataService>().UpdateItemAsync(c.CompanyId, new ItemUpdate(existing.Code, i.Text("Description"), i.Optional("Revenue"), i.Optional("Cost"),
                        i.OptionalNumber("Standard"), i.Optional("PriceVariance"), i.Optional("UsageVariance"), i.Optional("Category"), i.Optional("Unit"), i.Optional("Barcode"))).ConfigureAwait(true),
                        string.Empty);
                    return $"Item {existing.Code} changed.{await ItemPriceAsync(c, existing.Id, i.OptionalNumber("Price")).ConfigureAwait(true)}";
                }

                long id = Check(await c.Get<IMasterDataService>().CreateItemAsync(new NewItem(c.CompanyId, i.Text("Code"), i.Text("Description"), Enum.Parse<ItemType>(i.Text("Type")),
                    i.Optional("Revenue"), i.Optional("Cost"), i.OptionalNumber("Standard"), Enum.Parse<CostingMethod>(i.Text("Costing")), Enum.Parse<ItemTracking>(i.Text("Tracking")),
                    i.OptionalNumber("ShelfLife") is decimal days ? (int)days : null, i.Optional("PriceVariance"), i.Optional("UsageVariance"), i.Optional("Category"), i.Optional("Unit"),
                    i.Optional("Barcode"))).ConfigureAwait(true));
                return $"Item added.{await ItemPriceAsync(c, id, i.OptionalNumber("Price")).ConfigureAwait(true)}";
            },
            List = async c => Table(await c.Db.Items.AsNoTracking().Where(x => x.CompanyId == c.CompanyId).OrderBy(x => x.Code).Page(c, 500).ToListAsync().ConfigureAwait(true),
                ("Code", x => x.Code), ("Description", x => x.Description), ("Type", x => x.ItemType), ("Unit", x => x.UnitOfMeasure), ("Barcode", x => x.Barcode),
                ("Revenue", x => x.RevenueAccountCode), ("Cost of sales", x => x.CostOfSalesAccountCode),
                ("Costing", x => x.CostingMethod.ToString()), ("Tracking", x => x.Tracking.ToString()), ("Shelf life", x => x.ShelfLifeDays)),
        };
        yield return new Operation
        {
            Module = module, Title = "Create a user", ListTitle = "Users and their roles",
            Description = "A login who must change the initial password at first sign-in (8+ characters, letters and digits, not the user name). An existing login name changes "
                + "that user's display name and e-mail (a password given is set as a new one they must change). Delete deactivates the user: they can no longer sign in, and "
                + "their history stays; the last system administrator stays active.",
            Fields = [Req("Username", "Login name"), Req("Name", "Display name"), Opt("Email", "E-mail"), Opt("Password", "Initial password (blank when changing a user)")],
            Run = async (c, i) =>
            {
                string normalized = i.Text("Username").Trim().ToUpperInvariant();
                if (await c.Db.Users.FirstOrDefaultAsync(u => u.NormalizedUsername == normalized).ConfigureAwait(true) is { } existing)
                {
                    existing.DisplayName = i.Text("Name");
                    existing.Email = i.Optional("Email");
                    await c.Db.SaveChangesAsync().ConfigureAwait(true);
                    if (i.Optional("Password") is string password)
                    {
                        _ = Check(await c.Get<IUserAdminService>().ResetPasswordAsync(existing.Id, password).ConfigureAwait(true), string.Empty);
                        return $"User {existing.Username} changed; the new password must be changed at next sign-in.";
                    }

                    return $"User {existing.Username} changed.";
                }

                return Check(await c.Get<IUserAdminService>().CreateUserAsync(i.Text("Username"), i.Text("Name"), i.Optional("Email"), i.Text("Password")).ConfigureAwait(true),
                    id => $"User {i.Text("Username")} created.");
            },
            List = UsersAsync,
            KeyColumn = "Username",
            Delete = async (c, username) =>
            {
                long user = await UserAsync(c, username).ConfigureAwait(true);
                if (user == c.UserId)
                {
                    throw new OperationException("You cannot deactivate yourself; ask another administrator.");
                }

                return Check(await c.Get<IUserAdminService>().SetActiveAsync(user, false).ConfigureAwait(true), $"User {username} deactivated: they can no longer sign in (kept in history).");
            },
        };
        yield return new Operation
        {
            Module = module, Title = "API keys for integrations", ListTitle = "API clients",
            Description = "An integration signs in with the X-Api-Key header and acts with the permissions of the service user named (create one with only what it needs). "
                + "The key is shown once - copy it then; rotating issues a new one and the old stops working; revoking (or Delete on the list) stops the client.",
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
                ("Active", r => r.IsActive), ("Expires", r => r.ExpiresOn is DateOnly d ? D(d) : "-"), ("Rate/min", r => r.RequestsPerMinute?.ToString(CultureInfo.CurrentCulture) ?? "server"),
                ("Last used", r => r.LastUsedOnUtc?.ToLocalTime().ToString("g", CultureInfo.CurrentCulture) ?? "never")),
            KeyColumn = "Client",

            // Deleting an API client revokes it: its key stops working and the client stays listed.
            Delete = async (c, client) => Check(await c.Get<IApiClientService>().RevokeAsync(client).ConfigureAwait(true), $"Client {client} revoked (kept in history)."),
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
            Module = module, Title = "Create a role", ListTitle = "Roles",
            Description = "A role, optionally starting from a copy of another role's permissions (edit grants in the permission grid). An existing role name changes its description "
                + "(a deleted role with the name is brought back). Delete is refused for a system role and for a role users or groups still hold.",
            Fields = [Req("Name", "Role name"), Opt("Description", "Description"), Opt("Copy", "Copy permissions from role")],
            Run = async (c, i) =>
            {
                string normalized = i.Text("Name").Trim().ToUpperInvariant();
                if (await c.Db.Roles.IgnoreQueryFilters([Infrastructure.Persistence.One4AllDbContext.SoftDeleteFilter]).FirstOrDefaultAsync(r => r.NormalizedName == normalized).ConfigureAwait(true) is { } existing)
                {
                    bool restored = existing.IsDeleted;
                    existing.Description = i.Optional("Description");
                    BringBackDeleted(existing);
                    await c.Db.SaveChangesAsync().ConfigureAwait(true);
                    return restored ? $"Role {existing.Name} brought back into use." : $"Role {existing.Name} changed.";
                }

                return Check(await c.Get<IUserAdminService>().CreateRoleAsync(i.Text("Name"), i.Optional("Description"),
                    i.Optional("Copy") is string copy ? await RoleAsync(c, copy).ConfigureAwait(true) : null).ConfigureAwait(true), id => $"Role {i.Text("Name")} created.");
            },
            List = async c => Table(await c.Db.Roles.AsNoTracking().Include(r => r.Permissions).OrderBy(r => r.Name).ToListAsync().ConfigureAwait(true),
                ("Role name", r => r.Name), ("Description", r => r.Description), ("System", r => r.IsSystemRole), ("Permissions", r => r.Permissions.Count)),
            KeyColumn = "Role name",
            Delete = async (c, name) =>
            {
                string normalized = name.Trim().ToUpperInvariant();
                Role role = await c.Db.Roles.FirstOrDefaultAsync(r => r.NormalizedName == normalized).ConfigureAwait(true) ?? throw new OperationException($"No role {name}.");
                if (role.IsSystemRole)
                {
                    throw new OperationException($"{role.Name} is a system role and stays.");
                }

                if (await c.Db.Users.AnyAsync(u => u.Roles.Any(r => r.RoleId == role.Id)).ConfigureAwait(true)
                    || await c.Db.Groups.AnyAsync(g => g.Roles.Any(r => r.RoleId == role.Id)).ConfigureAwait(true))
                {
                    throw new OperationException($"Role {role.Name} is still held by users or groups; take it away from them first (Assign a role, Remove).");
                }

                role.IsDeleted = true;
                role.DeletedOnUtc = DateTimeOffset.UtcNow;
                role.DeletedByUserId = c.UserId;
                await c.Db.SaveChangesAsync().ConfigureAwait(true);
                return $"Role {role.Name} deleted (kept in history).";
            },
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
                + "Approve, VoidReverse - comma separated) on a permission code or a pattern such as Payroll.* across several roles (comma separated) at once. System roles are left alone.",
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

    // Brings a soft-deleted record back into use (adding its code again restores it rather than clashing with it).
    private static void BringBackDeleted(Core.Common.SoftDeletableEntity record)
    {
        record.IsDeleted = false;
        record.DeletedOnUtc = null;
        record.DeletedByUserId = null;
    }

    // Whether anything has been posted to a ledger account of the company.
    private static Task<bool> AccountHasPostingsAsync(OperationContext c, string accountCode)
        => c.Db.Set<Core.Accounting.JournalLine>().AnyAsync(l => l.CompanyId == c.CompanyId && l.AccountCode == accountCode);

    // A subscription's stored parameters as the form writes them (Name=value; ...).
    private static string SubscriptionParameters(string json)
    {
        try
        {
            Dictionary<string, string>? values = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            return values is null ? string.Empty : string.Join("; ", values.Select(p => $"{p.Key}={p.Value}"));
        }
        catch (System.Text.Json.JsonException)
        {
            return string.Empty;
        }
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
            ("Username", u => u.Username), ("Name", u => u.DisplayName), ("Email", u => u.Email), ("Active", u => u.IsActive), ("Administrator", u => u.IsSystemAdministrator),
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

    // Sets an item's selling price on the company's default price list; says so when there is no such list.
    private static async Task<string> ItemPriceAsync(OperationContext c, long itemId, decimal? price)
    {
        if (price is not decimal unitPrice)
        {
            return string.Empty;
        }

        long? listId = await c.Db.PriceLists.AsNoTracking().Where(l => l.CompanyId == c.CompanyId && l.IsDefault).Select(l => (long?)l.Id).FirstOrDefaultAsync().ConfigureAwait(true);
        if (listId is not long list)
        {
            return " No price was set: there is no default price list (create one under Customers, Price lists).";
        }

        _ = Check(await c.Get<Application.Abstractions.Inventory.IPricingService>().SetPriceAsync(c.CompanyId, list, itemId, 1m, unitPrice).ConfigureAwait(true), string.Empty);
        return $" Selling price {unitPrice:N2} set on the default price list.";
    }
}
