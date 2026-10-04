using One4All.Application.Housing;
using One4All.Core.Common;
using One4All.Core.Housing;

namespace One4All.Application.Abstractions.Housing;

/// <summary>A request to let a unit.</summary>
public sealed class LetUnitRequest
{
    /// <summary>The council.</summary>
    public long CompanyId { get; init; }

    /// <summary>The unit code.</summary>
    public string UnitCode { get; init; } = string.Empty;

    /// <summary>The tenant's customer account.</summary>
    public long TenantCustomerId { get; init; }

    /// <summary>The first day of the tenancy.</summary>
    public DateOnly StartDate { get; init; }

    /// <summary>The deposit taken.</summary>
    public decimal Deposit { get; init; }

    /// <summary>The bank account the deposit is banked to.</summary>
    public string BankAccountCode { get; init; } = string.Empty;

    /// <summary>The liability account the deposit is held on.</summary>
    public string DepositAccountCode { get; init; } = string.Empty;
}

/// <summary>A rent run's result.</summary>
/// <param name="TenanciesBilled">Tenancies billed for the month.</param>
/// <param name="TotalRent">The rent raised.</param>
public sealed record RentRunResult(int TenanciesBilled, decimal TotalRent);

/// <summary>A tenancy ended.</summary>
/// <param name="FinalRent">Rent raised at the end for months not yet billed.</param>
/// <param name="Settlement">How the deposit was applied and refunded.</param>
public sealed record TenancyEnded(decimal FinalRent, DepositSettlement Settlement);

/// <summary>A void unit.</summary>
/// <param name="UnitCode">The unit.</param>
/// <param name="VacantSince">The first empty day.</param>
/// <param name="DaysVoid">Days void to the date.</param>
/// <param name="RentLost">Rent lost to the date.</param>
public sealed record VoidUnit(string UnitCode, DateOnly VacantSince, int DaysVoid, decimal RentLost);

/// <summary>
/// Council rental housing (brief §4.10.7): the unit register; tenancies with the deposit held as a liability; the
/// monthly rent debit run, pro rata for part months and never billing a month twice; ending a tenancy — final rent, the
/// deposit applied to arrears and the rest refunded; and the voids report with the rent lost.
/// </summary>
public interface IRentalHousingService
{
    /// <summary>Registers a rental unit, vacant from a date.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="code">The unit code (unique).</param>
    /// <param name="description">The address or description.</param>
    /// <param name="unitType">The kind of unit.</param>
    /// <param name="rooms">The number of rooms.</param>
    /// <param name="monthlyRent">The monthly rent.</param>
    /// <param name="rentAccountCode">The rental income account.</param>
    /// <param name="vacantSince">The first empty day.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The unit, or why it was refused.</returns>
    Task<Result<long>> AddUnitAsync(long companyId, string code, string description, RentalUnitType unitType, int rooms, decimal monthlyRent, string rentAccountCode, DateOnly vacantSince, CancellationToken ct = default);

    /// <summary>Lets a vacant unit, banking the deposit as a liability (Dr bank / Cr deposits), in one unit.</summary>
    /// <param name="request">The letting.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The tenancy number, or why it was refused.</returns>
    Task<Result<string>> LetAsync(LetUnitRequest request, CancellationToken ct = default);

    /// <summary>Raises the month's rent on every tenancy covering it, pro rata, skipping months already billed.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="month">Any day in the month.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>What was raised.</returns>
    Task<Result<RentRunResult>> RunRentAsync(long companyId, DateOnly month, CancellationToken ct = default);

    /// <summary>
    /// Ends a tenancy: bills rent not yet raised up to the last day, applies the deposit to what the tenant owes and
    /// refunds the rest, and puts the unit back as vacant. One unit.
    /// </summary>
    /// <param name="companyId">The council.</param>
    /// <param name="tenancyNumber">The tenancy.</param>
    /// <param name="endDate">The last day.</param>
    /// <param name="refundBankAccountCode">The bank account any refund is paid from.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The final figures, or why it was refused.</returns>
    Task<Result<TenancyEnded>> EndTenancyAsync(long companyId, string tenancyNumber, DateOnly endDate, string refundBankAccountCode, CancellationToken ct = default);

    /// <summary>The void units as at a date, longest void first, with the rent lost.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="asAtDate">The date.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The voids.</returns>
    Task<IReadOnlyList<VoidUnit>> VoidsAsync(long companyId, DateOnly asAtDate, CancellationToken ct = default);
}
