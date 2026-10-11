namespace One4All.Application.MasterData;

/// <summary>A POS terminal a vertical starts with.</summary>
/// <param name="Code">The terminal code.</param>
/// <param name="Name">Its name.</param>
public sealed record TerminalSeed(string Code, string Name);

/// <summary>
/// What else a vertical seeds beyond its ledger (brief §12 Industry Setup Wizard): item categories, a POS terminal profile at head office,
/// and its report set — the vertical's documents (statements, receipts, slips) and key reports, featured first in the report viewer.
/// </summary>
/// <param name="ItemCategories">Item categories (code, name).</param>
/// <param name="Terminals">POS terminals.</param>
/// <param name="FeaturedReports">Report codes featured for the company.</param>
public sealed record VerticalExtras(IReadOnlyList<(string Code, string Name)> ItemCategories, IReadOnlyList<TerminalSeed> Terminals, IReadOnlyList<string> FeaturedReports);

/// <summary>The extras per vertical. Pure.</summary>
public static class IndustryExtras
{
    private static readonly Dictionary<Vertical, VerticalExtras> Extras = new()
    {
        [Vertical.Core] = new([("GEN", "General"), ("SERV", "Services"), ("CONS", "Consumables")], [],
            ["GL-TB", "GL-IS", "GL-BS", "GL-CF", "GL-DAYBOOK", "AR-STMT", "AP51"]),
        [Vertical.Retail] = new(
            [("GROC", "Groceries"), ("BEV", "Beverages"), ("FRESH", "Fresh produce"), ("HOME", "Household"), ("PERS", "Personal care")],
            [new TerminalSeed("TILL01", "Front till")],
            ["POS-SUM", "POS103", "POS102", "POS105", "POS107", "POS122", "INV79", "INV-VAL", "INV-AGE"]),
        [Vertical.Municipal] = new([("STAT", "Stationery"), ("METR", "Meters & fittings"), ("CHEM", "Water treatment chemicals")], [],
            ["M144", "M146", "M147", "M148", "M149", "M151", "M152", "M153", "RC35", "RC1", "RC2"]),
        [Vertical.Housing] = new([("BLDG", "Building materials"), ("SERV", "Servicing materials")], [],
            ["H1", "H2", "H18", "H27", "H74", "H43", "RC35"]),
        [Vertical.School] = new([("UNIF", "Uniforms"), ("BOOK", "Books & stationery"), ("TUCK", "Tuck shop")],
            [new TerminalSeed("TUCK01", "Tuck shop till")],
            ["SC173", "SC175", "SC176", "SC177", "SC179", "SC180", "SC181", "RC35"]),
        [Vertical.Construction] = new([("MATL", "Materials"), ("PLANT", "Plant & equipment"), ("SUBC", "Subcontract")], [],
            ["JC186", "JC189", "JC191", "JC193", "JOB-IPC", "INV98"]),
        [Vertical.Hospitality] = new([("FOOD", "Food"), ("BEV", "Beverages"), ("LIQ", "Liquor"), ("LINEN", "Consumables & linen")],
            [new TerminalSeed("BAR01", "Bar till"), new TerminalSeed("REST01", "Restaurant till")],
            ["POS-SUM", "POS103", "POS105", "POS107", "INV79", "INV-VAL"]),
    };

    /// <summary>The extras for a vertical.</summary>
    /// <param name="vertical">The vertical.</param>
    /// <returns>Its extras.</returns>
    public static VerticalExtras For(Vertical vertical) => Extras.TryGetValue(vertical, out VerticalExtras? extras) ? extras : new VerticalExtras([], [], []);
}
