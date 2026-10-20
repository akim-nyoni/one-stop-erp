using One4All.Core.Common;

namespace One4All.Core.Receipting;

/// <summary>
/// A third-party collection agent (brief §RCP-11, <c>rcp.agent</c>) - a shop, kiosk or aggregator that
/// collects on the council's behalf. Money the agent collects is held by the agent until remitted; the
/// running collected-minus-remitted balance is the agent's outstanding (the ageing of money not yet in).
/// </summary>
public sealed class Agent : SoftDeletableEntity, ICompanyScoped
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private Agent()
    {
        Code = string.Empty;
        Name = string.Empty;
    }

    /// <summary>Creates an agent.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="code">The agent code.</param>
    /// <param name="name">The agent name.</param>
    /// <param name="commissionPercent">The commission percentage deducted on remittance.</param>
    public Agent(long companyId, string code, string name, decimal commissionPercent)
    {
        CompanyId = companyId;
        Code = code.Trim().ToUpperInvariant();
        Name = name;
        CommissionPercent = commissionPercent;
        IsActive = true;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The agent code.</summary>
    public string Code { get; private set; }

    /// <summary>The agent name.</summary>
    public string Name { get; set; }

    /// <summary>The commission percentage deducted on remittance.</summary>
    public decimal CommissionPercent { get; private set; }

    /// <summary>Whether the agent is active.</summary>
    public bool IsActive { get; set; }

    /// <summary>The gross collected to date.</summary>
    public decimal CollectedToDate { get; private set; }

    /// <summary>The gross remitted to date.</summary>
    public decimal RemittedToDate { get; private set; }

    /// <summary>The gross held by the agent and not yet remitted (the ageing figure).</summary>
    public decimal Outstanding => CollectedToDate - RemittedToDate;

    /// <summary>Records a collection made by the agent.</summary>
    /// <param name="amount">The gross amount collected.</param>
    public void RecordCollection(decimal amount) => CollectedToDate += amount;

    /// <summary>Records a remittance received from the agent.</summary>
    /// <param name="grossAmount">The gross amount remitted (before commission).</param>
    public void RecordRemittance(decimal grossAmount) => RemittedToDate += grossAmount;

    /// <summary>Changes the commission deducted on later remittances (remittances already taken stand).</summary>
    /// <param name="commissionPercent">The commission percentage.</param>
    public void ChangeCommission(decimal commissionPercent) => CommissionPercent = commissionPercent;
}
