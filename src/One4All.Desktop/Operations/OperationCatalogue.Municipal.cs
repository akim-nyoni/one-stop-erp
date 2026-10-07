using System.Data;
using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Municipal;
using One4All.Application.Abstractions.Trading;
using One4All.Core.Municipal;
using static One4All.Desktop.Operations.Ops;

namespace One4All.Desktop.Operations;

/// <content>Municipal operations: the consumer enquiry desk, meter readings, billing runs and ratepayer statements.</content>
public static partial class OperationCatalogue
{
    private static IEnumerable<Operation> Municipal()
    {
        const string module = "Municipality";
        yield return new Operation
        {
            Module = module, Title = "Property enquiry", ListTitle = "Properties",
            Description = "One view of a stand: owners and ratepayer, services and meters, balance and overdue, recent bills and payments, subsidy, arrangement, open orders, licences and clearance.",
            Fields = [Req("Stand", "Stand number"), Today("AsAt", "As at")],
            Run = async (c, i) =>
            {
                PropertyEnquiry e = Check(await c.Get<IPropertyEnquiryService>().EnquireAsync(c.CompanyId, await PropertyAsync(c, i.Text("Stand")).ConfigureAwait(true), i.Date("AsAt")).ConfigureAwait(true));
                StringBuilder text = new();
                text.AppendLine(CultureInfo.InvariantCulture, $"Stand {e.StandNumber} · ward {e.Ward} · valuation {M(e.Valuation)}");
                text.AppendLine(CultureInfo.InvariantCulture, $"Ratepayer {e.RatepayerCode} {e.RatepayerName} · balance {M(e.Balance)} · overdue {M(e.Overdue)}");
                text.AppendLine("Owners: " + (e.Owners.Count == 0 ? "none recorded" : string.Join("; ", e.Owners.Select(o => $"{o.CustomerCode} {o.SharePercent:0.##}%{(o.IsRatepayer ? " (ratepayer)" : string.Empty)}"))));
                foreach (EnquiryService s in e.Services)
                {
                    string meter = s.MeterSerial is null ? string.Empty : string.Create(CultureInfo.InvariantCulture, $" · meter {s.MeterSerial} last {Q(s.LastReading ?? 0m)} on {s.LastReadingDate:yyyy-MM-dd}");
                    text.AppendLine(CultureInfo.InvariantCulture, $"  {s.ServiceType,-8} {s.TariffCode,-10} {(s.Connected ? "connected" : "DISCONNECTED")}{meter}");
                }

                if (e.IndigentSubsidyPercent is decimal subsidy)
                {
                    text.AppendLine(CultureInfo.InvariantCulture, $"Indigent subsidy {subsidy:0.##}%");
                }

                if (e.Arrangement is EnquiryArrangement a)
                {
                    text.AppendLine(CultureInfo.InvariantCulture, $"Arrangement: {M(a.TotalAmount)} at {M(a.InstalmentAmount)}/month from {a.StartDate:yyyy-MM-dd}");
                }

                e.OpenOrders.ToList().ForEach(o => text.AppendLine(CultureInfo.InvariantCulture, $"Open order {o.OrderNumber}: {o.Kind} {o.ServiceType} raised {o.RaisedOn:yyyy-MM-dd}"));
                e.Licences.ToList().ForEach(l => text.AppendLine(CultureInfo.InvariantCulture, $"Licence {l.LicenceNumber}: {l.Kind} {l.Status} to {l.ExpiresOn:yyyy-MM-dd}"));
                text.AppendLine(e.ClearanceValidUntil is DateOnly until ? $"Clearance valid until {until:yyyy-MM-dd}" : "No valid clearance certificate");
                text.AppendLine("Recent transactions:");
                e.RecentTransactions.ToList().ForEach(t => text.AppendLine(CultureInfo.InvariantCulture, $"  {t.Date:yyyy-MM-dd} {t.DocumentNumber,-14} {t.ItemType,-12} {M(t.Amount),14} outstanding {M(t.Outstanding)}"));
                return text.ToString();
            },
            List = async c => Table(await c.Db.Properties.AsNoTracking().Where(p => p.CompanyId == c.CompanyId).OrderBy(p => p.StandNumber).Take(500).ToListAsync().ConfigureAwait(true),
                ("Stand", p => p.StandNumber), ("Ward", p => p.Ward), ("Ratepayer", p => p.RatepayerCustomerId)),
        };
        yield return new Operation
        {
            Module = module, Title = "Ratepayer statement", ListTitle = "Properties",
            Description = "The ratepayer's statement for a stand, as at a date, ready to print or hand over.",
            Fields = [Req("Stand", "Stand number"), Today("AsAt", "As at")],
            Run = async (c, i) =>
            {
                long propertyId = await PropertyAsync(c, i.Text("Stand")).ConfigureAwait(true);
                long ratepayer = await c.Db.Properties.AsNoTracking().Where(p => p.Id == propertyId).Select(p => p.RatepayerCustomerId).SingleAsync().ConfigureAwait(true);
                return Check(await c.Get<IStatementService>().RenderCustomerStatementTextAsync(c.CompanyId, ratepayer, i.Date("AsAt"), 72).ConfigureAwait(true));
            },
            List = async c => Table(await c.Db.Properties.AsNoTracking().Where(p => p.CompanyId == c.CompanyId).OrderBy(p => p.StandNumber).Take(500).ToListAsync().ConfigureAwait(true),
                ("Stand", p => p.StandNumber), ("Ward", p => p.Ward), ("Ratepayer", p => p.RatepayerCustomerId)),
        };
        yield return new Operation
        {
            Module = module, Title = "Capture a meter reading", ListTitle = "Reading exceptions this month",
            Description = "An actual reading (or an estimate when the meter could not be read); readings outside the usual pattern are flagged for checking.",
            Fields = [Req("Meter", "Meter serial"), Today("Date", "Reading date"), OptNum("Reading", "Reading (blank to estimate)"), Tick("Tamper", "Tampering suspected")],
            Run = async (c, i) =>
            {
                string serial = i.Text("Meter").Trim();
                long meterId = await c.Db.Meters.AsNoTracking().Where(m => m.CompanyId == c.CompanyId && m.Serial == serial && m.IsActive).Select(m => (long?)m.Id).FirstOrDefaultAsync().ConfigureAwait(true)
                    ?? throw new OperationException($"No active meter {serial}.");
                IMeterReadingService readings = c.Get<IMeterReadingService>();
                ReadingOutcome outcome = Check(i.OptionalNumber("Reading") is decimal reading
                    ? await readings.CaptureAsync(c.CompanyId, meterId, i.Date("Date"), reading, i.Flag("Tamper")).ConfigureAwait(true)
                    : await readings.EstimateAsync(c.CompanyId, meterId, i.Date("Date")).ConfigureAwait(true));
                return $"Reading {Q(outcome.Reading)}: consumption {Q(outcome.Consumption)}, billed {Q(outcome.BilledConsumption)}"
                    + (outcome.Flags == 0 ? "." : $"; flagged {outcome.Flags}{(outcome.Accepted ? string.Empty : " — held for checking")}.");
            },
            List = async c =>
            {
                DateOnly today = DateOnly.FromDateTime(DateTime.Today);
                return Table(await c.Get<IMeterReadingService>().ExceptionsAsync(c.CompanyId, new DateOnly(today.Year, today.Month, 1), today).ConfigureAwait(true),
                    ("Meter", r => r.Serial), ("Date", r => r.ReadingDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)), ("Kind", r => r.Kind), ("Reading", r => Q(r.Reading)),
                    ("Consumption", r => Q(r.Consumption)), ("Flags", r => r.Flags), ("Accepted", r => r.Accepted));
            },
        };
        yield return new Operation
        {
            Module = module, Title = "Billing run", ListTitle = "Billing runs",
            Description = "Propose a run for a service (and ward), review its exceptions, include or exclude stands, then approve to post the bills — someone other than the proposer approves.",
            Fields = [Pick("Step", "Step", ["Propose", "Show exceptions", "Exclude a stand", "Include a stand", "Approve", "Resume posting", "Cancel"]), Pick("Service", "Service", Enum.GetNames<ServiceType>()),
                Today("BillDate", "Bill date"), Opt("Ward", "Ward (blank: all)"), Opt("Prefix", "Invoice prefix", "MB"), Opt("Run", "Run number"), Opt("Stand", "Stand number")],
            Run = async (c, i) =>
            {
                IBillingRunService runs = c.Get<IBillingRunService>();
                string Summary(BillingRunSummary s) => $"Run {s.RunNumber} {s.Status}: {s.Included} of {s.Lines} stand(s), {M(s.Total)}, {s.Exceptions} exception(s)."
                    + (s.Status is BillingRunStatus.Posting or BillingRunStatus.Posted ? $" Posted {s.Posted}, failed {s.Failed}{(s.Remaining > 0 ? $", {s.Remaining} still to post — resume posting" : string.Empty)}." : string.Empty);
                switch (i.Text("Step"))
                {
                    case "Propose":
                        return Summary(Check(await runs.ProposeAsync(new BillingRunProposal
                        {
                            CompanyId = c.CompanyId, ServiceType = Enum.Parse<ServiceType>(i.Text("Service")), BillDate = i.Date("BillDate"), Ward = i.Optional("Ward"),
                            InvoicePrefix = i.Optional("Prefix") ?? "MB", UserId = c.UserId,
                        }).ConfigureAwait(true)));
                    case "Show exceptions":
                        IReadOnlyList<BillingRunLineView> lines = Check(await runs.LinesAsync(c.CompanyId, i.Text("Run"), exceptionsOnly: true).ConfigureAwait(true));
                        return lines.Count == 0 ? "No exceptions." : string.Join(Environment.NewLine, lines.Select(l =>
                            $"{l.StandNumber,-12} {M(l.Charge),12} (was {(l.PreviousCharge is decimal p ? M(p) : "-")}) {l.Exceptions} {l.Message} {(l.Included ? string.Empty : "[excluded]")}"));
                    case "Exclude a stand":
                    case "Include a stand":
                        return Summary(Check(await runs.SetIncludedAsync(c.CompanyId, i.Text("Run"), await PropertyAsync(c, i.Text("Stand")).ConfigureAwait(true), i.Text("Step") == "Include a stand")
                            .ConfigureAwait(true)));
                    case "Approve":
                        return Summary(Check(await runs.ApproveAsync(c.CompanyId, i.Text("Run"), c.UserId).ConfigureAwait(true)));
                    case "Resume posting":
                        return Summary(Check(await runs.ResumeAsync(c.CompanyId, i.Text("Run"), new BillingPostOptions()).ConfigureAwait(true)));
                    default:
                        _ = Check(await runs.CancelAsync(c.CompanyId, i.Text("Run")).ConfigureAwait(true), string.Empty);
                        return "Run cancelled.";
                }
            },
            List = async c => Table(await c.Db.Set<BillingRun>().AsNoTracking().Where(r => r.CompanyId == c.CompanyId).OrderByDescending(r => r.Id).Take(100).ToListAsync().ConfigureAwait(true),
                ("Run", r => r.RunNumber), ("Service", r => r.ServiceType), ("Bill date", r => r.BillDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)), ("Ward", r => r.Ward),
                ("Status", r => r.Status), ("Proposed by", r => r.ProposedByUserId)),
        };
    }

    private static async Task<long> PropertyAsync(OperationContext c, string stand)
    {
        string key = stand.Trim();
        return await c.Db.Properties.AsNoTracking().Where(p => p.CompanyId == c.CompanyId && p.StandNumber == key).Select(p => (long?)p.Id).FirstOrDefaultAsync().ConfigureAwait(true)
            ?? throw new OperationException($"No stand {key}.");
    }
}
