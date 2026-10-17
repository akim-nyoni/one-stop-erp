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
                if (e.CorrespondenceAddress is string postTo)
                {
                    text.AppendLine(CultureInfo.InvariantCulture, $"Correspondence to: {postTo}");
                }

                e.Parties.ToList().ForEach(p => text.AppendLine(CultureInfo.InvariantCulture,
                    $"{p.Role}: {p.Name}{(p.Phone is null ? string.Empty : " " + p.Phone)} from {p.From:yyyy-MM-dd}{(p.ReceivesBills ? " (copied on bills)" : string.Empty)}"));
                text.AppendLine("Recent transactions:");
                e.RecentTransactions.ToList().ForEach(t => text.AppendLine(CultureInfo.InvariantCulture, $"  {t.Date:yyyy-MM-dd} {t.DocumentNumber,-14} {t.ItemType,-12} {M(t.Amount),14} outstanding {M(t.Outstanding)}"));
                if (e.Readings.Count > 0)
                {
                    text.AppendLine("Readings:");
                    e.Readings.ToList().ForEach(r => text.AppendLine(CultureInfo.InvariantCulture,
                        $"  {r.Date:yyyy-MM-dd} {r.MeterSerial,-14} {r.Reading,12:0.###} used {r.Consumption,10:0.###} {r.Kind}{(r.Accepted ? string.Empty : " (not accepted)")}"));
                }

                if (e.Tickets.Count > 0)
                {
                    text.AppendLine("Complaints and tickets:");
                    e.Tickets.ToList().ForEach(t => text.AppendLine(CultureInfo.InvariantCulture, $"  {t.ReportedAt:yyyy-MM-dd} {t.Reference,-12} {t.Category,-20} {t.Status,-10} {t.Description}"));
                }

                if (e.Correspondence.Count > 0)
                {
                    text.AppendLine("Correspondence:");
                    e.Correspondence.ToList().ForEach(m => text.AppendLine(CultureInfo.InvariantCulture,
                        $"  {m.At:yyyy-MM-dd} {(m.Outgoing ? "to  " : "from")} {m.Party,-24} {m.Channel,-8} {m.Subject} [{m.Status}]"));
                }

                return text.ToString();
            },
            List = async c => Table(await c.Db.Properties.AsNoTracking().Where(p => p.CompanyId == c.CompanyId).OrderBy(p => p.StandNumber).Page(c, 500).ToListAsync().ConfigureAwait(true),
                ("Stand", p => p.StandNumber), ("Ward", p => p.Ward), ("Ratepayer", p => p.RatepayerCustomerId)),
        };
        yield return new Operation
        {
            Module = module, Title = "Stand location and meter details", ListTitle = "Meters",
            Description = "A stand's suburb and GPS position (decimal degrees), and a meter's type, size (mm) and where it is on the stand. Blank fields stay as they are.",
            Fields = [Pick("Step", "Step", ["Stand location", "Meter details"]), Opt("Stand", "Stand number"), Opt("Suburb", "Suburb"), OptNum("Latitude", "Latitude"), OptNum("Longitude", "Longitude"),
                Opt("Meter", "Meter serial"), Opt("MeterType", "Meter type"), OptNum("Size", "Size (mm)"), Opt("Location", "Where the meter is")],
            Run = async (c, i) =>
            {
                IPropertyRegisterService register = c.Get<IPropertyRegisterService>();
                return i.Text("Step") == "Meter details"
                    ? Check(await register.SetMeterDetailsAsync(c.CompanyId, i.Optional("Meter") ?? string.Empty, i.Optional("MeterType"), i.OptionalNumber("Size") is decimal size ? (int)size : null,
                        i.Optional("Location")).ConfigureAwait(true), "Meter details saved.")
                    : Check(await register.SetLocationAsync(c.CompanyId, i.Optional("Stand") ?? string.Empty, i.Optional("Suburb"), i.OptionalNumber("Latitude"), i.OptionalNumber("Longitude"))
                        .ConfigureAwait(true), "Stand location saved.");
            },
            List = async c => Table(await c.Db.Set<Meter>().AsNoTracking().Where(m => m.CompanyId == c.CompanyId).OrderBy(m => m.Serial).Page(c, 500).ToListAsync().ConfigureAwait(true),
                ("Serial", m => m.Serial), ("Type", m => m.MeterType ?? string.Empty), ("Size (mm)", m => m.SizeMm?.ToString(CultureInfo.InvariantCulture) ?? string.Empty),
                ("Where", m => m.Location ?? string.Empty), ("Installed", m => m.InstalledOn is DateOnly d ? D(d) : string.Empty)),
        };
        yield return new Operation
        {
            Module = module, Title = "Fault ticket categories", ListTitle = "Fault ticket categories",
            Description = "The kinds of fault the public reports (a burst pipe, a missed refuse collection), each with the service it concerns and its response and resolution times - "
                + "the clocks the fault desk measures every ticket against.",
            Fields = [Req("Code", "Category code"), Req("Name", "Name"), Pick("Service", "Service", ["(none)", .. Enum.GetNames<ServiceType>()]), Num("Response", "Respond within (hours)", "4"),
                Num("Resolution", "Resolve within (hours)", "48")],
            Run = async (c, i) => Check(await c.Get<IServiceDeskService>().DefineCategoryAsync(c.CompanyId, i.Text("Code"), i.Text("Name"),
                    i.Text("Service") == "(none)" ? null : Enum.Parse<ServiceType>(i.Text("Service")), i.Number("Response"), i.Number("Resolution")).ConfigureAwait(true),
                id => $"Category {i.Text("Code").ToUpperInvariant()} saved."),
            List = async c => Table(await c.Db.Set<TicketCategory>().AsNoTracking().Where(x => x.CompanyId == c.CompanyId).OrderBy(x => x.Code).ToListAsync().ConfigureAwait(true),
                ("Code", x => x.Code), ("Name", x => x.Name), ("Service", x => x.ServiceType?.ToString() ?? "-"), ("Respond (h)", x => x.ResponseHours), ("Resolve (h)", x => x.ResolutionHours)),
        };
        yield return new Operation
        {
            Module = module, Title = "Ratepayer statement", ListTitle = "Properties",
            Description = "The ratepayer's statement for a stand, as at a date, ready to print or hand over: the account, then each service with its tariff (fixed charge, "
                + "rates and steps) and the meter's last readings, and a consumption graph for each meter over the months chosen.",
            Fields = [Req("Stand", "Stand number"), Today("AsAt", "As at"), Num("Months", "Months of consumption graph (0: none)", "12")],
            Run = async (c, i) =>
            {
                long propertyId = await PropertyAsync(c, i.Text("Stand")).ConfigureAwait(true);
                long ratepayer = await c.Db.Properties.AsNoTracking().Where(p => p.Id == propertyId).Select(p => p.RatepayerCustomerId).SingleAsync().ConfigureAwait(true);
                string statement = Check(await c.Get<IStatementService>().RenderCustomerStatementTextAsync(c.CompanyId, ratepayer, i.Date("AsAt"), 72).ConfigureAwait(true))
                    + await ServicesAndReadingsAsync(c, propertyId, i.Date("AsAt")).ConfigureAwait(true);
                if (i.Whole("Months") <= 0)
                {
                    return statement;
                }

                IReadOnlyList<Application.Municipal.MeterConsumption> meters = Check(await c.Get<IPropertyRegisterService>()
                    .ConsumptionAsync(c.CompanyId, i.Text("Stand"), i.Date("AsAt"), i.Whole("Months")).ConfigureAwait(true));
                return statement + string.Concat(meters.Select(m => Environment.NewLine + Application.Municipal.ConsumptionGraph.Render(m, 72)));
            },
            List = async c => Table(await c.Db.Properties.AsNoTracking().Where(p => p.CompanyId == c.CompanyId).OrderBy(p => p.StandNumber).Page(c, 500).ToListAsync().ConfigureAwait(true),
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
                    + (outcome.Flags == 0 ? "." : $"; flagged {outcome.Flags}{(outcome.Accepted ? string.Empty : " - held for checking")}.");
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
            Module = module, Title = "Tariffs and versions", ListTitle = "Tariff versions",
            Description = "A tariff and its effective-dated versions (brief §4.9): save a code with everything filled in to create it; save an existing code from a later date to add a version "
                + "(blank fields carry over from the version before, blocks and seasons too). Stepped blocks as from-to:rate separated by ';' (top block open: 30-:1.2); seasonal rates as "
                + "fromMonth-toMonth:rate (e.g. 5-8:0.9; 9-4:1.3). Withdraw takes a version out of force. Bills always use the version in force on the bill date.",
            Fields = [Pick("Step", "Step", ["Save version", "Withdraw version"]), Req("Code", "Tariff code"), Today("From", "Effective from"), Opt("Name", "Name"), Pick("Service", "Service", Enum.GetNames<ServiceType>()),
                Pick("Type", "Type", Enum.GetNames<TariffType>()), Opt("Account", "Revenue account"), OptNum("Fixed", "Fixed charge"), OptNum("Rate", "Per-unit rate"), OptNum("Minimum", "Minimum charge"),
                OptNum("Cents", "Cents in the dollar of valuation"), OptNum("PerSqm", "Rate per square metre"), Pick("Category", "Consumer category", ["(as before)", .. Enum.GetNames<ConsumerCategory>()]),
                Opt("Ward", "Ward (blank: as before; '-' all wards)"), Opt("Blocks", "Stepped blocks"), Opt("Seasons", "Seasonal rates")],
            Run = async (c, i) =>
            {
                IMunicipalTariffService tariffs = c.Get<IMunicipalTariffService>();
                if (i.Text("Step") == "Withdraw version")
                {
                    return Check(await tariffs.WithdrawVersionAsync(c.CompanyId, i.Text("Code"), i.Date("From")).ConfigureAwait(true), "Version withdrawn.");
                }

                static IReadOnlyList<TariffBlockInput>? Blocks(string? text) => text is null ? null : [.. text.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(b =>
                {
                    string[] parts = b.Split(':', StringSplitOptions.TrimEntries);
                    string[] range = parts.Length == 2 ? parts[0].Split('-', StringSplitOptions.TrimEntries) : [];
                    return range.Length == 2 ? new TariffBlockInput(decimal.Parse(range[0], CultureInfo.InvariantCulture), range[1].Length == 0 ? null : decimal.Parse(range[1], CultureInfo.InvariantCulture),
                        decimal.Parse(parts[1], CultureInfo.InvariantCulture)) : throw new OperationException($"Block '{b}' is not from-to:rate.");
                })];
                static IReadOnlyList<TariffSeasonInput>? Seasons(string? text) => text is null ? null : [.. text.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(s =>
                {
                    string[] parts = s.Split(':', StringSplitOptions.TrimEntries);
                    string[] months = parts.Length == 2 ? parts[0].Split('-', StringSplitOptions.TrimEntries) : [];
                    return months.Length == 2 ? new TariffSeasonInput(int.Parse(months[0], CultureInfo.InvariantCulture), int.Parse(months[1], CultureInfo.InvariantCulture), decimal.Parse(parts[1], CultureInfo.InvariantCulture))
                        : throw new OperationException($"Season '{s}' is not fromMonth-toMonth:rate.");
                })];
                bool isNew = !await c.Db.Tariffs.AnyAsync(t => t.CompanyId == c.CompanyId && t.Code == i.Text("Code").Trim().ToUpper()).ConfigureAwait(true);
                long id = Check(await tariffs.SaveVersionAsync(c.CompanyId, new TariffInput(i.Text("Code"), i.Date("From"), i.Optional("Name"),
                    isNew ? Enum.Parse<ServiceType>(i.Text("Service")) : null, isNew ? Enum.Parse<TariffType>(i.Text("Type")) : null, i.Optional("Account"), i.OptionalNumber("Fixed"), i.OptionalNumber("Rate"),
                    i.OptionalNumber("Minimum"), i.OptionalNumber("Cents"), i.OptionalNumber("PerSqm"), i.Text("Category") == "(as before)" ? null : Enum.Parse<ConsumerCategory>(i.Text("Category")),
                    i.Optional("Ward") is string ward ? (ward.Trim() == "-" ? string.Empty : ward) : null, Blocks(i.Optional("Blocks")), Seasons(i.Optional("Seasons")))).ConfigureAwait(true));
                return isNew ? $"Tariff {i.Text("Code").ToUpperInvariant()} created (id {id})." : $"Version of {i.Text("Code").ToUpperInvariant()} from {i.Date("From"):yyyy-MM-dd} saved (id {id}).";
            },
            List = async c => Table(await c.Db.Tariffs.AsNoTracking().Include(t => t.Blocks).Include(t => t.Seasons).Where(t => t.CompanyId == c.CompanyId).OrderBy(t => t.Code).ThenByDescending(t => t.EffectiveFrom)
                    .ToListAsync().ConfigureAwait(true),
                ("Tariff", t => t.Code), ("From", t => t.EffectiveFrom.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)), ("Name", t => t.Name), ("Service", t => t.ServiceType), ("Type", t => t.TariffType),
                ("Fixed", t => M(t.FixedCharge)), ("Rate", t => t.PerUnitRate.ToString("0.####", CultureInfo.InvariantCulture)), ("Minimum", t => M(t.MinimumCharge)), ("Category", t => t.Category),
                ("Ward", t => t.Ward ?? "all"), ("Blocks", t => string.Join("; ", t.Blocks.Select(b => $"{b.FromUnit:0.##}-{(b.ToUnit is decimal to ? to.ToString("0.##", CultureInfo.InvariantCulture) : string.Empty)}:{b.Rate:0.####}"))),
                ("Seasons", t => string.Join("; ", t.Seasons.Select(s => $"{s.FromMonth}-{s.ToMonth}:{s.PerUnitRate:0.####}"))), ("In force", t => t.IsActive ? "yes" : "withdrawn")),
        };
        yield return new Operation
        {
            Module = module, Title = "Tariff billing cycle", ListTitle = "Tariffs",
            Description = "How often a tariff is billed: monthly, quarterly, half-yearly or annually from a start month (e.g. 7 for a July rates year). Its amounts are per period; a period's "
                + "charge is raised once by the first run in the period, or over monthly instalments from its start (the last takes the rounding), pro rata to the days the service is in place.",
            Fields = [Req("Tariff", "Tariff code"), Pick("Cycle", "Cycle", Enum.GetNames<BillingCycle>()), Num("Start", "Start month (1–12)", "1"), Num("Instalments", "Instalments per period", "1")],
            Run = async (c, i) => Check(await c.Get<IMunicipalBillingService>().SetBillingCycleAsync(c.CompanyId, i.Text("Tariff"), Enum.Parse<BillingCycle>(i.Text("Cycle")), i.Whole("Start"),
                i.Whole("Instalments")).ConfigureAwait(true), "Billing cycle saved."),
            List = async c => Table(await c.Db.Set<Tariff>().AsNoTracking().Where(t => t.CompanyId == c.CompanyId).OrderBy(t => t.Code).ThenBy(t => t.EffectiveFrom).ToListAsync().ConfigureAwait(true),
                ("Tariff", t => t.Code), ("Name", t => t.Name), ("Service", t => t.ServiceType), ("From", t => t.EffectiveFrom.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
                ("Cycle", t => t.Cycle), ("Start month", t => t.CycleStartMonth), ("Instalments", t => t.Instalments)),
        };
        yield return new Operation
        {
            Module = module, Title = "Council service types", ListTitle = "Service types",
            Description = "The council's own services (e.g. extra refuse bins, fire levy): billed like a built-in category, metered or not, with the deposit a new connection pays.",
            Fields = [Req("Code", "Code"), Req("Name", "Name"), Pick("Category", "Bills as", Enum.GetNames<ServiceType>()), Tick("Metered", "Metered"), Num("Deposit", "Deposit on connection", "0"),
                Opt("Account", "Deposit account")],
            Run = async (c, i) => Check(await c.Get<IMunicipalServiceTypeService>().SaveAsync(c.CompanyId, i.Text("Code"), i.Text("Name"), Enum.Parse<ServiceType>(i.Text("Category")), i.Flag("Metered"),
                i.Number("Deposit"), i.Optional("Account")).ConfigureAwait(true), "Service type saved."),
            List = async c => Table(await c.Db.MunicipalServiceTypes.AsNoTracking().Where(t => t.CompanyId == c.CompanyId).OrderBy(t => t.Code).ToListAsync().ConfigureAwait(true),
                ("Code", t => t.Code), ("Name", t => t.Name), ("Bills as", t => t.Category), ("Metered", t => t.IsMetered), ("Deposit", t => M(t.DefaultDeposit)), ("Account", t => t.DepositAccountCode ?? "-")),
        };
        yield return new Operation
        {
            Module = module, Title = "Service connections", ListTitle = "Connections",
            Description = "Connect a council service to a stand (with a quantity and its deposit, invoiced to the ratepayer), change its quantity, or suspend, disconnect, reconnect or close it. "
                + "Only an active connection is billed, at the tariff times the quantity.",
            Fields = [Pick("Step", "Step", ["Connect", "Quantity", .. Enum.GetNames<ServiceLinkStatus>()]), Req("Stand", "Stand number"), Req("Service", "Service code"), Opt("Tariff", "Tariff code (connect)"),
                Today(), OptNum("Quantity", "Quantity"), OptNum("Deposit", "Deposit (connect; blank: the service's)")],
            Run = async (c, i) =>
            {
                IMunicipalServiceTypeService services = c.Get<IMunicipalServiceTypeService>();
                return i.Text("Step") switch
                {
                    "Connect" => Check(await services.ConnectAsync(c.CompanyId, i.Text("Stand"), i.Text("Service"), i.Optional("Tariff") ?? string.Empty, i.Date("Date"), i.OptionalNumber("Quantity"),
                        i.OptionalNumber("Deposit")).ConfigureAwait(true), deposit => deposit is null ? "Connected." : $"Connected; deposit billed on {deposit}."),
                    "Quantity" => Check(await services.SetQuantityAsync(c.CompanyId, i.Text("Stand"), i.Text("Service"), i.OptionalNumber("Quantity") ?? 0m).ConfigureAwait(true), "Quantity changed."),
                    string status => Check(await services.SetStatusAsync(c.CompanyId, i.Text("Stand"), i.Text("Service"), Enum.Parse<ServiceLinkStatus>(status), i.Date("Date")).ConfigureAwait(true),
                        $"Connection {status.ToLowerInvariant()}."),
                };
            },
            List = async c => Table(await c.Get<IMunicipalServiceTypeService>().ConnectionsAsync(c.CompanyId, null).ConfigureAwait(true),
                ("Stand", r => r.StandNumber), ("Service", r => r.Service), ("Tariff", r => r.Tariff), ("Quantity", r => r.Quantity), ("Deposit", r => M(r.Deposit)), ("Status", r => r.Status),
                ("Since", r => D(r.StartDate))),
        };
        yield return new Operation
        {
            Module = module, Title = "Billing run", ListTitle = "Billing runs",
            Description = "Propose a run for a service (limited to a ward, a meter-reading route or a billing cycle if you like), review its exceptions, include or exclude stands, then approve "
                + "to post the bills - someone other than the proposer approves. Email bills sends each ratepayer billed in a posted run their statement (those without email are counted for printing); "
                + "SMS bills texts each ratepayer with a mobile number from an SMS template (fields {{Name}} {{Account}} {{Stands}} {{Bills}} {{Amount}} {{Balance}} {{Currency}} {{BillDate}}); "
                + "Print bills prints every bill in the run, one ratepayer per page in stand order - or Print bills not emailed for only those without email.",
            Fields = [Pick("Step", "Step", ["Propose", "Show exceptions", "Exclude a stand", "Include a stand", "Approve", "Resume posting", "Email bills", "SMS bills", "Print bills",
                    "Print bills not emailed", "Cancel"]), Pick("Service", "Service", Enum.GetNames<ServiceType>()),
                Today("BillDate", "Bill date"), Opt("Ward", "Ward (blank: all)"), Opt("Route", "Reading route (blank: all)"), Pick("Cycle", "Billing cycle", ["Any", .. Enum.GetNames<BillingCycle>()]),
                Opt("ServiceCode", "Council service code (blank: the category's own)"), Opt("Prefix", "Invoice prefix", "MB"), Opt("Run", "Run number"), Opt("Stand", "Stand number"),
                Opt("Template", "Email / SMS template (email or SMS bills)", "BILL")],
            Run = async (c, i) =>
            {
                IBillingRunService runs = c.Get<IBillingRunService>();
                string Summary(BillingRunSummary s) => $"Run {s.RunNumber} {s.Status}: {s.Included} of {s.Lines} stand(s), {M(s.Total)}, {s.Exceptions} exception(s)."
                    + (s.Status is BillingRunStatus.Posting or BillingRunStatus.Posted ? $" Posted {s.Posted}, failed {s.Failed}{(s.Remaining > 0 ? $", {s.Remaining} still to post - resume posting" : string.Empty)}." : string.Empty);
                switch (i.Text("Step"))
                {
                    case "Propose":
                        return Summary(Check(await runs.ProposeAsync(new BillingRunProposal
                        {
                            CompanyId = c.CompanyId, ServiceType = Enum.Parse<ServiceType>(i.Text("Service")), BillDate = i.Date("BillDate"), Ward = i.Optional("Ward"),
                            InvoicePrefix = i.Optional("Prefix") ?? "MB", UserId = c.UserId, ServiceCode = i.Optional("ServiceCode"), RouteCode = i.Optional("Route"),
                            Cycle = i.Text("Cycle") == "Any" ? null : Enum.Parse<BillingCycle>(i.Text("Cycle")),
                        }).ConfigureAwait(true)));
                    case "Email bills":
                        var sent = Check(await runs.DespatchBillsAsync(c.CompanyId, i.Text("Run"), i.Optional("Template") ?? "BILL", Localization.UiText.Catalog.Culture.TwoLetterISOLanguageName).ConfigureAwait(true));
                        return $"{sent.Queued} bill(s) queued for email; {sent.NoEmail} ratepayer(s) without an email address to print and post (Print bills not emailed).";
                    case "SMS bills":
                        BillSmsDespatch texted = Check(await runs.SmsBillsAsync(c.CompanyId, i.Text("Run"), i.Optional("Template") ?? "BILL", Localization.UiText.Catalog.Culture.TwoLetterISOLanguageName)
                            .ConfigureAwait(true));
                        return $"{texted.Queued} ratepayer(s) texted (batch {texted.BatchId?.ToString(CultureInfo.InvariantCulture) ?? "-"}); {texted.NoMobile} without a mobile number.";
                    case "Print bills":
                    case "Print bills not emailed":
                        PrintedBills printed = Check(await runs.PrintBillsAsync(c.CompanyId, i.Text("Run"), i.Text("Step") == "Print bills not emailed").ConfigureAwait(true));
                        return printed.Bills == 0 ? "No bills to print: every ratepayer in the run has an email address." : $"{printed.Bills} bill(s):{Environment.NewLine}{printed.Text}";
                    case "Show exceptions":
                        IReadOnlyList<BillingRunLineView> lines = Check(await runs.LinesAsync(c.CompanyId, i.Text("Run"), exceptionsOnly: true).ConfigureAwait(true));
                        return lines.Count == 0 ? "No exceptions." : string.Join(Environment.NewLine, lines.Select(l =>
                            $"{l.StandNumber,-12} {M(l.Charge),12} (was {(l.PreviousCharge is decimal p ? M(p) : "-")}){(l.Consumption > 0 ? $" {l.Consumption:0.##} units" : string.Empty)} {l.Exceptions} {l.Message} {(l.Included ? string.Empty : "[excluded]")}"));
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
            List = async c => Table(await c.Db.Set<BillingRun>().AsNoTracking().Where(r => r.CompanyId == c.CompanyId).OrderByDescending(r => r.Id).Page(c, 100).ToListAsync().ConfigureAwait(true),
                ("Run", r => r.RunNumber), ("Service", r => r.ServiceType), ("Bill date", r => r.BillDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)), ("Ward", r => r.Ward),
                ("Route", r => r.RouteCode), ("Cycle", r => r.Cycle),
                ("Status", r => r.Status), ("Proposed by", r => r.ProposedByUserId)),
        };
        yield return new Operation
        {
            Module = module, Title = "Valuation roll", ListTitle = "Open objections",
            Description = "Import a general valuation roll (a CSV file: stand,value) effective from a date; record a supplementary valuation on one stand (bills since re-rated); "
                + "lodge an objection and decide it (a revised value re-rates the bills); History lists a stand's valuations.",
            Fields = [Pick("Step", "Step", ["Import roll", "Supplementary", "Objection", "Decide objection", "History"]), Opt("File", "Roll CSV file (stand,value)"), Opt("Roll", "Roll name"),
                Opt("Stand", "Stand number"), OptNum("Value", "Value / claimed value / revised value"), Opt("Text", "Reason / grounds / notes"), Opt("Objection", "Objection number (decide)"),
                Today("Effective", "Effective from"), Today()],
            Run = async (c, i) =>
            {
                IValuationRollService roll = c.Get<IValuationRollService>();
                switch (i.Text("Step"))
                {
                    case "Import roll":
                        List<RollEntry> entries = [];
                        foreach (string row in (await System.IO.File.ReadAllLinesAsync(i.Optional("File") ?? string.Empty).ConfigureAwait(true)).Where(r => r.Trim().Length > 0))
                        {
                            string[] bits = row.Split(',', StringSplitOptions.TrimEntries);
                            if (bits.Length == 2 && decimal.TryParse(bits[1], NumberStyles.Number, CultureInfo.InvariantCulture, out decimal value))
                            {
                                entries.Add(new RollEntry(bits[0], value));
                            }
                        }

                        return $"{Check(await roll.ImportRollAsync(c.CompanyId, i.Optional("Roll") ?? string.Empty, i.Date("Effective"), entries).ConfigureAwait(true))} valuation(s) loaded.";
                    case "Supplementary":
                        ValuationAdjustment adjusted = Check(await roll.RecordSupplementaryAsync(c.CompanyId, await PropertyAsync(c, i.Optional("Stand") ?? string.Empty).ConfigureAwait(true), i.Date("Effective"),
                            i.OptionalNumber("Value") ?? 0m, i.Optional("Text") ?? string.Empty, i.Date("Date")).ConfigureAwait(true));
                        return $"Recorded; {adjusted.BillsAdjusted} bill(s) re-rated by {M(adjusted.Amount)}{(adjusted.DocumentNumber is null ? string.Empty : $" ({adjusted.DocumentNumber})")}.";
                    case "Objection":
                        return $"Objection {Check(await roll.LodgeObjectionAsync(c.CompanyId, await PropertyAsync(c, i.Optional("Stand") ?? string.Empty).ConfigureAwait(true), i.Date("Date"),
                            i.OptionalNumber("Value") ?? 0m, i.Optional("Text") ?? string.Empty).ConfigureAwait(true))} lodged.";
                    case "Decide objection":
                        ValuationAdjustment decided = Check(await roll.DecideObjectionAsync(c.CompanyId, i.Optional("Objection") ?? string.Empty, i.Date("Date"), i.OptionalNumber("Value"), i.Optional("Text") ?? string.Empty)
                            .ConfigureAwait(true));
                        return $"Decided; {decided.BillsAdjusted} bill(s) re-rated by {M(decided.Amount)}.";
                    default:
                        IReadOnlyList<ValuationEntry> history = await roll.HistoryAsync(c.CompanyId, await PropertyAsync(c, i.Optional("Stand") ?? string.Empty).ConfigureAwait(true)).ConfigureAwait(true);
                        return string.Join(Environment.NewLine, history.Select(v => $"{v.EffectiveFrom:yyyy-MM-dd}  {M(v.Value),14}  {v.Kind,-12} {v.Reference}"));
                }
            },
            List = async c => Table(await c.Get<IValuationRollService>().ObjectionsAsync(c.CompanyId, ObjectionStatus.Lodged).ConfigureAwait(true),
                ("Objection", o => o.ObjectionNumber), ("Stand", o => o.StandNumber), ("Lodged", o => o.LodgedOn.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)), ("Objected to", o => M(o.ValueObjectedTo)),
                ("Claimed", o => M(o.ClaimedValue)), ("Status", o => o.Status)),
        };
        yield return new Operation
        {
            Module = module, Title = "Rates clearance certificates",
            Description = "Quotes what a stand must pay for a clearance certificate (its balance plus so many months' billing in advance), issues the certificate once that is "
                + "paid, valid for so many days, and prints it (Print reprints an issued certificate by its number).",
            Fields = [Pick("Step", "Step", ["Quote", "Issue", "Print"]), Opt("Stand", "Stand number (quote, issue)"), Num("Months", "Months in advance", "3"), Num("Valid", "Valid for (days)", "60"), Today(),
                Opt("Certificate", "Certificate number (print)")],
            Run = async (c, i) =>
            {
                IClearanceService clearance = c.Get<IClearanceService>();
                if (i.Text("Step") == "Print")
                {
                    string number = (i.Optional("Certificate") ?? throw new OperationException("Give the certificate number.")).Trim().ToUpperInvariant();
                    return await ClearanceCertificateTextAsync(c, number).ConfigureAwait(true);
                }

                long property = await PropertyAsync(c, i.Optional("Stand") ?? throw new OperationException("Give the stand.")).ConfigureAwait(true);
                if (i.Text("Step") == "Quote")
                {
                    One4All.Application.Municipal.ClearanceQuote quote = Check(await clearance.QuoteAsync(c.CompanyId, property, i.Date("Date"), i.Whole("Months")).ConfigureAwait(true));
                    return $"Balance {M(quote.Balance)} + {quote.MonthsInAdvance} month(s) at {M(quote.AverageMonthlyBilling)} = advance {M(quote.AdvanceRequired)}; to pay {M(quote.AmountDue)}"
                        + (quote.CanIssue ? " - can be issued now." : ".");
                }

                IssuedClearance issued = Check(await clearance.IssueAsync(c.CompanyId, property, i.Date("Date"), i.Whole("Months"), i.Whole("Valid"), c.UserId).ConfigureAwait(true));
                return await ClearanceCertificateTextAsync(c, issued.CertificateNumber).ConfigureAwait(true);
            },
            List = async c => Table(await (
                    from cert in c.Db.Set<Core.Municipal.ClearanceCertificate>().AsNoTracking()
                    join p in c.Db.Properties.AsNoTracking() on cert.PropertyId equals p.Id
                    where cert.CompanyId == c.CompanyId
                    orderby cert.IssuedOn descending
                    select new { cert.CertificateNumber, p.StandNumber, cert.IssuedOn, cert.ValidUntil, cert.AdvanceHeld }).Page(c, 200).ToListAsync().ConfigureAwait(true),
                ("Certificate", x => x.CertificateNumber), ("Stand", x => x.StandNumber), ("Issued", x => D(x.IssuedOn)), ("Valid until", x => D(x.ValidUntil)), ("Advance held", x => M(x.AdvanceHeld))),
        };
        yield return new Operation
        {
            Module = module, Title = "Prepaid vending", ListTitle = "Prepaid sales (this month)",
            Description = "Sells prepaid units (water or electricity) to a stand: part of the payment can go to its arrears; the rest buys units and gives a token.",
            Fields = [Req("Stand", "Stand number"), Pick("Service", "Service", [nameof(ServiceType.Water), nameof(ServiceType.Electricity)]), Num("Amount", "Amount paid"), Req("Bank", "Bank / cash account"),
                Num("Arrears", "% to arrears", "0"), Pick("Channel", "Channel", ["Counter", "Agent", "Mobile"]), Today()],
            Run = async (c, i) =>
            {
                VendResult vend = Check(await c.Get<IPrepaidVendingService>().VendAsync(new PrepaidVendRequest
                {
                    CompanyId = c.CompanyId, PropertyId = await PropertyAsync(c, i.Text("Stand")).ConfigureAwait(true), ServiceType = Enum.Parse<ServiceType>(i.Text("Service")), Amount = i.Number("Amount"),
                    Date = i.Date("Date"), BankAccountCode = i.Text("Bank"), ArrearsPercent = i.Number("Arrears"), Channel = i.Text("Channel"),
                }).ConfigureAwait(true));
                return $"Sale {vend.SaleNumber}: token {vend.Token} for {Q(vend.Units)} units ({M(vend.ToUnits)}); {M(vend.ToArrears)} to arrears, arrears now {M(vend.ArrearsAfter)}.";
            },
            List = async c => Table(await c.Get<IPrepaidVendingService>().SalesAsync(c.CompanyId, new DateOnly(DateTime.Today.Year, DateTime.Today.Month, 1), DateOnly.FromDateTime(DateTime.Today)).ConfigureAwait(true),
                ("Sale", x => x.SaleNumber), ("Stand", x => x.StandNumber), ("Date", x => x.SoldOn.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)), ("Amount", x => M(x.Amount)),
                ("To arrears", x => M(x.ToArrears)), ("Units", x => Q(x.Units)), ("Token", x => x.Token), ("Channel", x => x.Channel)),
        };
        yield return new Operation
        {
            Module = module, Title = "Service deposits",
            Description = "Takes a deposit for a service on a stand (held as a liability), refunds it or applies it to the arrears when the account closes; Held lists a stand's deposits.",
            Fields = [Pick("Step", "Step", ["Take", "Refund", "Apply to arrears", "Held"]), Opt("Stand", "Stand number (take / held)"), Pick("Service", "Service", Enum.GetNames<ServiceType>()),
                Num("Amount", "Amount", "0"), Opt("Reference", "Deposit reference (refund / apply)"), Opt("Bank", "Bank account"), Opt("Liability", "Deposit liability account (take)"), Today()],
            Run = async (c, i) =>
            {
                IServiceDepositService deposits = c.Get<IServiceDepositService>();
                switch (i.Text("Step"))
                {
                    case "Take":
                        DepositTaken taken = Check(await deposits.TakeAsync(new TakeDepositRequest
                        {
                            CompanyId = c.CompanyId, PropertyId = await PropertyAsync(c, i.Optional("Stand") ?? string.Empty).ConfigureAwait(true), ServiceType = Enum.Parse<ServiceType>(i.Text("Service")),
                            Amount = i.Number("Amount"), Date = i.Date("Date"), BankAccountCode = i.Optional("Bank") ?? string.Empty, LiabilityAccountCode = i.Optional("Liability") ?? string.Empty,
                        }).ConfigureAwait(true));
                        return $"Deposit {taken.Reference} of {M(taken.Amount)} taken ({taken.JournalNumber}).";
                    case "Refund":
                        return $"Refunded ({Check(await deposits.RefundAsync(c.CompanyId, i.Optional("Reference") ?? string.Empty, i.Date("Date"), i.Optional("Bank") ?? string.Empty).ConfigureAwait(true))}).";
                    case "Apply to arrears":
                        return $"Applied ({Check(await deposits.ApplyToArrearsAsync(c.CompanyId, i.Optional("Reference") ?? string.Empty, i.Date("Date")).ConfigureAwait(true))}).";
                    default:
                        IReadOnlyList<HeldDeposit> held = await deposits.HeldAsync(c.CompanyId, await PropertyAsync(c, i.Optional("Stand") ?? string.Empty).ConfigureAwait(true)).ConfigureAwait(true);
                        return held.Count == 0 ? "No deposits held." : string.Join(Environment.NewLine, held.Select(d => $"{d.Reference,-14} {d.ServiceType,-12} {M(d.Amount),12}  since {d.TakenOn:yyyy-MM-dd}"));
                }
            },
        };
        yield return new Operation
        {
            Module = module, Title = "Indigent register", ListTitle = "Reviews due in 60 days",
            Description = "Registers a household as indigent (income under the maximum) with its subsidy % and review date; reviews it (staying on only while still under the maximum); "
                + "applies the month's subsidies to their bills.",
            Fields = [Pick("Step", "Step", ["Register", "Review", "Apply subsidies"]), Opt("Stand", "Stand number (register)"), OptNum("Registration", "Registration id (review)"), Num("Income", "Household income", "0"),
                Num("Subsidy", "Subsidy %", "100"), Num("Maximum", "Maximum income to qualify", "0"), Today("Review", "Next review due"), Opt("Account", "Subsidy (expense) account"), Today()],
            Run = async (c, i) =>
            {
                IIndigentRegisterService register = c.Get<IIndigentRegisterService>();
                switch (i.Text("Step"))
                {
                    case "Register":
                        return Check(await register.RegisterAsync(c.CompanyId, new IndigentApplication(await PropertyAsync(c, i.Optional("Stand") ?? string.Empty).ConfigureAwait(true), i.Number("Income"),
                            i.Number("Subsidy"), i.Date("Date"), i.Date("Review"), c.UserId), i.Number("Maximum")).ConfigureAwait(true), id => $"Registered ({id}).");
                    case "Review":
                        bool stays = Check(await register.ReviewAsync(c.CompanyId, (long)(i.OptionalNumber("Registration") ?? 0m), i.Number("Income"), i.Number("Subsidy"), i.Date("Review"), i.Number("Maximum"), c.UserId)
                            .ConfigureAwait(true));
                        return stays ? "Still indigent; review date moved." : "No longer qualifies; removed from the register.";
                    default:
                        IReadOnlyList<SubsidyLine> subsidies = Check(await register.ApplySubsidiesAsync(c.CompanyId, i.Date("Date"), i.Optional("Account") ?? string.Empty).ConfigureAwait(true));
                        return $"{subsidies.Count} household(s) subsidised {M(subsidies.Sum(x => x.Subsidy))}.";
                }
            },
            List = async c => Table(await c.Get<IIndigentRegisterService>().ReviewsDueAsync(c.CompanyId, DateOnly.FromDateTime(DateTime.Today), 60).ConfigureAwait(true),
                ("Registration", r => r.RegistrationId), ("Stand", r => r.StandNumber), ("Review due", r => r.ReviewDue.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)), ("Overdue", r => r.Overdue)),
        };
        yield return new Operation
        {
            Module = module, Title = "Meter reading routes",
            Description = "Creates a reading route for a meter reader (meter serials in walking order, comma-separated); Sheet prints it with the last readings; Import loads a CSV of "
                + "readings taken on the round (each checked like a keyed reading).",
            Fields = [Pick("Step", "Step", ["Create route", "Sheet", "Import readings"]), Opt("Route", "Route code"), Opt("Name", "Route name"), Opt("Reader", "Meter reader"),
                Opt("Meters", "Meter serials in order, comma-separated"), Opt("File", "Readings CSV file (import)")],
            Run = async (c, i) =>
            {
                IReadingRouteService routes = c.Get<IReadingRouteService>();
                switch (i.Text("Step"))
                {
                    case "Create route":
                        List<long> meters = [];
                        foreach (string serial in (i.Optional("Meters") ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                        {
                            meters.Add(await FindAsync(c.Db.Meters, m => m.CompanyId == c.CompanyId && m.Serial == serial.ToUpper(), m => m.Id, "meter", serial).ConfigureAwait(true));
                        }

                        return Check(await routes.CreateRouteAsync(c.CompanyId, i.Optional("Route") ?? string.Empty, i.Optional("Name") ?? string.Empty, i.Optional("Reader") ?? string.Empty, meters).ConfigureAwait(true),
                            _ => "Route created.");
                    case "Sheet":
                        IReadOnlyList<RouteStop> stops = Check(await routes.RouteSheetAsync(c.CompanyId, i.Optional("Route") ?? string.Empty).ConfigureAwait(true));
                        return string.Join(Environment.NewLine, stops.Select(x => $"{x.Sequence,3}  {x.Serial,-14} {x.StandNumber,-12} last {Q(x.LastReading),12} {x.LastReadingDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}   ______"));
                    default:
                        ReadingImportResult imported = await routes.ImportReadingsAsync(c.CompanyId, await System.IO.File.ReadAllTextAsync(i.Optional("File") ?? string.Empty).ConfigureAwait(true)).ConfigureAwait(true);
                        return $"{imported.Captured} reading(s) captured, {imported.Rejected} rejected." + (imported.Errors.Count == 0 ? string.Empty
                            : Environment.NewLine + string.Join(Environment.NewLine, imported.Errors.Take(30).Select(e => $"Line {e.LineNumber}: {e.Message}")));
                }
            },
        };
        yield return new Operation
        {
            Module = module, Title = "Cemeteries", ListTitle = "Burials this year",
            Description = "Adds plots to a cemetery section (\"A1,A2,A3\") at a price and capacity, sells a plot to a holder, records an interment (with its fee), and lists a "
                + "cemetery's plots.",
            Fields = [Pick("Step", "Step", ["Add plots", "Sell plot", "Interment", "Plots"]), Opt("Cemetery", "Cemetery"), Opt("Section", "Section"), Opt("Plots", "Plot numbers, comma-separated"),
                Num("Price", "Price / interment fee", "0"), Num("Capacity", "Capacity (graves per plot)", "1"), Opt("Plot", "Plot reference (sell / interment)"), Opt("Holder", "Holder customer code"),
                Opt("Deceased", "Deceased name"), Opt("Died", "Date of death (yyyy-MM-dd)"), Opt("Account", "Revenue / fee account"), Today()],
            Run = async (c, i) =>
            {
                ICemeteryService cemetery = c.Get<ICemeteryService>();
                switch (i.Text("Step"))
                {
                    case "Add plots":
                        List<string> plots = [.. (i.Optional("Plots") ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
                        return $"{Check(await cemetery.AddPlotsAsync(c.CompanyId, i.Optional("Cemetery") ?? string.Empty, i.Optional("Section") ?? string.Empty, plots, i.Number("Price"), i.Whole("Capacity")).ConfigureAwait(true))} plot(s) added.";
                    case "Sell plot":
                        return $"Sold ({Check(await cemetery.SellAsync(c.CompanyId, i.Optional("Plot") ?? string.Empty, await CustomerAsync(c, i.Optional("Holder") ?? string.Empty).ConfigureAwait(true), i.Date("Date"),
                            i.Optional("Account") ?? string.Empty).ConfigureAwait(true))}).";
                    case "Interment":
                        return $"Interment {Check(await cemetery.RecordIntermentAsync(new IntermentRequest
                        {
                            CompanyId = c.CompanyId, PlotReference = i.Optional("Plot") ?? string.Empty, DeceasedName = i.Optional("Deceased") ?? string.Empty,
                            DateOfDeath = DateOnly.ParseExact(i.Optional("Died") ?? string.Empty, "yyyy-MM-dd", CultureInfo.InvariantCulture), IntermentDate = i.Date("Date"), IntermentFee = i.Number("Price"),
                            FeeAccountCode = i.Optional("Account"),
                        }).ConfigureAwait(true))} recorded.";
                    default:
                        IReadOnlyList<PlotEntry> list = await cemetery.PlotsAsync(c.CompanyId, i.Optional("Cemetery") ?? string.Empty, null).ConfigureAwait(true);
                        return string.Join(Environment.NewLine, list.Select(x => $"{x.Reference,-16} {x.Status,-10} {M(x.Price),10}  {x.Interred}/{x.Capacity}  {x.HolderCode}"));
                }
            },
            List = async c => Table(await c.Get<ICemeteryService>().BurialRegisterAsync(c.CompanyId, new DateOnly(DateTime.Today.Year, 1, 1), DateOnly.FromDateTime(DateTime.Today)).ConfigureAwait(true),
                ("Interment", b => b.IntermentNumber), ("Plot", b => b.PlotReference), ("Deceased", b => b.DeceasedName), ("Died", b => b.DateOfDeath.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
                ("Interred", b => b.IntermentDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))),
        };
        yield return new Operation
        {
            Module = module, Title = "Council licences", ListTitle = "Renewals due in 60 days",
            Description = "Issues a licence (business, liquor, shop, building plan, hawker…) with its fee and renewal cycle, records inspections (a failed one suspends it), renews it "
                + "(late renewals with a penalty %), or revokes it.",
            Fields = [Pick("Step", "Step", ["Issue", "Inspection", "Renew", "Revoke"]), Pick("Kind", "Licence kind", Enum.GetNames<CouncilLicenceKind>()), Opt("Holder", "Holder customer code (issue)"),
                Opt("Stand", "Stand number (optional)"), Opt("Description", "Description / inspector / reason"), Num("Cycle", "Renewal cycle (months)", "12"), Num("Fee", "Fee / penalty %", "0"),
                Opt("Account", "Revenue / penalty account"), Opt("Deferred", "Fees-in-advance account (issue; blank: revenue at once)"), Opt("Licence", "Licence number"),
                Tick("Passed", "Inspection passed", true), Opt("Notes", "Notes"), Today()],
            Run = async (c, i) =>
            {
                ICouncilLicensingService licensing = c.Get<ICouncilLicensingService>();
                string licence = i.Optional("Licence") ?? string.Empty;
                switch (i.Text("Step"))
                {
                    case "Issue":
                        IssuedLicence issued = Check(await licensing.IssueAsync(new IssueLicenceRequest
                        {
                            CompanyId = c.CompanyId, Kind = Enum.Parse<CouncilLicenceKind>(i.Text("Kind")), HolderCustomerId = await CustomerAsync(c, i.Optional("Holder") ?? string.Empty).ConfigureAwait(true),
                            PropertyId = i.Optional("Stand") is string stand ? await PropertyAsync(c, stand).ConfigureAwait(true) : null, Description = i.Optional("Description") ?? string.Empty,
                            IssuedOn = i.Date("Date"), CycleMonths = i.Whole("Cycle"), Fee = i.Number("Fee"), RevenueAccountCode = i.Optional("Account") ?? string.Empty,
                            DeferredIncomeAccountCode = i.Optional("Deferred"),
                        }).ConfigureAwait(true));
                        return $"Licence {issued.LicenceNumber}, expires {issued.ExpiresOn:yyyy-MM-dd}{(issued.FeeInvoiceNumber is null ? string.Empty : $"; fee invoice {issued.FeeInvoiceNumber}")}.";
                    case "Inspection":
                        return $"Licence is now {Check(await licensing.RecordInspectionAsync(c.CompanyId, licence, i.Date("Date"), i.Optional("Description") ?? string.Empty, i.Flag("Passed"), i.Optional("Notes")).ConfigureAwait(true))}.";
                    case "Renew":
                        LicenceRenewed renewed = Check(await licensing.RenewAsync(c.CompanyId, licence, i.Date("Date"), i.Number("Fee"), i.Optional("Account")).ConfigureAwait(true));
                        return $"Renewed to {renewed.Terms.NewExpiry:yyyy-MM-dd}: fee {M(renewed.Terms.Fee)}{(renewed.Terms.Late ? $", late penalty {M(renewed.Terms.Penalty)}" : string.Empty)}"
                            + (renewed.InvoiceNumber is null ? "." : $" ({renewed.InvoiceNumber}).");
                    default:
                        return Check(await licensing.RevokeAsync(c.CompanyId, licence, i.Optional("Description") ?? string.Empty).ConfigureAwait(true), "Licence revoked.");
                }
            },
            List = async c => Table(await c.Get<ICouncilLicensingService>().RenewalsDueAsync(c.CompanyId, DateOnly.FromDateTime(DateTime.Today), 60).ConfigureAwait(true),
                ("Licence", x => x.LicenceNumber), ("Kind", x => x.Kind), ("Holder", x => x.HolderCode), ("Description", x => x.Description), ("Expires", x => x.ExpiresOn.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
                ("Days left", x => x.DaysLeft)),
        };
        yield return new Operation
        {
            Module = module, Title = "Facility bookings",
            Description = "Halls, grounds and other council facilities: create one with its daily rate, book it for a customer (the fee invoiced; double bookings refused), cancel a "
                + "booking with a refund %, and see a facility's calendar.",
            Fields = [Pick("Step", "Step", ["Create facility", "Book", "Cancel", "Calendar"]), Req("Facility", "Facility code (booking number to cancel)"), Opt("Name", "Name / purpose"),
                Num("Rate", "Daily rate / refund %", "0"), Opt("Account", "Revenue account (create)"), Opt("Customer", "Customer code (book)"), Today("From", "From"), Today("To", "To"), Today("Date", "Date")],
            Run = async (c, i) =>
            {
                IFacilityBookingService bookings = c.Get<IFacilityBookingService>();
                switch (i.Text("Step"))
                {
                    case "Create facility":
                        return Check(await bookings.CreateFacilityAsync(c.CompanyId, i.Text("Facility"), i.Optional("Name") ?? string.Empty, i.Number("Rate"), i.Optional("Account") ?? string.Empty).ConfigureAwait(true),
                            _ => "Facility created.");
                    case "Book":
                        FacilityBooked booked = Check(await bookings.BookAsync(new FacilityBookingRequest
                        {
                            CompanyId = c.CompanyId, FacilityCode = i.Text("Facility"), CustomerId = await CustomerAsync(c, i.Optional("Customer") ?? string.Empty).ConfigureAwait(true), FromDate = i.Date("From"),
                            ToDate = i.Date("To"), Purpose = i.Optional("Name") ?? string.Empty, BookedOn = i.Date("Date"),
                        }).ConfigureAwait(true));
                        return $"Booking {booked.BookingNumber}: {booked.Days} day(s), fee {M(booked.Fee)}.";
                    case "Cancel":
                        return $"Cancelled; {M(Check(await bookings.CancelAsync(c.CompanyId, i.Text("Facility"), i.Date("Date"), i.Number("Rate")).ConfigureAwait(true)))} refunded.";
                    default:
                        IReadOnlyList<FacilityCalendarEntry> calendar = Check(await bookings.CalendarAsync(c.CompanyId, i.Text("Facility"), i.Date("From"), i.Date("To")).ConfigureAwait(true));
                        return calendar.Count == 0 ? "Free throughout." : string.Join(Environment.NewLine, calendar.Select(b =>
                            $"{b.FromDate:yyyy-MM-dd} – {b.ToDate:yyyy-MM-dd}  {b.BookingNumber,-12} {b.CustomerCode,-10} {b.Purpose}"));
                }
            },
        };
        yield return new Operation
        {
            Module = module, Title = "Property ownership",
            Description = "Registers a stand's owners and their shares (\"CUST1:50:R; CUST2:50\" - R marks the ratepayer), transfers it to buyers against a clearance certificate, "
                + "and shows the owners now or the full history. Also keeps the tenants, occupiers and managing agents (each optionally copied on the bills) and a correspondence "
                + "address separate from the property's own.",
            Fields = [Pick("Step", "Step", ["Register", "Transfer", "Settle transfer", "Owners", "History", "Add party", "End party", "Parties", "Correspondence address", "Bill goes to"]),
                Req("Stand", "Stand number"), Opt("Owners", "Owners: CUSTOMER:share[:R]; …"), Opt("Clearance", "Clearance certificate (transfer)"),
                Opt("Deeds", "Deeds reference"), Pick("Role", "Party is", Enum.GetNames<PropertyPartyRole>()), Opt("PartyName", "Party's name"), Opt("PartyAccount", "Party's own account (customer)"),
                Opt("PartyPhone", "Party's phone"), Opt("PartyEmail", "Party's e-mail"), Opt("Address", "Address (party's, or the correspondence address)"), Tick("Copies", "Copy the party on bills"),
                OptNum("Party", "Party id (end)"), Today()],
            Run = async (c, i) =>
            {
                IOwnershipService ownership = c.Get<IOwnershipService>();
                long property = await PropertyAsync(c, i.Text("Stand")).ConfigureAwait(true);
                async Task<List<One4All.Application.Municipal.OwnerShare>> Shares()
                {
                    List<One4All.Application.Municipal.OwnerShare> shares = [];
                    foreach (string part in (i.Optional("Owners") ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    {
                        string[] bits = part.Split(':', StringSplitOptions.TrimEntries);
                        shares.Add(bits.Length is 2 or 3 && decimal.TryParse(bits[1], NumberStyles.Number, CultureInfo.InvariantCulture, out decimal share)
                            ? new One4All.Application.Municipal.OwnerShare(await CustomerAsync(c, bits[0]).ConfigureAwait(true), share, bits.Length == 3 && bits[2].Equals("R", StringComparison.OrdinalIgnoreCase))
                            : throw new OperationException($"Write each owner as CUSTOMER:share[:R] (\"{part}\")."));
                    }

                    return shares;
                }

                static string Show(IReadOnlyList<OwnerEntry> owners) => owners.Count == 0 ? "No owners recorded." : string.Join(Environment.NewLine, owners.Select(o =>
                    $"{o.CustomerCode,-12} {o.SharePercent,6:0.##}%{(o.IsRatepayer ? " ratepayer" : string.Empty),-10} {o.OwnedFrom:yyyy-MM-dd} – {o.OwnedTo?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "now"}  {o.Reference}"));
                switch (i.Text("Step"))
                {
                    case "Register":
                        return Check(await ownership.RegisterOwnersAsync(c.CompanyId, property, await Shares().ConfigureAwait(true), i.Date("Date"), i.Optional("Deeds")).ConfigureAwait(true), "Owners registered.");
                    case "Settle transfer":
                        IReadOnlyList<TransferSettlementLine> settled = Check(await ownership.SettleTransferAsync(c.CompanyId, property, i.Date("Date")).ConfigureAwait(true));
                        return settled.Count == 0 ? "Nothing to settle: the seller was billed exactly for their days." : string.Join(Environment.NewLine, settled.Select(l =>
                            $"{l.TariffCode,-12} {l.PeriodStart:yyyy-MM-dd} – {l.PeriodEnd:yyyy-MM-dd}  charge {M(l.PeriodCharge)}, billed {M(l.BilledToSeller)}, seller's share {M(l.SellerShare)}: "
                            + (l.Settlement > 0m ? $"{M(l.Settlement)} credited to the seller, charged to the buyer" : $"{M(-l.Settlement)} charged to the seller, credited to the buyer")));
                    case "Transfer":
                        return Check(await ownership.TransferAsync(c.CompanyId, property, await Shares().ConfigureAwait(true), i.Date("Date"), i.Optional("Clearance") ?? string.Empty, i.Optional("Deeds")).ConfigureAwait(true),
                            "Transferred.");
                    case "Owners":
                        return Show(await ownership.OwnersAsync(c.CompanyId, property, i.Date("Date")).ConfigureAwait(true));
                    case "Add party":
                        long partyId = Check(await ownership.AddPartyAsync(c.CompanyId, property, new NewPropertyParty(Enum.Parse<PropertyPartyRole>(i.Text("Role")), i.Optional("PartyName") ?? string.Empty,
                            i.Date("Date"))
                        {
                            CustomerCode = i.Optional("PartyAccount"), Phone = i.Optional("PartyPhone"), Email = i.Optional("PartyEmail"), Address = i.Optional("Address"), ReceivesBills = i.Flag("Copies"),
                        }).ConfigureAwait(true));
                        return $"Recorded (party {partyId}).";
                    case "End party":
                        return Check(await ownership.EndPartyAsync(c.CompanyId, (long)(i.OptionalNumber("Party") ?? 0m), i.Date("Date")).ConfigureAwait(true), "Ended.");
                    case "Parties":
                        IReadOnlyList<PropertyPartyEntry> parties = await ownership.PartiesAsync(c.CompanyId, property, null).ConfigureAwait(true);
                        return parties.Count == 0 ? "No tenants, occupiers or agents recorded." : string.Join(Environment.NewLine, parties.Select(p =>
                            $"{p.Id,6} {p.Role,-14} {p.Name,-30} {p.From:yyyy-MM-dd} – {p.To?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "now"}{(p.ReceivesBills ? "  copied on bills" : string.Empty)}"));
                    case "Correspondence address":
                        return Check(await ownership.SetCorrespondenceAddressAsync(c.CompanyId, property, i.Optional("Address")).ConfigureAwait(true), "Correspondence address set.");
                    case "Bill goes to":
                        return string.Join(Environment.NewLine, Check(await ownership.BillRecipientsAsync(c.CompanyId, property, i.Date("Date")).ConfigureAwait(true))
                            .Select(r => $"{r.Capacity,-15} {r.Name}  {r.Address ?? "(no address)"}  {r.Email}"));
                    default:
                        return Show(await ownership.HistoryAsync(c.CompanyId, property).ConfigureAwait(true));
                }
            },
        };
        yield return new Operation
        {
            Module = module, Title = "Disconnections and reconnections",
            Description = "Lists stands whose arrears for a service pass a threshold, raises a disconnection (or reconnection) order for one, executes it (a reconnection with its fee) or "
                + "cancels it.",
            Fields = [Pick("Step", "Step", ["Candidates", "Raise", "Execute", "Cancel"]), Pick("Service", "Service", Enum.GetNames<ServiceType>()), Num("Threshold", "Arrears threshold", "0"),
                Opt("Stand", "Stand number (raise)"), Pick("Kind", "Order", Enum.GetNames<ServiceOrderKind>()), Opt("Order", "Order number"), Num("Fee", "Reconnection fee", "0"), Opt("Account", "Fee revenue account"), Today()],
            Run = async (c, i) =>
            {
                IServiceOrderService orders = c.Get<IServiceOrderService>();
                ServiceType service = Enum.Parse<ServiceType>(i.Text("Service"));
                switch (i.Text("Step"))
                {
                    case "Candidates":
                        IReadOnlyList<DisconnectionCandidate> candidates = await orders.CandidatesAsync(c.CompanyId, service, i.Date("Date"), i.Number("Threshold")).ConfigureAwait(true);
                        return candidates.Count == 0 ? "No stands over the threshold." : string.Join(Environment.NewLine, candidates.Select(x => $"{x.StandNumber,-12} {M(x.Arrears),14}  oldest due {x.OldestDueDate:yyyy-MM-dd}"));
                    case "Raise":
                        return $"Order {Check(await orders.RaiseAsync(c.CompanyId, await PropertyAsync(c, i.Optional("Stand") ?? string.Empty).ConfigureAwait(true), service, Enum.Parse<ServiceOrderKind>(i.Text("Kind")),
                            i.Date("Date"), i.Number("Threshold")).ConfigureAwait(true))} raised.";
                    case "Execute":
                        ReconnectionFee? fee = i.Number("Fee") > 0m ? new ReconnectionFee(i.Number("Fee"), i.Optional("Account") ?? string.Empty) : null;
                        return Check(await orders.ExecuteAsync(c.CompanyId, i.Optional("Order") ?? string.Empty, i.Date("Date"), fee).ConfigureAwait(true), "Order executed.");
                    default:
                        return Check(await orders.CancelAsync(c.CompanyId, i.Optional("Order") ?? string.Empty).ConfigureAwait(true), "Order cancelled.");
                }
            },
        };
        yield return new Operation
        {
            Module = module, Title = "Tariff catalogue",
            Description = "Lists a service's tariffs in force on a date, or resolves which tariff applies to a consumer category in a ward.",
            Fields = [Pick("Step", "Step", ["List", "Resolve"]), Pick("Service", "Service", Enum.GetNames<ServiceType>()), Pick("Category", "Consumer category (resolve)", Enum.GetNames<ConsumerCategory>()),
                Opt("Ward", "Ward (resolve)"), Today()],
            Run = async (c, i) =>
            {
                ITariffCatalogueService catalogue = c.Get<ITariffCatalogueService>();
                ServiceType service = Enum.Parse<ServiceType>(i.Text("Service"));
                static string Show(CatalogueTariff t) => $"{t.Code,-10} {t.Name,-30} {t.Category,-14} {t.Ward ?? "all wards",-12} from {t.EffectiveFrom:yyyy-MM-dd}";
                if (i.Text("Step") == "Resolve")
                {
                    return Show(Check(await catalogue.ResolveAsync(c.CompanyId, service, Enum.Parse<ConsumerCategory>(i.Text("Category")), i.Optional("Ward"), i.Date("Date")).ConfigureAwait(true)));
                }

                IReadOnlyList<CatalogueTariff> tariffs = await catalogue.ListAsync(c.CompanyId, service, i.Date("Date")).ConfigureAwait(true);
                return tariffs.Count == 0 ? "No tariffs in force." : string.Join(Environment.NewLine, tariffs.Select(Show));
            },
        };
    }

    private static async Task<long> PropertyAsync(OperationContext c, string stand)
    {
        string key = stand.Trim();
        return await c.Db.Properties.AsNoTracking().Where(p => p.CompanyId == c.CompanyId && p.StandNumber == key).Select(p => (long?)p.Id).FirstOrDefaultAsync().ConfigureAwait(true)
            ?? throw new OperationException($"No stand {key}.");
    }

    // The bill's services block: each active service with its tariff, and for a metered one the last readings up to the date.
    private static async Task<string> ServicesAndReadingsAsync(OperationContext c, long propertyId, DateOnly asAt)
    {
        Core.Municipal.Property property = await c.Db.Properties.AsNoTracking().Include(p => p.Services).SingleAsync(p => p.Id == propertyId).ConfigureAwait(true);
        List<Core.Municipal.PropertyServiceLink> services = [.. property.Services.Where(s => s.IsActive && s.StartDate <= asAt && (s.EndDate == null || s.EndDate >= asAt))];
        if (services.Count == 0)
        {
            return string.Empty;
        }

        List<long> tariffIds = [.. services.Select(s => s.TariffId)];
        Dictionary<long, Core.Municipal.Tariff> tariffs = await c.Db.Tariffs.AsNoTracking().Include(t => t.Blocks).Where(t => tariffIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id).ConfigureAwait(true);
        List<string> text = [string.Empty, $"SERVICES AND READINGS - stand {property.StandNumber}, {property.Ward}"];
        foreach (Core.Municipal.PropertyServiceLink service in services.OrderBy(s => s.ServiceType))
        {
            Core.Municipal.Tariff? tariff = tariffs.GetValueOrDefault(service.TariffId);
            List<string> terms = [];
            if (tariff is not null)
            {
                if (tariff.FixedCharge != 0m)
                {
                    terms.Add($"fixed {M(tariff.FixedCharge)}");
                }

                if (tariff.PerUnitRate != 0m)
                {
                    terms.Add($"{M(tariff.PerUnitRate)} a unit");
                }

                if (tariff.RatePerValuationCents != 0m)
                {
                    terms.Add($"{tariff.RatePerValuationCents.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture)} c in the dollar of {M(property.Valuation)}");
                }

                if (tariff.RatePerSquareMetre != 0m)
                {
                    terms.Add($"{M(tariff.RatePerSquareMetre)} a m² of {Q(property.SizeSquareMetres)}");
                }

                terms.AddRange(tariff.Blocks.Select(b => $"{Q(b.FromUnit)}–{(b.ToUnit is decimal to ? Q(to) : "up")} at {M(b.Rate)}"));
                if (tariff.MinimumCharge != 0m)
                {
                    terms.Add($"minimum {M(tariff.MinimumCharge)}");
                }
            }

            text.Add($"  {service.ServiceType,-10} {(tariff is null ? "?" : $"{tariff.Code} {tariff.Name}")}: {(terms.Count == 0 ? "no charge" : string.Join("; ", terms))}");
            if (service.MeterId is long meterId)
            {
                string serial = await c.Db.Meters.AsNoTracking().Where(m => m.Id == meterId).Select(m => m.Serial).FirstOrDefaultAsync().ConfigureAwait(true) ?? "?";
                List<Core.Municipal.MeterReading> readings = await c.Db.MeterReadings.AsNoTracking().Where(r => r.MeterId == meterId && r.ReadingDate <= asAt)
                    .OrderByDescending(r => r.ReadingDate).Take(2).ToListAsync().ConfigureAwait(true);
                text.AddRange(readings.Count == 0
                    ? [$"    meter {serial}: no readings yet"]
                    : readings.Select(r => $"    meter {serial}: {D(r.ReadingDate)} read {Q(r.Reading)} (previous {Q(r.PreviousReading)}), used {Q(r.Consumption)} - {r.Kind}"));
            }
        }

        return Environment.NewLine + string.Join(Environment.NewLine, text);
    }

    // The printed rates clearance certificate (§14 I.153): the council, the stand and its owners, that the rates and charges are paid with the advance held, and the validity.
    private static async Task<string> ClearanceCertificateTextAsync(OperationContext c, string certificateNumber)
    {
        Core.Municipal.ClearanceCertificate certificate = await c.Db.Set<Core.Municipal.ClearanceCertificate>().AsNoTracking()
            .FirstOrDefaultAsync(x => x.CompanyId == c.CompanyId && x.CertificateNumber == certificateNumber).ConfigureAwait(true) ?? throw new OperationException($"No certificate {certificateNumber}.");
        Core.Municipal.Property property = await c.Db.Properties.AsNoTracking().SingleAsync(p => p.Id == certificate.PropertyId).ConfigureAwait(true);
        string council = await c.Db.Companies.AsNoTracking().Where(x => x.Id == c.CompanyId).Select(x => x.Name).SingleAsync().ConfigureAwait(true);
        string ratepayer = await c.Db.Customers.AsNoTracking().Where(x => x.Id == property.RatepayerCustomerId).Select(x => x.Code + " " + x.Name).FirstOrDefaultAsync().ConfigureAwait(true) ?? "?";
        string issuer = await c.Db.Users.AsNoTracking().Where(u => u.Id == certificate.IssuedByUserId).Select(u => u.DisplayName).FirstOrDefaultAsync().ConfigureAwait(true) ?? "-";
        const int width = 72;
        string Centre(string text) => text.Length >= width ? text : new string(' ', (width - text.Length) / 2) + text;
        return string.Join(Environment.NewLine,
        [
            Centre(council.ToUpperInvariant()), Centre("RATES CLEARANCE CERTIFICATE"), new string('=', width),
            $"Certificate number : {certificate.CertificateNumber}",
            $"Stand              : {property.StandNumber}, ward {property.Ward}" + (property.Address is null ? string.Empty : $", {property.Address}"),
            $"Ratepayer          : {ratepayer}",
            $"Municipal valuation: {M(property.Valuation)}",
            string.Empty,
            "It is certified that all rates, service charges and other amounts due to the council on the",
            $"above property have been paid up to {D(certificate.IssuedOn)}, and that {M(certificate.AdvanceHeld)} is held in advance",
            "against charges to come.",
            string.Empty,
            $"Issued on          : {D(certificate.IssuedOn)}",
            $"Valid until        : {D(certificate.ValidUntil)}",
            $"Issued by          : {issuer}",
            string.Empty,
            "Signature: ______________________          Official stamp:",
        ]);
    }
}
