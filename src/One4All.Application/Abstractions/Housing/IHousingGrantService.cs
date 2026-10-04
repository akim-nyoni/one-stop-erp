using One4All.Core.Common;

namespace One4All.Application.Abstractions.Housing;

/// <summary>A grant received.</summary>
public sealed class GrantReceipt
{
    /// <summary>The council.</summary>
    public long CompanyId { get; init; }

    /// <summary>Who gave it.</summary>
    public string Grantor { get; init; } = string.Empty;

    /// <summary>The scheme it funds, if any.</summary>
    public long? SchemeId { get; init; }

    /// <summary>The amount received.</summary>
    public decimal Amount { get; init; }

    /// <summary>The expenditure it is meant to fund.</summary>
    public decimal BudgetedExpenditure { get; init; }

    /// <summary>Its conditions.</summary>
    public string Conditions { get; init; } = string.Empty;

    /// <summary>When it was received.</summary>
    public DateOnly ReceivedOn { get; init; }

    /// <summary>The bank account it was received into.</summary>
    public string BankAccountCode { get; init; } = string.Empty;

    /// <summary>The deferred grant income (liability) account.</summary>
    public string DeferredAccountCode { get; init; } = string.Empty;

    /// <summary>The grant income account.</summary>
    public string IncomeAccountCode { get; init; } = string.Empty;
}

/// <summary>A grant on the disclosure schedule.</summary>
/// <param name="GrantNumber">The grant.</param>
/// <param name="Grantor">Who gave it.</param>
/// <param name="Amount">The amount received.</param>
/// <param name="Recognised">The income recognised to date.</param>
/// <param name="Deferred">The amount still deferred.</param>
/// <param name="ConditionsAssured">Whether the conditions are reasonably assured.</param>
/// <param name="Conditions">The conditions (disclosed when unfulfilled).</param>
public sealed record GrantDisclosure(string GrantNumber, string Grantor, decimal Amount, decimal Recognised, decimal Deferred, bool ConditionsAssured, string Conditions);

/// <summary>
/// Housing grants and subsidies under IAS 20 (brief §4.10.9): received into deferred income (Dr bank / Cr deferred);
/// recognised in line with the expenditure they compensate once their conditions are reasonably assured, posting only
/// the movement (Dr deferred / Cr income); and the disclosure of amounts deferred and unfulfilled conditions.
/// </summary>
public interface IHousingGrantService
{
    /// <summary>Records a grant received into deferred income, in one unit.</summary>
    /// <param name="receipt">The grant.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The grant number, or why it was refused.</returns>
    Task<Result<string>> ReceiveAsync(GrantReceipt receipt, CancellationToken ct = default);

    /// <summary>Records that a grant's conditions are reasonably assured, recognising any income already earned.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="grantNumber">The grant.</param>
    /// <param name="date">When.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The income recognised to date, or why it was refused.</returns>
    Task<Result<decimal>> AssureConditionsAsync(long companyId, string grantNumber, DateOnly date, CancellationToken ct = default);

    /// <summary>Records the expenditure incurred to date and recognises the income it earns (the movement only).</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="grantNumber">The grant.</param>
    /// <param name="expenditureToDate">The expenditure incurred to date (cumulative).</param>
    /// <param name="date">When.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The income recognised to date, or why it was refused.</returns>
    Task<Result<decimal>> RecordExpenditureAsync(long companyId, string grantNumber, decimal expenditureToDate, DateOnly date, CancellationToken ct = default);

    /// <summary>The disclosure schedule: every grant with amounts deferred and recognised, unfulfilled conditions shown.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The grants.</returns>
    Task<IReadOnlyList<GrantDisclosure>> DisclosureAsync(long companyId, CancellationToken ct = default);
}
