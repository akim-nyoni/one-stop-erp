using One4All.Core.Common;

namespace One4All.Core.Municipal;

/// <summary>Where a cemetery plot stands.</summary>
public enum CemeteryPlotStatus
{
    /// <summary>Not yet sold.</summary>
    Available = 0,

    /// <summary>Sold to a holder, with room for interments.</summary>
    Sold = 1,

    /// <summary>Every place in the plot is used.</summary>
    Full = 2,
}

/// <summary>
/// A plot in a council cemetery (<c>mun.cemetery_plot</c>, brief §4.9 cemetery revenue): identified by cemetery,
/// section and number, sold to a holder at its price, and holding as many interments as its capacity allows (a family
/// plot takes several).
/// </summary>
public sealed class CemeteryPlot : AuditableEntity, ICompanyScoped
{
    private readonly List<CemeteryInterment> _interments = [];

    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private CemeteryPlot()
    {
        Reference = string.Empty;
        Cemetery = string.Empty;
        Section = string.Empty;
        PlotNumber = string.Empty;
    }

    /// <summary>Records a plot.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="cemetery">The cemetery.</param>
    /// <param name="section">The section.</param>
    /// <param name="plotNumber">The plot number.</param>
    /// <param name="price">The selling price.</param>
    /// <param name="capacity">How many interments it can hold.</param>
    public CemeteryPlot(long companyId, string cemetery, string section, string plotNumber, decimal price, int capacity)
    {
        CompanyId = companyId;
        Cemetery = cemetery;
        Section = section;
        PlotNumber = plotNumber;
        Reference = ReferenceFor(cemetery, section, plotNumber);
        Price = price;
        Capacity = capacity;
        Status = CemeteryPlotStatus.Available;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The plot reference: cemetery-section-number, upper case.</summary>
    public string Reference { get; private set; }

    /// <summary>The cemetery.</summary>
    public string Cemetery { get; private set; }

    /// <summary>The section.</summary>
    public string Section { get; private set; }

    /// <summary>The plot number.</summary>
    public string PlotNumber { get; private set; }

    /// <summary>The selling price.</summary>
    public decimal Price { get; private set; }

    /// <summary>How many interments it can hold.</summary>
    public int Capacity { get; private set; }

    /// <summary>Where it stands.</summary>
    public CemeteryPlotStatus Status { get; private set; }

    /// <summary>The holder it was sold to.</summary>
    public long? HolderCustomerId { get; private set; }

    /// <summary>The sale invoice.</summary>
    public string? SaleInvoiceNumber { get; private set; }

    /// <summary>When it was sold.</summary>
    public DateOnly? SoldOn { get; private set; }

    /// <summary>The interments in it.</summary>
    public IReadOnlyList<CemeteryInterment> Interments => _interments.AsReadOnly();

    /// <summary>The reference for a plot.</summary>
    /// <param name="cemetery">The cemetery.</param>
    /// <param name="section">The section.</param>
    /// <param name="plotNumber">The plot number.</param>
    /// <returns>The reference.</returns>
    public static string ReferenceFor(string cemetery, string section, string plotNumber)
        => $"{cemetery.Trim()}-{section.Trim()}-{plotNumber.Trim()}".ToUpperInvariant();

    /// <summary>Sells it to a holder.</summary>
    /// <param name="holderCustomerId">The holder.</param>
    /// <param name="invoiceNumber">The sale invoice.</param>
    /// <param name="date">When.</param>
    public void Sell(long holderCustomerId, string invoiceNumber, DateOnly date)
    {
        HolderCustomerId = holderCustomerId;
        SaleInvoiceNumber = invoiceNumber;
        SoldOn = date;
        Status = CemeteryPlotStatus.Sold;
    }

    /// <summary>Records an interment, marking the plot full when its last place is used.</summary>
    /// <param name="interment">The interment.</param>
    public void Inter(CemeteryInterment interment)
    {
        _interments.Add(interment);
        if (_interments.Count >= Capacity)
        {
            Status = CemeteryPlotStatus.Full;
        }
    }
}

/// <summary>An interment in a cemetery plot (<c>mun.cemetery_interment</c>): the burial register entry.</summary>
public sealed class CemeteryInterment : Entity
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private CemeteryInterment()
    {
        IntermentNumber = string.Empty;
        DeceasedName = string.Empty;
    }

    /// <summary>Records an interment.</summary>
    /// <param name="intermentNumber">The burial register number.</param>
    /// <param name="deceasedName">The deceased.</param>
    /// <param name="dateOfDeath">The date of death.</param>
    /// <param name="intermentDate">The burial date.</param>
    public CemeteryInterment(string intermentNumber, string deceasedName, DateOnly dateOfDeath, DateOnly intermentDate)
    {
        IntermentNumber = intermentNumber;
        DeceasedName = deceasedName;
        DateOfDeath = dateOfDeath;
        IntermentDate = intermentDate;
    }

    /// <summary>The plot.</summary>
    public long CemeteryPlotId { get; private set; }

    /// <summary>The burial register number.</summary>
    public string IntermentNumber { get; private set; }

    /// <summary>The deceased.</summary>
    public string DeceasedName { get; private set; }

    /// <summary>The date of death.</summary>
    public DateOnly DateOfDeath { get; private set; }

    /// <summary>The burial date.</summary>
    public DateOnly IntermentDate { get; private set; }
}
