using One4All.Core.Common;

namespace One4All.Core.Housing;

/// <summary>When a housing stand becomes a billable municipal property (brief §4.10.10, configurable trigger).</summary>
public enum MunicipalLinkTrigger
{
    /// <summary>When the beneficiary accepts the allocation.</summary>
    AllocationAccepted = 1,

    /// <summary>When the stand is sold.</summary>
    Sale = 2,

    /// <summary>When title is registered.</summary>
    TitleRegistered = 3,
}

/// <summary>
/// A council's housing-to-municipal settings (<c>hse.municipal_link_settings</c>): at which event a stand becomes a
/// billable property, the ward it is placed in, and the tariffs whose services start from that date. One per council.
/// </summary>
public sealed class MunicipalLinkSettings : AuditableEntity, ICompanyScoped
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private MunicipalLinkSettings()
    {
        Ward = string.Empty;
        TariffIds = [];
    }

    /// <summary>Creates the settings.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="trigger">When stands are linked.</param>
    /// <param name="ward">The ward new properties are placed in.</param>
    /// <param name="tariffIds">The tariffs whose services start on linking.</param>
    public MunicipalLinkSettings(long companyId, MunicipalLinkTrigger trigger, string ward, List<long> tariffIds)
    {
        CompanyId = companyId;
        Trigger = trigger;
        Ward = ward;
        TariffIds = tariffIds;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>When stands are linked.</summary>
    public MunicipalLinkTrigger Trigger { get; private set; }

    /// <summary>The ward new properties are placed in.</summary>
    public string Ward { get; private set; }

    /// <summary>The tariffs whose services start on linking.</summary>
    public List<long> TariffIds { get; private set; }

    /// <summary>Changes the settings.</summary>
    /// <param name="trigger">When stands are linked.</param>
    /// <param name="ward">The ward.</param>
    /// <param name="tariffIds">The tariffs.</param>
    public void Change(MunicipalLinkTrigger trigger, string ward, List<long> tariffIds)
    {
        Trigger = trigger;
        Ward = ward;
        TariffIds = tariffIds;
    }
}
