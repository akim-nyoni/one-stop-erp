using One4All.Core.Common;

namespace One4All.Core.Housing;

/// <summary>How far a stand's survey has gone (brief §14 H12); it only moves forward.</summary>
public enum SurveyStage
{
    /// <summary>Pegged on the ground.</summary>
    Pegged = 1,

    /// <summary>The survey diagram lodged with the Surveyor-General, awaiting approval.</summary>
    DiagramLodged = 2,

    /// <summary>The diagram approved and issued.</summary>
    DiagramApproved = 3,
}

/// <summary>
/// A survey stage a stand reached (<c>hse.stand_survey_step</c>, brief §14 H12), with the surveyor's or Surveyor-General's
/// reference. The stand's survey status as at any date is its latest step by then.
/// </summary>
public sealed class StandSurveyStep : AuditableEntity, ICompanyScoped
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private StandSurveyStep() => Reference = string.Empty;

    /// <summary>Records a survey stage.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="standId">The stand.</param>
    /// <param name="stage">The stage reached.</param>
    /// <param name="date">When.</param>
    /// <param name="reference">The surveyor's job or diagram number.</param>
    public StandSurveyStep(long companyId, long standId, SurveyStage stage, DateOnly date, string reference)
    {
        CompanyId = companyId;
        StandId = standId;
        Stage = stage;
        Date = date;
        Reference = reference;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The stand.</summary>
    public long StandId { get; private set; }

    /// <summary>The stage reached.</summary>
    public SurveyStage Stage { get; private set; }

    /// <summary>When.</summary>
    public DateOnly Date { get; private set; }

    /// <summary>The surveyor's job or diagram number.</summary>
    public string Reference { get; private set; }
}

/// <summary>A bulk service installed in a scheme (brief §14 H13).</summary>
public enum BulkService
{
    /// <summary>Water reticulation.</summary>
    Water = 1,

    /// <summary>Sewer reticulation.</summary>
    Sewer = 2,

    /// <summary>Roads.</summary>
    Roads = 3,

    /// <summary>Electricity reticulation.</summary>
    Electricity = 4,

    /// <summary>Storm-water drainage.</summary>
    StormWater = 5,
}

/// <summary>
/// A progress certificate for one bulk service in a scheme (<c>hse.scheme_servicing_progress</c>, brief §14 H13): the
/// engineer's percentage complete and how many stands the service now reaches. A stand counts as serviced once every
/// service reaches it, so a scheme's serviced stands are the fewest any service reaches.
/// </summary>
public sealed class SchemeServicingProgress : AuditableEntity, ICompanyScoped
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private SchemeServicingProgress() => CertificateReference = string.Empty;

    /// <summary>Records progress.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="schemeId">The scheme.</param>
    /// <param name="service">The service.</param>
    /// <param name="date">The certificate date.</param>
    /// <param name="percentComplete">The engineer's percentage complete.</param>
    /// <param name="standsReached">How many stands the service reaches.</param>
    /// <param name="certificateReference">The progress certificate.</param>
    public SchemeServicingProgress(long companyId, long schemeId, BulkService service, DateOnly date, decimal percentComplete, int standsReached, string certificateReference)
    {
        CompanyId = companyId;
        SchemeId = schemeId;
        Service = service;
        Date = date;
        PercentComplete = percentComplete;
        StandsReached = standsReached;
        CertificateReference = certificateReference;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The scheme.</summary>
    public long SchemeId { get; private set; }

    /// <summary>The service.</summary>
    public BulkService Service { get; private set; }

    /// <summary>The certificate date.</summary>
    public DateOnly Date { get; private set; }

    /// <summary>The engineer's percentage complete.</summary>
    public decimal PercentComplete { get; private set; }

    /// <summary>How many stands the service reaches.</summary>
    public int StandsReached { get; private set; }

    /// <summary>The progress certificate.</summary>
    public string CertificateReference { get; private set; }
}

/// <summary>
/// A valuation of an undivided land parcel (<c>hse.land_valuation</c>, brief §14 H9): the valuer's open-market value,
/// the evidence for its net realisable value in the IAS 2 test. The latest valuation by a date applies.
/// </summary>
public sealed class LandValuation : AuditableEntity, ICompanyScoped
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private LandValuation() => Valuer = string.Empty;

    /// <summary>Records a valuation.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="landParcelId">The parcel.</param>
    /// <param name="date">The valuation date.</param>
    /// <param name="value">The open-market value.</param>
    /// <param name="valuer">Who valued it.</param>
    public LandValuation(long companyId, long landParcelId, DateOnly date, decimal value, string valuer)
    {
        CompanyId = companyId;
        LandParcelId = landParcelId;
        Date = date;
        Value = value;
        Valuer = valuer;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The parcel.</summary>
    public long LandParcelId { get; private set; }

    /// <summary>The valuation date.</summary>
    public DateOnly Date { get; private set; }

    /// <summary>The open-market value.</summary>
    public decimal Value { get; private set; }

    /// <summary>Who valued it.</summary>
    public string Valuer { get; private set; }
}
