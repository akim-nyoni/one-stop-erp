using One4All.Core.Housing;

namespace One4All.Application.Housing;

/// <summary>A change in one use between two layout versions.</summary>
/// <param name="Use">The use.</param>
/// <param name="Before">Stands before.</param>
/// <param name="After">Stands after.</param>
public sealed record LayoutChange(StandUse Use, int Before, int After);

/// <summary>
/// Layout plan arithmetic (brief §4.10.2): what an amendment changed, use by use, and how many stands a layout creates
/// (every use but roads). Pure and unit-tested.
/// </summary>
public static class LayoutChanges
{
    /// <summary>The uses whose stand counts differ between two versions, in use order.</summary>
    /// <param name="before">The earlier version's counts.</param>
    /// <param name="after">The later version's counts.</param>
    /// <returns>The changes.</returns>
    public static IReadOnlyList<LayoutChange> Between(IReadOnlyDictionary<StandUse, int> before, IReadOnlyDictionary<StandUse, int> after)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        return [.. Enum.GetValues<StandUse>()
            .Select(u => new LayoutChange(u, before.GetValueOrDefault(u), after.GetValueOrDefault(u)))
            .Where(c => c.Before != c.After)];
    }

    /// <summary>The stands a layout creates: every use but roads.</summary>
    /// <param name="uses">The counts by use.</param>
    /// <returns>The number of stands.</returns>
    public static int Stands(IReadOnlyDictionary<StandUse, int> uses)
    {
        ArgumentNullException.ThrowIfNull(uses);
        return uses.Where(u => u.Key != StandUse.Roads).Sum(u => u.Value);
    }
}
