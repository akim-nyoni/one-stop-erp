using One4All.Core.Common;
using One4All.Core.Housing;

namespace One4All.Application.Abstractions.Housing;

/// <summary>A run's result for one contract.</summary>
/// <param name="ContractNumber">The contract.</param>
/// <param name="Apportioned">The cost moved into stands this run.</param>
/// <param name="JournalNumber">The capitalising journal, when anything moved.</param>
public sealed record ServicingRunLine(string ContractNumber, decimal Apportioned, string? JournalNumber);

/// <summary>A stand's cost with the servicing apportioned to it.</summary>
/// <param name="StandNumber">The stand.</param>
/// <param name="Cost">Its capitalised cost now (land plus servicing).</param>
/// <param name="Servicing">The servicing apportioned to it.</param>
public sealed record StandCostEntry(string StandNumber, decimal Cost, decimal Servicing);

/// <summary>
/// Servicing cost per stand (brief §4.10.2 servicing projects, §4.10.10 job costing): a servicing contract linked to a
/// scheme; stand frontage and zoning weights recorded; and each run capitalising the contract's cost added since the last
/// run into the scheme's unsold stands - Dr stands available, Cr servicing WIP - on the chosen basis, so every stand
/// carries an auditable cost.
/// </summary>
public interface IServicingCostService
{
    /// <summary>Links a servicing contract to a scheme.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="schemeId">The scheme.</param>
    /// <param name="jobContractId">The contract.</param>
    /// <param name="basis">How its cost is shared.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success, or why it was refused.</returns>
    Task<Result> LinkAsync(long companyId, long schemeId, long jobContractId, ServicingBasis basis, CancellationToken ct = default);

    /// <summary>Records a stand's frontage and zoning weight.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="standId">The stand.</param>
    /// <param name="frontageMetres">Its road frontage.</param>
    /// <param name="zoningWeight">Its zoning weight.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success, or why it was refused.</returns>
    Task<Result> SetMeasuresAsync(long companyId, long standId, decimal? frontageMetres, decimal? zoningWeight, CancellationToken ct = default);

    /// <summary>Capitalises each linked contract's new cost into the scheme's unsold stands.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="schemeId">The scheme.</param>
    /// <param name="date">The posting date.</param>
    /// <param name="servicingWipAccountCode">The servicing WIP account the contractor's costs were posted to (credited).</param>
    /// <param name="standsAvailableAccountCode">The stands available account (debited).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>What moved, contract by contract, or why it was refused.</returns>
    Task<Result<IReadOnlyList<ServicingRunLine>>> ApportionAsync(long companyId, long schemeId, DateOnly date, string servicingWipAccountCode, string standsAvailableAccountCode, CancellationToken ct = default);

    /// <summary>Each stand of a scheme with its cost and the servicing in it.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="schemeId">The scheme.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The stands, by number.</returns>
    Task<IReadOnlyList<StandCostEntry>> CostPerStandAsync(long companyId, long schemeId, CancellationToken ct = default);
}
