using One4All.Core.Common;

namespace One4All.Core.Crm;

/// <summary>The stage of a sales opportunity in the pipeline (brief §4.16).</summary>
public enum OpportunityStage
{
    /// <summary>A new lead.</summary>
    Lead = 1,

    /// <summary>Qualified.</summary>
    Qualified = 2,

    /// <summary>Proposal / quotation sent.</summary>
    Proposal = 3,

    /// <summary>Negotiation.</summary>
    Negotiation = 4,

    /// <summary>Won.</summary>
    Won = 5,

    /// <summary>Lost.</summary>
    Lost = 6,
}

/// <summary>
/// A CRM sales opportunity (brief §4.16, lightweight, <c>crm.opportunity</c>). Tracks the pipeline stage,
/// estimated value and win probability; the weighted value feeds a simple pipeline forecast.
/// </summary>
public sealed class Opportunity : SoftDeletableEntity, ICompanyScoped
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private Opportunity()
    {
        Name = string.Empty;
        ContactName = string.Empty;
    }

    /// <summary>Creates an opportunity.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="name">The opportunity name.</param>
    /// <param name="contactName">The contact name.</param>
    /// <param name="estimatedValue">The estimated value.</param>
    /// <param name="customerId">The linked customer, if any.</param>
    public Opportunity(long companyId, string name, string contactName, decimal estimatedValue, long? customerId = null)
    {
        CompanyId = companyId;
        Name = name;
        ContactName = contactName;
        EstimatedValue = estimatedValue;
        CustomerId = customerId;
        Stage = OpportunityStage.Lead;
        ProbabilityPercent = 10m;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The opportunity name.</summary>
    public string Name { get; set; }

    /// <summary>The contact name.</summary>
    public string ContactName { get; set; }

    /// <summary>The linked customer, if any.</summary>
    public long? CustomerId { get; set; }

    /// <summary>The estimated value.</summary>
    public decimal EstimatedValue { get; set; }

    /// <summary>Gets or sets the quotation sent for it.</summary>
    public long? QuotationId { get; set; }

    /// <summary>The current stage.</summary>
    public OpportunityStage Stage { get; private set; }

    /// <summary>The win probability (%).</summary>
    public decimal ProbabilityPercent { get; private set; }

    /// <summary>Gets or sets when the opportunity is expected to close.</summary>
    public DateOnly? ExpectedCloseOn { get; set; }

    /// <summary>Gets when it was won or lost.</summary>
    public DateOnly? ClosedOn { get; private set; }

    /// <summary>Gets why it was lost.</summary>
    public string? LostReason { get; private set; }

    /// <summary>Gets a value indicating whether it is won or lost.</summary>
    public bool IsClosed => Stage is OpportunityStage.Won or OpportunityStage.Lost;

    /// <summary>Closes the opportunity as won or lost on a date.</summary>
    /// <param name="won">Won or lost.</param>
    /// <param name="on">When.</param>
    /// <param name="lostReason">Why it was lost.</param>
    public void Close(bool won, DateOnly on, string? lostReason)
    {
        MoveTo(won ? OpportunityStage.Won : OpportunityStage.Lost);
        ClosedOn = on;
        LostReason = won ? null : lostReason;
    }

    /// <summary>Whether the opportunity is still open (not won or lost).</summary>
    public bool IsOpen => Stage is not (OpportunityStage.Won or OpportunityStage.Lost);

    /// <summary>The probability-weighted pipeline value (zero once closed lost).</summary>
    public decimal WeightedValue => Stage == OpportunityStage.Lost ? 0m : decimal.Round(EstimatedValue * ProbabilityPercent / 100m, 4, MidpointRounding.ToEven);

    /// <summary>Advances the opportunity to a stage, adjusting the default probability.</summary>
    /// <param name="stage">The new stage.</param>
    /// <param name="probabilityPercent">An explicit probability, or null to use the stage default.</param>
    public void MoveTo(OpportunityStage stage, decimal? probabilityPercent = null)
    {
        Stage = stage;
        ProbabilityPercent = probabilityPercent ?? stage switch
        {
            OpportunityStage.Lead => 10m,
            OpportunityStage.Qualified => 25m,
            OpportunityStage.Proposal => 50m,
            OpportunityStage.Negotiation => 75m,
            OpportunityStage.Won => 100m,
            _ => 0m,
        };
    }
}
