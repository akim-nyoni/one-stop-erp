namespace One4All.Core.Municipal;

/// <summary>
/// The consumer category a property and a tariff belong to (brief §4.9): tariffs vary by category, so a commercial stand
/// is billed on the commercial tariff and a domestic stand on the domestic one.
/// </summary>
public enum ConsumerCategory
{
    /// <summary>A household.</summary>
    Domestic = 1,

    /// <summary>A business.</summary>
    Commercial = 2,

    /// <summary>Industry.</summary>
    Industrial = 3,

    /// <summary>A school, church, clinic or other institution.</summary>
    Institutional = 4,

    /// <summary>A farm or agricultural holding.</summary>
    Agricultural = 5,

    /// <summary>An indigent household on the social tariff.</summary>
    Indigent = 6,
}
