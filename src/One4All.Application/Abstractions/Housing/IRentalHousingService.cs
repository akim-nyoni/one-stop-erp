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

/// <summary>A rent review's result.</summary>
/// <param name="UnitsReviewed">Units whose rent was raised.</param>
/// <param name="TenanciesReviewed">Running tenancies given a rent change.</param>
public sealed record RentReviewResult(int UnitsReviewed, int TenanciesReviewed);

/// <summary>A void unit.</summary>
/// <param name="UnitCode">The unit.</param>
/// <param name="VacantSince">The first empty day.</param>
/// <param name="DaysVoid">Days void to the date.</param>
/// <param name="RentLost">Rent lost to the date.</param>
public sealed record VoidUnit(string UnitCode, DateOnly VacantSince, int DaysVoid, decimal RentLost)
{
    /// <summary>Gets why it is empty.</summary>
    public string? Reason { get; init; }

    /// <summary>Gets the refurbishment work order (job) number.</summary>
    public string? RefurbishmentJob { get; init; }
}

/// <summary>
/// Council rental housing (brief §4.10.7): the unit register; tenancies with the deposit held as a liability; the
/// monthly rent debit run, pro rata for part months and never billing a month twice; ending a tenancy - final rent, the
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

    /// <summary>
    /// Runs a rent review: raises the rent of every unit (or every unit of one type) by a percentage from the first of a
    /// month, and gives each running tenancy on them a rent change from that date. Months before it keep the old rent.
    /// </summary>
    /// <param name="companyId">The council.</param>
    /// <param name="effectiveFrom">The first day of the month the new rents apply from.</param>
    /// <param name="percent">The escalation percentage.</param>
    /// <param name="unitType">Only this kind of unit, or null for all.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>What was reviewed, or why it was refused.</returns>
    Task<Result<RentReviewResult>> ReviewRentsAsync(long companyId, DateOnly effectiveFrom, decimal percent, RentalUnitType? unitType, CancellationToken ct = default);

    /// <summary>Sets a rent tariff (replacing one for the same unit type, suburb, category and date).</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="unitType">The kind of unit.</param>
    /// <param name="suburb">Only in this suburb, or null for any.</param>
    /// <param name="category">Only for this income category, or null for any.</param>
    /// <param name="effectiveFrom">The first of the month it applies from.</param>
    /// <param name="monthlyRent">The monthly rent.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success, or why it was refused.</returns>
    Task<Result> SetRentTariffAsync(long companyId, RentalUnitType unitType, string? suburb, RentIncomeCategory? category, DateOnly effectiveFrom, decimal monthlyRent, CancellationToken ct = default);

    /// <summary>Places a unit in a suburb, for the tariffs.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="unitCode">The unit.</param>
    /// <param name="suburb">The suburb, or null.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success, or why it was refused.</returns>
    Task<Result> SetUnitSuburbAsync(long companyId, string unitCode, string? suburb, CancellationToken ct = default);

    /// <summary>Records a tenant household's income category, for the tariffs.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="tenancyNumber">The tenancy.</param>
    /// <param name="category">The category, or null.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success, or why it was refused.</returns>
    Task<Result> SetIncomeCategoryAsync(long companyId, string tenancyNumber, RentIncomeCategory? category, CancellationToken ct = default);

    /// <summary>
    /// The tariff rent review run: from the first of a month, every unit's asking rent becomes its tariff (unit type and suburb) and every running tenancy
    /// whose tariff (adding the household's income category) differs from what it pays gets a rent change. Units and tenancies no tariff covers keep
    /// their rent; tenancies already changed from that date are left alone, so a rerun changes nothing.
    /// </summary>
    /// <param name="companyId">The council.</param>
    /// <param name="effectiveFrom">The first of the month.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>How many units and tenancies changed, or why it was refused.</returns>
    Task<Result<RentReviewResult>> ApplyRentTariffsAsync(long companyId, DateOnly effectiveFrom, CancellationToken ct = default);

    /// <summary>The void units as at a date, longest void first, with the rent lost.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="asAtDate">The date.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The voids.</returns>
    Task<IReadOnlyList<VoidUnit>> VoidsAsync(long companyId, DateOnly asAtDate, CancellationToken ct = default);
}
