using One4All.Core.Common;

namespace One4All.Core.Restaurant;

/// <summary>
/// Per-company Restaurant module policy (<c>res.policy</c>) - currently the automatic gratuity (service
/// charge) applied to large parties. One row per company. Operational configuration (no GL by itself).
/// </summary>
public sealed class RestaurantPolicy : AuditableEntity, ICompanyScoped
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private RestaurantPolicy() => ServiceChargeAccountCode = string.Empty;

    /// <summary>Creates a policy.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="autoGratuityMinParty">The party size at or above which auto-gratuity applies.</param>
    /// <param name="autoGratuityPercent">The gratuity percentage of the net (e.g. 10 for 10%).</param>
    /// <param name="serviceChargeAccountCode">The service-charge income account the gratuity books to.</param>
    public RestaurantPolicy(long companyId, int autoGratuityMinParty, decimal autoGratuityPercent, string serviceChargeAccountCode)
    {
        CompanyId = companyId;
        Set(autoGratuityMinParty, autoGratuityPercent, serviceChargeAccountCode);
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The party size at or above which auto-gratuity applies (0 disables it).</summary>
    public int AutoGratuityMinParty { get; private set; }

    /// <summary>The gratuity percentage of the net (e.g. 10 for 10%).</summary>
    public decimal AutoGratuityPercent { get; private set; }

    /// <summary>The service-charge income account the gratuity books to.</summary>
    public string ServiceChargeAccountCode { get; private set; } = string.Empty;

    /// <summary>Gets or sets after how many minutes an open tab is flagged (0: never).</summary>
    public int TabWarningMinutes { get; set; }

    /// <summary>Gets or sets the tab total above which an open tab is flagged (0: never).</summary>
    public decimal TabWarningAmount { get; set; }

    /// <summary>Updates the policy values.</summary>
    /// <param name="minParty">The party-size threshold.</param>
    /// <param name="percent">The gratuity percentage.</param>
    /// <param name="serviceChargeAccountCode">The service-charge income account.</param>
    public void Set(int minParty, decimal percent, string serviceChargeAccountCode)
    {
        AutoGratuityMinParty = minParty < 0 ? 0 : minParty;
        AutoGratuityPercent = percent < 0m ? 0m : percent;
        ServiceChargeAccountCode = serviceChargeAccountCode;
    }
}
