using System.Globalization;
using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Housing;
using One4All.Application.Housing;
using One4All.Core.Housing;
using static One4All.Desktop.Operations.Ops;

namespace One4All.Desktop.Operations;

/// <content>Housing operations: the waiting list, allocation runs and offers, and the stand register.</content>
public static partial class OperationCatalogue
{
    private static IEnumerable<Operation> Housing()
    {
        const string module = "Housing";
        yield return new Operation
        {
            Module = module, Title = "Register a waiting-list applicant", ListTitle = "Applicants",
            Description = "Registers an applicant (a second registration with the same national ID is refused) and gives the waiting-list number and priority score.",
            Fields = [Req("Id", "National ID"), Req("Name", "Full name"), Pick("Category", "Category", Enum.GetNames<HousingCategory>()), Req("Born", "Date of birth (yyyy-MM-dd)"),
                Req("Resident", "Resident since (yyyy-MM-dd)"), Num("Dependants", "Dependants", "0"), Tick("Disability", "Has a disability"), Num("Income", "Monthly income", "0"),
                Pick("Tenure", "Current tenure", Enum.GetNames<CurrentTenure>()), Today("Registered", "Registration date"),
                Pick("Marital", "Marital status", Enum.GetNames<MaritalStatus>()), Pick("Regime", "Marriage regime", ["", .. Enum.GetNames<MaritalRegime>()]),
                Opt("SpouseId", "Spouse / co-applicant national ID"), Opt("SpouseName", "Spouse / co-applicant name")],
            Run = async (c, i) =>
            {
                WaitingListRegistration registered = Check(await c.Get<IWaitingListService>().RegisterAsync(new WaitingListApplication
                {
                    CompanyId = c.CompanyId, IdNumber = i.Text("Id"), FullName = i.Text("Name"), Category = Enum.Parse<HousingCategory>(i.Text("Category")), DateOfBirth = Day(i.Text("Born")),
                    ResidentSince = Day(i.Text("Resident")), Dependants = i.Whole("Dependants"), HasDisability = i.Flag("Disability"), MonthlyIncome = i.Number("Income"),
                    Tenure = Enum.Parse<CurrentTenure>(i.Text("Tenure")), RegistrationDate = i.Date("Registered"),
                    MaritalStatus = Enum.Parse<MaritalStatus>(i.Optional("Marital") ?? nameof(MaritalStatus.Single)),
                    MaritalRegime = i.Optional("Regime") is string regime ? Enum.Parse<MaritalRegime>(regime) : null,
                    SpouseIdNumber = i.Optional("SpouseId"), SpouseName = i.Optional("SpouseName"),
                }).ConfigureAwait(true));
                return $"Registered as {registered.WaitingListNumber}, priority score {registered.Score.Total:0.##} ({string.Join(", ", registered.Score.Lines.Select(l => $"{l.Factor} {l.Points:0.##}"))}).";
            },
            List = ApplicantsAsync,
        };
        yield return new Operation
        {
            Module = module, Title = "Stand lifecycle", ListTitle = "Stands",
            Description = "Moves a stand along raw land → planned → surveyed → under servicing → serviced → available → reserved / offered, and after sale → fully paid → "
                + "title applied → registered → handed over, never jumping a state, each move with its authority; withholds a stand (dispute or caveat) and releases it back; "
                + "consolidates free stands into one or re-subdivides one (\"S1-A:300; S1-B:200\"), lineage kept back to the parcel. History shows every move.",
            Fields = [Pick("Step", "Step", ["History", "Move", "Withhold", "Release hold", "Consolidate", "Re-subdivide"]), Req("Stand", "Stand number (consolidate: several, comma-separated)"),
                Pick("To", "Move to", Enum.GetNames<StandStatus>()), Opt("Authority", "Authority (resolution / minute / document)"), Opt("Reason", "Reason"),
                Opt("New", "New stand number (consolidate) or parts (re-subdivide)"), Today()],
            Run = async (c, i) =>
            {
                IStandLifecycleService lifecycle = c.Get<IStandLifecycleService>();
                string authority = i.Optional("Authority") ?? string.Empty;
                switch (i.Text("Step"))
                {
                    case "Move":
                        return $"Stand is now {Check(await lifecycle.MoveAsync(c.CompanyId, i.Text("Stand"), Enum.Parse<StandStatus>(i.Text("To")), i.Date("Date"), c.UserId, authority, i.Optional("Reason")).ConfigureAwait(true))}.";
                    case "Withhold":
                        return $"Stand is now {Check(await lifecycle.MoveAsync(c.CompanyId, i.Text("Stand"), StandStatus.Withheld, i.Date("Date"), c.UserId, authority, i.Optional("Reason")).ConfigureAwait(true))}.";
                    case "Release hold":
                        StandHistory held = Check(await lifecycle.HistoryAsync(c.CompanyId, i.Text("Stand")).ConfigureAwait(true));
                        StandStatus back = held.Moves.LastOrDefault(m => m.To == StandStatus.Withheld)?.From ?? throw new OperationException("The stand is not withheld.");
                        return $"Stand is now {Check(await lifecycle.MoveAsync(c.CompanyId, i.Text("Stand"), back, i.Date("Date"), c.UserId, authority, i.Optional("Reason")).ConfigureAwait(true))}.";
                    case "Consolidate":
                        return $"Consolidated into {Check(await lifecycle.ConsolidateAsync(c.CompanyId, i.Text("Stand").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                            i.Optional("New") ?? string.Empty, i.Date("Date"), c.UserId, authority).ConfigureAwait(true))}.";
                    case "Re-subdivide":
                        List<StandPart> parts = [.. (i.Optional("New") ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(p =>
                        {
                            string[] bits = p.Split(':', StringSplitOptions.TrimEntries);
                            return bits.Length == 2 && decimal.TryParse(bits[1], NumberStyles.Number, CultureInfo.InvariantCulture, out decimal size)
                                ? new StandPart(bits[0], size) : throw new OperationException($"Write each part as number:size (\"{p}\").");
                        })];
                        return $"Re-subdivided into {string.Join(", ", Check(await lifecycle.ResubdivideAsync(c.CompanyId, i.Text("Stand"), parts, i.Date("Date"), c.UserId, authority).ConfigureAwait(true)))}.";
                    default:
                        StandHistory h = Check(await lifecycle.HistoryAsync(c.CompanyId, i.Text("Stand")).ConfigureAwait(true));
                        return $"{h.StandNumber}: {h.Status}; next can be {string.Join(", ", h.Next)}{Environment.NewLine}"
                            + string.Join(Environment.NewLine, h.Moves.Select(m => $"  {D(m.Date)}  {m.From} → {m.To}  [{m.Authority}] {m.Reason}")) + Environment.NewLine
                            + "Lineage:" + Environment.NewLine + string.Join(Environment.NewLine, h.Lineage.Select(l => "  " + l));
                }
            },
            List = async c => Table(await c.Db.HousingStands.AsNoTracking().Where(s => s.CompanyId == c.CompanyId).OrderBy(s => s.StandNumber).Take(1000).ToListAsync().ConfigureAwait(true),
                ("Stand", s => s.StandNumber), ("Status", s => s.Status), ("Size m²", s => s.SizeSquareMetres), ("Cost", s => M(s.AllocatedCost)), ("Parent", s => s.ParentStandId),
                ("Consolidated into", s => s.ConsolidatedIntoStandId)),
        };
        yield return new Operation
        {
            Module = module, Title = "Waiting list enquiry and ranking", ListTitle = "Applicants",
            Description = "With a waiting-list number (or applicant id): the applicant's status, place by seniority and score. Without one: the ranking for a category, by seniority or score.",
            Fields = [Opt("Number", "Waiting-list number (blank for the ranking)"), Pick("Category", "Category", Enum.GetNames<HousingCategory>()),
                Pick("Order", "Order", Enum.GetNames<WaitingListOrder>()), Today("AsAt", "As at")],
            Run = async (c, i) =>
            {
                IWaitingListService list = c.Get<IWaitingListService>();
                if (i.Optional("Number") is string number)
                {
                    WaitingListEnquiry e = Check(await list.EnquireAsync(c.CompanyId, number, i.Date("AsAt")).ConfigureAwait(true));
                    return $"{e.WaitingListNumber} · {e.Category} · {e.Status} · place by seniority {(e.PositionBySeniority?.ToString(CultureInfo.InvariantCulture) ?? "-")} · score {e.Score.Total:0.##}";
                }

                IReadOnlyList<WaitingListPlace> ranking = await list.RankingAsync(c.CompanyId, Enum.Parse<HousingCategory>(i.Text("Category")), Enum.Parse<WaitingListOrder>(i.Text("Order")), i.Date("AsAt"))
                    .ConfigureAwait(true);
                return ranking.Count == 0 ? "Nobody is waiting in that category."
                    : string.Join(Environment.NewLine, ranking.Take(200).Select(p => $"{p.Position,4}. {p.WaitingListNumber,-12} {p.FullName,-30} since {p.RegistrationDate:yyyy-MM-dd} score {p.Score:0.##}"));
            },
            List = ApplicantsAsync,
        };
        yield return new Operation
        {
            Module = module, Title = "Allocation run", ListTitle = "Allocation runs",
            Description = "Propose a run for a scheme's available stands (seniority, score, or a seeded ballot anyone can re-run to verify), approve it (someone else; offers go out with a deadline), "
                + "cancel it, or verify a ballot's draw.",
            Fields = [Pick("Step", "Step", ["Propose", "Approve", "Cancel", "Verify draw"]), Opt("Scheme", "Scheme code (propose)"), Pick("Category", "Category", Enum.GetNames<HousingCategory>()),
                Pick("Method", "Method", Enum.GetNames<AllocationMethod>()), OptNum("Seed", "Ballot seed (blank: random)"), Opt("Run", "Run number"), Today("Date", "Date"), Num("OfferDays", "Days to accept an offer", "30")],
            Run = async (c, i) =>
            {
                IAllocationService allocation = c.Get<IAllocationService>();
                static string Show(AllocationRunView run) => string.Join(Environment.NewLine, run.Lines.Select(l => $"{l.Sequence,4}. stand {l.StandNumber,-10} → {l.WaitingListNumber,-12} {l.Status}"
                        + (l.CertificateNumber is null ? string.Empty : $" certificate {l.CertificateNumber}") + (l.OfferedPrice is decimal p ? $" at {M(p)}" : string.Empty))
                    .Prepend($"Run {run.RunNumber} {run.Status} ({run.Method}{(run.Seed is long s ? $", seed {s}" : string.Empty)}), input hash {run.InputHash[..12]}…"));
                switch (i.Text("Step"))
                {
                    case "Propose":
                        string code = i.Optional("Scheme") ?? throw new OperationException("Give the scheme code.");
                        long scheme = await c.Db.HousingSchemes.AsNoTracking().Where(s => s.CompanyId == c.CompanyId && s.Code == code).Select(s => (long?)s.Id).FirstOrDefaultAsync().ConfigureAwait(true)
                            ?? throw new OperationException($"No scheme {code}.");
                        return Show(Check(await allocation.ProposeAsync(new AllocationProposal
                        {
                            CompanyId = c.CompanyId, SchemeId = scheme, Category = Enum.Parse<HousingCategory>(i.Text("Category")), Method = Enum.Parse<AllocationMethod>(i.Text("Method")),
                            Seed = (long?)i.OptionalNumber("Seed"), AsAtDate = i.Date("Date"), UserId = c.UserId,
                        }).ConfigureAwait(true)));
                    case "Approve":
                        return Show(Check(await allocation.ApproveAsync(c.CompanyId, i.Text("Run"), c.UserId, i.Date("Date"), i.Whole("OfferDays")).ConfigureAwait(true)));
                    case "Cancel":
                        _ = Check(await allocation.CancelAsync(c.CompanyId, i.Text("Run")).ConfigureAwait(true), string.Empty);
                        return "Run cancelled.";
                    default:
                        DrawVerification v = Check(await allocation.VerifyDrawAsync(c.CompanyId, i.Text("Run")).ConfigureAwait(true));
                        return v.IsVerified ? "Verified: the inputs are unchanged and re-running the draw gives the same order."
                            : $"NOT verified — inputs unchanged: {v.HashMatches}; same order: {v.OrderMatches}.";
                }
            },
            List = async c => Table(await c.Db.Set<AllocationRun>().AsNoTracking().Where(r => r.CompanyId == c.CompanyId).OrderByDescending(r => r.Id).Take(100).ToListAsync().ConfigureAwait(true),
                ("Run", r => r.RunNumber), ("Scheme", r => r.SchemeId), ("Category", r => r.Category), ("Method", r => r.Method), ("Status", r => r.Status), ("Approved by", r => r.ApprovedByUserId),
                ("Approved", r => r.ApprovedOn?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))),
        };
        yield return new Operation
        {
            Module = module, Title = "Accept or decline an offer", ListTitle = "Stands",
            Description = "Records the applicant's answer to an allocation offer by its certificate number; a decline needs the reason and frees the stand.",
            Fields = [Req("Certificate", "Certificate number"), Pick("Answer", "Answer", ["Accept", "Decline"]), Opt("Reason", "Reason (decline)"), Today("Date", "Date")],
            Run = async (c, i) =>
            {
                IAllocationService allocation = c.Get<IAllocationService>();
                if (i.Text("Answer") == "Accept")
                {
                    _ = Check(await allocation.AcceptAsync(c.CompanyId, i.Text("Certificate"), i.Date("Date")).ConfigureAwait(true), string.Empty);
                    return "Offer accepted.";
                }

                _ = Check(await allocation.DeclineAsync(c.CompanyId, i.Text("Certificate"), i.Date("Date"), i.Optional("Reason") ?? string.Empty).ConfigureAwait(true), string.Empty);
                return "Offer declined; the stand is available again.";
            },
            List = async c => Table(await c.Db.HousingStands.AsNoTracking().Where(s => s.CompanyId == c.CompanyId).OrderBy(s => s.StandNumber).Take(1000).ToListAsync().ConfigureAwait(true),
                ("Stand", s => s.StandNumber), ("Scheme", s => s.SchemeId), ("Size m²", s => Q(s.SizeSquareMetres)), ("Price", s => M(s.SellingPrice)), ("Cost", s => M(s.AllocatedCost)),
                ("Status", s => s.Status), ("Holder", s => s.CurrentHolderCustomerId)),
        };
        yield return new Operation
        {
            Module = module, Title = "Rental units and tenancies", ListTitle = "Tenancies",
            Description = "Add unit: a council rental unit with its rooms and rent. Let: a tenancy to a customer (the deposit is held as a liability). Rent run: bills every tenancy "
                + "for a month (lodger fees included). End: final rent, the deposit applied to arrears and the rest refunded. Occupancy limit: the most people a unit may house.",
            Fields = [Pick("Step", "Step", ["Add unit", "Let", "Rent run", "End tenancy", "Occupancy limit"]), Opt("Unit", "Unit code"), Opt("Description", "Description (add unit)"),
                Pick("Type", "Unit type", Enum.GetNames<RentalUnitType>()), Num("Rooms", "Rooms", "1"), Num("Rent", "Monthly rent", "0"), Opt("RentAccount", "Rent income account (add unit)"),
                Opt("Tenant", "Tenant customer code (let)"), Opt("Tenancy", "Tenancy number (end)"), Num("Deposit", "Deposit (let)", "0"), Opt("Bank", "Bank account (deposit / refund)"),
                Opt("DepositAccount", "Deposit liability account (let)"), OptNum("Limit", "Most people (blank: no limit)"), Today()],
            Run = async (c, i) =>
            {
                IRentalHousingService rentals = c.Get<IRentalHousingService>();
                switch (i.Text("Step"))
                {
                    case "Add unit":
                        return Check(await rentals.AddUnitAsync(c.CompanyId, i.Text("Unit"), i.Optional("Description") ?? i.Text("Unit"), Enum.Parse<RentalUnitType>(i.Text("Type")), i.Whole("Rooms"),
                            i.Number("Rent"), i.Text("RentAccount"), i.Date("Date")).ConfigureAwait(true), id => $"Unit {i.Text("Unit").ToUpperInvariant()} added.");
                    case "Let":
                        string tenancy = Check(await rentals.LetAsync(new LetUnitRequest
                        {
                            CompanyId = c.CompanyId, UnitCode = i.Text("Unit"), TenantCustomerId = await CustomerAsync(c, i.Text("Tenant")).ConfigureAwait(true), StartDate = i.Date("Date"),
                            Deposit = i.Number("Deposit"), BankAccountCode = i.Optional("Bank") ?? string.Empty, DepositAccountCode = i.Optional("DepositAccount") ?? string.Empty,
                        }).ConfigureAwait(true));
                        return $"Let as tenancy {tenancy}.";
                    case "Rent run":
                        RentRunResult run = Check(await rentals.RunRentAsync(c.CompanyId, i.Date("Date")).ConfigureAwait(true));
                        return $"{run.TenanciesBilled} tenancies billed, {M(run.TotalRent)}.";
                    case "End tenancy":
                        TenancyEnded ended = Check(await rentals.EndTenancyAsync(c.CompanyId, i.Text("Tenancy"), i.Date("Date"), i.Optional("Bank") ?? string.Empty).ConfigureAwait(true));
                        return $"Ended: final rent {M(ended.FinalRent)}; deposit {M(ended.Settlement.AppliedToArrears)} applied to arrears, {M(ended.Settlement.Refund)} refunded.";
                    default:
                        int? limit = i.OptionalNumber("Limit") is decimal most ? (int)most : null;
                        return Check(await c.Get<IRentalOccupancyService>().SetMaxOccupantsAsync(c.CompanyId, i.Text("Unit"), limit).ConfigureAwait(true), "Occupancy limit saved.");
                }
            },
            List = async c => Table(await (from t in c.Db.Set<Tenancy>().AsNoTracking()
                                           join u in c.Db.Set<RentalUnit>().AsNoTracking() on t.UnitId equals u.Id
                                           where t.CompanyId == c.CompanyId
                                           orderby t.Id descending
                                           select new { t.TenancyNumber, u.Code, t.StartDate, t.EndDate, t.MonthlyRent, t.Deposit, t.Status, u.MaxOccupants }).Take(300).ToListAsync().ConfigureAwait(true),
                ("Tenancy", t => t.TenancyNumber), ("Unit", t => t.Code), ("From", t => D(t.StartDate)), ("To", t => t.EndDate is DateOnly end ? D(end) : "-"), ("Rent", t => M(t.MonthlyRent)),
                ("Deposit", t => M(t.Deposit)), ("Status", t => t.Status), ("Most people", t => t.MaxOccupants)),
        };
        yield return new Operation
        {
            Module = module, Title = "Occupants and lodger permits", ListTitle = "Lodger permits",
            Description = "Add occupant / move out: the household living with the tenant, within the unit's occupancy limit. Permit: a lodger on a permit (the tenant keeps a room of their own; "
                + "a monthly fee goes on the rent). Revoke: ends a permit. Who lives there: the household and lodgers on a date.",
            Fields = [Pick("Step", "Step", ["Who lives there", "Add occupant", "Move out", "Issue permit", "Revoke permit"]), Req("Tenancy", "Tenancy number (permit: to revoke, its number)"),
                Opt("Name", "Name (occupant / lodger)"), Opt("IdNumber", "National ID"), Opt("Relationship", "Relationship to the tenant"), OptNum("Occupant", "Occupant id (move out)"),
                Num("Rooms", "Rooms let to the lodger", "1"), Num("Fee", "Monthly lodger fee", "0"), Opt("FeeAccount", "Lodger fee income account"), Today("Date", "Date / from"),
                Today("Expires", "Permit last day"), Opt("Reason", "Reason (revoke)")],
            Run = async (c, i) =>
            {
                IRentalOccupancyService occupancy = c.Get<IRentalOccupancyService>();
                switch (i.Text("Step"))
                {
                    case "Add occupant":
                        return Check(await occupancy.AddOccupantAsync(c.CompanyId, i.Text("Tenancy"), i.Optional("Name") ?? string.Empty, i.Optional("IdNumber"), i.Optional("Relationship") ?? string.Empty,
                            i.Date("Date")).ConfigureAwait(true), id => $"Occupant {id} added.");
                    case "Move out":
                        return Check(await occupancy.RemoveOccupantAsync(c.CompanyId, i.Text("Tenancy"), (long)(i.OptionalNumber("Occupant") ?? 0m), i.Date("Date")).ConfigureAwait(true), "Moved out.");
                    case "Issue permit":
                        string permit = Check(await occupancy.IssueLodgerPermitAsync(new LodgerPermitRequest(c.CompanyId, i.Text("Tenancy"), i.Optional("Name") ?? string.Empty, i.Optional("IdNumber"),
                            i.Whole("Rooms"), i.Number("Fee"), i.Optional("FeeAccount"), i.Date("Date"), i.Date("Expires"))).ConfigureAwait(true));
                        return $"Permit {permit} issued.";
                    case "Revoke permit":
                        return Check(await occupancy.RevokeLodgerPermitAsync(c.CompanyId, i.Text("Tenancy"), i.Date("Date"), i.Optional("Reason") ?? string.Empty).ConfigureAwait(true), "Permit revoked.");
                    default:
                        TenancyOccupancy who = Check(await occupancy.OccupancyAsync(c.CompanyId, i.Text("Tenancy"), i.Date("Date")).ConfigureAwait(true));
                        return $"{who.UnitCode}: {who.People} people{(who.MaxOccupants is int max ? $" of {max}" : string.Empty)}. Household: {(who.Occupants.Count == 0 ? "-" : string.Join(", ", who.Occupants))}. "
                            + $"Lodgers: {(who.Lodgers.Count == 0 ? "-" : string.Join(", ", who.Lodgers))}.";
                }
            },
            List = async c => Table(await c.Db.Set<LodgerPermit>().AsNoTracking().Where(p => p.CompanyId == c.CompanyId).OrderByDescending(p => p.Id).Take(300).ToListAsync().ConfigureAwait(true),
                ("Permit", p => p.PermitNumber), ("Lodger", p => p.LodgerName), ("Rooms", p => p.Rooms), ("Fee", p => M(p.MonthlyFee)), ("From", p => D(p.IssuedOn)), ("To", p => D(p.LastDay)),
                ("Status", p => p.Status)),
        };
        yield return new Operation
        {
            Module = module, Title = "Compulsory acquisition and compensation", ListTitle = "Compensation claims",
            Description = "Lodge a claim on a land parcel, serve the notice (opening the objection window), record objections and decide them, assess, offer and record the answer, "
                + "refer a dispute and record the award (each moving the provision), pay in part or settle, take possession (once the window has closed with no objection standing), "
                + "or withdraw. Ids for objections, offers and disputes are given when they are recorded.",
            Fields = [Pick("Step", "Step", ["Lodge", "Serve notice", "Objection", "Decide objection", "Assess", "Offer", "Answer offer", "Dispute", "Award", "Pay part", "Settle", "Possession", "Withdraw"]),
                Opt("Claim", "Claim number"), Opt("Parcel", "Land parcel number (lodge)"), Opt("Party", "Claimant / objector / assessor / forum / notice reference"), Opt("Note", "Grounds / note"),
                OptNum("Amount", "Amount"), OptNum("Id", "Objection / offer / dispute id"), Tick("Yes", "Upheld / accepted"), Num("Days", "Objection window (days)", "30"),
                Opt("LandAccount", "Land cost account (lodge)"), Opt("ProvisionAccount", "Provision account (lodge)"), Opt("Bank", "Bank account (pay)"), Today()],
            Run = async (c, i) =>
            {
                ICompensationService compensation = c.Get<ICompensationService>();
                string claim = i.Optional("Claim") ?? string.Empty, party = i.Optional("Party") ?? string.Empty;
                decimal amount = i.OptionalNumber("Amount") ?? 0m;
                long id = (long)(i.OptionalNumber("Id") ?? 0m);
                DateOnly date = i.Date("Date");
                switch (i.Text("Step"))
                {
                    case "Lodge":
                        long parcel = await FindAsync(c.Db.LandParcels, p => p.CompanyId == c.CompanyId && p.ParcelNumber == (i.Optional("Parcel") ?? string.Empty).ToUpper(), p => p.Id, "land parcel",
                            i.Optional("Parcel") ?? string.Empty).ConfigureAwait(true);
                        return $"Claim {Check(await compensation.LodgeAsync(c.CompanyId, parcel, party, date, i.Optional("LandAccount") ?? string.Empty, i.Optional("ProvisionAccount") ?? string.Empty).ConfigureAwait(true))} lodged.";
                    case "Serve notice":
                        return Check(await compensation.ServeNoticeAsync(c.CompanyId, claim, party, date, i.Whole("Days")).ConfigureAwait(true), "Notice served.");
                    case "Objection":
                        return $"Objection {Check(await compensation.LodgeObjectionAsync(c.CompanyId, claim, party, i.Optional("Note") ?? string.Empty, date).ConfigureAwait(true))} lodged.";
                    case "Decide objection":
                        return Check(await compensation.DecideObjectionAsync(c.CompanyId, claim, id, i.Flag("Yes"), date, i.Optional("Note")).ConfigureAwait(true), i.Flag("Yes") ? "Objection upheld." : "Objection dismissed.");
                    case "Assess":
                        return Check(await compensation.AssessAsync(c.CompanyId, claim, party, date, amount).ConfigureAwait(true), $"Assessed at {M(amount)}; provided for.");
                    case "Offer":
                        return $"Offer {Check(await compensation.OfferAsync(c.CompanyId, claim, amount, date).ConfigureAwait(true))} made.";
                    case "Answer offer":
                        return Check(await compensation.RespondToOfferAsync(c.CompanyId, claim, id, i.Flag("Yes"), date).ConfigureAwait(true), i.Flag("Yes") ? "Offer accepted; provided for." : "Offer rejected.");
                    case "Dispute":
                        return $"Dispute {Check(await compensation.DisputeAsync(c.CompanyId, claim, party, date, i.Optional("Note")).ConfigureAwait(true))} referred.";
                    case "Award":
                        return Check(await compensation.AwardAsync(c.CompanyId, claim, id, amount, date).ConfigureAwait(true), $"Award of {M(amount)} recorded; provided for.");
                    case "Pay part":
                        return Check(await compensation.PayPartAsync(c.CompanyId, claim, amount, i.Optional("Bank") ?? string.Empty, date).ConfigureAwait(true), $"{M(amount)} paid.");
                    case "Settle":
                        return Check(await compensation.SettleAsync(c.CompanyId, claim, amount, i.Optional("Bank") ?? string.Empty, date).ConfigureAwait(true), "Claim settled.");
                    case "Possession":
                        return Check(await compensation.TakePossessionAsync(c.CompanyId, claim, date).ConfigureAwait(true), "Possession taken.");
                    default:
                        return Check(await compensation.WithdrawAsync(c.CompanyId, claim, date).ConfigureAwait(true), "Claim withdrawn; provision released.");
                }
            },
            List = async c => Table(await c.Db.Set<CompensationClaim>().AsNoTracking().Include(x => x.Steps).Where(x => x.CompanyId == c.CompanyId).OrderByDescending(x => x.Id).Take(300)
                    .ToListAsync().ConfigureAwait(true),
                ("Claim", x => x.ClaimNumber), ("Claimant", x => x.Claimant), ("Status", x => x.Status), ("Notice", x => x.NoticeServedOn is DateOnly n ? D(n) : "-"),
                ("Open steps", x => string.Join(", ", x.Steps.Where(s => s.Status == CompensationStepStatus.Open).Select(s => $"{s.Kind} {s.Id}"))), ("Assessed", x => x.AssessedAmount is decimal a ? M(a) : "-"),
                ("Provision", x => M(x.Provision)), ("Paid", x => M(x.PaidToDate)), ("Possession", x => x.PossessionOn is DateOnly o ? D(o) : "-")),
        };
        yield return new Operation
        {
            Module = module, Title = "Instalment sales of stands", ListTitle = "Instalment agreements",
            Description = "Sign: sells a stand on instalments (deposit, rate, zero / flat / reducing-balance interest, term) and shows the schedule. Raise due: bills the instalments due "
                + "by a date. Quote / settle early: the principal outstanding and the interest rebated when paid off now.",
            Fields = [Pick("Step", "Step", ["Sign", "Raise due", "Quote settlement", "Settle early"]), Opt("Stand", "Stand number (sign)"), Opt("Buyer", "Buyer customer code (sign)"),
                Opt("Agreement", "Agreement number (quote / settle)"), Num("Price", "Cash price", "0"), Num("Deposit", "Deposit", "0"), Num("Rate", "Annual interest %", "0"),
                Pick("Method", "Interest", Enum.GetNames<InterestMethod>()), Num("Term", "Term (months)", "12"), Today("Date", "Signed / as at"), Today("FirstDue", "First instalment due"),
                Opt("Sales", "Sales account"), Opt("Available", "Stands available (inventory) account"), Opt("Cost", "Cost of sales account"), Opt("Interest", "Interest income account"),
                Opt("Bank", "Bank account (deposit)")],
            Run = async (c, i) =>
            {
                IInstalmentSaleService sales = c.Get<IInstalmentSaleService>();
                switch (i.Text("Step"))
                {
                    case "Sign":
                        long stand = await FindAsync(c.Db.HousingStands, x => x.CompanyId == c.CompanyId && x.StandNumber == (i.Optional("Stand") ?? string.Empty).ToUpper(), x => x.Id, "stand",
                            i.Optional("Stand") ?? string.Empty).ConfigureAwait(true);
                        InstalmentSaleResult signed = Check(await sales.SignAsync(new InstalmentSaleRequest
                        {
                            CompanyId = c.CompanyId, StandId = stand, BuyerCustomerId = await CustomerAsync(c, i.Optional("Buyer") ?? string.Empty).ConfigureAwait(true), CashPrice = i.Number("Price"),
                            Deposit = i.Number("Deposit"), AnnualRatePercent = i.Number("Rate"), Method = Enum.Parse<InterestMethod>(i.Text("Method")), TermMonths = i.Whole("Term"),
                            SignedOn = i.Date("Date"), FirstDueDate = i.Date("FirstDue"), SalesAccountCode = i.Optional("Sales") ?? string.Empty,
                            StandsAvailableAccountCode = i.Optional("Available") ?? string.Empty, CostOfSalesAccountCode = i.Optional("Cost") ?? string.Empty,
                            InterestAccountCode = i.Optional("Interest") ?? string.Empty, BankAccountCode = i.Optional("Bank") ?? string.Empty,
                        }).ConfigureAwait(true));
                        return $"Agreement {signed.AgreementNumber}:" + Environment.NewLine + string.Join(Environment.NewLine, signed.Schedule.Select(x =>
                            $"{x.Number,3}  {D(x.DueDate)}  {M(x.Amount),12}  interest {M(x.Interest),10}  principal {M(x.Principal),10}  balance {M(x.BalanceAfter),12}"));
                    case "Raise due":
                        InstalmentRunResult run = Check(await sales.RaiseDueAsync(c.CompanyId, i.Date("Date")).ConfigureAwait(true));
                        return $"{run.InstalmentsRaised} instalment(s) raised, interest {M(run.InterestRaised)}.";
                    case "Quote settlement":
                        SettlementQuote quote = Check(await sales.QuoteSettlementAsync(c.CompanyId, i.Optional("Agreement") ?? string.Empty, i.Date("Date")).ConfigureAwait(true));
                        return $"Settles for {M(quote.PrincipalOutstanding)}; interest rebated {M(quote.InterestRebated)}.";
                    default:
                        SettlementQuote settled = Check(await sales.SettleEarlyAsync(c.CompanyId, i.Optional("Agreement") ?? string.Empty, i.Date("Date")).ConfigureAwait(true));
                        return $"Settled early: {M(settled.PrincipalOutstanding)} principal, {M(settled.InterestRebated)} interest rebated.";
                }
            },
            List = async c => Table(await c.Db.Set<InstalmentAgreement>().AsNoTracking().Where(a => a.CompanyId == c.CompanyId).OrderByDescending(a => a.Id).Take(300).ToListAsync().ConfigureAwait(true),
                ("Agreement", a => a.AgreementNumber), ("Stand", a => a.StandId), ("Buyer", a => a.BuyerCustomerId), ("Price", a => M(a.CashPrice)), ("Deposit", a => M(a.Deposit)),
                ("Rate %", a => Q(a.AnnualRatePercent)), ("Term", a => a.TermMonths), ("Signed", a => D(a.SignedOn)), ("Status", a => a.Status)),
        };
        yield return new Operation
        {
            Module = module, Title = "Transfer of ownership", ListTitle = "Transfer applications",
            Description = "Lodge a transfer (sale, cession, inheritance, donation, court order) to a new holder; verify each requirement with its document; the checklist shows what is "
                + "outstanding (rates and housing account clearances are checked live); submit, approve (by someone else), complete or reject.",
            Fields = [Pick("Step", "Step", ["Lodge", "Verify", "Checklist", "Submit", "Approve", "Complete", "Reject"]), Opt("Application", "Application number"), Opt("Stand", "Stand number (lodge)"),
                Pick("Type", "Transfer type", Enum.GetNames<TransferType>()), Opt("To", "New holder customer code (lodge)"), Num("Consideration", "Consideration", "0"),
                Pick("Requirement", "Requirement (verify)", Enum.GetNames<TransferRequirement>()), Opt("Document", "Document reference / reason"), Today()],
            Run = async (c, i) =>
            {
                ITransferApplicationService transfers = c.Get<ITransferApplicationService>();
                string application = i.Optional("Application") ?? string.Empty;
                DateOnly date = i.Date("Date");
                switch (i.Text("Step"))
                {
                    case "Lodge":
                        long stand = await FindAsync(c.Db.HousingStands, x => x.CompanyId == c.CompanyId && x.StandNumber == (i.Optional("Stand") ?? string.Empty).ToUpper(), x => x.Id, "stand",
                            i.Optional("Stand") ?? string.Empty).ConfigureAwait(true);
                        string lodged = Check(await transfers.LodgeAsync(new TransferLodgement
                        {
                            CompanyId = c.CompanyId, StandId = stand, TransferType = Enum.Parse<TransferType>(i.Text("Type")), ToCustomerId = await CustomerAsync(c, i.Optional("To") ?? string.Empty).ConfigureAwait(true),
                            Consideration = i.Number("Consideration"), Date = date,
                        }).ConfigureAwait(true));
                        return $"Transfer {lodged} lodged.";
                    case "Verify":
                        return Check(await transfers.VerifyAsync(c.CompanyId, application, Enum.Parse<TransferRequirement>(i.Text("Requirement")), i.Optional("Document") ?? string.Empty, c.UserId, date)
                            .ConfigureAwait(true), "Requirement verified.");
                    case "Checklist":
                        IReadOnlyList<TransferChecklistLine> lines = Check(await transfers.ChecklistAsync(c.CompanyId, application, date).ConfigureAwait(true));
                        return string.Join(Environment.NewLine, lines.Select(l => $"{(l.Satisfied ? "OK " : "-- ")} {l.Requirement}{(l.IsLive ? " (live)" : string.Empty)}{(l.Detail is null ? string.Empty : ": " + l.Detail)}"));
                    case "Submit":
                        return Check(await transfers.SubmitAsync(c.CompanyId, application, c.UserId, date).ConfigureAwait(true), "Submitted for approval.");
                    case "Approve":
                        return Check(await transfers.ApproveAsync(c.CompanyId, application, c.UserId, date).ConfigureAwait(true), "Approved.");
                    case "Complete":
                        return Check(await transfers.CompleteAsync(c.CompanyId, application, date).ConfigureAwait(true), "Transfer completed; the stand has its new holder.");
                    default:
                        return Check(await transfers.RejectAsync(c.CompanyId, application, i.Optional("Document") ?? string.Empty, date).ConfigureAwait(true), "Rejected.");
                }
            },
            List = async c => Table(await c.Db.Set<TransferApplication>().AsNoTracking().Where(a => a.CompanyId == c.CompanyId).OrderByDescending(a => a.Id).Take(300).ToListAsync().ConfigureAwait(true),
                ("Application", a => a.ApplicationNumber), ("Stand", a => a.StandId), ("Type", a => a.TransferType), ("Consideration", a => M(a.Consideration)), ("Status", a => a.Status)),
        };
        yield return new Operation
        {
            Module = module, Title = "Evictions", ListTitle = "Open eviction cases",
            Description = "Open a case on a tenancy in arrears (with the notice period), advance it reminder → demand → notice to vacate → legal handover → court order → eviction → unit "
                + "returned (each with its reference; the last settles the deposit), or withdraw it with the reason.",
            Fields = [Pick("Step", "Step", ["Open", "Advance", "Withdraw"]), Opt("Tenancy", "Tenancy number (open)"), Opt("Case", "Case number"), Num("Notice", "Notice period (days)", "30"),
                Pick("Stage", "Stage (advance)", Enum.GetNames<EvictionStage>()), Opt("Reference", "Reference / reason"), Opt("Bank", "Refund bank account (unit returned)"), Today()],
            Run = async (c, i) =>
            {
                IEvictionService evictions = c.Get<IEvictionService>();
                return i.Text("Step") switch
                {
                    "Open" => $"Case {Check(await evictions.OpenAsync(c.CompanyId, i.Optional("Tenancy") ?? string.Empty, i.Date("Date"), i.Whole("Notice"), c.UserId).ConfigureAwait(true))} opened.",
                    "Advance" => Check(await evictions.AdvanceAsync(c.CompanyId, i.Optional("Case") ?? string.Empty, Enum.Parse<EvictionStage>(i.Text("Stage")), i.Date("Date"), i.Optional("Reference"),
                        i.Optional("Bank"), c.UserId).ConfigureAwait(true), $"Moved to {i.Text("Stage")}."),
                    _ => Check(await evictions.WithdrawAsync(c.CompanyId, i.Optional("Case") ?? string.Empty, i.Optional("Reference") ?? string.Empty).ConfigureAwait(true), "Case withdrawn."),
                };
            },
            List = async c => Table(await c.Get<IEvictionService>().RegisterAsync(c.CompanyId, openOnly: true).ConfigureAwait(true),
                ("Case", e => e.CaseNumber), ("Tenancy", e => e.TenancyNumber), ("Unit", e => e.UnitCode), ("Arrears", e => M(e.ArrearsAtOpening)), ("Stage", e => e.Stage), ("Since", e => D(e.StageDate)),
                ("Status", e => e.Status)),
        };
        yield return new Operation
        {
            Module = module, Title = "Land acquisition", ListTitle = "Acquisitions",
            Description = "Open an acquisition (method, counterparty, authorising resolution), record its valuation, sign off each due-diligence item, capitalise costs into land WIP, "
                + "see its position (cost build-up and what still blocks it), complete it into the land bank, or abandon it (costs written off).",
            Fields = [Pick("Step", "Step", ["Open", "Valuation", "Sign off", "Add cost", "Position", "Complete", "Abandon"]), Opt("Acquisition", "Acquisition number"),
                Opt("Parcel", "Parcel number (open)"), Opt("Description", "Description / finding / reference"), Num("Area", "Area m² (open)", "0"), Pick("Method", "Method (open)", Enum.GetNames<AcquisitionMethod>()),
                Opt("Party", "Counterparty / valuer"), Opt("Resolution", "Resolution (open) / reason (abandon)"), Num("Amount", "Value / cost", "0"),
                Pick("Item", "Due-diligence item", Enum.GetNames<DueDiligenceItem>()), Tick("Acceptable", "Acceptable", true), Pick("Component", "Cost component", Enum.GetNames<AcquisitionCostComponent>()),
                Opt("Wip", "Land WIP account"), Opt("Expense", "Write-off account (abandon)"), Today()],
            Run = async (c, i) =>
            {
                ILandAcquisitionService land = c.Get<ILandAcquisitionService>();
                string acquisition = i.Optional("Acquisition") ?? string.Empty;
                DateOnly date = i.Date("Date");
                switch (i.Text("Step"))
                {
                    case "Open":
                        string opened = Check(await land.OpenAsync(new AcquisitionCase
                        {
                            CompanyId = c.CompanyId, ParcelNumber = i.Optional("Parcel") ?? string.Empty, Description = i.Optional("Description") ?? string.Empty, AreaSquareMetres = i.Number("Area"),
                            Method = Enum.Parse<AcquisitionMethod>(i.Text("Method")), Counterparty = i.Optional("Party") ?? string.Empty, ResolutionReference = i.Optional("Resolution") ?? string.Empty, Date = date,
                        }).ConfigureAwait(true));
                        return $"Acquisition {opened} opened.";
                    case "Valuation":
                        return Check(await land.RecordValuationAsync(c.CompanyId, acquisition, i.Optional("Party") ?? string.Empty, date, i.Number("Amount")).ConfigureAwait(true), "Valuation recorded.");
                    case "Sign off":
                        return Check(await land.SignOffAsync(c.CompanyId, acquisition, Enum.Parse<DueDiligenceItem>(i.Text("Item")), i.Optional("Description") ?? string.Empty, i.Flag("Acceptable"), c.UserId, date)
                            .ConfigureAwait(true), "Signed off.");
                    case "Add cost":
                        string journal = Check(await land.AddCostAsync(c.CompanyId, acquisition, new AcquisitionCostInput
                        {
                            Component = Enum.Parse<AcquisitionCostComponent>(i.Text("Component")), Amount = i.Number("Amount"), Date = date, Reference = i.Optional("Description") ?? string.Empty,
                            WipAccountCode = i.Optional("Wip") ?? string.Empty,
                        }).ConfigureAwait(true));
                        return $"Cost capitalised ({journal}).";
                    case "Position":
                        AcquisitionPosition position = Check(await land.PositionAsync(c.CompanyId, acquisition).ConfigureAwait(true));
                        return $"{position.AcquisitionNumber} {position.Status}: total {M(position.TotalCost)}." + Environment.NewLine
                            + (position.Outstanding.Count == 0 ? "Nothing outstanding." : "Outstanding: " + string.Join("; ", position.Outstanding));
                    case "Complete":
                        return Check(await land.CompleteAsync(c.CompanyId, acquisition, date).ConfigureAwait(true), parcel => $"Completed into the land bank (parcel {parcel}).");
                    default:
                        decimal written = Check(await land.AbandonAsync(c.CompanyId, acquisition, date, i.Optional("Resolution") ?? string.Empty, i.Optional("Expense") ?? string.Empty, i.Optional("Wip") ?? string.Empty)
                            .ConfigureAwait(true));
                        return $"Abandoned; {M(written)} written off.";
                }
            },
            List = async c => Table(await c.Db.Set<LandAcquisition>().AsNoTracking().Include(a => a.Costs).Where(a => a.CompanyId == c.CompanyId).OrderByDescending(a => a.Id).Take(300).ToListAsync()
                    .ConfigureAwait(true),
                ("Acquisition", a => a.AcquisitionNumber), ("Parcel", a => a.ParcelNumber), ("Method", a => a.Method), ("Counterparty", a => a.Counterparty), ("Valued", a => a.ValuedAt is decimal v ? M(v) : "-"),
                ("Cost", a => M(a.TotalCost)), ("Status", a => a.Status)),
        };
        yield return new Operation
        {
            Module = module, Title = "Title deeds", ListTitle = "Title pipeline",
            Description = "Apply for a stand's title, then move the application through conveyancer assigned → lodged → query raised / resolved → registered → in custody → "
                + "handed over (a conveyancer is named when assigned). The list shows open applications with days at their stage.",
            Fields = [Pick("Step", "Step", ["Apply", "Advance"]), Opt("Stand", "Stand number (apply)"), Opt("Application", "Title application number (advance)"),
                Pick("Stage", "To stage", Enum.GetNames<TitleStage>()), Opt("Conveyancer", "Conveyancer"), Opt("Note", "Note"), Today()],
            Run = async (c, i) =>
            {
                ITitleDeedService titles = c.Get<ITitleDeedService>();
                if (i.Text("Step") == "Apply")
                {
                    long stand = await FindAsync(c.Db.HousingStands, x => x.CompanyId == c.CompanyId && x.StandNumber == (i.Optional("Stand") ?? string.Empty).ToUpper(), x => x.Id, "stand",
                        i.Optional("Stand") ?? string.Empty).ConfigureAwait(true);
                    return $"Title application {Check(await titles.ApplyAsync(c.CompanyId, stand, i.Date("Date")).ConfigureAwait(true))} opened.";
                }

                return Check(await titles.AdvanceAsync(c.CompanyId, i.Optional("Application") ?? string.Empty, new TitleStepInput
                {
                    Stage = Enum.Parse<TitleStage>(i.Text("Stage")), Date = i.Date("Date"), Note = i.Optional("Note"), Conveyancer = i.Optional("Conveyancer"),
                }).ConfigureAwait(true), $"Moved to {i.Text("Stage")}.");
            },
            List = async c => Table((await c.Get<ITitleDeedService>().PipelineAsync(c.CompanyId, DateOnly.FromDateTime(DateTime.Today)).ConfigureAwait(true)).Applications,
                ("Application", t => t.ApplicationNumber), ("Stand", t => t.StandNumber), ("Stage", t => t.Stage), ("Days at stage", t => t.DaysAtStage), ("Days since applied", t => t.DaysSinceApplied),
                ("Conveyancer", t => t.Conveyancer)),
        };
        yield return new Operation
        {
            Module = module, Title = "Stand blocks and caveats", ListTitle = "Blocks in force",
            Description = "Places a block on a stand (court interdict, family dispute, suspected fraud, double allocation, boundary dispute…) with its document — a blocked stand cannot be "
                + "allocated, sold, leased or transferred — or lifts it with the reason. History shows every block on a stand.",
            Fields = [Pick("Step", "Step", ["Place", "Lift", "History"]), Req("Stand", "Stand number"), Pick("Reason", "Reason (place)", Enum.GetNames<BlockReason>()),
                Opt("Detail", "Detail / reason for lifting"), Opt("Document", "Document reference"), Today()],
            Run = async (c, i) =>
            {
                IStandBlockService blocks = c.Get<IStandBlockService>();
                long stand = await FindAsync(c.Db.HousingStands, x => x.CompanyId == c.CompanyId && x.StandNumber == i.Text("Stand").ToUpper(), x => x.Id, "stand", i.Text("Stand")).ConfigureAwait(true);
                switch (i.Text("Step"))
                {
                    case "Place":
                        return Check(await blocks.PlaceAsync(c.CompanyId, stand, Enum.Parse<BlockReason>(i.Text("Reason")), i.Optional("Detail") ?? string.Empty, i.Optional("Document") ?? string.Empty,
                            c.UserId, i.Date("Date")).ConfigureAwait(true), "Block placed.");
                    case "Lift":
                        return Check(await blocks.LiftAsync(c.CompanyId, stand, i.Optional("Detail") ?? string.Empty, i.Optional("Document") ?? string.Empty, c.UserId, i.Date("Date")).ConfigureAwait(true),
                            "Block lifted.");
                    default:
                        IReadOnlyList<StandBlockEntry> history = await blocks.HistoryAsync(c.CompanyId, stand).ConfigureAwait(true);
                        return history.Count == 0 ? "No blocks." : string.Join(Environment.NewLine, history.Select(b =>
                            $"{D(b.PlacedOn)} {b.Reason}: {b.Detail} ({b.DocumentReference}){(b.LiftedOn is DateOnly lifted ? $" — lifted {D(lifted)}: {b.LiftReason}" : " — in force")}"));
                }
            },
            List = async c => Table(await c.Get<IStandBlockService>().InForceAsync(c.CompanyId).ConfigureAwait(true),
                ("Stand", b => b.StandNumber), ("Reason", b => b.Reason), ("Detail", b => b.Detail), ("Document", b => b.DocumentReference), ("Since", b => D(b.PlacedOn))),
        };
        yield return new Operation
        {
            Module = module, Title = "Repossess a stand", ListTitle = "Repossessions this year",
            Description = "Takes back a stand sold on instalments (requested by you, approved by another user): the receivable is derecognised, the stand comes back into inventory at "
                + "its net realisable value, and the difference is a gain or loss.",
            Fields = [Req("Agreement", "Instalment agreement number"), Num("Nrv", "Net realisable value"), Req("Reason", "Reason"), Req("Approver", "Approving user name"),
                Req("Available", "Stands available (inventory) account"), Req("GainLoss", "Gain / loss account"), Today()],
            Run = async (c, i) =>
            {
                RepossessionEntry taken = Check(await c.Get<IRepossessionService>().RepossessAsync(new RepossessionRequest
                {
                    CompanyId = c.CompanyId, AgreementNumber = i.Text("Agreement"), Date = i.Date("Date"), NetRealisableValue = i.Number("Nrv"), Reason = i.Text("Reason"),
                    RequestedByUserId = c.UserId, ApprovedByUserId = await UserAsync(c, i.Text("Approver")).ConfigureAwait(true),
                    StandsAvailableAccountCode = i.Text("Available"), GainLossAccountCode = i.Text("GainLoss"),
                }).ConfigureAwait(true));
                return $"{taken.Reference}: stand {taken.StandNumber} back at {M(taken.InventoryRecognised)}; receivable {M(taken.ReceivableDerecognised)} derecognised; "
                    + $"{(taken.GainOrLoss >= 0m ? "gain" : "loss")} {M(Math.Abs(taken.GainOrLoss))}.";
            },
            List = async c => Table(await c.Get<IRepossessionService>().RegisterAsync(c.CompanyId, new DateOnly(DateTime.Today.Year, 1, 1), DateOnly.FromDateTime(DateTime.Today)).ConfigureAwait(true),
                ("Reference", r => r.Reference), ("Agreement", r => r.AgreementNumber), ("Stand", r => r.StandNumber), ("Date", r => D(r.Date)), ("Receivable", r => M(r.ReceivableDerecognised)),
                ("Inventory", r => M(r.InventoryRecognised)), ("Gain / loss", r => M(r.GainOrLoss))),
        };
        yield return new Operation
        {
            Module = module, Title = "Rental maintenance", ListTitle = "Rental units",
            Description = "Report a repair on a unit; complete it with its cost (recharged to the tenant when they caused it); record an inspection with its condition grade (1–5) and "
                + "whether it needs follow-up, and close the follow-up; History summarises a unit's repairs and condition.",
            Fields = [Pick("Step", "Step", ["Report repair", "Complete repair", "Inspection", "Close follow-up", "History"]), Opt("Unit", "Unit code"), Opt("Repair", "Repair number (complete)"),
                Opt("Description", "Description / findings"), Num("Cost", "Cost", "0"), Opt("Expense", "Repairs expense account"), Opt("PaidFrom", "Paid from account"), Tick("TenantCaused", "Tenant caused it"),
                Opt("Recharge", "Recharge income account"), Opt("Inspector", "Inspector"), Num("Grade", "Condition grade (1–5)", "3"), Tick("FollowUp", "Needs follow-up"),
                OptNum("Inspection", "Inspection id (close)"), Today()],
            Run = async (c, i) =>
            {
                IRentalMaintenanceService maintenance = c.Get<IRentalMaintenanceService>();
                string unit = i.Optional("Unit") ?? string.Empty;
                switch (i.Text("Step"))
                {
                    case "Report repair":
                        return $"Repair {Check(await maintenance.ReportRepairAsync(c.CompanyId, unit, i.Date("Date"), i.Optional("Description") ?? string.Empty).ConfigureAwait(true))} reported.";
                    case "Complete repair":
                        string? recharge = Check(await maintenance.CompleteRepairAsync(new RepairCompletion
                        {
                            CompanyId = c.CompanyId, RepairNumber = i.Optional("Repair") ?? string.Empty, Date = i.Date("Date"), Cost = i.Number("Cost"), ExpenseAccountCode = i.Optional("Expense") ?? string.Empty,
                            PaidFromAccountCode = i.Optional("PaidFrom") ?? string.Empty, TenantCaused = i.Flag("TenantCaused"), RechargeAccountCode = i.Optional("Recharge"),
                        }).ConfigureAwait(true));
                        return recharge is null ? "Repair completed." : $"Repair completed; recharged to the tenant ({recharge}).";
                    case "Inspection":
                        return Check(await maintenance.RecordInspectionAsync(c.CompanyId, unit, i.Date("Date"), i.Optional("Inspector") ?? string.Empty, i.Whole("Grade"), i.Optional("Description") ?? string.Empty,
                            i.Flag("FollowUp")).ConfigureAwait(true), id => $"Inspection {id} recorded.");
                    case "Close follow-up":
                        return Check(await maintenance.CloseFollowUpAsync(c.CompanyId, (long)(i.OptionalNumber("Inspection") ?? 0m), i.Date("Date")).ConfigureAwait(true), "Follow-up closed.");
                    default:
                        UnitMaintenanceHistory history = Check(await maintenance.HistoryAsync(c.CompanyId, unit).ConfigureAwait(true));
                        return $"{history.UnitCode}: {history.RepairsOpen} repair(s) open, repairs cost {M(history.RepairCost)}, recharged {M(history.Recharged)}, "
                            + $"latest condition {(history.LatestConditionGrade is int grade ? grade.ToString(CultureInfo.InvariantCulture) : "-")}, {history.FollowUpsOpen} follow-up(s) open.";
                }
            },
            List = async c => Table(await c.Db.Set<RentalUnit>().AsNoTracking().Where(u => u.CompanyId == c.CompanyId).OrderBy(u => u.Code).Take(500).ToListAsync().ConfigureAwait(true),
                ("Unit", u => u.Code), ("Description", u => u.Description), ("Type", u => u.UnitType), ("Rooms", u => u.Rooms), ("Rent", u => M(u.MonthlyRent)),
                ("Vacant since", u => u.VacantSince is DateOnly v ? D(v) : "let"), ("Most people", u => u.MaxOccupants)),
        };
        yield return new Operation
        {
            Module = module, Title = "Allocation appeals", ListTitle = "Appeals",
            Description = "An applicant appeals an allocation run: lodge the grounds, set the hearing, decide (upheld or dismissed, with reasons), and link an upheld appeal to the "
                + "re-allocation certificate that put it right.",
            Fields = [Pick("Step", "Step", ["Lodge", "Hearing", "Decide", "Link re-allocation"]), Opt("Appeal", "Appeal number"), Opt("Applicant", "Waiting-list number (lodge)"),
                Opt("Run", "Allocation run number (lodge)"), Opt("Text", "Grounds / reasons"), Tick("Upheld", "Upheld"), Opt("Certificate", "Re-allocation certificate"), Today()],
            Run = async (c, i) =>
            {
                IAllocationAppealService appeals = c.Get<IAllocationAppealService>();
                string appeal = i.Optional("Appeal") ?? string.Empty;
                return i.Text("Step") switch
                {
                    "Lodge" => $"Appeal {Check(await appeals.LodgeAsync(c.CompanyId, i.Optional("Applicant") ?? string.Empty, i.Optional("Run") ?? string.Empty, i.Optional("Text") ?? string.Empty, i.Date("Date")).ConfigureAwait(true))} lodged.",
                    "Hearing" => Check(await appeals.SetHearingAsync(c.CompanyId, appeal, i.Date("Date")).ConfigureAwait(true), "Hearing set."),
                    "Decide" => Check(await appeals.DecideAsync(c.CompanyId, appeal, i.Flag("Upheld"), i.Date("Date"), i.Optional("Text") ?? string.Empty).ConfigureAwait(true), i.Flag("Upheld") ? "Upheld." : "Dismissed."),
                    _ => Check(await appeals.LinkReallocationAsync(c.CompanyId, appeal, i.Optional("Certificate") ?? string.Empty).ConfigureAwait(true), "Re-allocation linked."),
                };
            },
            List = async c => Table(await c.Get<IAllocationAppealService>().RegisterAsync(c.CompanyId).ConfigureAwait(true),
                ("Appeal", a => a.AppealNumber), ("Applicant", a => a.WaitingListNumber), ("Run", a => a.RunNumber), ("Lodged", a => D(a.LodgedOn)), ("Status", a => a.Status),
                ("Hearing", a => a.HearingDate is DateOnly h ? D(h) : "-"), ("Re-allocation", a => a.ReallocationCertificate)),
        };
        yield return new Operation
        {
            Module = module, Title = "Allocation quotas", ListTitle = "Quotas",
            Description = "Sets the share of allocations reserved for a beneficiary group (women, youth, disabled, elderly, civil servants), for a scheme or overall; declares the groups "
                + "an applicant belongs to (comma-separated) so allocation runs can meet the quotas.",
            Fields = [Pick("Step", "Step", ["Set quota", "Declare groups"]), Opt("Scheme", "Scheme code (blank: all schemes)"), Pick("Group", "Group (quota)", Enum.GetNames<BeneficiaryGroups>()),
                Num("Percent", "Target %", "0"), Opt("Applicant", "Waiting-list number (declare)"), Opt("Groups", "Groups, e.g. Women,Youth (declare)")],
            Run = async (c, i) =>
            {
                IAllocationQuotaService quotas = c.Get<IAllocationQuotaService>();
                if (i.Text("Step") == "Set quota")
                {
                    return Check(await quotas.SetQuotaAsync(c.CompanyId, i.Optional("Scheme"), Enum.Parse<BeneficiaryGroups>(i.Text("Group")), i.Number("Percent")).ConfigureAwait(true), "Quota saved.");
                }

                BeneficiaryGroups groups = Enum.TryParse(i.Optional("Groups") ?? "None", ignoreCase: true, out BeneficiaryGroups parsed) ? parsed
                    : throw new OperationException("Name the groups from: " + string.Join(", ", Enum.GetNames<BeneficiaryGroups>()) + ".");
                return Check(await quotas.DeclareGroupsAsync(c.CompanyId, i.Optional("Applicant") ?? string.Empty, groups).ConfigureAwait(true), "Groups declared.");
            },
            List = async c => Table(await c.Db.Set<AllocationQuota>().AsNoTracking().Where(q => q.CompanyId == c.CompanyId).ToListAsync().ConfigureAwait(true),
                ("Scheme", q => q.SchemeId), ("Group", q => q.Group), ("Target %", q => Q(q.TargetPercent))),
        };
        yield return new Operation
        {
            Module = module, Title = "Housing cooperatives", ListTitle = "Cooperatives",
            Description = "Registers a cooperative, adds and removes members, allocates stands to it (\"S1,S2\") and sub-allocates each to a member; Stands lists what it holds and to whom.",
            Fields = [Pick("Step", "Step", ["Register", "Add member", "Remove member", "Allocate stands", "Sub-allocate", "Stands"]), Req("Cooperative", "Cooperative code"), Opt("Name", "Name / member name"),
                Opt("Registration", "Registration number"), Opt("IdNumber", "Member national ID"), Opt("Stands", "Stand number(s), comma-separated"), Today()],
            Run = async (c, i) =>
            {
                ICooperativeService cooperatives = c.Get<ICooperativeService>();
                string code = i.Text("Cooperative");
                async Task<List<long>> StandIds()
                {
                    List<long> ids = [];
                    foreach (string number in (i.Optional("Stands") ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    {
                        ids.Add(await FindAsync(c.Db.HousingStands, x => x.CompanyId == c.CompanyId && x.StandNumber == number.ToUpper(), x => x.Id, "stand", number).ConfigureAwait(true));
                    }

                    return ids;
                }

                switch (i.Text("Step"))
                {
                    case "Register":
                        return Check(await cooperatives.RegisterAsync(c.CompanyId, code, i.Optional("Name") ?? string.Empty, i.Optional("Registration") ?? string.Empty).ConfigureAwait(true), _ => "Cooperative registered.");
                    case "Add member":
                        return Check(await cooperatives.AddMemberAsync(c.CompanyId, code, i.Optional("IdNumber") ?? string.Empty, i.Optional("Name") ?? string.Empty, i.Date("Date")).ConfigureAwait(true), "Member added.");
                    case "Remove member":
                        return Check(await cooperatives.RemoveMemberAsync(c.CompanyId, code, i.Optional("IdNumber") ?? string.Empty).ConfigureAwait(true), "Member removed.");
                    case "Allocate stands":
                        return $"{Check(await cooperatives.AllocateStandsAsync(c.CompanyId, code, await StandIds().ConfigureAwait(true), i.Date("Date")).ConfigureAwait(true))} stand(s) allocated.";
                    case "Sub-allocate":
                        List<long> stand = await StandIds().ConfigureAwait(true);
                        return Check(await cooperatives.SubAllocateAsync(c.CompanyId, code, stand.FirstOrDefault(), i.Optional("IdNumber") ?? string.Empty, i.Date("Date")).ConfigureAwait(true), "Sub-allocated.");
                    default:
                        IReadOnlyList<CooperativeStandEntry> held = Check(await cooperatives.StandsAsync(c.CompanyId, code).ConfigureAwait(true));
                        return held.Count == 0 ? "No stands." : string.Join(Environment.NewLine, held.Select(h => $"{h.StandNumber,-12} {D(h.AllocatedOn)}  {h.MemberName ?? "(not sub-allocated)"}"));
                }
            },
            List = async c => Table(await c.Db.Set<Cooperative>().AsNoTracking().Where(x => x.CompanyId == c.CompanyId).OrderBy(x => x.Code).ToListAsync().ConfigureAwait(true),
                ("Code", x => x.Code), ("Name", x => x.Name), ("Registration", x => x.RegistrationNumber)),
        };
        yield return new Operation
        {
            Module = module, Title = "Development levies", ListTitle = "Levies",
            Description = "Defines a levy charged on allocation or on transfer — flat, per m² or a percentage of the price — to an income account, or deactivates it; charges the "
                + "levies due on an allocation certificate or a transfer application.",
            Fields = [Pick("Step", "Step", ["Define", "Deactivate", "Charge allocation", "Charge transfer"]), Opt("Code", "Levy code"), Opt("Name", "Name"),
                Pick("Trigger", "Charged on", Enum.GetNames<LevyTrigger>()), Pick("Basis", "Basis", Enum.GetNames<LevyBasis>()), Num("Rate", "Rate / amount", "0"), Opt("Income", "Income account"),
                Opt("Document", "Certificate / application number (charge)"), Today()],
            Run = async (c, i) =>
            {
                IDevelopmentLevyService levies = c.Get<IDevelopmentLevyService>();
                static string Raised(IReadOnlyList<LevyRaised> raised) => raised.Count == 0 ? "No levies due." : string.Join(Environment.NewLine, raised.Select(r => $"{r.LevyCode}: {M(r.Amount)} ({r.DocumentNumber})"));
                return i.Text("Step") switch
                {
                    "Define" => Check(await levies.DefineAsync(c.CompanyId, i.Optional("Code") ?? string.Empty, i.Optional("Name") ?? string.Empty, Enum.Parse<LevyTrigger>(i.Text("Trigger")),
                        Enum.Parse<LevyBasis>(i.Text("Basis")), i.Number("Rate"), i.Optional("Income") ?? string.Empty).ConfigureAwait(true), _ => "Levy defined."),
                    "Deactivate" => Check(await levies.DeactivateAsync(c.CompanyId, i.Optional("Code") ?? string.Empty).ConfigureAwait(true), "Levy deactivated."),
                    "Charge allocation" => Raised(Check(await levies.ChargeAllocationAsync(c.CompanyId, i.Optional("Document") ?? string.Empty, i.Date("Date")).ConfigureAwait(true))),
                    _ => Raised(Check(await levies.ChargeTransferAsync(c.CompanyId, i.Optional("Document") ?? string.Empty, i.Date("Date")).ConfigureAwait(true))),
                };
            },
            List = async c => Table(await c.Db.Set<DevelopmentLevy>().AsNoTracking().Where(x => x.CompanyId == c.CompanyId).OrderBy(x => x.Code).ToListAsync().ConfigureAwait(true),
                ("Code", x => x.Code), ("Name", x => x.Name), ("On", x => x.Trigger), ("Basis", x => x.Basis), ("Rate", x => Q(x.Rate)), ("Income", x => x.IncomeAccountCode), ("Active", x => x.IsActive)),
        };
        yield return new Operation
        {
            Module = module, Title = "Stand deposits and cancellations", ListTitle = "Allocation certificates",
            Description = "Take a deposit against an allocation (held as a liability), apply it when the sale goes ahead or refund it; cancel an instalment agreement at the buyer's "
                + "request (requested by you, approved by another user): what was paid less occupation rental, the admin charge and arrears is refunded and the stand repossessed.",
            Fields = [Pick("Step", "Step", ["Take deposit", "Apply deposit", "Refund deposit", "Cancel agreement"]), Opt("Certificate", "Allocation certificate (deposit)"),
                Opt("Agreement", "Instalment agreement (cancel)"), Opt("Customer", "Customer code (take)"), Num("Amount", "Amount (take) / NRV (cancel)", "0"), Opt("Reference", "Receipt reference / reason"),
                Opt("Bank", "Bank account"), Opt("Liability", "Deposit liability account (take)"), Num("Rental", "Monthly occupation rental (cancel)", "0"), Num("Admin", "Admin charge % (cancel)", "0"),
                Opt("Approver", "Approving user name (cancel)"), Opt("Available", "Stands available account (cancel)"), Opt("GainLoss", "Gain / loss account (cancel)"), Today()],
            Run = async (c, i) =>
            {
                IHousingDepositService deposits = c.Get<IHousingDepositService>();
                string certificate = i.Optional("Certificate") ?? string.Empty;
                switch (i.Text("Step"))
                {
                    case "Take deposit":
                        return $"Deposit taken ({Check(await deposits.TakeDepositAsync(new StandDepositReceipt
                        {
                            CompanyId = c.CompanyId, CertificateNumber = certificate, CustomerId = await CustomerAsync(c, i.Optional("Customer") ?? string.Empty).ConfigureAwait(true), Amount = i.Number("Amount"),
                            Date = i.Date("Date"), ReceiptReference = i.Optional("Reference") ?? string.Empty, BankAccountCode = i.Optional("Bank") ?? string.Empty,
                            LiabilityAccountCode = i.Optional("Liability") ?? string.Empty,
                        }).ConfigureAwait(true))}).";
                    case "Apply deposit":
                        return $"Deposit applied ({Check(await deposits.ApplyDepositAsync(c.CompanyId, certificate, i.Date("Date")).ConfigureAwait(true))}).";
                    case "Refund deposit":
                        return $"Deposit refunded ({Check(await deposits.RefundDepositAsync(c.CompanyId, certificate, i.Date("Date"), i.Optional("Bank") ?? string.Empty).ConfigureAwait(true))}).";
                    default:
                        AgreementCancellationResult cancelled = Check(await deposits.CancelAgreementAsync(new AgreementCancellationRequest
                        {
                            CompanyId = c.CompanyId, AgreementNumber = i.Optional("Agreement") ?? string.Empty, Date = i.Date("Date"), Reason = i.Optional("Reference") ?? string.Empty,
                            MonthlyOccupationRental = i.Number("Rental"), AdminChargePercent = i.Number("Admin"), NetRealisableValue = i.Number("Amount"), RequestedByUserId = c.UserId,
                            ApprovedByUserId = await UserAsync(c, i.Optional("Approver") ?? string.Empty).ConfigureAwait(true), StandsAvailableAccountCode = i.Optional("Available") ?? string.Empty,
                            GainLossAccountCode = i.Optional("GainLoss") ?? string.Empty, BankAccountCode = i.Optional("Bank") ?? string.Empty,
                        }).ConfigureAwait(true));
                        return $"Cancelled: paid {M(cancelled.Paid)} − {cancelled.MonthsOccupied} month(s) rental {M(cancelled.OccupationRental)} − admin {M(cancelled.AdminCharge)} − arrears "
                            + $"{M(cancelled.Arrears)} = refund {M(cancelled.Refund)}; repossession {cancelled.RepossessionReference}.";
                }
            },
            List = async c => Table(await c.Db.HousingStands.AsNoTracking().Where(x => x.CompanyId == c.CompanyId && x.CurrentHolderCustomerId != null).OrderBy(x => x.StandNumber).Take(500)
                    .ToListAsync().ConfigureAwait(true),
                ("Stand", x => x.StandNumber), ("Status", x => x.Status), ("Holder", x => x.CurrentHolderCustomerId), ("Price", x => M(x.SellingPrice))),
        };
        yield return new Operation
        {
            Module = module, Title = "Housing grants", ListTitle = "Grant disclosure",
            Description = "Receives a grant (deferred until its conditions are assured), assures the conditions, and records expenditure to date so income is recognised as the money "
                + "is spent (IPSAS 23); the list is the disclosure of each grant's recognised and deferred amounts.",
            Fields = [Pick("Step", "Step", ["Receive", "Assure conditions", "Expenditure to date"]), Opt("Grant", "Grant number"), Opt("Grantor", "Grantor (receive)"), Num("Amount", "Amount / expenditure to date", "0"),
                Num("Budget", "Budgeted expenditure (receive)", "0"), Opt("Conditions", "Conditions (receive)"), Opt("Bank", "Bank account"), Opt("Deferred", "Deferred income account"),
                Opt("Income", "Grant income account"), Today()],
            Run = async (c, i) =>
            {
                IHousingGrantService grants = c.Get<IHousingGrantService>();
                string grant = i.Optional("Grant") ?? string.Empty;
                return i.Text("Step") switch
                {
                    "Receive" => $"Grant {Check(await grants.ReceiveAsync(new GrantReceipt
                    {
                        CompanyId = c.CompanyId, Grantor = i.Optional("Grantor") ?? string.Empty, Amount = i.Number("Amount"), BudgetedExpenditure = i.Number("Budget"),
                        Conditions = i.Optional("Conditions") ?? string.Empty, ReceivedOn = i.Date("Date"), BankAccountCode = i.Optional("Bank") ?? string.Empty,
                        DeferredAccountCode = i.Optional("Deferred") ?? string.Empty, IncomeAccountCode = i.Optional("Income") ?? string.Empty,
                    }).ConfigureAwait(true))} received.",
                    "Assure conditions" => $"Conditions assured; {M(Check(await grants.AssureConditionsAsync(c.CompanyId, grant, i.Date("Date")).ConfigureAwait(true)))} recognised.",
                    _ => $"{M(Check(await grants.RecordExpenditureAsync(c.CompanyId, grant, i.Number("Amount"), i.Date("Date")).ConfigureAwait(true)))} recognised.",
                };
            },
            List = async c => Table(await c.Get<IHousingGrantService>().DisclosureAsync(c.CompanyId).ConfigureAwait(true),
                ("Grant", g => g.GrantNumber), ("Grantor", g => g.Grantor), ("Amount", g => M(g.Amount)), ("Recognised", g => M(g.Recognised)), ("Deferred", g => M(g.Deferred)),
                ("Assured", g => g.ConditionsAssured), ("Conditions", g => g.Conditions)),
        };
        yield return new Operation
        {
            Module = module, Title = "Applicant fees and transfer duty", ListTitle = "Applicants",
            Description = "Records a fee an applicant pays (registration, renewal…) to income; withholds transfer duty on a transfer application at a rate (held as a liability) and "
                + "remits it to the authority.",
            Fields = [Pick("Step", "Step", ["Applicant fee", "Withhold duty", "Remit duty"]), Opt("Applicant", "Waiting-list number (fee)"), Pick("FeeType", "Fee", Enum.GetNames<ApplicantFeeType>()),
                Opt("Application", "Transfer application (duty)"), Num("Amount", "Amount (fee) / rate % (duty)", "0"), Opt("Reference", "Receipt / remittance reference"), Opt("Bank", "Bank account"),
                Opt("Account", "Income account (fee) / duty liability account"), Today()],
            Run = async (c, i) =>
            {
                IHousingFeeService fees = c.Get<IHousingFeeService>();
                return i.Text("Step") switch
                {
                    "Applicant fee" => $"Fee recorded ({Check(await fees.RecordApplicantFeeAsync(new ApplicantFeeReceipt
                    {
                        CompanyId = c.CompanyId, WaitingListNumber = i.Optional("Applicant") ?? string.Empty, FeeType = Enum.Parse<ApplicantFeeType>(i.Text("FeeType")), Amount = i.Number("Amount"),
                        Date = i.Date("Date"), ReceiptReference = i.Optional("Reference") ?? string.Empty, BankAccountCode = i.Optional("Bank") ?? string.Empty, IncomeAccountCode = i.Optional("Account") ?? string.Empty,
                    }).ConfigureAwait(true))}).",
                    "Withhold duty" => $"{M(Check(await fees.WithholdTransferDutyAsync(new TransferDutyReceipt
                    {
                        CompanyId = c.CompanyId, ApplicationNumber = i.Optional("Application") ?? string.Empty, RatePercent = i.Number("Amount"), Date = i.Date("Date"),
                        ReceiptReference = i.Optional("Reference") ?? string.Empty, BankAccountCode = i.Optional("Bank") ?? string.Empty, LiabilityAccountCode = i.Optional("Account") ?? string.Empty,
                    }).ConfigureAwait(true)))} transfer duty withheld.",
                    _ => $"Duty remitted ({Check(await fees.RemitTransferDutyAsync(c.CompanyId, i.Optional("Application") ?? string.Empty, i.Date("Date"), i.Optional("Reference") ?? string.Empty,
                        i.Optional("Bank") ?? string.Empty).ConfigureAwait(true))}).",
                };
            },
            List = ApplicantsAsync,
        };
    }

    private static async Task<System.Data.DataTable> ApplicantsAsync(OperationContext c)
        => Table(await c.Db.HousingApplicants.AsNoTracking().Where(a => a.CompanyId == c.CompanyId).OrderByDescending(a => a.Id).Take(500).ToListAsync().ConfigureAwait(true),
            ("Waiting list", a => a.WaitingListNumber), ("Name", a => a.FullName), ("ID", a => a.IdNumber), ("Category", a => a.Category), ("Registered", a => a.RegistrationDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
            ("Status", a => a.Status), ("Verified", a => a.IsVerified));

    private static DateOnly Day(string text)
        => DateOnly.TryParseExact(text.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly day) ? day : throw new OperationException($"\"{text}\" is not a date (yyyy-MM-dd).");
}
