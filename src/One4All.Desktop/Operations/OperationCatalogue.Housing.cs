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
    }

    private static async Task<System.Data.DataTable> ApplicantsAsync(OperationContext c)
        => Table(await c.Db.HousingApplicants.AsNoTracking().Where(a => a.CompanyId == c.CompanyId).OrderByDescending(a => a.Id).Take(500).ToListAsync().ConfigureAwait(true),
            ("Waiting list", a => a.WaitingListNumber), ("Name", a => a.FullName), ("ID", a => a.IdNumber), ("Category", a => a.Category), ("Registered", a => a.RegistrationDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
            ("Status", a => a.Status), ("Verified", a => a.IsVerified));

    private static DateOnly Day(string text)
        => DateOnly.TryParseExact(text.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly day) ? day : throw new OperationException($"\"{text}\" is not a date (yyyy-MM-dd).");
}
