using System.Data;
using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Budgeting;
using One4All.Core.Budgeting;
using static One4All.Desktop.Operations.Ops;

namespace One4All.Desktop.Operations;

/// <content>Budget preparation: submission, review, negotiation and approval.</content>
public static partial class OperationCatalogue
{
    private static IEnumerable<Operation> Budgeting()
    {
        const string module = "Budgeting";
        string[] steps = ["Submit", "Start review", "Return", "Recommend", "Approve", "Accept proposals"];
        yield return new Operation
        {
            Module = module, Title = "Move a budget through preparation", ListTitle = "Budgets in preparation",
            Description = "Submit (department) → start review (Finance) → return with reasons or recommend → approve with the board/council resolution (not by the submitter). "
                + "A returned budget can take Finance's proposals and be resubmitted as a new round.",
            Fields = [Num("Budget", "Budget id"), Pick("Step", "Step", steps), Opt("Note", "Comment / resolution reference")],
            Run = async (c, i) =>
            {
                IBudgetPreparationService prep = c.Get<IBudgetPreparationService>();
                long id = (long)i.Number("Budget");
                string? note = i.Optional("Note");
                return i.Text("Step") switch
                {
                    "Submit" => Check(await prep.SubmitAsync(c.CompanyId, id, c.UserId, note).ConfigureAwait(true), "Submitted."),
                    "Start review" => Check(await prep.StartReviewAsync(c.CompanyId, id, c.UserId).ConfigureAwait(true), "Under review."),
                    "Return" => Check(await prep.ReturnAsync(c.CompanyId, id, c.UserId, note ?? string.Empty).ConfigureAwait(true), "Returned to the department."),
                    "Recommend" => Check(await prep.RecommendAsync(c.CompanyId, id, c.UserId, note).ConfigureAwait(true), "Recommended."),
                    "Approve" => Check(await prep.ApproveAsync(c.CompanyId, id, c.UserId, note ?? string.Empty).ConfigureAwait(true), "Approved and sealed."),
                    _ => $"{Check(await prep.AcceptProposalsAsync(c.CompanyId, id, c.UserId).ConfigureAwait(true))} line(s) took Finance's figures.",
                };
            },
            List = BudgetsInPreparationAsync,
        };
        yield return new Operation
        {
            Module = module, Title = "Propose or revise a figure", ListTitle = "Budgets in preparation",
            Description = "Finance proposes a figure on a budget under review (kept beside the department's request); the department revises a draft or returned one.",
            Fields = [Num("Budget", "Budget id"), Req("Account", "Account"), Num("Period", "Period", "1"), Num("Amount", "Amount"), Opt("CostCentre", "Cost centre"), Opt("Comment", "Why"),
                Tick("Propose", "Finance proposal (untick to revise)")],
            Run = async (c, i) =>
            {
                ProposedFigure figure = new(i.Text("Account"), i.Whole("Period"), i.Number("Amount"), i.Optional("CostCentre"), i.Optional("Comment"));
                IBudgetPreparationService prep = c.Get<IBudgetPreparationService>();
                return i.Flag("Propose")
                    ? Check(await prep.ProposeAsync(c.CompanyId, (long)i.Number("Budget"), c.UserId, [figure]).ConfigureAwait(true), "Proposal recorded.")
                    : Check(await prep.ReviseAsync(c.CompanyId, (long)i.Number("Budget"), c.UserId, figure).ConfigureAwait(true), "Figure revised.");
            },
            List = BudgetsInPreparationAsync,
        };
        yield return new Operation
        {
            Module = module, Title = "Show a budget's negotiation trail", ListTitle = "Budgets in preparation",
            Description = "Every step and every proposal, the department's figure beside Finance's, round by round.",
            Fields = [Num("Budget", "Budget id")],
            Run = async (c, i) =>
            {
                BudgetTrail trail = Check(await c.Get<IBudgetPreparationService>().TrailAsync(c.CompanyId, (long)i.Number("Budget")).ConfigureAwait(true));
                IEnumerable<string> steps = trail.Events.Select(e => $"Round {e.Round}: {e.Action} by user {e.UserId} on {e.OccurredOnUtc:yyyy-MM-dd HH:mm}{(e.Comment is null ? string.Empty : $" — {e.Comment}")}");
                IEnumerable<string> figures = trail.Proposals.Select(p => $"Round {p.Round}: {p.AccountCode} p{p.Period} requested {M(p.Requested)}, proposed {M(p.Proposed)}{(p.Comment is null ? string.Empty : $" ({p.Comment})")}");
                return string.Join(Environment.NewLine, [$"{trail.Status}, round {trail.Round}{(trail.ResolutionReference is null ? string.Empty : $", resolution {trail.ResolutionReference}")}", .. steps, .. figures]);
            },
            List = BudgetsInPreparationAsync,
        };
    }

    private static async Task<DataTable> BudgetsInPreparationAsync(OperationContext c)
        => Table(await c.Db.Set<Budget>().AsNoTracking().Include(b => b.Lines).Where(b => b.CompanyId == c.CompanyId).OrderByDescending(b => b.Id).Take(200).ToListAsync().ConfigureAwait(true),
            ("Id", b => b.Id), ("Budget", b => $"{b.Label} v{b.VersionNumber}"), ("Type", b => b.Type), ("Department", b => b.Department), ("Status", b => b.Status), ("Round", b => b.Round),
            ("Total", b => M(b.AnnualTotal)), ("Resolution", b => b.ResolutionReference), ("Active", b => b.IsActive));
}
