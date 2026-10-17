using One4All.Core.Housing;

namespace One4All.Application.Housing;

/// <summary>An open title application as the ageing report sees it.</summary>
/// <param name="Stage">Its stage.</param>
/// <param name="StageSince">Since when.</param>
/// <param name="AppliedOn">When it was applied for.</param>
public sealed record TitleAge(TitleStage Stage, DateOnly StageSince, DateOnly AppliedOn);

/// <summary>Ageing of one pipeline stage.</summary>
/// <param name="Stage">The stage.</param>
/// <param name="Count">Applications at it.</param>
/// <param name="AverageDays">Their average days at the stage.</param>
/// <param name="OldestDays">The longest any has been at it.</param>
public sealed record StageAgeing(TitleStage Stage, int Count, int AverageDays, int OldestDays);

/// <summary>
/// The title deed pipeline (brief §4.10.6): which stage may follow which - applied, conveyancer assigned, lodged, then
/// any number of registry queries raised and resolved, registered, received into council custody, handed over - and the
/// ageing of open applications at each stage. Pure and unit-tested.
/// </summary>
public static class TitlePipeline
{
    /// <summary>Whether a stage may follow another.</summary>
    /// <param name="from">The current stage.</param>
    /// <param name="to">The next stage.</param>
    /// <returns>True when allowed.</returns>
    public static bool CanMove(TitleStage from, TitleStage to) => (from, to) switch
    {
        (TitleStage.Applied, TitleStage.ConveyancerAssigned) => true,
        (TitleStage.ConveyancerAssigned, TitleStage.Lodged) => true,
        (TitleStage.Lodged or TitleStage.QueryResolved, TitleStage.QueryRaised) => true,
        (TitleStage.QueryRaised, TitleStage.QueryResolved) => true,
        (TitleStage.Lodged or TitleStage.QueryResolved, TitleStage.Registered) => true,
        (TitleStage.Registered, TitleStage.InCustody) => true,
        (TitleStage.InCustody, TitleStage.HandedOver) => true,
        _ => false,
    };

    /// <summary>Ageing by stage of the open applications as at a date, in pipeline order (closed ones are left out).</summary>
    /// <param name="open">The applications.</param>
    /// <param name="asAt">The date.</param>
    /// <returns>One row per stage with applications at it.</returns>
    public static IReadOnlyList<StageAgeing> Ageing(IEnumerable<TitleAge> open, DateOnly asAt)
    {
        ArgumentNullException.ThrowIfNull(open);
        return [.. open.Where(a => a.Stage != TitleStage.HandedOver)
            .GroupBy(a => a.Stage)
            .OrderBy(g => g.Key)
            .Select(g =>
            {
                List<int> days = [.. g.Select(a => Math.Max(0, asAt.DayNumber - a.StageSince.DayNumber))];
                return new StageAgeing(g.Key, days.Count, (int)Math.Round(days.Average(), MidpointRounding.AwayFromZero), days.Max());
            })];
    }
}
