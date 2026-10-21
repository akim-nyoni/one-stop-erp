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
            Module = module, Title = "Waiting-list policy and qualification rules", ListTitle = "Rules in force",
            Description = "Policy: who qualifies (minimum age, years resident, overall income ceiling) and the points each factor earns. Qualification rules: first-time buyers "
                + "only, refuse those whose earlier stand was repossessed or agreement cancelled, require a rates clearance, and an income ceiling per category (blank removes it). "
                + "Registration checks every rule, for the applicant and the spouse.",
            Fields = [Pick("Step", "Step", ["Policy", "Qualification rules"]), Num("MinAge", "Minimum age", "18"), Num("Years", "Minimum years resident", "0"),
                OptNum("Income", "Overall income ceiling (blank: none)"), Num("PerYearOnList", "Points per year on the list", "10"), Num("PerYearResident", "Points per year resident", "2"),
                Num("PerDependant", "Points per dependant", "5"), Num("Disability", "Points for a disability", "15"), Num("Homeless", "Points if homeless", "20"),
                Num("Lodger", "Points if lodging", "8"), OptNum("PerYearOfAge", "Points per year of age (policy)"), OptNum("AgeFrom", "Age points from age"),
                OptNum("Cooperative", "Points for a cooperative member"), Tick("FirstTime", "First-time buyers only"), Tick("Forfeited", "Refuse earlier forfeiture"), Tick("Clearance", "Require rates clearance"), Tick("RentalList", "Let rental units only from the rental waiting list"),
                Pick("Category", "Category for the ceiling", Enum.GetNames<HousingCategory>()), OptNum("Ceiling", "Its income ceiling (blank: none)")],
            Run = async (c, i) =>
            {
                IWaitingListService list = c.Get<IWaitingListService>();
                if (i.Text("Step") == "Policy")
                {
                    return Check(await list.SetPolicyAsync(c.CompanyId, new WaitingListPolicyInput(new WaitingListTerms(i.Whole("MinAge"), i.Whole("Years"), i.OptionalNumber("Income"),
                        i.Number("PerYearOnList"), i.Number("PerYearResident"), i.Number("PerDependant"), i.Number("Disability"), i.Number("Homeless"), i.Number("Lodger"),
                        i.OptionalNumber("PerYearOfAge") ?? 0m, (int)(i.OptionalNumber("AgeFrom") ?? 0m), i.OptionalNumber("Cooperative") ?? 0m))).ConfigureAwait(true),
                        "Policy saved.");
                }

                Dictionary<HousingCategory, decimal> ceilings = new((await list.QualificationRulesAsync(c.CompanyId).ConfigureAwait(true)).CategoryIncomeCeilings);
                HousingCategory category = Enum.Parse<HousingCategory>(i.Text("Category"));
                if (i.OptionalNumber("Ceiling") is decimal ceiling)
                {
                    ceilings[category] = ceiling;
                }
                else
                {
                    ceilings.Remove(category);
                }

                return Check(await list.SetQualificationRulesAsync(c.CompanyId, new QualificationRules(i.Flag("FirstTime"), i.Flag("Forfeited"), i.Flag("Clearance"), ceilings, i.Flag("RentalList")))
                    .ConfigureAwait(true), "Qualification rules saved.");
            },
            List = async c =>
            {
                IWaitingListService list = c.Get<IWaitingListService>();
                WaitingListTerms t = await list.PolicyAsync(c.CompanyId).ConfigureAwait(true);
                QualificationRules q = await list.QualificationRulesAsync(c.CompanyId).ConfigureAwait(true);
                List<(string Rule, string Value)> rows =
                [
                    ("Minimum age", t.MinimumAge.ToString(CultureInfo.InvariantCulture)), ("Minimum years resident", t.MinimumResidencyYears.ToString(CultureInfo.InvariantCulture)),
                    ("Overall income ceiling", t.MaximumMonthlyIncome is decimal m ? M(m) : "none"), ("First-time buyers only", q.FirstTimeBuyersOnly ? "Yes" : "No"),
                    ("Refuse earlier forfeiture", q.RefuseForfeited ? "Yes" : "No"), ("Rates clearance required", q.RequireRatesClearance ? "Yes" : "No"),
                    .. q.CategoryIncomeCeilings.OrderBy(x => x.Key).Select(x => ($"Income ceiling: {x.Key}", M(x.Value))),
                    ("Points per year on the list", t.PointsPerYearOnList.ToString("0.##", CultureInfo.InvariantCulture)),
                    ("Points per year resident", t.PointsPerYearResident.ToString("0.##", CultureInfo.InvariantCulture)),
                    ("Points per dependant", t.PointsPerDependant.ToString("0.##", CultureInfo.InvariantCulture)),
                    ("Disability / homeless / lodger points", $"{t.DisabilityPoints:0.##} / {t.HomelessPoints:0.##} / {t.LodgerPoints:0.##}"),
                ];
                return Table(rows, ("Rule", r => r.Rule), ("Setting", r => r.Value));
            },
        };
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
            List = async c =>
            {
                HousingNames n = await HousingNamesAsync(c).ConfigureAwait(true);
                return Table(await c.Db.HousingStands.AsNoTracking().Where(s => s.CompanyId == c.CompanyId).OrderBy(s => s.StandNumber).Page(c, 1000).ToListAsync().ConfigureAwait(true),
                    ("Stand", s => s.StandNumber), ("Status", s => s.Status), ("Size m²", s => s.SizeSquareMetres), ("Cost", s => M(s.AllocatedCost)), ("Parent", s => n.Stand(s.ParentStandId)),
                    ("Consolidated into", s => n.Stand(s.ConsolidatedIntoStandId)));
            },
        };
        yield return new Operation
        {
            Module = module, Title = "Waiting list enquiry and ranking", ListTitle = "Applicants",
            Description = "With a waiting-list number, national ID or name (or part of it): the applicant's status, place by seniority and score; when several names match they are "
                + "listed to narrow down. Without one: the ranking for a category, by seniority or score.",
            Fields = [Opt("Number", "Waiting-list number, national ID or name (blank for the ranking)"), Pick("Category", "Category", Enum.GetNames<HousingCategory>()),
                Pick("Order", "Order", Enum.GetNames<WaitingListOrder>()), Today("AsAt", "As at")],
            Run = async (c, i) =>
            {
                IWaitingListService list = c.Get<IWaitingListService>();
                static string Show(WaitingListEnquiry e)
                    => $"{e.WaitingListNumber} · {e.Category} · {e.Status} · place by seniority {(e.PositionBySeniority?.ToString(CultureInfo.InvariantCulture) ?? "-")} · score {e.Score.Total:0.##}";
                if (i.Optional("Number") is string number)
                {
                    Core.Common.Result<WaitingListEnquiry> exact = await list.EnquireAsync(c.CompanyId, number, i.Date("AsAt")).ConfigureAwait(true);
                    if (exact.IsSuccess)
                    {
                        return Show(exact.Value);
                    }

                    // Not a waiting-list number: the national ID exactly, then the name containing what was typed.
                    string key = number.ToUpperInvariant();
                    IQueryable<HousingApplicant> listed = c.Db.HousingApplicants.AsNoTracking().Where(a => a.CompanyId == c.CompanyId && a.WaitingListNumber != null);
                    var byId = await listed.Where(a => a.IdNumber.ToUpper() == key).OrderBy(a => a.FullName)
                        .Select(a => new { a.WaitingListNumber, a.FullName, a.IdNumber, a.Status }).Take(11).ToListAsync().ConfigureAwait(true);
                    var matches = byId.Count > 0 ? byId : await listed.Where(a => a.FullName.ToUpper().Contains(key)).OrderBy(a => a.FullName)
                        .Select(a => new { a.WaitingListNumber, a.FullName, a.IdNumber, a.Status }).Take(11).ToListAsync().ConfigureAwait(true);
                    if (matches.Count == 0)
                    {
                        throw new OperationException($"No applicant on the waiting list has the number, national ID or a name containing \"{number}\".");
                    }

                    if (matches.Count == 1)
                    {
                        return Show(Check(await list.EnquireAsync(c.CompanyId, matches[0].WaitingListNumber!, i.Date("AsAt")).ConfigureAwait(true)));
                    }

                    return $"{(matches.Count > 10 ? "More than 10" : matches.Count.ToString(CultureInfo.InvariantCulture))} applicants match \"{number}\"; type more of the name, "
                        + "the national ID or the waiting-list number to narrow it down:" + Environment.NewLine
                        + string.Join(Environment.NewLine, matches.Take(10).Select(m => $"  {m.WaitingListNumber,-12} {m.FullName,-30} {m.IdNumber,-16} {m.Status}"));
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
            Description = "Propose a run for a scheme's available stands (seniority, score, or a seeded ballot anyone can re-run to verify); the committee rejects a pairing with a reason or "
                + "substitutes the next applicant stating why the skipped one was skipped; approve it (someone else; offers go out with a deadline; with a resolution the meeting's date and "
                + "members are kept; approving needs the allocation-approval permission), cancel it, or verify a ballot's draw. Expired offers wait in a queue for an officer to lapse or "
                + "extend them with a reason. A swap of two allottees' stands is asked for as a document and approved or rejected by another officer before the stands change hands.",
            Fields = [Pick("Step", "Step", ["Propose", "Reject line", "Substitute line", "Approve", "Cancel", "Verify draw", "Swap stands", "Approve swap", "Reject swap", "Expired offers", "Lapse offer",
                "Extend offer"]), Opt("Stands", "Only these stands (substitution run; ',')"), Lookup("Swap", "Swap document (approve / reject swap)", false, PendingSwapsAsync),
                OptNum("Line", "Pairing sequence (reject / substitute)"), Opt("LineReason", "Reason (reject / substitute)"), Opt("Resolution", "Committee resolution (approve)"),
                OptDate("Meeting", "Meeting date (approve)"), Opt("Members", "Committee members present (approve)"), Opt("Certificate", "Certificate (swap / lapse / extend)"), Opt("Other", "Other certificate (swap)"), Opt("Document", "Swap document"),
                Opt("Reason", "Reason (swap / lapse / extend)"), Opt("Scheme", "Scheme code (propose)"), Pick("Category", "Category", Enum.GetNames<HousingCategory>()),
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
                            Seed = (long?)i.OptionalNumber("Seed"), AsAtDate = i.Date("Date"), UserId = c.UserId, OfferDays = i.Whole("OfferDays"),
                            StandNumbers = [.. (i.Optional("Stands") ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)],
                        }).ConfigureAwait(true)));
                    case "Reject line":
                        return Show(Check(await allocation.RejectLineAsync(c.CompanyId, i.Text("Run"), (int)(i.OptionalNumber("Line") ?? 0), i.Optional("LineReason") ?? string.Empty).ConfigureAwait(true)));
                    case "Substitute line":
                        return Show(Check(await allocation.SubstituteLineAsync(c.CompanyId, i.Text("Run"), (int)(i.OptionalNumber("Line") ?? 0), i.Optional("LineReason") ?? string.Empty).ConfigureAwait(true)));
                    case "Approve":
                        // Approving a run is the allocation-approval right (brief §4.10.4, SOD-HSE-01), not the right to run allocations.
                        if (!(await c.Get<One4All.Application.Abstractions.Security.IPermissionService>().ResolveAsync(c.UserId).ConfigureAwait(true))
                                .Has("Housing.AllocationApprove", One4All.Core.Security.OperationFlags.Approve))
                        {
                            throw new OperationException("Approving an allocation run needs the allocation-approval permission.");
                        }

                        return Show(Check(await (i.Optional("Resolution") is string resolution
                            ? allocation.ApproveByCommitteeAsync(c.CompanyId, i.Text("Run"), c.UserId, i.Date("Date"), i.Whole("OfferDays"), new CommitteeMeeting(resolution,
                                i.Optional("Meeting") is string meeting ? DateOnly.ParseExact(meeting, "yyyy-MM-dd", CultureInfo.InvariantCulture) : null, i.Optional("Members")))
                            : allocation.ApproveAsync(c.CompanyId, i.Text("Run"), c.UserId, i.Date("Date"), i.Whole("OfferDays"))).ConfigureAwait(true)));
                    case "Expired offers":
                        IReadOnlyList<ExpiredOffer> queue = await allocation.ExpiredOffersAsync(c.CompanyId, i.Date("Date")).ConfigureAwait(true);
                        return queue.Count == 0 ? "No expired offers await a decision." : string.Join(Environment.NewLine, queue.Select(o =>
                            $"{o.CertificateNumber,-14} stand {o.StandNumber,-10} {o.WaitingListNumber,-12} expired {D(o.OfferExpires)} ({o.DaysOverdue} day(s) ago; extended {o.Extensions} time(s))"));
                    case "Lapse offer":
                        return Check(await allocation.LapseOfferAsync(c.CompanyId, i.Optional("Certificate") ?? string.Empty, i.Date("Date"), i.Optional("Reason") ?? string.Empty).ConfigureAwait(true),
                            "Offer lapsed; the stand is back in the pool.");
                    case "Extend offer":
                        return Check(await allocation.ExtendOfferAsync(c.CompanyId, i.Optional("Certificate") ?? string.Empty, i.Date("Date").AddDays(i.Whole("OfferDays")), i.Optional("Reason") ?? string.Empty)
                            .ConfigureAwait(true), $"Offer extended to {D(i.Date("Date").AddDays(i.Whole("OfferDays")))}.");
                    case "Swap stands":
                        long swap = Check(await allocation.SwapAsync(c.CompanyId, i.Optional("Certificate") ?? string.Empty, i.Optional("Other") ?? string.Empty, i.Date("Date"),
                            i.Optional("Document") ?? string.Empty, i.Optional("Reason") ?? string.Empty, c.UserId).ConfigureAwait(true));
                        return $"Swap {swap} asked for; another officer approves it (Approve swap) before the stands change hands.";
                    case "Approve swap":
                    case "Reject swap":
                        bool approveSwap = i.Text("Step") == "Approve swap";
                        return Check(await allocation.DecideSwapAsync(c.CompanyId, PickedId(i, "Swap"), approveSwap, c.UserId, i.Date("Date"), i.Optional("Reason")).ConfigureAwait(true),
                            approveSwap ? "Swap approved; the stands have changed hands." : "Swap rejected.");
                    case "Cancel":
                        _ = Check(await allocation.CancelAsync(c.CompanyId, i.Text("Run")).ConfigureAwait(true), string.Empty);
                        return "Run cancelled.";
                    default:
                        DrawVerification v = Check(await allocation.VerifyDrawAsync(c.CompanyId, i.Text("Run")).ConfigureAwait(true));
                        return v.IsVerified ? "Verified: the inputs are unchanged and re-running the draw gives the same order."
                            : $"NOT verified - inputs unchanged: {v.HashMatches}; same order: {v.OrderMatches}.";
                }
            },
            List = async c =>
            {
                HousingNames n = await HousingNamesAsync(c).ConfigureAwait(true);
                return Table(await c.Db.Set<AllocationRun>().AsNoTracking().Where(r => r.CompanyId == c.CompanyId).OrderByDescending(r => r.Id).Page(c, 100).ToListAsync().ConfigureAwait(true),
                    ("Run", r => r.RunNumber), ("Scheme", r => n.Scheme(r.SchemeId)), ("Category", r => r.Category), ("Method", r => r.Method), ("Status", r => r.Status), ("Approved by", r => r.ApprovedByUserId),
                    ("Approved", r => r.ApprovedOn?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)), ("Resolution", r => r.CommitteeResolution ?? string.Empty),
                    ("Meeting", r => r.MeetingDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty), ("Members", r => r.CommitteeMembers ?? string.Empty));
            },
        };
        yield return new Operation
        {
            Module = module, Title = "Accept or decline an offer", ListTitle = "Stands",
            Description = "Records the applicant's answer to an allocation offer by its certificate number; a decline needs the reason and frees the stand. "
                + "Offer letter prints the offer (price, deposit, deadline) for the applicant; Allocation letter prints the confirmation once accepted. "
                + "When the scheme wants a deposit held before acceptance, it can be taken here with the acceptance: give its amount, receipt and accounts.",
            Fields = [Req("Certificate", "Certificate number"), Pick("Answer", "Answer", ["Accept", "Decline", "Offer letter", "Allocation letter"]), Opt("Reason", "Reason (decline)"),
                Today("Date", "Date"), OptNum("Deposit", "Deposit % (letters)"), OptNum("DepositAmount", "Deposit taken now (accept; blank: none)"),
                Opt("Receipt", "Deposit receipt reference (accept)"), Opt("Bank", "Bank / cash account the deposit is paid into (accept)"),
                Opt("Liability", "Deposit liability account (accept)"), Opt("Customer", "Customer paying the deposit (accept; blank: the applicant's customer by national ID)")],
            Run = async (c, i) =>
            {
                IAllocationService allocation = c.Get<IAllocationService>();
                if (i.Text("Answer").EndsWith("letter", StringComparison.Ordinal))
                {
                    return Check(await Infrastructure.Documents.HousingLetters.RenderAsync(c.Db, c.CompanyId, i.Text("Certificate"), i.Text("Answer") == "Offer letter", i.Date("Date"),
                        i.OptionalNumber("Deposit") ?? Infrastructure.Documents.HousingLetters.DefaultDepositPercent).ConfigureAwait(true));
                }

                if (i.Text("Answer") == "Accept")
                {
                    // A deposit given here is taken first (it secures the open offer), then the offer is accepted.
                    string certificate = i.Text("Certificate");
                    string? journal = null;

                    // The deposit and the acceptance are one unit: the services join this transaction, so an acceptance that fails takes the deposit back out.
                    await using Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction unit = await c.Db.Database.BeginTransactionAsync().ConfigureAwait(true);
                    if (i.OptionalNumber("DepositAmount") is decimal amount and > 0m)
                    {
                        long payer = i.Optional("Customer") is string customer ? await CustomerAsync(c, customer).ConfigureAwait(true)
                            : await (from x in c.Db.Set<StandAllocation>().AsNoTracking()
                                     where x.CompanyId == c.CompanyId && x.CertificateNumber == certificate
                                     join a in c.Db.HousingApplicants.AsNoTracking() on x.ApplicantId equals a.Id
                                     join k in c.Db.Customers.AsNoTracking() on a.IdNumber equals k.NationalIdNumber
                                     where k.CompanyId == c.CompanyId
                                     select (long?)k.Id).FirstOrDefaultAsync().ConfigureAwait(true)
                                ?? throw new OperationException("No customer has the applicant's national ID; give the customer paying the deposit.");
                        journal = Check(await c.Get<IHousingDepositService>().TakeDepositAsync(new StandDepositReceipt
                        {
                            CompanyId = c.CompanyId, CertificateNumber = certificate, CustomerId = payer, Amount = amount, Date = i.Date("Date"),
                            ReceiptReference = i.Optional("Receipt") ?? string.Empty, BankAccountCode = i.Optional("Bank") ?? string.Empty, LiabilityAccountCode = i.Optional("Liability") ?? string.Empty,
                        }).ConfigureAwait(true));
                    }

                    Core.Common.Result accepted = await allocation.AcceptAsync(c.CompanyId, certificate, i.Date("Date")).ConfigureAwait(true);
                    if (accepted.IsFailure)
                    {
                        await unit.RollbackAsync().ConfigureAwait(true);
                        string why = accepted.Error.Code == "Hse.DepositRequired"
                            ? $"{accepted.Error.Message} Take the deposit first under Housing > Sales and instalments > Stand deposits and cancellations (Take deposit), "
                                + "or give the deposit taken now, its receipt and accounts here."
                            : accepted.Error.Message;
                        throw new OperationException(journal is null ? why : $"The offer was not accepted, so the deposit was not taken either: {why}");
                    }

                    await unit.CommitAsync().ConfigureAwait(true);
                    return journal is null ? "Offer accepted." : $"Deposit taken ({journal}); offer accepted.";
                }

                _ = Check(await allocation.DeclineAsync(c.CompanyId, i.Text("Certificate"), i.Date("Date"), i.Optional("Reason") ?? string.Empty).ConfigureAwait(true), string.Empty);
                return "Offer declined; the stand is available again.";
            },
            List = async c =>
            {
                HousingNames n = await HousingNamesAsync(c).ConfigureAwait(true);
                return Table(await c.Db.HousingStands.AsNoTracking().Where(s => s.CompanyId == c.CompanyId).OrderBy(s => s.StandNumber).Page(c, 1000).ToListAsync().ConfigureAwait(true),
                    ("Stand", s => s.StandNumber), ("Scheme", s => n.Scheme(s.SchemeId)), ("Size m²", s => Q(s.SizeSquareMetres)), ("Price", s => M(s.SellingPrice)), ("Cost", s => M(s.AllocatedCost)),
                    ("Status", s => s.Status), ("Holder", s => n.Customer(s.CurrentHolderCustomerId)));
            },
        };
        yield return new Operation
        {
            Module = module, Title = "Rent tariffs and review", ListTitle = "Rent tariffs",
            Description = "Tariff: the monthly rent for a kind of unit from the first of a month, optionally only in one suburb and for one income category (the most specific "
                + "applies). Suburb / Income category: place a unit, classify a tenant household. Apply tariffs: from the first of a month every unit's asking rent and every "
                + "running tenancy's rent follows its tariff. Review by %: raises every rent (or one unit type's) by a percentage. A tariff for a unit type, suburb, "
                + "category and date already listed changes its rent (Edit fills it in); a tariff is superseded by a later one rather than deleted.",
            Editable = true,
            Fields = [Pick("Step", "Step", ["Tariff", "Apply tariffs", "Review by %", "Suburb", "Income category"]), Pick("Type", "Unit type", ["(any)", .. Enum.GetNames<RentalUnitType>()]),
                Opt("Suburb", "Suburb (blank: any)"), Pick("Category", "Income category", ["(any)", .. Enum.GetNames<RentIncomeCategory>()]), Num("Rent", "Monthly rent / percentage", "0"),
                Opt("Unit", "Unit code (suburb)"), Opt("Tenancy", "Tenancy number (income category)"), Today("Date", "Effective from (first of the month)")],
            Run = async (c, i) =>
            {
                IRentalHousingService rentals = c.Get<IRentalHousingService>();
                RentalUnitType? type = i.Text("Type") == "(any)" ? null : Enum.Parse<RentalUnitType>(i.Text("Type"));
                RentIncomeCategory? category = i.Text("Category") == "(any)" ? null : Enum.Parse<RentIncomeCategory>(i.Text("Category"));
                switch (i.Text("Step"))
                {
                    case "Tariff":
                        if (type is not RentalUnitType unitType)
                        {
                            return "Choose the unit type the tariff is for.";
                        }

                        return Check(await rentals.SetRentTariffAsync(c.CompanyId, unitType, i.Optional("Suburb"), category, i.Date("Date"), i.Number("Rent")).ConfigureAwait(true), "Tariff saved.");
                    case "Apply tariffs":
                        RentReviewResult applied = Check(await rentals.ApplyRentTariffsAsync(c.CompanyId, i.Date("Date")).ConfigureAwait(true));
                        return $"{applied.UnitsReviewed} unit(s) and {applied.TenanciesReviewed} tenancy(ies) moved to the tariff.";
                    case "Review by %":
                        RentReviewResult reviewed = Check(await rentals.ReviewRentsAsync(c.CompanyId, i.Date("Date"), i.Number("Rent"), type).ConfigureAwait(true));
                        return $"{reviewed.UnitsReviewed} unit(s) and {reviewed.TenanciesReviewed} tenancy(ies) reviewed.";
                    case "Suburb":
                        return Check(await rentals.SetUnitSuburbAsync(c.CompanyId, i.Text("Unit"), i.Optional("Suburb")).ConfigureAwait(true), "Suburb saved.");
                    default:
                        return Check(await rentals.SetIncomeCategoryAsync(c.CompanyId, i.Text("Tenancy"), category).ConfigureAwait(true), "Income category saved.");
                }
            },
            List = async c => Table(await c.Db.Set<RentTariff>().AsNoTracking().Where(t => t.CompanyId == c.CompanyId)
                    .OrderBy(t => t.UnitType).ThenBy(t => t.Suburb).ThenBy(t => t.IncomeCategory).ThenByDescending(t => t.EffectiveFrom).Page(c, 500).ToListAsync().ConfigureAwait(true),
                ("Step", _ => "Tariff"), ("Unit type", t => t.UnitType), ("Suburb", t => t.Suburb), ("Category", t => t.IncomeCategory?.ToString() ?? "(any)"), ("Date", t => D(t.EffectiveFrom)),
                ("Rent", t => M(t.MonthlyRent))),
        };
        yield return new Operation
        {
            Module = module, Title = "Rental units and tenancies", ListTitle = "Tenancies",
            Description = "Add unit: a council rental unit with its rooms and rent. Let: a tenancy to a customer (the deposit is held as a liability). Rent run: bills every tenancy "
                + "for a month (lodger fees included). End: final rent, the deposit applied to arrears and the rest refunded. Occupancy limit: the most people a unit may house.",
            Fields = [Pick("Step", "Step", ["Add unit", "Let", "Rent run", "End tenancy", "Occupancy limit"]), Opt("Unit", "Unit code"), Opt("Description", "Description (add unit)"),
                Pick("Type", "Unit type", Enum.GetNames<RentalUnitType>()), Num("Rooms", "Rooms", "1"), Num("Rent", "Monthly rent", "0"), Opt("RentAccount", "Rent income account (add unit)"),
                Opt("Tenant", "Tenant customer code (let)"), Opt("Applicant", "Rental waiting-list number (let)"), Opt("Tenancy", "Tenancy number (end)"), Num("Deposit", "Deposit (let)", "0"), Opt("Bank", "Bank account (deposit / refund)"),
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
                            WaitingListNumber = i.Optional("Applicant"),
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
                                           select new { t.TenancyNumber, u.Code, t.StartDate, t.EndDate, t.MonthlyRent, t.Deposit, t.Status, u.MaxOccupants }).Page(c, 300).ToListAsync().ConfigureAwait(true),
                ("Tenancy", t => t.TenancyNumber), ("Unit", t => t.Code), ("From", t => D(t.StartDate)), ("To", t => t.EndDate is DateOnly end ? D(end) : "-"), ("Rent", t => M(t.MonthlyRent)),
                ("Deposit", t => M(t.Deposit)), ("Status", t => t.Status), ("Most people", t => t.MaxOccupants)),
        };
        yield return new Operation
        {
            Module = module, Title = "Occupants and lodger permits", ListTitle = "Lodger permits",
            Description = "Add occupant / move out: the household living with the tenant, within the unit's occupancy limit. Permit: a lodger on a permit (the tenant keeps a room of their own; "
                + "a monthly fee goes on the rent). Revoke: ends a permit. Who lives there: the household and lodgers on a date.",
            Fields = [Pick("Step", "Step", ["Who lives there", "Add occupant", "Move out", "Issue permit", "Revoke permit"]), Req("Tenancy", "Tenancy number (permit: to revoke, its number)"),
                Opt("Name", "Name (occupant / lodger)"), Opt("IdNumber", "National ID"), Opt("Relationship", "Relationship to the tenant"), Lookup("Occupant", "Occupant (move out)", false, CurrentOccupantsAsync),
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
                        return Check(await occupancy.RemoveOccupantAsync(c.CompanyId, i.Text("Tenancy"), PickedId(i, "Occupant"), i.Date("Date")).ConfigureAwait(true), "Moved out.");
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
            List = async c => Table(await c.Db.Set<LodgerPermit>().AsNoTracking().Where(p => p.CompanyId == c.CompanyId).OrderByDescending(p => p.Id).Page(c, 300).ToListAsync().ConfigureAwait(true),
                ("Permit", p => p.PermitNumber), ("Lodger", p => p.LodgerName), ("Rooms", p => p.Rooms), ("Fee", p => M(p.MonthlyFee)), ("From", p => D(p.IssuedOn)), ("To", p => D(p.LastDay)),
                ("Status", p => p.Status)),
        };
        yield return new Operation
        {
            Module = module, Title = "Compulsory acquisition and compensation", ListTitle = "Compensation claims",
            Description = "Lodge a claim on a land parcel, serve the notice (opening the objection window), record objections and decide them, assess, offer and record the answer, "
                + "refer a dispute and record the award (each moving the provision), pay in part or settle, take possession (once the window has closed with no objection standing), "
                + "or withdraw. Ids for objections, offers and disputes are given when they are recorded.",
            Fields = [Pick("Step", "Step", ["Lodge", "Serve notice", "Objection", "Decide objection", "Assess", "Provide", "Offer", "Answer offer", "Dispute", "Award", "Pay part", "Settle", "Possession", "Withdraw"]),
                Opt("Claim", "Claim number"), Opt("Parcel", "Land parcel number (lodge)"), Opt("Party", "Claimant / objector / assessor / forum / notice reference"), Opt("Note", "Grounds / note"),
                OptNum("Amount", "Amount"), OptNum("Id", "Objection / offer / dispute id (from the list, under Open steps)"), Tick("Yes", "Upheld / accepted"), Num("Days", "Objection window (days)", "30"),
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
                    case "Provide":
                        decimal provision = Check(await compensation.ProvideAsync(c.CompanyId, claim, amount, date).ConfigureAwait(true));
                        return $"Provision for {claim} now {M(provision)} (the best estimate of what will be paid).";
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
            List = async c => Table(await c.Db.Set<CompensationClaim>().AsNoTracking().Include(x => x.Steps).Where(x => x.CompanyId == c.CompanyId).OrderByDescending(x => x.Id).Page(c, 300)
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
            Fields = [Pick("Step", "Step", ["Sign", "Raise due", "Quote settlement", "Settle early", "Set market rate"]), Opt("Stand", "Stand number (sign)"), Opt("Buyer", "Buyer customer code (sign)"),
                Opt("Agreement", "Agreement number (quote / settle)"), Num("Price", "Cash price", "0"), Num("Deposit", "Deposit", "0"), Num("Rate", "Annual interest %", "0"),
                Pick("Method", "Interest", Enum.GetNames<InterestMethod>()), Pick("Tenure", "Tenure", Enum.GetNames<InstalmentTenure>()), Num("Term", "Term (months)", "12"),
                Num("Escalation", "Yearly instalment escalation %", "0"), Today("Date", "Signed / as at"), Today("FirstDue", "First instalment due"),
                Opt("Sales", "Sales account"), Opt("Available", "Stands available (inventory) account"), Opt("Cost", "Cost of sales account"), Opt("Interest", "Interest income account"),
                Opt("Bank", "Bank account (deposit)"), Opt("Deferred", "Deferred financing account (plans over a year below the market rate)"),
                OptNum("Market", "Market credit rate % (set market rate; blank: no discounting)")],
            Run = async (c, i) =>
            {
                IInstalmentSaleService sales = c.Get<IInstalmentSaleService>();
                switch (i.Text("Step"))
                {
                    case "Set market rate":
                        Core.System.Company company = await c.Db.Companies.SingleAsync(x => x.Id == c.CompanyId).ConfigureAwait(true);
                        company.HousingMarketRatePercent = i.OptionalNumber("Market");
                        await c.Db.SaveChangesAsync().ConfigureAwait(true);
                        return company.HousingMarketRatePercent is decimal rate
                            ? $"Plans over a year below {rate:0.##}% are discounted to present value; the difference is earned as interest over the term."
                            : "Instalment sales are not discounted.";
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
                            DeferredFinancingAccountCode = i.Optional("Deferred"), Tenure = Enum.Parse<InstalmentTenure>(i.Text("Tenure")), EscalationPercent = i.Number("Escalation"),
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
            List = async c =>
            {
                HousingNames n = await HousingNamesAsync(c).ConfigureAwait(true);
                return Table(await c.Db.Set<InstalmentAgreement>().AsNoTracking().Where(a => a.CompanyId == c.CompanyId).OrderByDescending(a => a.Id).Page(c, 300).ToListAsync().ConfigureAwait(true),
                    ("Agreement", a => a.AgreementNumber), ("Stand", a => n.Stand(a.StandId)), ("Buyer", a => n.Customer(a.BuyerCustomerId)), ("Price", a => M(a.CashPrice)), ("Deposit", a => M(a.Deposit)),
                    ("Rate %", a => Q(a.AnnualRatePercent)), ("Term", a => a.TermMonths), ("Signed", a => D(a.SignedOn)), ("Status", a => a.Status));
            },
        };
        yield return new Operation
        {
            Module = module, Title = "Transfer of ownership", ListTitle = "Transfer applications",
            Description = "Lodge a transfer (sale, cession, inheritance, donation, court order) to a new holder; verify each requirement with its document; the checklist shows what is "
                + "outstanding (rates and housing account clearances are checked live); an approving officer may record that the transferee assumes the housing balance; submit, approve (by someone else), complete or reject.",
            Fields = [Pick("Step", "Step", ["Lodge", "Verify", "Checklist", "Assume balance", "Submit", "Approve", "Complete", "Reject"]), Opt("Application", "Application number"), Opt("Stand", "Stand number (lodge)"),
                Pick("Type", "Transfer type", Enum.GetNames<TransferType>()), Opt("To", "New holder customer code (lodge)"), Num("Consideration", "Sale price", "0"),
                Pick("Requirement", "Requirement (verify)", Enum.GetNames<TransferRequirement>()), Opt("Document", "Document reference / reason"), Opt("Clearing", "Clearing account (assume balance)"), Today()],
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
                    case "Assume balance":
                        decimal assumed = Check(await transfers.AssumeBalanceAsync(c.CompanyId, application, c.UserId, i.Optional("Clearing") ?? throw new OperationException("Give the clearing account the balance passes through."), date)
                            .ConfigureAwait(true));
                        return $"Balance of {M(assumed)} assumed by the transferee (approved); it moves to them, with any instalment agreement, when the transfer completes.";
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
            List = async c =>
            {
                HousingNames n = await HousingNamesAsync(c).ConfigureAwait(true);
                return Table(await c.Db.Set<TransferApplication>().AsNoTracking().Where(a => a.CompanyId == c.CompanyId).OrderByDescending(a => a.Id).Page(c, 300).ToListAsync().ConfigureAwait(true),
                    ("Application", a => a.ApplicationNumber), ("Stand", a => n.Stand(a.StandId)), ("Type", a => a.TransferType), ("Sale price", a => M(a.Consideration)), ("Status", a => a.Status));
            },
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
                + "see its position (cost build-up and what still blocks it), complete it into the land bank, or abandon it (costs written off). Under a land acquisition workflow, "
                + "submit it for approval at its value first; it completes once approved.",
            Fields = [Pick("Step", "Step", ["Open", "Valuation", "Council offer", "Counterparty offer", "Agree price", "Break off talks", "Sign off", "Add cost", "Position", "Submit for approval", "Complete", "Abandon"]),
                Opt("Acquisition", "Acquisition number"),
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
                    case "Council offer":
                    case "Counterparty offer":
                        NegotiationParty party = i.Text("Step") == "Council offer" ? NegotiationParty.Council : NegotiationParty.Counterparty;
                        return Check(await land.OfferAsync(c.CompanyId, acquisition, party, i.Number("Amount"), date, i.Optional("Description")).ConfigureAwait(true),
                            n => $"Offer {n} recorded: {party} {M(i.Number("Amount"))}. Agree price accepts the latest offer.");
                    case "Agree price":
                        return Check(await land.AgreePriceAsync(c.CompanyId, acquisition, date).ConfigureAwait(true), price => $"Price agreed at {M(price)}; capitalise it as the purchase price cost.");
                    case "Break off talks":
                        return Check(await land.BreakOffAsync(c.CompanyId, acquisition, i.Optional("Resolution") ?? i.Optional("Description") ?? string.Empty).ConfigureAwait(true), "Talks broken off.");
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
                    case "Submit for approval":
                        return Check(await land.SubmitForApprovalAsync(c.CompanyId, acquisition, c.UserId).ConfigureAwait(true), value => $"Submitted for approval at {M(value)}.");
                    case "Complete":
                        return Check(await land.CompleteAsync(c.CompanyId, acquisition, date).ConfigureAwait(true), parcel => $"Completed into the land bank (parcel {parcel}).");
                    default:
                        decimal written = Check(await land.AbandonAsync(c.CompanyId, acquisition, date, i.Optional("Resolution") ?? string.Empty, i.Optional("Expense") ?? string.Empty, i.Optional("Wip") ?? string.Empty)
                            .ConfigureAwait(true));
                        return $"Abandoned; {M(written)} written off.";
                }
            },
            List = async c => Table(await c.Db.Set<LandAcquisition>().AsNoTracking().Include(a => a.Costs).Where(a => a.CompanyId == c.CompanyId).OrderByDescending(a => a.Id).Page(c, 300).ToListAsync()
                    .ConfigureAwait(true),
                ("Acquisition", a => a.AcquisitionNumber), ("Parcel", a => a.ParcelNumber), ("Method", a => a.Method), ("Counterparty", a => a.Counterparty), ("Valued", a => a.ValuedAt is decimal v ? M(v) : "-"),
                ("Cost", a => M(a.TotalCost)), ("Status", a => a.Status), ("Approval", a => a.Approval)),
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
            Description = "Places a block on a stand (court interdict, family dispute, suspected fraud, double allocation, boundary dispute…) with its document - a blocked stand cannot be "
                + "allocated, sold, leased or transferred - or lifts it with the reason. History shows every block on a stand.",
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
                            $"{D(b.PlacedOn)} {b.Reason}: {b.Detail} ({b.DocumentReference}){(b.LiftedOn is DateOnly lifted ? $" - lifted {D(lifted)}: {b.LiftReason}" : " - in force")}"));
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
            Module = module, Title = "Surrender a stand", ListTitle = "Surrenders this year",
            Description = "A holder gives a stand back to council (requested by you, approved by another user): an instalment agreement closes and what is owed is written off, "
                + "a sold stand comes back into inventory at the lower of cost and net realisable value, and any refund is credited to the holder's account.",
            Fields = [Req("Stand", "Stand number"), Req("Reason", "Reason"), Req("Approver", "Approving user name"), Req("GainLoss", "Surrender gain / loss account"),
                Opt("Available", "Stands available (inventory) account (a sold stand)"), OptNum("Nrv", "Net realisable value (a sold stand)"), OptNum("Refund", "Refund to the holder"),
                Opt("RefundAccount", "Refund charged to account"), Today()],
            Run = async (c, i) =>
            {
                StandSurrenderEntry done = Check(await c.Get<IRepossessionService>().SurrenderAsync(new StandSurrenderRequest
                {
                    CompanyId = c.CompanyId, StandNumber = i.Text("Stand"), Date = i.Date("Date"), Reason = i.Text("Reason"), RequestedByUserId = c.UserId,
                    ApprovedByUserId = await UserAsync(c, i.Text("Approver")).ConfigureAwait(true), GainLossAccountCode = i.Text("GainLoss"),
                    StandsAvailableAccountCode = i.Optional("Available") ?? string.Empty, NetRealisableValue = i.OptionalNumber("Nrv") ?? 0m, Refund = i.OptionalNumber("Refund") ?? 0m,
                    RefundAccountCode = i.Optional("RefundAccount"),
                }).ConfigureAwait(true));
                return $"{done.Reference}: stand {done.StandNumber} surrendered by {done.Holder}; receivable {M(done.ReceivableDerecognised)} written off, "
                    + $"back in inventory at {M(done.InventoryRecognised)}, refund {M(done.Refund)} credited.";
            },
            List = async c => Table(await c.Get<IRepossessionService>().SurrenderRegisterAsync(c.CompanyId, new DateOnly(DateTime.Today.Year, 1, 1), DateOnly.FromDateTime(DateTime.Today))
                    .ConfigureAwait(true),
                ("Reference", r => r.Reference), ("Stand", r => r.StandNumber), ("Holder", r => r.Holder), ("Date", r => D(r.Date)), ("Receivable", r => M(r.ReceivableDerecognised)),
                ("Inventory", r => M(r.InventoryRecognised)), ("Refund", r => M(r.Refund))),
        };
        yield return new Operation
        {
            Module = module, Title = "Receivables control per tenure type", ListTitle = "Tenure control accounts",
            Description = "Instalment debtors post to a dedicated receivables control account per tenure type (instalment sale, rent-to-buy, lease with option): a buyer goes "
                + "onto it when their agreement is signed. Customer control sets one customer's control directly (only before they have items). The control reconciliation "
                + "matches each control to its own customers.",
            Fields = [Pick("Step", "Step", ["Tenure control", "Customer control"]), Pick("Tenure", "Tenure type", Enum.GetNames<InstalmentTenure>()), CustomerPick("Customer", "Customer", required: false),
                Opt("Account", "Receivables control account (blank: main)")],
            Run = async (c, i) =>
            {
                Application.Abstractions.Trading.IReceivableControlService controls = c.Get<Application.Abstractions.Trading.IReceivableControlService>();
                return i.Text("Step") == "Customer control"
                    ? Check(await controls.SetCustomerControlAsync(c.CompanyId, i.Optional("Customer") ?? string.Empty, i.Optional("Account")).ConfigureAwait(true), "Customer control set.")
                    : Check(await controls.SetTenureControlAsync(c.CompanyId, Enum.Parse<InstalmentTenure>(i.Text("Tenure")), i.Optional("Account")).ConfigureAwait(true), "Tenure control set.");
            },
            List = async c => Table(await c.Get<Application.Abstractions.Trading.IReceivableControlService>().TenureControlsAsync(c.CompanyId).ConfigureAwait(true),
                ("Tenure", t => t.Tenure), ("Control account", t => t.AccountCode)),
        };
        yield return new Operation
        {
            Module = module, Title = "Rental property accounting (IAS 40)", ListTitle = "Investment property and PPE register",
            Description = "Rental units are held to earn rentals: carry each as investment property (cost or fair value model) or PPE, never as stand stock. Designate brings the unit "
                + "into its asset account at its carrying amount from the account it comes from; Revalue remeasures fair-value investment property, the change to the fair-value "
                + "gain or loss account.",
            Fields = [Pick("Step", "Step", ["Designate", "Revalue"]), Req("Unit", "Unit code"), Pick("Treatment", "Carried as", Enum.GetNames<RentalPropertyTreatment>()),
                Opt("Asset", "Asset account"), Num("Amount", "Carrying amount / fair value", "0"), Opt("Contra", "From account (designate)"), Opt("GainLoss", "Fair-value gain/loss account (revalue)"), Today()],
            Run = async (c, i) =>
            {
                IRentalPropertyAccountingService accounting = c.Get<IRentalPropertyAccountingService>();
                string journal = i.Text("Step") == "Revalue"
                    ? Check(await accounting.RevalueAsync(c.CompanyId, i.Text("Unit"), i.Number("Amount"), i.Date("Date"), i.Optional("GainLoss") ?? string.Empty).ConfigureAwait(true))
                    : Check(await accounting.DesignateAsync(c.CompanyId, i.Text("Unit"), Enum.Parse<RentalPropertyTreatment>(i.Text("Treatment")), i.Optional("Asset") ?? string.Empty, i.Number("Amount"),
                        i.Optional("Contra") ?? string.Empty, i.Date("Date")).ConfigureAwait(true));
                return journal.Length == 0 ? "Saved (nothing to post)." : $"Posted in journal {journal}.";
            },
            List = async c => Table(await c.Get<IRentalPropertyAccountingService>().RegisterAsync(c.CompanyId).ConfigureAwait(true),
                ("Unit", r => r.UnitCode), ("Carried as", r => r.Treatment), ("Account", r => r.AssetAccountCode), ("Carrying amount", r => M(r.CarryingAmount)),
                ("Valued", r => r.LastValuedOn is DateOnly d ? D(d) : "-")),
        };
        yield return new Operation
        {
            Module = module, Title = "Rental maintenance", ListTitle = "Rental units",
            Description = "Report a repair on a unit; complete it with its cost (recharged to the tenant when they caused it); record an inspection with its condition grade (1–5) and "
                + "whether it needs follow-up, and close the follow-up; History summarises a unit's repairs and condition. Void details record why a void unit is empty and its "
                + "refurbishment job. The planned maintenance programme: plan a task (due on the date, then every so many months), list what is due, and mark one done - "
                + "which raises the repair that carries its cost.",
            Fields = [Pick("Step", "Step", ["Report repair", "Complete repair", "Inspection", "Close follow-up", "History", "Void details", "Plan task", "Planned due", "Planned done"]),
                Opt("Unit", "Unit code"), Opt("Repair", "Repair number (complete)"), Opt("Reason", "Void reason"), Opt("Job", "Refurbishment job (contract) number"),
                Num("Every", "Planned: every (months, 0 = once)", "12"), Lookup("Task", "Planned task (Planned done)", false, PlannedTasksAsync),
                Opt("Description", "Description / findings"), Num("Cost", "Cost", "0"), Opt("Expense", "Repairs expense account"), Opt("PaidFrom", "Paid from account"), Tick("TenantCaused", "Tenant caused it"),
                Opt("Recharge", "Recharge income account"), Opt("Inspector", "Inspector"), Num("Grade", "Condition grade (1–5)", "3"), Tick("FollowUp", "Needs follow-up"),
                Lookup("Inspection", "Inspection (close)", false, OpenFollowUpsAsync), Today()],
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
                        return Check(await maintenance.CloseFollowUpAsync(c.CompanyId, PickedId(i, "Inspection"), i.Date("Date")).ConfigureAwait(true), "Follow-up closed.");
                    case "Void details":
                        return Check(await maintenance.SetVoidDetailsAsync(c.CompanyId, unit, i.Optional("Reason"), i.Optional("Job")).ConfigureAwait(true), "Void details saved.");
                    case "Plan task":
                        return $"Planned task {Check(await maintenance.PlanAsync(c.CompanyId, unit, i.Optional("Description") ?? string.Empty, i.Date("Date"), i.Whole("Every")).ConfigureAwait(true))} added.";
                    case "Planned due":
                        IReadOnlyList<PlannedTaskDue> due = await maintenance.DueAsync(c.CompanyId, i.Date("Date")).ConfigureAwait(true);
                        return due.Count == 0 ? "Nothing planned is due." : string.Join(Environment.NewLine, due.Select(t => $"{t.Id,6} {D(t.Due)} {t.UnitCode,-10} {t.Description}"));
                    case "Planned done":
                        return $"Done; repair {Check(await maintenance.MarkDoneAsync(c.CompanyId, PickedId(i, "Task"), i.Date("Date")).ConfigureAwait(true))} raised for its cost.";
                    default:
                        UnitMaintenanceHistory history = Check(await maintenance.HistoryAsync(c.CompanyId, unit).ConfigureAwait(true));
                        return $"{history.UnitCode}: {history.RepairsOpen} repair(s) open, repairs cost {M(history.RepairCost)}, recharged {M(history.Recharged)}, "
                            + $"latest condition {(history.LatestConditionGrade is int grade ? grade.ToString(CultureInfo.InvariantCulture) : "-")}, {history.FollowUpsOpen} follow-up(s) open.";
                }
            },
            List = async c => Table(await c.Db.Set<RentalUnit>().AsNoTracking().Where(u => u.CompanyId == c.CompanyId).OrderBy(u => u.Code).Page(c, 500).ToListAsync().ConfigureAwait(true),
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
                + "an applicant belongs to (comma-separated) so allocation runs can meet the quotas. Setting a quota already listed changes its target; Delete sets it to 0% "
                + "(no share reserved).",
            KeyColumn = "Quota",
            Delete = async (c, key) =>
            {
                string[] parts = key.Split(" in ", 2, StringSplitOptions.TrimEntries);
                BeneficiaryGroups group = Enum.TryParse(parts[0], ignoreCase: true, out BeneficiaryGroups parsed) ? parsed : throw new OperationException($"\"{key}\" is not a quota.");
                return Check(await c.Get<IAllocationQuotaService>().SetQuotaAsync(c.CompanyId, parts.Length > 1 ? parts[1] : null, group, 0m).ConfigureAwait(true), $"Quota {key} set to 0%.");
            },
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
            List = async c =>
            {
                Dictionary<long, string> schemes = await c.Db.HousingSchemes.AsNoTracking().Where(s => s.CompanyId == c.CompanyId).ToDictionaryAsync(s => s.Id, s => s.Code).ConfigureAwait(true);
                string? Scheme(AllocationQuota q) => q.SchemeId is long id ? schemes.GetValueOrDefault(id, id.ToString(CultureInfo.InvariantCulture)) : null;
                return Table(await c.Db.Set<AllocationQuota>().AsNoTracking().Where(q => q.CompanyId == c.CompanyId).ToListAsync().ConfigureAwait(true),
                    ("Step", _ => "Set quota"), ("Quota", q => Scheme(q) is string s ? $"{q.Group} in {s}" : q.Group.ToString()), ("Scheme", q => Scheme(q)), ("Group", q => q.Group),
                    ("Target %", q => Q(q.TargetPercent)));
            },
        };
        yield return new Operation
        {
            Module = module, Title = "Applicant intake and duplicate queue", ListTitle = "Duplicate queue",
            Description = "Contact: an applicant's gender, addresses, phone and employer. Lodge / Verify: supporting documents (ID copy, proof of residence, payslip, affidavit) "
                + "verified one by one with the officer's name - the applicant is verified once all are. Death: adds to the deceased register. Scan: finds suspected duplicates "
                + "(same ID, similar name with the same birth date, same phone) and applicants on the deceased register and queues them. Merge (keeps the earlier registration), "
                + "Reject (removes the second) or Clear a case, with a reason.",
            Fields = [Pick("Step", "Step", ["Contact", "Lodge document", "Verify document", "Death", "Scan", "Merge", "Reject", "Clear"]), Opt("Applicant", "Waiting-list number"),
                Pick("Gender", "Gender", Enum.GetNames<Core.Payroll.Gender>()), Opt("Physical", "Physical address"), Opt("Postal", "Postal address"), Opt("Phone", "Phone"), Opt("Employer", "Employer"),
                Pick("DocType", "Document", Enum.GetNames<ApplicantDocumentType>()), Opt("Description", "Description / name (death)"), OptNum("Id", "Document / case id (case from the list)"),
                Opt("Officer", "Verifying officer"), Opt("IdNumber", "National ID (death)"), Opt("Reason", "Reason (decision)"), Today()],
            Run = async (c, i) =>
            {
                IApplicantIntakeService intake = c.Get<IApplicantIntakeService>();
                string applicant = i.Optional("Applicant") ?? string.Empty;
                long id = (long)(i.OptionalNumber("Id") ?? 0m);
                switch (i.Text("Step"))
                {
                    case "Contact":
                        return Check(await intake.SetContactAsync(c.CompanyId, applicant, new ApplicantContact(Enum.Parse<Core.Payroll.Gender>(i.Text("Gender")), i.Optional("Physical"),
                            i.Optional("Postal"), i.Optional("Phone"), i.Optional("Employer"))).ConfigureAwait(true), "Details saved.");
                    case "Lodge document":
                        return Check(await intake.LodgeDocumentAsync(c.CompanyId, applicant, Enum.Parse<ApplicantDocumentType>(i.Text("DocType")), i.Optional("Description") ?? string.Empty,
                            i.Date("Date")).ConfigureAwait(true), doc => $"Document {doc} lodged.");
                    case "Verify document":
                        bool all = Check(await intake.VerifyDocumentAsync(c.CompanyId, id, i.Optional("Officer") ?? string.Empty, i.Date("Date")).ConfigureAwait(true));
                        return all ? "Verified - every document is, so the applicant is verified." : "Verified; other documents still await verification.";
                    case "Death":
                        return Check(await intake.RecordDeathAsync(c.CompanyId, i.Optional("IdNumber") ?? string.Empty, i.Optional("Description") ?? string.Empty, i.Date("Date")).ConfigureAwait(true),
                            "Added to the deceased register.");
                    case "Scan":
                        DuplicateScan scan = await intake.ScanAsync(c.CompanyId, i.Date("Date")).ConfigureAwait(true);
                        return $"{scan.Opened} new case(s) queued ({scan.Deceased} applicant(s) on the deceased register).";
                    default:
                        DuplicateCaseStatus decision = i.Text("Step") switch { "Merge" => DuplicateCaseStatus.Merged, "Reject" => DuplicateCaseStatus.Rejected, _ => DuplicateCaseStatus.Cleared };
                        return Check(await intake.DecideAsync(c.CompanyId, id, decision, i.Optional("Reason") ?? string.Empty, c.UserId, i.Date("Date")).ConfigureAwait(true), $"Case {id} {decision.ToString().ToLowerInvariant()}.");
                }
            },
            List = async c => Table(await c.Get<IApplicantIntakeService>().QueueAsync(c.CompanyId).ConfigureAwait(true),
                ("Case", x => x.Id), ("Status", x => x.Status), ("First", x => $"{x.FirstSource} {x.FirstReference}"), ("Second", x => $"{x.SecondSource} {x.SecondReference}"),
                ("Reason", x => x.Reason), ("Decision", x => x.DecisionReason ?? "")),
        };
        yield return new Operation
        {
            Module = module, Title = "Stand, parcel and scheme details", ListTitle = "Stand register",
            Description = "Stand: address, ward / suburb / zone, intended use, zoning, servicing phase and the services available (water, sewer, road, electricity, storm water). "
                + "Remark: adds a dated line to the stand's remarks history; Remarks lists it. Parcel: locality, ward, land use, tenure, improvements and occupation. "
                + "Scheme: funding source, start / completion dates and the deposit an offer must be secured with before it is accepted. Edit fills a stand's details in to "
                + "change them (a stand carries cost and history, so it is never deleted here).",
            Editable = true,
            Fields = [Pick("Step", "Step", ["Stand", "Remark", "Remarks", "Parcel", "Scheme"]), Opt("Code", "Stand / parcel / scheme number"), Opt("Address", "Street address"),
                Opt("Ward", "Ward"), Opt("Suburb", "Suburb / locality"), Opt("Zone", "Zone"), Opt("Use", "Intended use / land use"), Opt("Zoning", "Zoning"), Opt("Phase", "Servicing phase"),
                Tick("Water", "Water"), Tick("Sewer", "Sewer"), Tick("Road", "Road"), Tick("Power", "Electricity"), Tick("Storm", "Storm water"), Opt("Remark", "Remark / improvements"),
                Pick("Tenure", "Parcel tenure", ["", .. Enum.GetNames<ParcelTenure>()]), Pick("Occupation", "Parcel occupation", ["", .. Enum.GetNames<ParcelOccupation>()]),
                Pick("Funding", "Scheme funding", ["", .. Enum.GetNames<SchemeFunding>()]), OptDate("Start", "Scheme start"), OptDate("End", "Scheme completion"),
                OptNum("Deposit", "Scheme deposit before acceptance")],
            Run = async (c, i) =>
            {
                IHousingRegisterDetailsService details = c.Get<IHousingRegisterDetailsService>();
                string code = i.Optional("Code") ?? string.Empty;
                static DateOnly? Date(string? text) => text is null ? null : DateOnly.ParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture);
                static T? Choice<T>(string text) where T : struct, Enum => text.Length == 0 ? null : Enum.Parse<T>(text);
                switch (i.Text("Step"))
                {
                    case "Stand":
                        return Check(await details.SetStandDetailsAsync(c.CompanyId, code, new StandDetails(i.Optional("Address"), i.Optional("Ward"), i.Optional("Suburb"), i.Optional("Zone"),
                            i.Optional("Use"), i.Optional("Zoning"), i.Optional("Phase"), i.Flag("Water"), i.Flag("Sewer"), i.Flag("Road"), i.Flag("Power"), i.Flag("Storm"))).ConfigureAwait(true), "Stand details saved.");
                    case "Remark":
                        return Check(await details.AddStandRemarkAsync(c.CompanyId, code, i.Optional("Remark") ?? string.Empty, $"user {c.UserId}").ConfigureAwait(true), "Remark added.");
                    case "Remarks":
                        IReadOnlyList<(DateTimeOffset At, string RecordedBy, string Text)> remarks = await details.StandRemarksAsync(c.CompanyId, code).ConfigureAwait(true);
                        return remarks.Count == 0 ? "No remarks." : string.Join(Environment.NewLine, remarks.Select(r => $"{r.At.ToLocalTime():yyyy-MM-dd HH:mm}  {r.RecordedBy}: {r.Text}"));
                    case "Parcel":
                        return Check(await details.SetParcelDetailsAsync(c.CompanyId, code, new ParcelDetails(i.Optional("Suburb"), i.Optional("Ward"), i.Optional("Use"),
                            Choice<ParcelTenure>(i.Text("Tenure")), i.Optional("Remark"), Choice<ParcelOccupation>(i.Text("Occupation")))).ConfigureAwait(true), "Parcel details saved.");
                    default:
                        return Check(await details.SetSchemeDetailsAsync(c.CompanyId, code, new SchemeDetails(Choice<SchemeFunding>(i.Text("Funding")), Date(i.Optional("Start")), Date(i.Optional("End")), i.OptionalNumber("Deposit")))
                            .ConfigureAwait(true), "Scheme details saved.");
                }
            },
            List = async c => Table(await c.Db.HousingStands.AsNoTracking().Where(s => s.CompanyId == c.CompanyId).OrderBy(s => s.StandNumber).Page(c, 1000).ToListAsync().ConfigureAwait(true),
                ("Step", _ => "Stand"), ("Code", s => s.StandNumber), ("Address", s => s.StreetAddress), ("Ward", s => s.Ward), ("Suburb", s => s.Suburb), ("Zone", s => s.Zone),
                ("Use", s => s.IntendedUse), ("Zoning", s => s.Zoning), ("Phase", s => s.ServicingPhase), ("Water", s => s.HasWater), ("Sewer", s => s.HasSewer), ("Road", s => s.HasRoad),
                ("Electricity", s => s.HasElectricity), ("Storm water", s => s.HasStormWater), ("Status", s => s.Status)),
        };
        yield return new Operation
        {
            Module = module, Title = "Instalment arrears and default notices", ListTitle = "Agreements in arrears",
            Description = "The arrears stages: a reminder from the first unpaid instalment, a letter of demand after 30 days, a notice of breach after 60 and a notice of cancellation "
                + "after 90 (once the breach notice has stood 30 days) - each issued only after the previous one is served, with proof of service recorded. "
                + "Penalty: sets an agreement's annual penalty rate on arrears; Charge penalties invoices a month's penalty interest on each agreement's arrears once.",
            Fields = [Pick("Step", "Step", ["Issue next notice", "Record service", "Penalty rate", "Charge penalties"]), Opt("Agreement", "Agreement number"), Opt("Notice", "Notice number (service)"),
                Opt("Method", "Served by (hand, registered post, email)"), Opt("ServedBy", "Who served it"), OptNum("Rate", "Penalty rate % a year"), Today()],
            Run = async (c, i) =>
            {
                IInstalmentDefaultService defaults = c.Get<IInstalmentDefaultService>();
                switch (i.Text("Step"))
                {
                    case "Issue next notice":
                        (string number, string text) = Check(await defaults.IssueNextAsync(c.CompanyId, i.Optional("Agreement") ?? string.Empty, i.Date("Date")).ConfigureAwait(true));
                        return $"Notice {number} issued:{Environment.NewLine}{Environment.NewLine}{text}";
                    case "Record service":
                        return Check(await defaults.RecordServiceAsync(c.CompanyId, i.Optional("Notice") ?? string.Empty, i.Date("Date"), i.Optional("Method") ?? string.Empty,
                            i.Optional("ServedBy") ?? string.Empty).ConfigureAwait(true), "Service recorded.");
                    case "Penalty rate":
                        return Check(await defaults.SetPenaltyRateAsync(c.CompanyId, i.Optional("Agreement") ?? string.Empty, i.OptionalNumber("Rate") ?? 0m).ConfigureAwait(true), "Penalty rate saved.");
                    default:
                        PenaltyRun run = Check(await defaults.ChargePenaltyInterestAsync(c.CompanyId, i.Date("Date")).ConfigureAwait(true));
                        return $"{run.Agreements} agreement(s) charged {M(run.Total)} penalty interest.";
                }
            },
            List = async c => Table(await c.Get<IInstalmentDefaultService>().ReviewAsync(c.CompanyId, DateOnly.FromDateTime(DateTime.Today)).ConfigureAwait(true),
                ("Agreement", d => d.AgreementNumber), ("Buyer", d => d.Buyer), ("Arrears", d => M(d.Arrears)), ("Days", d => d.DaysOverdue), ("Last notice", d => d.LastStage?.ToString() ?? "-"),
                ("Next", d => d.NextStage?.ToString() ?? d.Why ?? "-")),
        };
        yield return new Operation
        {
            Module = module, Title = "Housing cooperatives", ListTitle = "Cooperatives",
            Description = "Registers a cooperative, adds and removes members, allocates stands to it (\"S1,S2\") and sub-allocates each to a member; Stands lists what it holds and to whom. "
                + "Account: links its receivables account (customer code). Charge: invoices it. Contribution: a member's payment receipted on its account. Statement: its account for the "
                + "period from the date in Since to Date, with each member's contributions. Register with an existing code corrects that cooperative's name and registration "
                + "number (a cooperative holding stands and an account is not deleted).",
            Editable = true,
            Fields = [Pick("Step", "Step", ["Register", "Add member", "Remove member", "Allocate stands", "Sub-allocate", "Stands", "Account", "Charge", "Contribution", "Statement"]),
                Req("Cooperative", "Cooperative code"), Opt("Name", "Name / member name"), Opt("Registration", "Registration number"), Opt("IdNumber", "Member national ID"),
                Opt("Stands", "Stand number(s), comma-separated"), Opt("Customer", "Account customer code"), Num("Amount", "Amount", "0"), Opt("Account", "Income account (charge) / bank (contribution)"),
                Opt("Reference", "Document / receipt number"), OptDate("Since", "Statement from"), Opt("Approver", "Approving user name (allocate stands)"), Today()],
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
                        if (await ExistingAsync<Cooperative>(c, code, x => x.Code).ConfigureAwait(true) is { } known)
                        {
                            if (string.IsNullOrWhiteSpace(i.Optional("Name")) || string.IsNullOrWhiteSpace(i.Optional("Registration")))
                            {
                                throw new OperationException("A cooperative needs a name and a registration number.");
                            }

                            known.Rename(i.Optional("Name")!.Trim(), i.Optional("Registration")!.Trim());
                            await c.Db.SaveChangesAsync().ConfigureAwait(true);
                            return $"Cooperative {known.Code} changed.";
                        }

                        return Check(await cooperatives.RegisterAsync(c.CompanyId, code, i.Optional("Name") ?? string.Empty, i.Optional("Registration") ?? string.Empty).ConfigureAwait(true), _ => "Cooperative registered.");
                    case "Add member":
                        return Check(await cooperatives.AddMemberAsync(c.CompanyId, code, i.Optional("IdNumber") ?? string.Empty, i.Optional("Name") ?? string.Empty, i.Date("Date")).ConfigureAwait(true), "Member added.");
                    case "Remove member":
                        return Check(await cooperatives.RemoveMemberAsync(c.CompanyId, code, i.Optional("IdNumber") ?? string.Empty).ConfigureAwait(true), "Member removed.");
                    case "Allocate stands":
                        return $"{Check(await cooperatives.AllocateStandsAsync(c.CompanyId, code, await StandIds().ConfigureAwait(true), i.Date("Date"), c.UserId,
                            await UserAsync(c, i.Optional("Approver") ?? throw new OperationException("Give the approving user.")).ConfigureAwait(true)).ConfigureAwait(true))} stand(s) allocated.";
                    case "Sub-allocate":
                        List<long> stand = await StandIds().ConfigureAwait(true);
                        return Check(await cooperatives.SubAllocateAsync(c.CompanyId, code, stand.FirstOrDefault(), i.Optional("IdNumber") ?? string.Empty, i.Date("Date")).ConfigureAwait(true), "Sub-allocated.");
                    case "Account":
                        return Check(await cooperatives.LinkAccountAsync(c.CompanyId, code, await CustomerAsync(c, i.Optional("Customer") ?? string.Empty).ConfigureAwait(true)).ConfigureAwait(true),
                            "Account linked.");
                    case "Charge":
                        return Check(await cooperatives.ChargeAsync(c.CompanyId, code, i.Date("Date"), i.Number("Amount"), i.Optional("Account") ?? string.Empty, i.Optional("Reference") ?? string.Empty)
                            .ConfigureAwait(true), "Charged.");
                    case "Contribution":
                        return Check(await cooperatives.RecordContributionAsync(c.CompanyId, code, i.Optional("IdNumber") ?? string.Empty, i.Date("Date"), i.Number("Amount"),
                            i.Optional("Account") ?? string.Empty, i.Optional("Reference") ?? string.Empty).ConfigureAwait(true), "Contribution receipted.");
                    case "Statement":
                        DateOnly since = i.Optional("Since") is string text ? DateOnly.ParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture) : new DateOnly(i.Date("Date").Year, 1, 1);
                        CooperativeStatement statement = Check(await cooperatives.StatementAsync(c.CompanyId, code, since, i.Date("Date")).ConfigureAwait(true));
                        return string.Join(Environment.NewLine, new[] { $"{statement.Cooperative}: {D(statement.From)} to {D(statement.To)}", $"Opening balance {M(statement.Opening),14}" }
                            .Concat(statement.Lines.Select(l => $"{D(l.Date)}  {l.Reference,-14} {l.Description,-46} {(l.Debit == 0m ? string.Empty : M(l.Debit)),12} {(l.Credit == 0m ? string.Empty : M(l.Credit)),12} {M(l.Balance),14}"))
                            .Append($"Closing balance {M(statement.Closing),14}")
                            .Concat(statement.Contributions.Select(m => $"  contributed by {m.FullName} ({m.IdNumber}): {M(m.Total)}")));
                    default:
                        IReadOnlyList<CooperativeStandEntry> held = Check(await cooperatives.StandsAsync(c.CompanyId, code).ConfigureAwait(true));
                        return held.Count == 0 ? "No stands." : string.Join(Environment.NewLine, held.Select(h => $"{h.StandNumber,-12} {D(h.AllocatedOn)}  {h.MemberName ?? "(not sub-allocated)"}"));
                }
            },
            List = async c => Table(await c.Db.Set<Cooperative>().AsNoTracking().Where(x => x.CompanyId == c.CompanyId).OrderBy(x => x.Code).ToListAsync().ConfigureAwait(true),
                ("Step", _ => "Register"), ("Cooperative", x => x.Code), ("Name", x => x.Name), ("Registration", x => x.RegistrationNumber)),
        };
        yield return new Operation
        {
            Module = module, Title = "Development levies", ListTitle = "Levies",
            Description = "Defines a levy charged on allocation or on transfer - flat, per m² or a percentage of the price - to an income account, or deactivates it; charges the "
                + "levies due on an allocation certificate or a transfer application. Define with an existing code changes that levy's name, rate and income account for "
                + "charges raised from now on (its trigger and basis stay); Delete deactivates it (charges already raised stand).",
            Delete = async (c, code) => Check(await c.Get<IDevelopmentLevyService>().DeactivateAsync(c.CompanyId, code).ConfigureAwait(true), $"Levy {code.Trim().ToUpperInvariant()} deactivated (kept in history)."),
            Fields = [Pick("Step", "Step", ["Define", "Deactivate", "Defer", "Charge allocation", "Charge transfer"]), Opt("Code", "Levy code"), Opt("Name", "Name"),
                OptNum("Months", "Months its income is spread over (defer; 0: at once)"), Opt("Deferred", "Deferred income account (defer)"),
                Pick("Trigger", "Charged on", Enum.GetNames<LevyTrigger>()), Pick("Basis", "Basis", Enum.GetNames<LevyBasis>()), Num("Rate", "Rate / amount", "0"), Opt("Income", "Income account"),
                Opt("Document", "Certificate / application number (charge)"), Today()],
            Run = async (c, i) =>
            {
                IDevelopmentLevyService levies = c.Get<IDevelopmentLevyService>();
                static string Raised(IReadOnlyList<LevyRaised> raised) => raised.Count == 0 ? "No levies due." : string.Join(Environment.NewLine, raised.Select(r => $"{r.LevyCode}: {M(r.Amount)} ({r.DocumentNumber})"));
                if (i.Text("Step") == "Define" && await ExistingAsync<DevelopmentLevy>(c, i.Optional("Code") ?? string.Empty, x => x.Code).ConfigureAwait(true) is { } levy)
                {
                    if (levy.Trigger != Enum.Parse<LevyTrigger>(i.Text("Trigger")) || levy.Basis != Enum.Parse<LevyBasis>(i.Text("Basis")))
                    {
                        throw new OperationException($"Levy {levy.Code}'s trigger and basis cannot change; deactivate it and define a new levy instead.");
                    }

                    if (string.IsNullOrWhiteSpace(i.Optional("Name")) || string.IsNullOrWhiteSpace(i.Optional("Income")) || i.Number("Rate") <= 0m
                        || (levy.Basis == LevyBasis.PercentOfPrice && i.Number("Rate") > 100m))
                    {
                        throw new OperationException("A levy needs a name, a positive rate (a percentage no more than 100) and its income account.");
                    }

                    levy.Change(i.Optional("Name")!.Trim(), i.Number("Rate"), i.Optional("Income")!.Trim());
                    await c.Db.SaveChangesAsync().ConfigureAwait(true);
                    return $"Levy {levy.Code} changed.";
                }

                return i.Text("Step") switch
                {
                    "Define" => Check(await levies.DefineAsync(c.CompanyId, i.Optional("Code") ?? string.Empty, i.Optional("Name") ?? string.Empty, Enum.Parse<LevyTrigger>(i.Text("Trigger")),
                        Enum.Parse<LevyBasis>(i.Text("Basis")), i.Number("Rate"), i.Optional("Income") ?? string.Empty).ConfigureAwait(true), _ => "Levy defined."),
                    "Deactivate" => Check(await levies.DeactivateAsync(c.CompanyId, i.Optional("Code") ?? string.Empty).ConfigureAwait(true), "Levy deactivated."),
                    "Defer" => Check(await levies.SetDeferralAsync(c.CompanyId, i.Optional("Code") ?? string.Empty, (int)(i.OptionalNumber("Months") ?? 0m), i.Optional("Deferred")).ConfigureAwait(true),
                        "Levy deferral saved; its income is released monthly with the prepayment releases."),
                    "Charge allocation" => Raised(Check(await levies.ChargeAllocationAsync(c.CompanyId, i.Optional("Document") ?? string.Empty, i.Date("Date")).ConfigureAwait(true))),
                    _ => Raised(Check(await levies.ChargeTransferAsync(c.CompanyId, i.Optional("Document") ?? string.Empty, i.Date("Date")).ConfigureAwait(true))),
                };
            },
            List = async c => Table(await c.Db.Set<DevelopmentLevy>().AsNoTracking().Where(x => x.CompanyId == c.CompanyId).OrderBy(x => x.Code).ToListAsync().ConfigureAwait(true),
                ("Step", _ => "Define"), ("Code", x => x.Code), ("Name", x => x.Name), ("Charged on", x => x.Trigger), ("Basis", x => x.Basis), ("Rate", x => Q(x.Rate)), ("Income", x => x.IncomeAccountCode),
                ("Active", x => x.IsActive)),
        };
        yield return new Operation
        {
            Module = module, Title = "Stand deposits and cancellations", ListTitle = "Allocation certificates",
            Description = "Take a deposit against an allocation (held as a liability), apply it when the sale goes ahead or refund it; cancel an instalment agreement at the buyer's "
                + "request (requested by you, approved by another user): what was paid less occupation rental, the admin charge and arrears is refunded and the stand repossessed.",
            Fields = [Pick("Step", "Step", ["Take deposit", "Apply deposit", "Refund deposit", "Cancel agreement"]), Opt("Certificate", "Allocation certificate (deposit)"),
                Opt("Agreement", "Instalment agreement (cancel)"), Opt("Customer", "Customer code (take)"), Num("Amount", "Amount (take) / current market value (cancel)", "0"), Opt("Reference", "Receipt reference / reason"),
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
            List = async c =>
            {
                HousingNames n = await HousingNamesAsync(c).ConfigureAwait(true);
                return Table(await c.Db.HousingStands.AsNoTracking().Where(x => x.CompanyId == c.CompanyId && x.CurrentHolderCustomerId != null).OrderBy(x => x.StandNumber).Page(c, 500)
                        .ToListAsync().ConfigureAwait(true),
                    ("Stand", x => x.StandNumber), ("Status", x => x.Status), ("Holder", x => n.Customer(x.CurrentHolderCustomerId)), ("Price", x => M(x.SellingPrice)));
            },
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
        yield return new Operation
        {
            Module = module, Title = "Development covenants", ListTitle = "Covenants",
            Description = "A covenant that the holder develops a stand within so many years: record its milestones (plan submitted / approved, construction started, completed), grant "
                + "an extension (with any fee), run the breach check (a penalty levy on those past their deadline) and issue a notice.",
            Fields = [Pick("Step", "Step", ["Create", "Milestone", "Extension", "Run breaches", "Notice"]), Opt("Covenant", "Covenant number"), Opt("Stand", "Stand number (create)"),
                Opt("Holder", "Holder customer code (create)"), Num("Years", "Years to develop (create)", "2"), Pick("Milestone", "Milestone", Enum.GetNames<CovenantMilestone>()),
                Opt("Reference", "Reference"), Num("Months", "Extension months", "6"), Num("Amount", "Extension fee / penalty levy", "0"), Opt("Account", "Fee / levy income account"), Today()],
            Run = async (c, i) =>
            {
                IDevelopmentCovenantService covenants = c.Get<IDevelopmentCovenantService>();
                string covenant = i.Optional("Covenant") ?? string.Empty;
                switch (i.Text("Step"))
                {
                    case "Create":
                        long stand = await FindAsync(c.Db.HousingStands, x => x.CompanyId == c.CompanyId && x.StandNumber == (i.Optional("Stand") ?? string.Empty).ToUpper(), x => x.Id, "stand",
                            i.Optional("Stand") ?? string.Empty).ConfigureAwait(true);
                        return $"Covenant {Check(await covenants.CreateAsync(c.CompanyId, stand, await CustomerAsync(c, i.Optional("Holder") ?? string.Empty).ConfigureAwait(true), i.Date("Date"), i.Whole("Years"))
                            .ConfigureAwait(true))} created.";
                    case "Milestone":
                        return Check(await covenants.RecordMilestoneAsync(c.CompanyId, covenant, Enum.Parse<CovenantMilestone>(i.Text("Milestone")), i.Date("Date"), i.Optional("Reference")).ConfigureAwait(true),
                            "Milestone recorded.");
                    case "Extension":
                        return $"Extended to {D(Check(await covenants.GrantExtensionAsync(c.CompanyId, covenant, i.Whole("Months"), i.Date("Date"), i.Number("Amount"), i.Optional("Account")).ConfigureAwait(true)))}.";
                    case "Run breaches":
                        IReadOnlyList<string> breached = Check(await covenants.RunBreachesAsync(c.CompanyId, i.Date("Date"), i.Number("Amount"), i.Optional("Account") ?? string.Empty).ConfigureAwait(true));
                        return breached.Count == 0 ? "No covenants in breach." : $"In breach: {string.Join(", ", breached)}.";
                    default:
                        return Check(await covenants.IssueNoticeAsync(c.CompanyId, covenant, i.Date("Date")).ConfigureAwait(true), "Notice issued.");
                }
            },
            List = async c => Table(await c.Get<IDevelopmentCovenantService>().RegisterAsync(c.CompanyId).ConfigureAwait(true),
                ("Covenant", x => x.CovenantNumber), ("Stand", x => x.StandNumber), ("Deadline", x => D(x.Deadline)), ("Latest milestone", x => x.Latest), ("Extensions", x => x.Extensions), ("Status", x => x.Status)),
        };
        yield return new Operation
        {
            Module = module, Title = "Waiting-list control", ListTitle = "Applicants",
            Description = "Request a change to an applicant's registration date (with the reason) and have another user decide it; confirm an applicant's continued interest; run "
                + "renewals (suspending, then removing, applicants who have not renewed within the cycle and grace); see the notices sent to an applicant.",
            Fields = [Pick("Step", "Step", ["Request date change", "Decide date change", "Confirm interest", "Run renewals", "Notices"]), Opt("Applicant", "Waiting-list number"),
                Opt("Reason", "Reason (date change)"), Lookup("Request", "Date change request (decide)", false, PendingDateChangesAsync), Tick("Approve", "Approve"), Num("Cycle", "Renewal cycle (months)", "12"), Num("Grace", "Grace (days)", "30"), Today()],
            Run = async (c, i) =>
            {
                IWaitingListControlService control = c.Get<IWaitingListControlService>();
                string applicant = i.Optional("Applicant") ?? string.Empty;
                switch (i.Text("Step"))
                {
                    case "Request date change":
                        return Check(await control.RequestDateChangeAsync(c.CompanyId, applicant, i.Date("Date"), i.Optional("Reason") ?? string.Empty, c.UserId).ConfigureAwait(true),
                            id => $"Date change request {id} awaits another user's decision.");
                    case "Decide date change":
                        return Check(await control.DecideDateChangeAsync(c.CompanyId, PickedId(i, "Request"), i.Flag("Approve"), c.UserId).ConfigureAwait(true), i.Flag("Approve") ? "Approved." : "Refused.");
                    case "Confirm interest":
                        return Check(await control.ConfirmAsync(c.CompanyId, applicant, i.Date("Date")).ConfigureAwait(true), "Interest confirmed.");
                    case "Run renewals":
                        RenewalRunResult run = await control.RunRenewalsAsync(c.CompanyId, i.Date("Date"), i.Whole("Cycle"), i.Whole("Grace")).ConfigureAwait(true);
                        return $"{run.Reminded} reminded, {run.Suspended} suspended, {run.Removed} removed; {run.ByLetter} notice(s) to send by letter (no phone number).";
                    default:
                        IReadOnlyList<ApplicantNoticeEntry> notices = await control.NoticesAsync(c.CompanyId, applicant).ConfigureAwait(true);
                        return notices.Count == 0 ? "No notices." : string.Join(Environment.NewLine, notices.Select(n => $"{D(n.SentOn)}  {n.NoticeType}  {n.Channel} {n.Recipient}"));
                }
            },
            List = ApplicantsAsync,
        };
        yield return new Operation
        {
            Module = module, Title = "Joint ownership", ListTitle = "Stands",
            Description = "Registers a stand's joint holders (\"CUST1:50; CUST2:50\", jointly with survivorship or in common), transfers part of a holder's share, records a holder's "
                + "death (survivors take the share under survivorship) and shows the holders on a date.",
            Fields = [Pick("Step", "Step", ["Register", "Transfer share", "Record death", "Holders"]), Req("Stand", "Stand number"), Pick("Regime", "Regime", Enum.GetNames<CoOwnershipRegime>()),
                Opt("Holders", "Holders: CUSTOMER:share %; …"), Opt("From", "From / deceased customer code"), Opt("To", "To customer code"), Num("Portion", "Portion (% of the stand)", "0"), Today()],
            Run = async (c, i) =>
            {
                IJointOwnershipService joint = c.Get<IJointOwnershipService>();
                long stand = await FindAsync(c.Db.HousingStands, x => x.CompanyId == c.CompanyId && x.StandNumber == i.Text("Stand").ToUpper(), x => x.Id, "stand", i.Text("Stand")).ConfigureAwait(true);
                static string Show(IReadOnlyList<StandHolderEntry> holders) => holders.Count == 0 ? "No holders." : string.Join(Environment.NewLine,
                    holders.Select(h => $"{h.Name,-30} {h.SharePercent,7:0.##}%  since {D(h.HeldFrom)}{(h.Deceased ? "  (deceased)" : string.Empty)}"));
                switch (i.Text("Step"))
                {
                    case "Register":
                        List<HolderShare> shares = [];
                        foreach (string part in (i.Optional("Holders") ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                        {
                            string[] bits = part.Split(':', StringSplitOptions.TrimEntries);
                            shares.Add(bits.Length == 2 && decimal.TryParse(bits[1], NumberStyles.Number, CultureInfo.InvariantCulture, out decimal share)
                                ? new HolderShare(await CustomerAsync(c, bits[0]).ConfigureAwait(true), share) : throw new OperationException($"Write each holder as CUSTOMER:share % (\"{part}\")."));
                        }

                        return Check(await joint.RegisterAsync(c.CompanyId, stand, Enum.Parse<CoOwnershipRegime>(i.Text("Regime")), shares, i.Date("Date")).ConfigureAwait(true), "Holders registered.");
                    case "Transfer share":
                    {
                        // A share passes like a stand: lodged, verified, approved by two officers and completed on the ownership transfer screen.
                        string lodged = Check(await c.Get<ITransferApplicationService>().LodgeAsync(new TransferLodgement
                        {
                            CompanyId = c.CompanyId, StandId = stand, TransferType = TransferType.Sale, FromCustomerId = await CustomerAsync(c, i.Optional("From") ?? string.Empty).ConfigureAwait(true),
                            ToCustomerId = await CustomerAsync(c, i.Optional("To") ?? string.Empty).ConfigureAwait(true), SharePercent = i.Number("Portion"), Date = i.Date("Date"),
                        }).ConfigureAwait(true));
                        return $"Share transfer lodged as {lodged}: verify, submit, approve and complete it under the ownership transfer.";
                    }
                    case "Record death":
                        return Show(Check(await joint.RecordDeathAsync(c.CompanyId, stand, await CustomerAsync(c, i.Optional("From") ?? string.Empty).ConfigureAwait(true), i.Date("Date")).ConfigureAwait(true)));
                    default:
                        return Show(await joint.HoldersAsync(c.CompanyId, stand, i.Date("Date")).ConfigureAwait(true));
                }
            },
            List = async c =>
            {
                HousingNames n = await HousingNamesAsync(c).ConfigureAwait(true);
                return Table(await c.Db.HousingStands.AsNoTracking().Where(s => s.CompanyId == c.CompanyId).OrderBy(s => s.StandNumber).Page(c, 1000).ToListAsync().ConfigureAwait(true),
                    ("Stand", s => s.StandNumber), ("Status", s => s.Status), ("Holder", s => n.Customer(s.CurrentHolderCustomerId)));
            },
        };
        yield return new Operation
        {
            Module = module, Title = "Sectional title schemes", ListTitle = "Stands",
            Description = "Registers a sectional title scheme on a stand with its units' floor areas (\"U1:80; U2:120\" - participation quotas follow from the areas), transfers a unit, "
                + "lists the units and their owners, and raises a period's levy shared by participation quota.",
            Fields = [Pick("Step", "Step", ["Register", "Transfer unit", "Units", "Raise levy"]), Req("Scheme", "Scheme code"), Opt("Name", "Name (register)"), Opt("Stand", "Stand number (register)"),
                Opt("Units", "Units: number:floor area; … (register)"), Opt("Unit", "Unit number (transfer)"), Opt("To", "To customer code (transfer)"), Num("Amount", "Sale price / levy", "0"),
                Num("Period", "Levy period (yyyyMM)", "0"), Opt("Income", "Levy income account"), Today()],
            Run = async (c, i) =>
            {
                ISectionalTitleService sectional = c.Get<ISectionalTitleService>();
                string scheme = i.Text("Scheme");
                static string Show(IReadOnlyList<SectionalUnitEntry> units) => string.Join(Environment.NewLine,
                    units.Select(u => $"{u.UnitNumber,-8} {Q(u.FloorArea),10} m²  quota {u.ParticipationQuota:0.####}  {u.Owner ?? "(scheme)"}"));
                switch (i.Text("Step"))
                {
                    case "Register":
                        long stand = await FindAsync(c.Db.HousingStands, x => x.CompanyId == c.CompanyId && x.StandNumber == (i.Optional("Stand") ?? string.Empty).ToUpper(), x => x.Id, "stand",
                            i.Optional("Stand") ?? string.Empty).ConfigureAwait(true);
                        List<UnitArea> areas = [];
                        foreach (string part in (i.Optional("Units") ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                        {
                            string[] bits = part.Split(':', StringSplitOptions.TrimEntries);
                            areas.Add(bits.Length == 2 && decimal.TryParse(bits[1], NumberStyles.Number, CultureInfo.InvariantCulture, out decimal area)
                                ? new UnitArea(bits[0], area) : throw new OperationException($"Write each unit as number:floor area (\"{part}\")."));
                        }

                        return Show(Check(await sectional.RegisterAsync(c.CompanyId, scheme, i.Optional("Name") ?? scheme, stand, areas).ConfigureAwait(true)));
                    case "Transfer unit":
                    {
                        // A unit passes through the transfer pipeline too: lodged here, completed under the ownership transfer once approved.
                        string lodged = Check(await c.Get<ITransferApplicationService>().LodgeAsync(new TransferLodgement
                        {
                            CompanyId = c.CompanyId, TransferType = TransferType.Sale, SectionalSchemeCode = scheme, UnitNumber = i.Optional("Unit") ?? string.Empty,
                            ToCustomerId = await CustomerAsync(c, i.Optional("To") ?? string.Empty).ConfigureAwait(true), Consideration = i.Number("Amount"), Date = i.Date("Date"),
                        }).ConfigureAwait(true));
                        return $"Unit transfer lodged as {lodged}: verify, submit, approve and complete it under the ownership transfer.";
                    }
                    case "Units":
                        return Show(Check(await sectional.UnitsAsync(c.CompanyId, scheme).ConfigureAwait(true)));
                    default:
                        SectionalLevyResult levy = Check(await sectional.RaiseLevyAsync(c.CompanyId, scheme, i.Whole("Period"), i.Number("Amount"), i.Optional("Income") ?? string.Empty, i.Date("Date")).ConfigureAwait(true));
                        return string.Join(Environment.NewLine, levy.Billed.Select(b => $"{b.UnitNumber,-8} {M(b.Amount)}")) + Environment.NewLine + $"Not billed (no owner): {M(levy.Unbilled)}.";
                }
            },
            List = async c =>
            {
                HousingNames n = await HousingNamesAsync(c).ConfigureAwait(true);
                return Table(await c.Db.HousingStands.AsNoTracking().Where(s => s.CompanyId == c.CompanyId).OrderBy(s => s.StandNumber).Page(c, 1000).ToListAsync().ConfigureAwait(true),
                    ("Stand", s => s.StandNumber), ("Status", s => s.Status), ("Holder", s => n.Customer(s.CurrentHolderCustomerId)));
            },
        };
        yield return new Operation
        {
            Module = module, Title = "Developer agreements", ListTitle = "Developer agreements",
            Description = "A developer services a scheme for a share of its stands: create the agreement (council and developer quotas, delivery deadline, penalty per stand a month "
                + "late), add and complete milestones, assign stands to a quota (\"S1,S2\"), hand stands over, reconcile delivery against the quotas, and charge accrued penalties.",
            Fields = [Pick("Step", "Step", ["Create", "Add milestone", "Complete milestone", "Assign stands", "Hand over", "Reconcile", "Charge penalties"]), Opt("Agreement", "Agreement number"),
                Opt("Scheme", "Scheme code (create)"), Opt("Developer", "Developer customer code (create)"), Num("CouncilQuota", "Council stands", "0"), Num("DeveloperQuota", "Developer stands", "0"),
                Today("Deadline", "Delivery deadline / milestone due"), Num("Penalty", "Penalty per stand per month late", "0"), Opt("Milestone", "Milestone description"),
                Pick("Quota", "Quota (assign)", Enum.GetNames<StandQuota>()), Opt("Stands", "Stand number(s), comma-separated"), Opt("Income", "Penalty income account"), Today()],
            Run = async (c, i) =>
            {
                IDeveloperAgreementService developers = c.Get<IDeveloperAgreementService>();
                string agreement = i.Optional("Agreement") ?? string.Empty;
                DateOnly date = i.Date("Date");
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
                    case "Create":
                        long scheme = await FindAsync(c.Db.HousingSchemes, x => x.CompanyId == c.CompanyId && x.Code == (i.Optional("Scheme") ?? string.Empty).ToUpper(), x => x.Id, "scheme",
                            i.Optional("Scheme") ?? string.Empty).ConfigureAwait(true);
                        return $"Agreement {Check(await developers.CreateAsync(new DeveloperAgreementTerms
                        {
                            CompanyId = c.CompanyId, SchemeId = scheme, DeveloperCustomerId = await CustomerAsync(c, i.Optional("Developer") ?? string.Empty).ConfigureAwait(true),
                            CouncilQuota = i.Whole("CouncilQuota"), DeveloperQuota = i.Whole("DeveloperQuota"), SignedOn = date, DeliveryDeadline = i.Date("Deadline"),
                            PenaltyPerStandPerMonth = i.Number("Penalty"),
                        }).ConfigureAwait(true))} created.";
                    case "Add milestone":
                        return Check(await developers.AddMilestoneAsync(c.CompanyId, agreement, i.Optional("Milestone") ?? string.Empty, i.Date("Deadline")).ConfigureAwait(true), "Milestone added.");
                    case "Complete milestone":
                        return Check(await developers.CompleteMilestoneAsync(c.CompanyId, agreement, i.Optional("Milestone") ?? string.Empty, date).ConfigureAwait(true), "Milestone completed.");
                    case "Assign stands":
                        return $"{Check(await developers.AssignStandsAsync(c.CompanyId, agreement, Enum.Parse<StandQuota>(i.Text("Quota")), await StandIds().ConfigureAwait(true), date).ConfigureAwait(true))} stand(s) assigned.";
                    case "Hand over":
                        return Check(await developers.HandOverAsync(c.CompanyId, agreement, (await StandIds().ConfigureAwait(true)).FirstOrDefault(), date).ConfigureAwait(true), "Handed over.");
                    case "Reconcile":
                        DeveloperReconciliation rec = Check(await developers.ReconcileAsync(c.CompanyId, agreement, date).ConfigureAwait(true));
                        return string.Join(Environment.NewLine, rec.Quotas.Select(q => $"{q.Quota,-10} contracted {q.Contracted}, assigned {q.Assigned}, handed over {q.HandedOver}, outstanding {q.Outstanding}"))
                            + Environment.NewLine + $"Overdue milestones: {(rec.OverdueMilestones.Count == 0 ? "none" : string.Join(", ", rec.OverdueMilestones))}. "
                            + $"Penalty accrued {M(rec.PenaltyAccrued)}, charged {M(rec.PenaltiesCharged)}.";
                    default:
                        return $"{M(Check(await developers.ChargePenaltiesAsync(c.CompanyId, agreement, date, i.Optional("Income") ?? string.Empty).ConfigureAwait(true)))} penalties charged.";
                }
            },
            List = async c =>
            {
                HousingNames n = await HousingNamesAsync(c).ConfigureAwait(true);
                return Table(await c.Db.Set<DeveloperAgreement>().AsNoTracking().Where(x => x.CompanyId == c.CompanyId).OrderByDescending(x => x.Id).Page(c, 200).ToListAsync().ConfigureAwait(true),
                    ("Agreement", x => x.AgreementNumber), ("Scheme", x => n.Scheme(x.SchemeId)), ("Developer", x => n.Customer(x.DeveloperCustomerId)), ("Council stands", x => x.CouncilQuota),
                    ("Developer stands", x => x.DeveloperQuota), ("Deadline", x => D(x.DeliveryDeadline)));
            },
        };
        yield return new Operation
        {
            Module = module, Title = "Housing expected credit loss",
            Description = "Assesses the IFRS 9 expected credit loss on housing debtors (instalment buyers and tenants) by arrears band with the default matrices, showing the allowance "
                + "needed against what is booked; Post books the difference.",
            Fields = [Pick("Step", "Step", ["Assess", "Post"]), Today("Date", "As at"), Opt("Allowance", "Loss allowance account"), Opt("Expense", "Impairment expense account")],
            Run = async (c, i) =>
            {
                IHousingEclService ecl = c.Get<IHousingEclService>();
                HousingEclRequest request = new()
                {
                    CompanyId = c.CompanyId, AsAtDate = i.Date("Date"), AllowanceAccountCode = i.Optional("Allowance") ?? string.Empty, ExpenseAccountCode = i.Optional("Expense") ?? string.Empty,
                };
                HousingEclReport report = Check(await (i.Text("Step") == "Post" ? ecl.PostAsync(request) : ecl.AssessAsync(request)).ConfigureAwait(true));
                return string.Join(Environment.NewLine, report.Portfolios.SelectMany(p => p.Bands.Select(b => $"{p.Portfolio,-12} {b.Band,-12} exposure {M(b.Exposure),14}  provision {M(b.Provision),12}")))
                    + Environment.NewLine + $"Needed {M(report.TargetAllowance)}, booked {M(report.BookedAllowance)}, adjustment {M(report.Adjustment)}{(report.JournalNumber is null ? string.Empty : $" (posted {report.JournalNumber})")}.";
            },
        };
        yield return new Operation
        {
            Module = module, Title = "Informal settlements", ListTitle = "Settlements",
            Description = "Registers a settlement (with its enumeration cut-off date), approves its upgrading plan, runs an enumeration of its households (eligibility checked on each), "
                + "closes it, then turns an eligible household into a waiting-list registration or regularises it on a stand; Summary shows where the settlement stands.",
            Fields = [Pick("Step", "Step", ["Register", "Approve plan", "Start enumeration", "Enumerate household", "Close enumeration", "To waiting list", "Regularise", "Summary"]),
                Opt("Settlement", "Settlement code"), Opt("Name", "Name / plan / enumeration reference"), Opt("Ward", "Ward (register)"), Today("CutOff", "Cut-off date (register)"),
                Opt("Household", "Household number"), Opt("IdNumber", "Head's national ID"), Opt("Head", "Head's name"), OptDate("Born", "Head's date of birth"), Num("Size", "Household size", "1"),
                Num("Income", "Monthly income", "0"), OptDate("Since", "Occupied since"), Pick("Structure", "Structure", Enum.GetNames<StructureType>()), Opt("StructureRef", "Structure reference"),
                Pick("Category", "Waiting-list category", Enum.GetNames<HousingCategory>()), Opt("Stand", "Stand number (regularise)"), Opt("Approver", "Approving user name (regularise)"),
                Today()],
            Run = async (c, i) =>
            {
                IInformalSettlementService settlements = c.Get<IInformalSettlementService>();
                string code = i.Optional("Settlement") ?? string.Empty;
                DateOnly date = i.Date("Date");
                switch (i.Text("Step"))
                {
                    case "Register":
                        return Check(await settlements.RegisterAsync(new SettlementRegistration
                        {
                            CompanyId = c.CompanyId, Code = code, Name = i.Optional("Name") ?? code, Ward = i.Optional("Ward") ?? string.Empty, CutOffDate = i.Date("CutOff"),
                        }).ConfigureAwait(true), _ => "Settlement registered.");
                    case "Approve plan":
                        return Check(await settlements.ApprovePlanAsync(c.CompanyId, code, i.Optional("Name") ?? string.Empty, date).ConfigureAwait(true), "Upgrading plan approved.");
                    case "Start enumeration":
                        return Check(await settlements.StartEnumerationAsync(c.CompanyId, code, i.Optional("Name") ?? string.Empty, date).ConfigureAwait(true), _ => "Enumeration started.");
                    case "Enumerate household":
                        EnumerationResult household = Check(await settlements.EnumerateAsync(new HouseholdSurvey
                        {
                            CompanyId = c.CompanyId, SettlementCode = code, HeadIdNumber = i.Optional("IdNumber") ?? string.Empty, HeadName = i.Optional("Head") ?? string.Empty,
                            HeadDateOfBirth = Day(i.Optional("Born") ?? string.Empty), HouseholdSize = i.Whole("Size"), MonthlyIncome = i.Number("Income"), OccupiedSince = Day(i.Optional("Since") ?? string.Empty),
                            StructureType = Enum.Parse<StructureType>(i.Text("Structure")), StructureReference = i.Optional("StructureRef") ?? string.Empty, Date = date,
                        }).ConfigureAwait(true));
                        return household.IsEligible ? $"Household {household.HouseholdNumber} enumerated (eligible)." : $"Household {household.HouseholdNumber} enumerated - not eligible: {household.IneligibleReason}.";
                    case "Close enumeration":
                        return $"Enumeration closed with {Check(await settlements.CloseEnumerationAsync(c.CompanyId, code, date).ConfigureAwait(true))} household(s).";
                    case "To waiting list":
                        return $"Registered on the waiting list as {Check(await settlements.ConvertToWaitingListAsync(c.CompanyId, i.Optional("Household") ?? string.Empty, Enum.Parse<HousingCategory>(i.Text("Category")), date).ConfigureAwait(true))}.";
                    case "Regularise":
                        long stand = await FindAsync(c.Db.HousingStands, x => x.CompanyId == c.CompanyId && x.StandNumber == (i.Optional("Stand") ?? string.Empty).ToUpper(), x => x.Id, "stand",
                            i.Optional("Stand") ?? string.Empty).ConfigureAwait(true);
                        return Check(await settlements.RegulariseAsync(c.CompanyId, i.Optional("Household") ?? string.Empty, stand, date, c.UserId,
                            await UserAsync(c, i.Optional("Approver") ?? throw new OperationException("Give the approving user.")).ConfigureAwait(true)).ConfigureAwait(true), "Regularised on the stand.");
                    default:
                        SettlementSummary summary = Check(await settlements.SummaryAsync(c.CompanyId, code).ConfigureAwait(true));
                        return $"{summary.Code} {summary.Name}: plan {(summary.PlanApprovedOn is DateOnly approved ? $"approved {D(approved)}" : "not approved")}; {summary.Enumerated} enumerated, "
                            + $"{summary.Eligible} eligible, {summary.WaitingListed} waiting-listed, {summary.Regularised} regularised, {summary.Outstanding} outstanding.";
                }
            },
            List = async c => Table(await c.Db.Set<InformalSettlement>().AsNoTracking().Where(x => x.CompanyId == c.CompanyId).OrderBy(x => x.Code).ToListAsync().ConfigureAwait(true),
                ("Code", x => x.Code), ("Name", x => x.Name), ("Ward", x => x.Ward)),
        };
        yield return new Operation
        {
            Module = module, Title = "Stand leases", ListTitle = "Stand leases",
            Description = "Leases a stand (commercial, industrial, agricultural…) for a term at an annual rent reviewed and escalated every so many years; Schedule shows the rent "
                + "for each year; the monthly run bills the leases and recognises the income straight-line.",
            Fields = [Pick("Step", "Step", ["Create", "Schedule", "Run month"]), Opt("Lease", "Lease number (schedule)"), Opt("Stand", "Stand number (create)"), Opt("Lessee", "Lessee customer code"),
                Pick("Use", "Use", Enum.GetNames<LeaseUse>()), Num("Years", "Term (years)", "10"), Num("Rent", "Annual rent", "0"), Num("Review", "Review every (years)", "1"), Num("Escalation", "Escalation %", "0"),
                Opt("Income", "Lease income account"), Opt("Accrued", "Accrued income account"), Today()],
            Run = async (c, i) =>
            {
                IStandLeaseService leases = c.Get<IStandLeaseService>();
                switch (i.Text("Step"))
                {
                    case "Create":
                        long stand = await FindAsync(c.Db.HousingStands, x => x.CompanyId == c.CompanyId && x.StandNumber == (i.Optional("Stand") ?? string.Empty).ToUpper(), x => x.Id, "stand",
                            i.Optional("Stand") ?? string.Empty).ConfigureAwait(true);
                        return $"Lease {Check(await leases.CreateAsync(new StandLeaseRequest
                        {
                            CompanyId = c.CompanyId, StandId = stand, LesseeCustomerId = await CustomerAsync(c, i.Optional("Lessee") ?? string.Empty).ConfigureAwait(true), Use = Enum.Parse<LeaseUse>(i.Text("Use")),
                            StartDate = i.Date("Date"), TermYears = i.Whole("Years"), AnnualRent = i.Number("Rent"), ReviewEveryYears = i.Whole("Review"), EscalationPercent = i.Number("Escalation"),
                            IncomeAccountCode = i.Optional("Income") ?? string.Empty, AccruedIncomeAccountCode = i.Optional("Accrued") ?? string.Empty,
                        }).ConfigureAwait(true))} created.";
                    case "Schedule":
                        IReadOnlyList<LeaseYear> years = Check(await leases.ScheduleAsync(c.CompanyId, i.Optional("Lease") ?? string.Empty).ConfigureAwait(true));
                        return string.Join(Environment.NewLine, years.Select(y => $"Year {y.Year,3}: {M(y.AnnualRent)}"));
                    default:
                        LeaseRunResult run = Check(await leases.RunMonthAsync(c.CompanyId, i.Date("Date")).ConfigureAwait(true));
                        return $"{run.LeasesBilled} lease(s) billed {M(run.RentBilled)}; income recognised {M(run.IncomeRecognised)}.";
                }
            },
            List = async c =>
            {
                HousingNames n = await HousingNamesAsync(c).ConfigureAwait(true);
                return Table(await c.Db.Set<StandLease>().AsNoTracking().Where(x => x.CompanyId == c.CompanyId).OrderByDescending(x => x.Id).Page(c, 300).ToListAsync().ConfigureAwait(true),
                    ("Lease", x => x.LeaseNumber), ("Stand", x => n.Stand(x.StandId)), ("Lessee", x => n.Customer(x.LesseeCustomerId)), ("Use", x => x.Use), ("From", x => D(x.StartDate)),
                    ("Years", x => x.TermYears), ("Rent", x => M(x.AnnualRent)));
            },
        };
        yield return new Operation
        {
            Module = module, Title = "Scheme budget and costs", ListTitle = "Schemes",
            Description = "Sets a housing scheme's budget for each cost element (land, servicing, professional fees, overheads) and records actual costs against them, capitalised "
                + "into the scheme's stands.",
            Fields = [Pick("Step", "Step", ["Set budget", "Record cost"]), Req("Scheme", "Scheme code"), Pick("Element", "Cost element", Enum.GetNames<SchemeCostElement>()), Num("Amount", "Amount"),
                Opt("Reference", "Reference (cost)"), Opt("Description", "Description (cost)"), Opt("Stands", "Stands (WIP) account"), Opt("Funding", "Paid from / funding account"), Today()],
            Run = async (c, i) =>
            {
                ISchemeBudgetService budgets = c.Get<ISchemeBudgetService>();
                SchemeCostElement element = Enum.Parse<SchemeCostElement>(i.Text("Element"));
                return i.Text("Step") == "Set budget"
                    ? Check(await budgets.SetBudgetAsync(c.CompanyId, i.Text("Scheme"), element, i.Number("Amount")).ConfigureAwait(true), "Budget saved.")
                    : $"Cost recorded ({Check(await budgets.RecordCostAsync(new SchemeCostRequest
                    {
                        CompanyId = c.CompanyId, SchemeCode = i.Text("Scheme"), Element = element, Date = i.Date("Date"), Amount = i.Number("Amount"), Reference = i.Optional("Reference") ?? string.Empty,
                        Description = i.Optional("Description") ?? string.Empty, StandsAccountCode = i.Optional("Stands") ?? string.Empty, FundingAccountCode = i.Optional("Funding") ?? string.Empty,
                    }).ConfigureAwait(true))}).";
            },
            List = SchemesAsync,
        };
        yield return new Operation
        {
            Module = module, Title = "Scheme progress", ListTitle = "Schemes",
            Description = "Records survey progress on stands (pegged, diagram lodged, diagram approved - several stands comma-separated), a bulk-service servicing certificate (water, "
                + "sewer, roads, electricity, storm water: % complete and stands reached), and a land parcel's valuation.",
            Fields = [Pick("Step", "Step", ["Survey", "Servicing certificate", "Valuation"]), Opt("Stands", "Stand numbers, comma-separated (survey)"), Pick("Stage", "Survey stage", Enum.GetNames<SurveyStage>()),
                Opt("Scheme", "Scheme code (servicing)"), Pick("Service", "Bulk service", Enum.GetNames<BulkService>()), Num("Percent", "% complete", "0"), Num("Reached", "Stands reached", "0"),
                Opt("Parcel", "Land parcel number (valuation)"), Num("Value", "Value", "0"), Opt("Valuer", "Valuer"), Opt("Reference", "Reference"), Today()],
            Run = async (c, i) =>
            {
                ISchemeProgressService progress = c.Get<ISchemeProgressService>();
                switch (i.Text("Step"))
                {
                    case "Survey":
                        List<string> stands = [.. (i.Optional("Stands") ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
                        return $"{Check(await progress.RecordSurveyAsync(c.CompanyId, stands, Enum.Parse<SurveyStage>(i.Text("Stage")), i.Date("Date"), i.Optional("Reference") ?? string.Empty).ConfigureAwait(true))} stand(s) updated.";
                    case "Servicing certificate":
                        return Check(await progress.RecordServicingAsync(new ServicingCertificate
                        {
                            CompanyId = c.CompanyId, SchemeCode = i.Optional("Scheme") ?? string.Empty, Service = Enum.Parse<BulkService>(i.Text("Service")), Date = i.Date("Date"), PercentComplete = i.Number("Percent"),
                            StandsReached = i.Whole("Reached"), Reference = i.Optional("Reference") ?? string.Empty,
                        }).ConfigureAwait(true), _ => "Certificate recorded.");
                    default:
                        return Check(await progress.RecordValuationAsync(c.CompanyId, i.Optional("Parcel") ?? string.Empty, i.Date("Date"), i.Number("Value"), i.Optional("Valuer") ?? string.Empty).ConfigureAwait(true),
                            _ => "Valuation recorded.");
                }
            },
            List = SchemesAsync,
        };
        yield return new Operation
        {
            Module = module, Title = "Servicing cost apportionment", ListTitle = "Schemes",
            Description = "Links a scheme to the servicing contract whose cost it carries and how it is shared (equally, by area, by frontage, by zoning weight); sets a stand's frontage "
                + "and zoning weight; apportions the contract's cost to date to the stands; Cost per stand shows each stand's land and servicing cost.",
            Fields = [Pick("Step", "Step", ["Link contract", "Stand measures", "Apportion", "Cost per stand"]), Opt("Scheme", "Scheme code"), Opt("Contract", "Job contract number (link)"),
                Pick("Basis", "Share by", Enum.GetNames<ServicingBasis>()), Opt("Stand", "Stand number (measures)"), OptNum("Frontage", "Frontage (m)"), OptNum("Weight", "Zoning weight"),
                Opt("Wip", "Servicing WIP account (apportion)"), Opt("Available", "Stands available account (apportion)"), Today()],
            Run = async (c, i) =>
            {
                IServicingCostService servicing = c.Get<IServicingCostService>();
                async Task<long> SchemeId() => await FindAsync(c.Db.HousingSchemes, x => x.CompanyId == c.CompanyId && x.Code == (i.Optional("Scheme") ?? string.Empty).ToUpper(), x => x.Id, "scheme",
                    i.Optional("Scheme") ?? string.Empty).ConfigureAwait(true);
                switch (i.Text("Step"))
                {
                    case "Link contract":
                        return Check(await servicing.LinkAsync(c.CompanyId, await SchemeId().ConfigureAwait(true), await ContractAsync(c, i.Optional("Contract") ?? string.Empty).ConfigureAwait(true),
                            Enum.Parse<ServicingBasis>(i.Text("Basis"))).ConfigureAwait(true), "Contract linked.");
                    case "Stand measures":
                        long stand = await FindAsync(c.Db.HousingStands, x => x.CompanyId == c.CompanyId && x.StandNumber == (i.Optional("Stand") ?? string.Empty).ToUpper(), x => x.Id, "stand",
                            i.Optional("Stand") ?? string.Empty).ConfigureAwait(true);
                        return Check(await servicing.SetMeasuresAsync(c.CompanyId, stand, i.OptionalNumber("Frontage"), i.OptionalNumber("Weight")).ConfigureAwait(true), "Measures saved.");
                    case "Apportion":
                        IReadOnlyList<ServicingRunLine> lines = Check(await servicing.ApportionAsync(c.CompanyId, await SchemeId().ConfigureAwait(true), i.Date("Date"), i.Optional("Wip") ?? string.Empty,
                            i.Optional("Available") ?? string.Empty).ConfigureAwait(true));
                        return lines.Count == 0 ? "Nothing new to apportion." : string.Join(Environment.NewLine, lines.Select(l => $"{l.ContractNumber}: {M(l.Apportioned)} {l.JournalNumber}"));
                    default:
                        IReadOnlyList<StandCostEntry> costs = await servicing.CostPerStandAsync(c.CompanyId, await SchemeId().ConfigureAwait(true)).ConfigureAwait(true);
                        return string.Join(Environment.NewLine, costs.Select(x => $"{x.StandNumber,-12} cost {M(x.Cost),12}  of which servicing {M(x.Servicing),12}"));
                }
            },
            List = SchemesAsync,
        };
        yield return new Operation
        {
            Module = module, Title = "Link stands to municipal accounts", ListTitle = "Schemes",
            Description = "Sets when a housing stand becomes a municipal property (on allocation accepted, sale or title registered) for a ward, and which service tariffs it starts on "
                + "(tariff codes, comma-separated); the link then happens as those events occur.",
            Fields = [Pick("Trigger", "When", Enum.GetNames<MunicipalLinkTrigger>()), Req("Ward", "Ward"), Opt("Tariffs", "Tariff codes, comma-separated")],
            Run = async (c, i) =>
            {
                List<long> tariffs = [];
                foreach (string code in (i.Optional("Tariffs") ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    tariffs.Add(await FindAsync(c.Db.Tariffs, t => t.CompanyId == c.CompanyId && t.Code == code.ToUpper(), t => t.Id, "tariff", code).ConfigureAwait(true));
                }

                return Check(await c.Get<IHousingMunicipalLinkService>().ConfigureAsync(c.CompanyId, Enum.Parse<MunicipalLinkTrigger>(i.Text("Trigger")), i.Text("Ward"), tariffs).ConfigureAwait(true),
                    "Municipal link set.");
            },
            List = SchemesAsync,
        };
        yield return new Operation
        {
            Module = module, Title = "Land register", ListTitle = "Land disputes",
            Description = "On a land parcel: register an encumbrance (servitude, caveat, mortgage, lease, restrictive condition, wayleave) and lift it; open a land dispute and move it "
                + "reported → investigation → negotiation → mediation → litigation → judgment → resolved; dispose of land by sale, exchange (receiving a parcel in return), donation or "
                + "expropriation, with the cost out and the gain or loss.",
            Fields = [Pick("Step", "Step", ["Encumbrance", "Lift encumbrance", "Open dispute", "Advance dispute", "Dispose"]), Opt("Parcel", "Parcel number"), Pick("EncType", "Encumbrance type", Enum.GetNames<EncumbranceType>()),
                Lookup("Encumbrance", "Encumbrance (lift)", false, EncumbrancesInForceAsync), Opt("Reference", "Reference / resolution"), Opt("Party", "In favour of / claimant / counterparty"), Opt("Text", "Description / particulars / note"),
                Pick("DisputeType", "Dispute type", Enum.GetNames<LandDisputeType>()), Opt("Dispute", "Dispute number (advance)"), Pick("Stage", "Dispute stage", Enum.GetNames<LandDisputeStage>()),
                Opt("Stand", "Stand number (dispute, optional)"), Pick("DisposalType", "Disposal", Enum.GetNames<LandDisposalType>()), Num("Area", "Area m² (disposal)", "0"), Num("Consideration", "Sale price", "0"),
                Opt("LandAccount", "Land account"), Opt("Proceeds", "Proceeds account"), Opt("GainLoss", "Gain / loss account"), Opt("Exchange", "Parcel received in exchange: number"),
                Num("ExchangeArea", "Parcel received: area m²", "0"), Today()],
            Run = async (c, i) =>
            {
                ILandRegisterService register = c.Get<ILandRegisterService>();
                string parcel = i.Optional("Parcel") ?? string.Empty;
                DateOnly date = i.Date("Date");
                switch (i.Text("Step"))
                {
                    case "Encumbrance":
                        return Check(await register.RegisterEncumbranceAsync(new EncumbranceRegistration
                        {
                            CompanyId = c.CompanyId, ParcelNumber = parcel, Type = Enum.Parse<EncumbranceType>(i.Text("EncType")), Reference = i.Optional("Reference") ?? string.Empty,
                            InFavourOf = i.Optional("Party") ?? string.Empty, Description = i.Optional("Text") ?? string.Empty, Date = date,
                        }).ConfigureAwait(true), id => $"Encumbrance {id} registered.");
                    case "Lift encumbrance":
                        return Check(await register.LiftEncumbranceAsync(c.CompanyId, PickedId(i, "Encumbrance"), date, i.Optional("Reference") ?? string.Empty).ConfigureAwait(true), "Encumbrance lifted.");
                    case "Open dispute":
                        return $"Dispute {Check(await register.OpenDisputeAsync(new LandDisputeLodgement
                        {
                            CompanyId = c.CompanyId, ParcelNumber = parcel, StandNumber = i.Optional("Stand"), Type = Enum.Parse<LandDisputeType>(i.Text("DisputeType")), Claimant = i.Optional("Party") ?? string.Empty,
                            Particulars = i.Optional("Text") ?? string.Empty, Date = date,
                        }).ConfigureAwait(true))} opened.";
                    case "Advance dispute":
                        return Check(await register.AdvanceDisputeAsync(c.CompanyId, i.Optional("Dispute") ?? string.Empty, Enum.Parse<LandDisputeStage>(i.Text("Stage")), date, i.Optional("Text") ?? string.Empty)
                            .ConfigureAwait(true), $"Moved to {i.Text("Stage")}.");
                    default:
                        LandDisposalResult disposed = Check(await register.DisposeAsync(new LandDisposalRequest
                        {
                            CompanyId = c.CompanyId, ParcelNumber = parcel, Type = Enum.Parse<LandDisposalType>(i.Text("DisposalType")), Counterparty = i.Optional("Party") ?? string.Empty,
                            ResolutionReference = i.Optional("Reference") ?? string.Empty, Date = date, AreaSquareMetres = i.Number("Area"), Consideration = i.Number("Consideration"),
                            LandAccountCode = i.Optional("LandAccount") ?? string.Empty, ProceedsAccountCode = i.Optional("Proceeds") ?? string.Empty, GainLossAccountCode = i.Optional("GainLoss") ?? string.Empty,
                            ExchangeParcelNumber = i.Optional("Exchange"), ExchangeParcelDescription = i.Optional("Exchange") is string received ? $"Received in exchange for {parcel}" : null,
                            ExchangeParcelArea = i.Number("ExchangeArea"),
                        }).ConfigureAwait(true));
                        return $"Disposal {disposed.DisposalNumber} ({disposed.JournalNumber}): cost out {M(disposed.CostDerecognised)}, {(disposed.GainOrLoss >= 0m ? "gain" : "loss")} {M(Math.Abs(disposed.GainOrLoss))}.";
                }
            },
            List = async c => Table(await c.Db.Set<LandDispute>().AsNoTracking().Where(x => x.CompanyId == c.CompanyId).OrderByDescending(x => x.Id).Page(c, 300).ToListAsync().ConfigureAwait(true),
                ("Dispute", x => x.DisputeNumber), ("Parcel", x => x.LandParcelId), ("Type", x => x.Type), ("Claimant", x => x.Claimant), ("Opened", x => D(x.OpenedOn)), ("Stage", x => x.Stage),
                ("Since", x => D(x.StageSince)), ("Outcome", x => x.Outcome)),
        };
        yield return new Operation
        {
            Module = module, Title = "Layout plans", ListTitle = "Layout plans",
            Description = "Submits a layout plan for a land parcel with its stands by use (\"Residential:120; Commercial:6; Roads:4\"), approves it with the authority, amends it as a new "
                + "version with the reason, shows every version with what changed, and reconciles the plan in force against the stands on the register.",
            Fields = [Pick("Step", "Step", ["Submit", "Approve", "Amend", "History", "Reconcile"]), Req("Plan", "Plan number"), Opt("Parcel", "Land parcel number (submit / amend)"),
                Opt("Planner", "Planner"), Pick("Density", "Density", Enum.GetNames<PlanDensity>()), Opt("Uses", "Stands by use: use:count; …"), Opt("Text", "Approval authority / amendment reason"), Today()],
            Run = async (c, i) =>
            {
                ILayoutPlanService plans = c.Get<ILayoutPlanService>();
                async Task<LayoutSubmission> Submission()
                {
                    Dictionary<StandUse, int> uses = [];
                    foreach (string part in (i.Optional("Uses") ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    {
                        string[] bits = part.Split(':', StringSplitOptions.TrimEntries);
                        if (bits.Length != 2 || !Enum.TryParse(bits[0], ignoreCase: true, out StandUse use) || !int.TryParse(bits[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int count))
                        {
                            throw new OperationException($"Write each use as use:count (\"{part}\"); uses are {string.Join(", ", Enum.GetNames<StandUse>())}.");
                        }

                        uses[use] = count;
                    }

                    long parcel = await FindAsync(c.Db.LandParcels, x => x.CompanyId == c.CompanyId && x.ParcelNumber == (i.Optional("Parcel") ?? string.Empty).ToUpper(), x => x.Id, "land parcel",
                        i.Optional("Parcel") ?? string.Empty).ConfigureAwait(true);
                    return new LayoutSubmission
                    {
                        CompanyId = c.CompanyId, PlanNumber = i.Text("Plan"), LandParcelId = parcel, Planner = i.Optional("Planner") ?? string.Empty, Density = Enum.Parse<PlanDensity>(i.Text("Density")),
                        Uses = uses, Date = i.Date("Date"), AmendmentReason = i.Optional("Text"),
                    };
                }

                switch (i.Text("Step"))
                {
                    case "Submit":
                        return Check(await plans.SubmitAsync(await Submission().ConfigureAwait(true)).ConfigureAwait(true), "Plan submitted.");
                    case "Approve":
                        return Check(await plans.ApproveAsync(c.CompanyId, i.Text("Plan"), i.Optional("Text") ?? string.Empty, i.Date("Date")).ConfigureAwait(true), "Plan approved.");
                    case "Amend":
                        return $"Amended as version {Check(await plans.AmendAsync(await Submission().ConfigureAwait(true)).ConfigureAwait(true))}.";
                    case "History":
                        IReadOnlyList<LayoutVersionEntry> versions = Check(await plans.HistoryAsync(c.CompanyId, i.Text("Plan")).ConfigureAwait(true));
                        return string.Join(Environment.NewLine, versions.Select(v => $"v{v.Version} {D(v.SubmittedOn)} {v.Planner}, {v.Density}, {v.Stands} stands"
                            + (v.ApprovedOn is DateOnly approved ? $", approved {D(approved)} ({v.ApprovalAuthority})" : ", not approved") + (v.AmendmentReason is null ? string.Empty : $" - {v.AmendmentReason}")));
                    default:
                        LayoutReconciliation rec = Check(await plans.ReconcileAsync(c.CompanyId, i.Text("Plan")).ConfigureAwait(true));
                        return $"{rec.PlanNumber} v{rec.VersionInForce}: {rec.PlannedStands} planned, {rec.StandsOnRegister} on the register, difference {rec.Difference}.";
                }
            },
            List = async c => Table(await c.Db.Set<LayoutPlan>().AsNoTracking().Where(x => x.CompanyId == c.CompanyId).OrderBy(x => x.PlanNumber).ToListAsync().ConfigureAwait(true),
                ("Plan", x => x.PlanNumber), ("Parcel", x => x.LandParcelId)),
        };
        yield return new Operation
        {
            Module = module, Title = "Stand pricing", ListTitle = "Pricing rules",
            Description = "Proposes a pricing rule - for one stand, a size band or a whole scheme, from a date: cost plus overhead %, levies per stand and per m², margin %, rounded - "
                + "which another user approves; Quote shows a stand's price build-up under the rule in force.",
            Fields = [Pick("Step", "Step", ["Propose", "Approve", "Quote"]), Pick("Scope", "Applies to", Enum.GetNames<PricingScope>()), Opt("Scheme", "Scheme code"), OptNum("MinSize", "Size band from (m²)"),
                OptNum("MaxSize", "Size band to (m²)"), Opt("Stand", "Stand number (stand rule / quote)"), Num("Overhead", "Overhead %", "0"), Num("LevyStand", "Levy per stand", "0"),
                Num("LevyM2", "Levy per m²", "0"), Num("Margin", "Margin %", "0"), Num("RoundTo", "Round to", "1"), OptNum("Rule", "Rule id (approve, from the list)"), Today()],
            Run = async (c, i) =>
            {
                IStandPricingService pricing = c.Get<IStandPricingService>();
                async Task<long?> StandId() => i.Optional("Stand") is string number
                    ? await FindAsync(c.Db.HousingStands, x => x.CompanyId == c.CompanyId && x.StandNumber == number.ToUpper(), x => x.Id, "stand", number).ConfigureAwait(true) : null;
                switch (i.Text("Step"))
                {
                    case "Propose":
                        long? scheme = i.Optional("Scheme") is string code
                            ? await FindAsync(c.Db.HousingSchemes, x => x.CompanyId == c.CompanyId && x.Code == code.ToUpper(), x => x.Id, "scheme", code).ConfigureAwait(true) : null;
                        return Check(await pricing.ProposeAsync(new PricingRuleInput
                        {
                            CompanyId = c.CompanyId, Scope = Enum.Parse<PricingScope>(i.Text("Scope")), SchemeId = scheme, MinSize = i.OptionalNumber("MinSize"), MaxSize = i.OptionalNumber("MaxSize"),
                            StandId = await StandId().ConfigureAwait(true), EffectiveFrom = i.Date("Date"), OverheadPercent = i.Number("Overhead"), LevyPerStand = i.Number("LevyStand"),
                            LevyPerSquareMetre = i.Number("LevyM2"), MarginPercent = i.Number("Margin"), RoundTo = i.Number("RoundTo"), UserId = c.UserId,
                        }).ConfigureAwait(true), id => $"Rule {id} proposed; another user approves it.");
                    case "Approve":
                        return Check(await pricing.ApproveAsync(c.CompanyId, (long)(i.OptionalNumber("Rule") ?? 0m), c.UserId, i.Date("Date")).ConfigureAwait(true), "Rule approved.");
                    default:
                        StandPriceQuote quote = Check(await pricing.QuoteAsync(c.CompanyId, await StandId().ConfigureAwait(true) ?? 0L, i.Date("Date")).ConfigureAwait(true));
                        return $"{quote.StandNumber} (rule {quote.RuleId}, {quote.Scope} from {D(quote.EffectiveFrom)}): cost {M(quote.BuildUp.Cost)} + overhead {M(quote.BuildUp.Overhead)} + levies "
                            + $"{M(quote.BuildUp.Levy)} + margin {M(quote.BuildUp.Margin)} = {M(quote.BuildUp.Price)}.";
                }
            },
            List = async c =>
            {
                HousingNames n = await HousingNamesAsync(c).ConfigureAwait(true);
                return Table(await c.Db.Set<StandPricingRule>().AsNoTracking().Where(x => x.CompanyId == c.CompanyId).OrderByDescending(x => x.Id).Page(c, 200).ToListAsync().ConfigureAwait(true),
                    ("Rule", x => x.Id), ("Scope", x => x.Scope), ("Scheme", x => n.Scheme(x.SchemeId)), ("Stand", x => n.Stand(x.StandId)), ("From", x => D(x.EffectiveFrom)),
                    ("Overhead %", x => Q(x.OverheadPercent)), ("Margin %", x => Q(x.MarginPercent)), ("Approved", x => x.ApprovedOn is DateOnly a ? D(a) : "pending"));
            },
        };
        yield return new Operation
        {
            Module = module, Title = "Land, schemes and stands", ListTitle = "Schemes",
            Description = "Acquire land into land WIP at its cost; create a housing scheme; subdivide a parcel into stands (\"S1:300; S2:250\" - number:size m²), its cost apportioned "
                + "equally or by area into stands available; sell a stand outright for cash (an invoice to the buyer, cost of sales out of stands available). "
                + "Create scheme with an existing code changes that scheme's name and delivery model; Delete removes a scheme that has no stands, developer agreements, "
                + "pricing rules or quotas (kept in history).",
            KeyColumn = "Scheme",
            Delete = async (c, code) =>
            {
                HousingScheme scheme = await ExistingAsync<HousingScheme>(c, code, x => x.Code).ConfigureAwait(true) ?? throw new OperationException($"No scheme {code}.");
                if (await c.Db.HousingStands.AnyAsync(s => s.CompanyId == c.CompanyId && s.SchemeId == scheme.Id).ConfigureAwait(true)
                    || await c.Db.Set<DeveloperAgreement>().AnyAsync(a => a.CompanyId == c.CompanyId && a.SchemeId == scheme.Id).ConfigureAwait(true)
                    || await c.Db.Set<StandPricingRule>().AnyAsync(r => r.CompanyId == c.CompanyId && r.SchemeId == scheme.Id).ConfigureAwait(true)
                    || await c.Db.Set<AllocationQuota>().AnyAsync(q => q.CompanyId == c.CompanyId && q.SchemeId == scheme.Id && q.TargetPercent > 0m).ConfigureAwait(true))
                {
                    throw new OperationException($"Scheme {scheme.Code} is in use (stands, a developer agreement, a pricing rule or a quota refer to it), so it cannot be deleted.");
                }

                scheme.IsDeleted = true;
                scheme.DeletedOnUtc = DateTimeOffset.UtcNow;
                scheme.DeletedByUserId = c.UserId;
                await c.Db.SaveChangesAsync().ConfigureAwait(true);
                return $"Scheme {scheme.Code} deleted (kept in history).";
            },
            Fields = [Pick("Step", "Step", ["Acquire land", "Create scheme", "Subdivide", "Sell stand"]), Opt("Parcel", "Parcel number"), Opt("Description", "Description / scheme name"),
                Num("Area", "Area m² (acquire)", "0"), Num("Amount", "Cost (acquire) / price (sell)", "0"), Opt("Scheme", "Scheme code"), Pick("SchemeType", "Delivery model", Enum.GetNames<SchemeType>()),
                Opt("Stands", "Stands: number:size m²; … (subdivide)"), Pick("Apportion", "Apportion cost", Enum.GetNames<ApportionMethod>()), Opt("Stand", "Stand number (sell)"),
                Opt("Buyer", "Buyer customer code (sell)"), Opt("Wip", "Land WIP account"), Opt("Funding", "Funding / paid from account (acquire)"), Opt("Available", "Stands available account"),
                Opt("Sales", "Sales account (sell)"), Opt("Cost", "Cost of sales account (sell)"), Opt("Document", "Document / invoice number"), Today()],
            Run = async (c, i) =>
            {
                IHousingService housing = c.Get<IHousingService>();
                DateOnly date = i.Date("Date");
                switch (i.Text("Step"))
                {
                    case "Acquire land":
                        AcquisitionResult acquired = Check(await housing.AcquireLandAsync(c.CompanyId, i.Optional("Parcel") ?? string.Empty, i.Optional("Description") ?? string.Empty, i.Number("Area"),
                            i.Number("Amount"), date, i.Optional("Wip") ?? string.Empty, i.Optional("Funding") ?? string.Empty, i.Optional("Document") ?? string.Empty).ConfigureAwait(true));
                        return $"Parcel acquired ({acquired.JournalNumber}).";
                    case "Create scheme":
                        if (await ExistingAsync<HousingScheme>(c, i.Optional("Scheme") ?? string.Empty, x => x.Code).ConfigureAwait(true) is { } known)
                        {
                            known.Name = i.Optional("Description")?.Trim() ?? known.Name;
                            known.SchemeType = Enum.Parse<SchemeType>(i.Text("SchemeType"));
                            await c.Db.SaveChangesAsync().ConfigureAwait(true);
                            return $"Scheme {known.Code} changed.";
                        }

                        return Check(await housing.CreateSchemeAsync(c.CompanyId, i.Optional("Scheme") ?? string.Empty, i.Optional("Description") ?? string.Empty, Enum.Parse<SchemeType>(i.Text("SchemeType")))
                            .ConfigureAwait(true), _ => "Scheme created.");
                    case "Subdivide":
                        long parcel = await FindAsync(c.Db.LandParcels, x => x.CompanyId == c.CompanyId && x.ParcelNumber == (i.Optional("Parcel") ?? string.Empty).ToUpper(), x => x.Id, "land parcel",
                            i.Optional("Parcel") ?? string.Empty).ConfigureAwait(true);
                        long? scheme = i.Optional("Scheme") is string code
                            ? await FindAsync(c.Db.HousingSchemes, x => x.CompanyId == c.CompanyId && x.Code == code.ToUpper(), x => x.Id, "scheme", code).ConfigureAwait(true) : null;
                        List<NewStand> stands = [];
                        foreach (string part in (i.Optional("Stands") ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                        {
                            string[] bits = part.Split(':', StringSplitOptions.TrimEntries);
                            stands.Add(bits.Length == 2 && decimal.TryParse(bits[1], NumberStyles.Number, CultureInfo.InvariantCulture, out decimal size)
                                ? new NewStand(bits[0], size) : throw new OperationException($"Write each stand as number:size m² (\"{part}\")."));
                        }

                        SubdivisionResult subdivided = Check(await housing.SubdivideAsync(c.CompanyId, parcel, scheme, stands, Enum.Parse<ApportionMethod>(i.Text("Apportion")), date,
                            i.Optional("Wip") ?? string.Empty, i.Optional("Available") ?? string.Empty).ConfigureAwait(true));
                        return $"{subdivided.StandIds.Count} stand(s) created ({subdivided.JournalNumber}).";
                    default:
                        long stand = await FindAsync(c.Db.HousingStands, x => x.CompanyId == c.CompanyId && x.StandNumber == (i.Optional("Stand") ?? string.Empty).ToUpper(), x => x.Id, "stand",
                            i.Optional("Stand") ?? string.Empty).ConfigureAwait(true);
                        long buyer = await CustomerAsync(c, i.Optional("Buyer") ?? string.Empty).ConfigureAwait(true);
                        string buyerName = await c.Db.Customers.AsNoTracking().Where(x => x.Id == buyer).Select(x => x.Name).FirstAsync().ConfigureAwait(true);
                        StandSaleResult sold = Check(await housing.SellStandAsync(c.CompanyId, stand, buyer, buyerName, i.Number("Amount"), date, i.Optional("Sales") ?? string.Empty,
                            i.Optional("Available") ?? string.Empty, i.Optional("Cost") ?? string.Empty, i.Optional("Document") ?? string.Empty).ConfigureAwait(true));
                        return $"Sold: invoice posted ({sold.SaleJournalNumber}); cost of sales {M(sold.CostOfSales)} ({sold.CostOfSalesJournalNumber}).";
                }
            },
            List = async c => Table(await c.Db.HousingSchemes.AsNoTracking().Where(x => x.CompanyId == c.CompanyId).OrderBy(x => x.Code).ToListAsync().ConfigureAwait(true),
                ("Step", _ => "Create scheme"), ("Scheme", x => x.Code), ("Description", x => x.Name), ("Delivery model", x => x.SchemeType)),
        };
    }

    private static async Task<System.Data.DataTable> SchemesAsync(OperationContext c)
        => Table(await c.Db.HousingSchemes.AsNoTracking().Where(x => x.CompanyId == c.CompanyId).OrderBy(x => x.Code).ToListAsync().ConfigureAwait(true),
            ("Scheme", x => x.Code), ("Name", x => x.Name));

    private static async Task<System.Data.DataTable> ApplicantsAsync(OperationContext c)
        => Table(await c.Db.HousingApplicants.AsNoTracking().Where(a => a.CompanyId == c.CompanyId).OrderByDescending(a => a.Id).Take(500).ToListAsync().ConfigureAwait(true),
            ("Waiting list", a => a.WaitingListNumber), ("Name", a => a.FullName), ("ID", a => a.IdNumber), ("Category", a => a.Category), ("Registered", a => a.RegistrationDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
            ("Status", a => a.Status), ("Verified", a => a.IsVerified));

    /// <summary>Loads the company's stand numbers, scheme codes and customer codes with names, so housing lists show them instead of database ids.</summary>
    private static async Task<HousingNames> HousingNamesAsync(OperationContext c)
    {
        Dictionary<long, string> stands = await c.Db.HousingStands.AsNoTracking().Where(s => s.CompanyId == c.CompanyId).Select(s => new { s.Id, s.StandNumber })
            .ToDictionaryAsync(s => s.Id, s => s.StandNumber).ConfigureAwait(true);
        Dictionary<long, string> schemes = await c.Db.HousingSchemes.AsNoTracking().Where(s => s.CompanyId == c.CompanyId).Select(s => new { s.Id, s.Code })
            .ToDictionaryAsync(s => s.Id, s => s.Code).ConfigureAwait(true);
        Dictionary<long, string> customers = await c.Db.Customers.AsNoTracking().Where(x => x.CompanyId == c.CompanyId).Select(x => new { x.Id, x.Code, x.Name })
            .ToDictionaryAsync(x => x.Id, x => x.Code + " " + x.Name).ConfigureAwait(true);
        return new HousingNames(stands, schemes, customers);
    }

    /// <summary>Stand numbers, scheme codes and customer "code name" by id; blank for no id, the id itself when the record is not found.</summary>
    private sealed record HousingNames(IReadOnlyDictionary<long, string> Stands, IReadOnlyDictionary<long, string> Schemes, IReadOnlyDictionary<long, string> Customers)
    {
        public string Stand(long? id) => Name(Stands, id);

        public string Scheme(long? id) => Name(Schemes, id);

        public string Customer(long? id) => Name(Customers, id);

        private static string Name(IReadOnlyDictionary<long, string> names, long? id)
            => id is long key ? names.GetValueOrDefault(key, key.ToString(CultureInfo.InvariantCulture)) : string.Empty;
    }

    /// <summary>The record id picked from a lookup (0 when none is picked, which the service refuses as not found).</summary>
    private static long PickedId(OperationInput i, string name)
        => i.Optional(name) is not string text ? 0L
            : long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out long id) ? id : throw new OperationException($"Pick one from the list (\"{text}\" is not one).");

    private static string Short(string text, int length = 40) => text.Length <= length ? text : text[..length].TrimEnd() + "...";

    // Swap documents waiting for another officer's decision.
    private static async Task<List<(string Code, string Name)>> PendingSwapsAsync(OperationContext c)
        => (await c.Db.Set<StandSwap>().AsNoTracking().Where(x => x.CompanyId == c.CompanyId && x.Status == StandSwapStatus.Pending).OrderBy(x => x.Id)
                .Select(x => new { x.Id, x.FirstCertificate, x.SecondCertificate, x.Reference, x.Date }).Take(500).ToListAsync().ConfigureAwait(true))
            .Select(x => (x.Id.ToString(CultureInfo.InvariantCulture), $"{x.FirstCertificate} with {x.SecondCertificate} - document {x.Reference} - {D(x.Date)}")).ToList();

    // Household members still living in a let unit.
    private static async Task<List<(string Code, string Name)>> CurrentOccupantsAsync(OperationContext c)
        => (await (from t in c.Db.Set<Tenancy>().AsNoTracking()
                   where t.CompanyId == c.CompanyId
                   from o in t.Occupants
                   where o.MovedOutOn == null
                   join u in c.Db.Set<RentalUnit>().AsNoTracking() on t.UnitId equals u.Id
                   orderby t.TenancyNumber, o.FullName
                   select new { o.Id, o.FullName, t.TenancyNumber, u.Code, o.MovedInOn }).Take(2000).ToListAsync().ConfigureAwait(true))
            .Select(x => (x.Id.ToString(CultureInfo.InvariantCulture), $"{x.FullName} - tenancy {x.TenancyNumber} - unit {x.Code} - since {D(x.MovedInOn)}")).ToList();

    // Inspections whose follow-up is still open.
    private static async Task<List<(string Code, string Name)>> OpenFollowUpsAsync(OperationContext c)
        => (await (from x in c.Db.Set<UnitInspection>().AsNoTracking()
                   where x.CompanyId == c.CompanyId && x.FollowUpRequired && x.FollowUpDoneOn == null
                   join u in c.Db.Set<RentalUnit>().AsNoTracking() on x.UnitId equals u.Id
                   orderby x.InspectedOn
                   select new { x.Id, u.Code, x.InspectedOn, x.ConditionGrade, x.Findings }).Take(1000).ToListAsync().ConfigureAwait(true))
            .Select(x => (x.Id.ToString(CultureInfo.InvariantCulture), $"unit {x.Code} - inspected {D(x.InspectedOn)} - grade {x.ConditionGrade} - {Short(x.Findings)}")).ToList();

    // Planned maintenance tasks still in the programme, soonest due first.
    private static async Task<List<(string Code, string Name)>> PlannedTasksAsync(OperationContext c)
        => (await (from x in c.Db.Set<PlannedMaintenance>().AsNoTracking()
                   where x.CompanyId == c.CompanyId && x.IsActive
                   join u in c.Db.Set<RentalUnit>().AsNoTracking() on x.UnitId equals u.Id
                   orderby x.NextDue, u.Code
                   select new { x.Id, u.Code, x.Description, x.NextDue }).Take(1000).ToListAsync().ConfigureAwait(true))
            .Select(x => (x.Id.ToString(CultureInfo.InvariantCulture), $"unit {x.Code} - {Short(x.Description)} - due {D(x.NextDue)}")).ToList();

    // Registration date changes waiting for another user's decision.
    private static async Task<List<(string Code, string Name)>> PendingDateChangesAsync(OperationContext c)
        => (await (from x in c.Db.Set<RegistrationDateChange>().AsNoTracking()
                   where x.CompanyId == c.CompanyId && x.Status == ChangeRequestStatus.Pending
                   join a in c.Db.HousingApplicants.AsNoTracking() on x.ApplicantId equals a.Id
                   orderby x.Id
                   select new { x.Id, a.FullName, a.WaitingListNumber, x.OldDate, x.NewDate, x.Reason }).Take(500).ToListAsync().ConfigureAwait(true))
            .Select(x => (x.Id.ToString(CultureInfo.InvariantCulture), $"{x.FullName} {x.WaitingListNumber} - {D(x.OldDate)} to {D(x.NewDate)} - {Short(x.Reason)}")).ToList();

    // Encumbrances still in force (not lifted).
    private static async Task<List<(string Code, string Name)>> EncumbrancesInForceAsync(OperationContext c)
        => (await (from x in c.Db.Set<LandEncumbrance>().AsNoTracking()
                   where x.CompanyId == c.CompanyId && x.LiftedOn == null
                   join p in c.Db.LandParcels.AsNoTracking() on x.LandParcelId equals p.Id
                   orderby p.ParcelNumber, x.RegisteredOn
                   select new { x.Id, p.ParcelNumber, x.Type, x.Reference, x.InFavourOf, x.RegisteredOn }).Take(1000).ToListAsync().ConfigureAwait(true))
            .Select(x => (x.Id.ToString(CultureInfo.InvariantCulture), $"parcel {x.ParcelNumber} - {x.Type} {x.Reference} - in favour of {x.InFavourOf} - since {D(x.RegisteredOn)}")).ToList();

    private static DateOnly Day(string text)
        => DateOnly.TryParseExact(text.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly day) ? day : throw new OperationException($"\"{text}\" is not a date (yyyy-MM-dd).");
}
