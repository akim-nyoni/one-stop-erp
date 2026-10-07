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
    }

    private static async Task<System.Data.DataTable> ApplicantsAsync(OperationContext c)
        => Table(await c.Db.HousingApplicants.AsNoTracking().Where(a => a.CompanyId == c.CompanyId).OrderByDescending(a => a.Id).Take(500).ToListAsync().ConfigureAwait(true),
            ("Waiting list", a => a.WaitingListNumber), ("Name", a => a.FullName), ("ID", a => a.IdNumber), ("Category", a => a.Category), ("Registered", a => a.RegistrationDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
            ("Status", a => a.Status), ("Verified", a => a.IsVerified));

    private static DateOnly Day(string text)
        => DateOnly.TryParseExact(text.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly day) ? day : throw new OperationException($"\"{text}\" is not a date (yyyy-MM-dd).");
}
