using One4All.Core.Common;

namespace One4All.Core.Pos;

/// <summary>An item pinned to the till's favourites (<c>pos.favourite</c>, brief §4.6): for one terminal, or for every till when no terminal is named.</summary>
public sealed class PosFavourite : Entity, ICompanyScoped
{
    private PosFavourite()
    {
    }

    /// <summary>Pins an item.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="terminalId">The terminal (null: every till).</param>
    /// <param name="itemId">The item.</param>
    /// <param name="sortOrder">Where it sits among the favourites.</param>
    public PosFavourite(long companyId, long? terminalId, long itemId, int sortOrder)
    {
        CompanyId = companyId;
        TerminalId = terminalId;
        ItemId = itemId;
        SortOrder = sortOrder;
    }

    /// <summary>Gets the company.</summary>
    public long CompanyId { get; private set; }

    /// <summary>Gets the terminal (null: every till).</summary>
    public long? TerminalId { get; private set; }

    /// <summary>Gets the item.</summary>
    public long ItemId { get; private set; }

    /// <summary>Gets or sets where it sits among the favourites.</summary>
    public int SortOrder { get; set; }
}
