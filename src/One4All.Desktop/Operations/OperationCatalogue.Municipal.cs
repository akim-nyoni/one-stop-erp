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
            Description = "Quotes what a stand must pay for a clearance certificate (its balance plus so many months' billing in advance), and issues the certificate once that is "
                + "paid, valid for so many days.",
            Fields = [Pick("Step", "Step", ["Quote", "Issue"]), Req("Stand", "Stand number"), Num("Months", "Months in advance", "3"), Num("Valid", "Valid for (days)", "60"), Today()],
            Run = async (c, i) =>
            {
                IClearanceService clearance = c.Get<IClearanceService>();
                long property = await PropertyAsync(c, i.Text("Stand")).ConfigureAwait(true);
                if (i.Text("Step") == "Quote")
                {
                    One4All.Application.Municipal.ClearanceQuote quote = Check(await clearance.QuoteAsync(c.CompanyId, property, i.Date("Date"), i.Whole("Months")).ConfigureAwait(true));
                    return $"Balance {M(quote.Balance)} + {quote.MonthsInAdvance} month(s) at {M(quote.AverageMonthlyBilling)} = advance {M(quote.AdvanceRequired)}; to pay {M(quote.AmountDue)}"
                        + (quote.CanIssue ? " — can be issued now." : ".");
                }

                IssuedClearance issued = Check(await clearance.IssueAsync(c.CompanyId, property, i.Date("Date"), i.Whole("Months"), i.Whole("Valid"), c.UserId).ConfigureAwait(true));
                return $"Certificate {issued.CertificateNumber} for {issued.StandNumber}, valid until {issued.ValidUntil:yyyy-MM-dd}; advance held {M(issued.AdvanceHeld)}.";
            },
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
    }

    private static async Task<long> PropertyAsync(OperationContext c, string stand)
    {
        string key = stand.Trim();
        return await c.Db.Properties.AsNoTracking().Where(p => p.CompanyId == c.CompanyId && p.StandNumber == key).Select(p => (long?)p.Id).FirstOrDefaultAsync().ConfigureAwait(true)
            ?? throw new OperationException($"No stand {key}.");
    }
}
