using One4All.Core.Common;

namespace One4All.Core.Municipal;

/// <summary>Where a property value came from.</summary>
public enum ValuationKind
{
    /// <summary>A general valuation roll (or the property's opening value).</summary>
    General = 1,

    /// <summary>A supplementary valuation between general rolls (improvements, subdivision, rezoning).</summary>
    Supplementary = 2,

    /// <summary>The value set by deciding an objection.</summary>
    Objection = 3,
}

/// <summary>Where a valuation objection stands.</summary>
public enum ObjectionStatus
{
    /// <summary>Lodged and awaiting a decision.</summary>
    Lodged = 0,

    /// <summary>Upheld: the value was revised.</summary>
    Upheld = 1,

    /// <summary>Dismissed: the value stands.</summary>
    Dismissed = 2,
}

/// <summary>
/// A property value on the valuation roll (<c>mun.property_valuation</c>, brief §4.9): the value, the date it takes
/// effect and where it came from. Records are never changed; a later record replaces an earlier one from its date.
/// </summary>
public sealed class PropertyValuation : AuditableEntity, ICompanyScoped
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private PropertyValuation()
    {
        Reference = string.Empty;
    }

    /// <summary>Records a value.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="propertyId">The property.</param>
    /// <param name="effectiveFrom">The first day it applies.</param>
    /// <param name="value">The value.</param>
    /// <param name="kind">Where it came from.</param>
    /// <param name="reference">The roll name, supplementary number or objection number.</param>
    /// <param name="reason">Why, for a supplementary valuation.</param>
    public PropertyValuation(long companyId, long propertyId, DateOnly effectiveFrom, decimal value, ValuationKind kind, string reference, string? reason)
    {
        CompanyId = companyId;
        PropertyId = propertyId;
        EffectiveFrom = effectiveFrom;
        Value = value;
        Kind = kind;
        Reference = reference;
        Reason = reason;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The property.</summary>
    public long PropertyId { get; private set; }

    /// <summary>The first day it applies.</summary>
    public DateOnly EffectiveFrom { get; private set; }

    /// <summary>The value.</summary>
    public decimal Value { get; private set; }

    /// <summary>Where it came from.</summary>
    public ValuationKind Kind { get; private set; }

    /// <summary>The roll name, supplementary number or objection number.</summary>
    public string Reference { get; private set; }

    /// <summary>Why, for a supplementary valuation.</summary>
    public string? Reason { get; private set; }
}

/// <summary>
/// An objection to a property's value (<c>mun.valuation_objection</c>, the brief's objections register): lodged against
/// the value in force, then upheld with a revised value or dismissed.
/// </summary>
public sealed class ValuationObjection : AuditableEntity, ICompanyScoped
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private ValuationObjection()
    {
        ObjectionNumber = string.Empty;
        Grounds = string.Empty;
    }

    /// <summary>Lodges an objection.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="objectionNumber">The objection number.</param>
    /// <param name="propertyId">The property.</param>
    /// <param name="objectedValuationId">The value objected to.</param>
    /// <param name="valueObjectedTo">That value.</param>
    /// <param name="claimedValue">The value the objector claims.</param>
    /// <param name="grounds">The grounds.</param>
    /// <param name="lodgedOn">When it was lodged.</param>
    public ValuationObjection(long companyId, string objectionNumber, long propertyId, long objectedValuationId, decimal valueObjectedTo, decimal claimedValue, string grounds, DateOnly lodgedOn)
    {
        CompanyId = companyId;
        ObjectionNumber = objectionNumber;
        PropertyId = propertyId;
        ObjectedValuationId = objectedValuationId;
        ValueObjectedTo = valueObjectedTo;
        ClaimedValue = claimedValue;
        Grounds = grounds;
        LodgedOn = lodgedOn;
        Status = ObjectionStatus.Lodged;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The objection number.</summary>
    public string ObjectionNumber { get; private set; }

    /// <summary>The property.</summary>
    public long PropertyId { get; private set; }

    /// <summary>The value objected to.</summary>
    public long ObjectedValuationId { get; private set; }

    /// <summary>That value.</summary>
    public decimal ValueObjectedTo { get; private set; }

    /// <summary>The value the objector claims.</summary>
    public decimal ClaimedValue { get; private set; }

    /// <summary>The grounds.</summary>
    public string Grounds { get; private set; }

    /// <summary>When it was lodged.</summary>
    public DateOnly LodgedOn { get; private set; }

    /// <summary>Where it stands.</summary>
    public ObjectionStatus Status { get; private set; }

    /// <summary>When it was decided.</summary>
    public DateOnly? DecidedOn { get; private set; }

    /// <summary>The revised value, when upheld.</summary>
    public decimal? DecidedValue { get; private set; }

    /// <summary>The decision's reasons.</summary>
    public string? DecisionNotes { get; private set; }

    /// <summary>Upholds it with a revised value.</summary>
    /// <param name="date">When.</param>
    /// <param name="value">The revised value.</param>
    /// <param name="notes">The reasons.</param>
    public void Uphold(DateOnly date, decimal value, string notes)
    {
        Status = ObjectionStatus.Upheld;
        DecidedOn = date;
        DecidedValue = value;
        DecisionNotes = notes;
    }

    /// <summary>Dismisses it.</summary>
    /// <param name="date">When.</param>
    /// <param name="notes">The reasons.</param>
    public void Dismiss(DateOnly date, string notes)
    {
        Status = ObjectionStatus.Dismissed;
        DecidedOn = date;
        DecisionNotes = notes;
    }
}
