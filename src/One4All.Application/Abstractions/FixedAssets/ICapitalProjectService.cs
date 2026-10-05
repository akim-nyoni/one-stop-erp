using One4All.Core.Common;

namespace One4All.Application.Abstractions.FixedAssets;

/// <summary>Capital work in progress (brief §14 M 211): projects collect cost until they are capitalised into the asset register.</summary>
public interface ICapitalProjectService
{
    /// <summary>Opens a capital project.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="code">The project code.</param>
    /// <param name="name">The name.</param>
    /// <param name="cwipAccountCode">The CWIP account.</param>
    /// <param name="budget">The approved budget.</param>
    /// <param name="openedOn">When it opens.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The project id, or an error.</returns>
    Task<Result<long>> OpenAsync(long companyId, string code, string name, string cwipAccountCode, decimal budget, DateOnly openedOn, CancellationToken ct = default);

    /// <summary>Charges a cost to a project: Dr CWIP, Cr the funding account (bank, payables, accrual).</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="projectId">The project.</param>
    /// <param name="date">The date.</param>
    /// <param name="amount">The amount.</param>
    /// <param name="fundingAccountCode">The account credited.</param>
    /// <param name="description">What it was.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The journal number, or an error.</returns>
    Task<Result<string>> AddCostAsync(long companyId, long projectId, DateOnly date, decimal amount, string fundingAccountCode, string description, CancellationToken ct = default);

    /// <summary>
    /// Capitalises a project: registers an asset at the project's whole cost funded from CWIP (Dr asset cost, Cr CWIP), so CWIP clears.
    /// The request's cost and funding account are taken from the project.
    /// </summary>
    /// <param name="projectId">The project.</param>
    /// <param name="asset">The asset to register (number, class, life, accounts, date).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The asset id, or an error.</returns>
    Task<Result<long>> CapitaliseAsync(long projectId, RegisterAssetRequest asset, CancellationToken ct = default);
}
