namespace One4All.Application.Pos;

/// <summary>Rearranging a page of favourite tiles by dragging one onto another. Pure and unit-tested.</summary>
public static class FavouriteOrder
{
    /// <summary>The page's order with one tile moved to where another was (the others shift along).</summary>
    /// <param name="order">The items in their current order.</param>
    /// <param name="moving">The item dragged.</param>
    /// <param name="onto">The item it was dropped on.</param>
    /// <returns>The new order (unchanged when either is not on the page or they are the same).</returns>
    public static IReadOnlyList<long> Move(IReadOnlyList<long> order, long moving, long onto)
    {
        ArgumentNullException.ThrowIfNull(order);
        int from = IndexOf(order, moving), to = IndexOf(order, onto);
        if (from < 0 || to < 0 || from == to)
        {
            return order;
        }

        List<long> moved = [.. order];
        moved.RemoveAt(from);
        moved.Insert(to, moving);
        return moved;
    }

    private static int IndexOf(IReadOnlyList<long> order, long item)
    {
        for (int i = 0; i < order.Count; i++)
        {
            if (order[i] == item)
            {
                return i;
            }
        }

        return -1;
    }
}
