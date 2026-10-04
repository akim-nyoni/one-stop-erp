using One4All.Core.Common;

namespace One4All.Core.Housing;

/// <summary>The lifecycle of a land parcel in the land bank (brief §4.10.1).</summary>
public enum LandParcelStatus
{
    /// <summary>Acquired into the land bank (Land &amp; Housing Stock WIP).</summary>
    Acquired = 1,

    /// <summary>Subdivided into stands; cost released to Stands Available for Sale.</summary>
    Subdivided = 2,
}

/// <summary>
/// A parcel of land in the land bank (brief §4.10.1, <c>hse.land_parcel</c>). Acquisition cost plus
/// capitalisable servicing is inventory under IAS 2 (brief §4.10.9), held in a Land &amp; Housing Stock
/// (WIP) account until the parcel is subdivided into stands available for sale.
/// </summary>
public sealed class LandParcel : SoftDeletableEntity, ICompanyScoped
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private LandParcel()
    {
        ParcelNumber = string.Empty;
        Description = string.Empty;
    }

    /// <summary>Creates a land parcel.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="parcelNumber">The parcel/title reference.</param>
    /// <param name="description">The description.</param>
    /// <param name="areaSquareMetres">The area in square metres.</param>
    /// <param name="acquisitionCost">The acquisition cost (into WIP).</param>
    public LandParcel(long companyId, string parcelNumber, string description, decimal areaSquareMetres, decimal acquisitionCost)
    {
        CompanyId = companyId;
        ParcelNumber = parcelNumber.Trim().ToUpperInvariant();
        Description = description;
        AreaSquareMetres = areaSquareMetres;
        AcquisitionCost = acquisitionCost;
        Status = LandParcelStatus.Acquired;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The parcel/title reference.</summary>
    public string ParcelNumber { get; private set; }

    /// <summary>The description.</summary>
    public string Description { get; set; }

    /// <summary>The area in square metres.</summary>
    public decimal AreaSquareMetres { get; set; }

    /// <summary>The total capitalised cost (acquisition + servicing) held in WIP.</summary>
    public decimal AcquisitionCost { get; private set; }

    /// <summary>The parcel status.</summary>
    public LandParcelStatus Status { get; private set; }

    /// <summary>Capitalises additional servicing cost onto the parcel (still in WIP).</summary>
    /// <param name="amount">The cost to add.</param>
    public void CapitaliseServicing(decimal amount) => AcquisitionCost += amount;

    /// <summary>Marks the parcel subdivided once its stands have been created.</summary>
    public void MarkSubdivided() => Status = LandParcelStatus.Subdivided;

    /// <summary>The mapped boundary, once surveyed onto the map.</summary>
    public MappedBoundary? Boundary { get; private set; }

    /// <summary>Records the mapped boundary.</summary>
    /// <param name="boundary">The boundary.</param>
    public void Map(MappedBoundary boundary) => Boundary = boundary;
}
