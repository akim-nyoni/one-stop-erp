using One4All.Application.Housing;
using One4All.Core.Common;

namespace One4All.Application.Abstractions.Housing;

/// <summary>A unit on the sectional register.</summary>
/// <param name="UnitNumber">The unit.</param>
/// <param name="FloorArea">Its floor area.</param>
/// <param name="ParticipationQuota">Its participation quota (%).</param>
/// <param name="Owner">Its current owner, if any.</param>
public sealed record SectionalUnitEntry(string UnitNumber, decimal FloorArea, decimal ParticipationQuota, string? Owner);

/// <summary>A body corporate levy run.</summary>
/// <param name="Period">The period (yyyyMM).</param>
/// <param name="Billed">Each owned unit's levy, invoiced.</param>
/// <param name="Unbilled">The levy falling on units with no owner.</param>
public sealed record SectionalLevyResult(int Period, IReadOnlyList<UnitAmount> Billed, decimal Unbilled);

/// <summary>
/// Sectional title (brief §4.10.6): a building on a stand registered as units with participation quotas from their floor
/// areas; units owned and transferred individually (refused while the stand is blocked); and the body corporate's levy
/// split by quota and invoiced to the unit owners once per period.
/// </summary>
public interface ISectionalTitleService
{
    /// <summary>Registers a scheme and its units; the quotas follow the floor areas.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="code">The scheme code.</param>
    /// <param name="name">The building's name.</param>
    /// <param name="standId">The stand it stands on.</param>
    /// <param name="units">The units and their floor areas.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The register, or why it was refused.</returns>
    Task<Result<IReadOnlyList<SectionalUnitEntry>>> RegisterAsync(long companyId, string code, string name, long standId, IReadOnlyList<UnitArea> units, CancellationToken ct = default);

    /// <summary>Transfers a unit to a new owner (its first owner, or the next).</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="code">The scheme.</param>
    /// <param name="unitNumber">The unit.</param>
    /// <param name="toCustomerId">The new owner.</param>
    /// <param name="consideration">The consideration.</param>
    /// <param name="date">From when.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success, or why it was refused.</returns>
    Task<Result> TransferUnitAsync(long companyId, string code, string unitNumber, long toCustomerId, decimal consideration, DateOnly date, CancellationToken ct = default);

    /// <summary>The scheme's units and current owners.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="code">The scheme.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The register, or why there is none.</returns>
    Task<Result<IReadOnlyList<SectionalUnitEntry>>> UnitsAsync(long companyId, string code, CancellationToken ct = default);

    /// <summary>Raises the body corporate levy for a period, split by quota and invoiced to the owners; once per period.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="code">The scheme.</param>
    /// <param name="period">The period (yyyyMM).</param>
    /// <param name="levy">The levy for the period.</param>
    /// <param name="incomeAccountCode">The levy income account.</param>
    /// <param name="date">The invoice date.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The split, or why it was refused.</returns>
    Task<Result<SectionalLevyResult>> RaiseLevyAsync(long companyId, string code, int period, decimal levy, string incomeAccountCode, DateOnly date, CancellationToken ct = default);
}
