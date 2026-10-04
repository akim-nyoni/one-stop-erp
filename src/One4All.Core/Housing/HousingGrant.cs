using One4All.Core.Common;

namespace One4All.Core.Housing;

/// <summary>
/// A government grant or housing subsidy (<c>hse.housing_grant</c>, brief §4.10.9 / IAS 20): received into deferred
/// income, recognised in line with the expenditure it compensates once its conditions are reasonably assured, with any
/// unfulfilled conditions disclosed.
/// </summary>
public sealed class HousingGrant : AuditableEntity, ICompanyScoped
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private HousingGrant()
    {
        GrantNumber = string.Empty;
        Grantor = string.Empty;
        Conditions = string.Empty;
        DeferredAccountCode = string.Empty;
        IncomeAccountCode = string.Empty;
    }

    /// <summary>Records a grant received.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="grantNumber">The grant number.</param>
    /// <param name="grantor">Who gave it.</param>
    /// <param name="schemeId">The scheme it funds, if any.</param>
    /// <param name="amount">The amount received.</param>
    /// <param name="budgetedExpenditure">The expenditure it is meant to fund.</param>
    /// <param name="conditions">Its conditions.</param>
    /// <param name="receivedOn">When it was received.</param>
    /// <param name="deferredAccountCode">The deferred grant income (liability) account.</param>
    /// <param name="incomeAccountCode">The grant income account.</param>
    public HousingGrant(long companyId, string grantNumber, string grantor, long? schemeId, decimal amount, decimal budgetedExpenditure, string conditions,
        DateOnly receivedOn, string deferredAccountCode, string incomeAccountCode)
    {
        CompanyId = companyId;
        GrantNumber = grantNumber;
        Grantor = grantor;
        SchemeId = schemeId;
        Amount = amount;
        BudgetedExpenditure = budgetedExpenditure;
        Conditions = conditions;
        ReceivedOn = receivedOn;
        DeferredAccountCode = deferredAccountCode;
        IncomeAccountCode = incomeAccountCode;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The grant number.</summary>
    public string GrantNumber { get; private set; }

    /// <summary>Who gave it.</summary>
    public string Grantor { get; private set; }

    /// <summary>The scheme it funds, if any.</summary>
    public long? SchemeId { get; private set; }

    /// <summary>The amount received.</summary>
    public decimal Amount { get; private set; }

    /// <summary>The expenditure it is meant to fund.</summary>
    public decimal BudgetedExpenditure { get; private set; }

    /// <summary>Its conditions.</summary>
    public string Conditions { get; private set; }

    /// <summary>When it was received.</summary>
    public DateOnly ReceivedOn { get; private set; }

    /// <summary>The deferred grant income (liability) account.</summary>
    public string DeferredAccountCode { get; private set; }

    /// <summary>The grant income account.</summary>
    public string IncomeAccountCode { get; private set; }

    /// <summary>When the conditions were assessed as reasonably assured, if they have been.</summary>
    public DateOnly? ConditionsAssuredOn { get; private set; }

    /// <summary>The expenditure incurred to date that the grant compensates.</summary>
    public decimal ExpenditureToDate { get; private set; }

    /// <summary>The income recognised to date.</summary>
    public decimal RecognisedToDate { get; private set; }

    /// <summary>The grant still held in deferred income.</summary>
    public decimal Deferred => Amount - RecognisedToDate;

    /// <summary>Records that the conditions are reasonably assured.</summary>
    /// <param name="date">When.</param>
    public void AssureConditions(DateOnly date) => ConditionsAssuredOn = date;

    /// <summary>Records the expenditure to date and the income recognised as a result.</summary>
    /// <param name="expenditureToDate">The expenditure incurred to date.</param>
    /// <param name="recognisedToDate">The income recognised to date.</param>
    public void Recognise(decimal expenditureToDate, decimal recognisedToDate)
    {
        ExpenditureToDate = expenditureToDate;
        RecognisedToDate = recognisedToDate;
    }
}
