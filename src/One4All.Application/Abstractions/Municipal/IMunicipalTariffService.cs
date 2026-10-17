using One4All.Core.Common;
using One4All.Core.Municipal;

namespace One4All.Application.Abstractions.Municipal;

/// <summary>A stepped block of a tariff being saved.</summary>
/// <param name="FromUnit">The block's lower bound (inclusive).</param>
/// <param name="ToUnit">Its upper bound (inclusive), or null for "and above".</param>
/// <param name="Rate">The per-unit rate in the block.</param>
public sealed record TariffBlockInput(decimal FromUnit, decimal? ToUnit, decimal Rate);

/// <summary>A seasonal per-unit rate of a tariff being saved.</summary>
/// <param name="FromMonth">The first month (1–12).</param>
/// <param name="ToMonth">The last month (1–12; across the year end when below the first).</param>
/// <param name="PerUnitRate">The per-unit rate in those months.</param>
public sealed record TariffSeasonInput(int FromMonth, int ToMonth, decimal PerUnitRate);

/// <summary>
/// A tariff version being saved (brief §4.9: effective-dated versions so historical bills can be recomputed and explained). Saving a code that exists
/// adds a version from the date given; what is left null is carried over from the version before it (its blocks and seasons too, unless given).
/// </summary>
/// <param name="Code">The tariff code.</param>
/// <param name="EffectiveFrom">The day this version takes effect.</param>
/// <param name="Name">The name (null: as before).</param>
/// <param name="ServiceType">The service (a new code only; a version keeps its service).</param>
/// <param name="TariffType">How the charge is worked out (a new code only).</param>
/// <param name="RevenueAccountCode">The revenue account (a new code only).</param>
/// <param name="FixedCharge">The fixed charge (null: as before).</param>
/// <param name="PerUnitRate">The per-unit rate (null: as before).</param>
/// <param name="MinimumCharge">The minimum charge (null: as before).</param>
/// <param name="RatePerValuationCents">Cents in the dollar of valuation (null: as before).</param>
/// <param name="RatePerSquareMetre">The rate per square metre (null: as before).</param>
/// <param name="Category">The consumer category (null: as before).</param>
/// <param name="Ward">The ward, or null for all (an empty string clears it).</param>
/// <param name="Blocks">The stepped blocks (null: as before).</param>
/// <param name="Seasons">The seasonal rates (null: as before).</param>
public sealed record TariffInput(string Code, DateOnly EffectiveFrom, string? Name = null, ServiceType? ServiceType = null, TariffType? TariffType = null, string? RevenueAccountCode = null,
    decimal? FixedCharge = null, decimal? PerUnitRate = null, decimal? MinimumCharge = null, decimal? RatePerValuationCents = null, decimal? RatePerSquareMetre = null, ConsumerCategory? Category = null,
    string? Ward = null, IReadOnlyList<TariffBlockInput>? Blocks = null, IReadOnlyList<TariffSeasonInput>? Seasons = null);

/// <summary>Creates tariffs and their effective-dated versions; what an approved tariff change and the tariff screen go through.</summary>
public interface IMunicipalTariffService
{
    /// <summary>Saves a tariff version: a new code with everything given, or a new version of an existing code from its date.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="input">The version.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The version's id, or why not (<c>Mun.TariffInvalid</c>, <c>Mun.TariffVersionExists</c>, <c>Mun.AccountNotFound</c>).</returns>
    Task<Result<long>> SaveVersionAsync(long companyId, TariffInput input, CancellationToken ct = default);

    /// <summary>Withdraws a version (it no longer bills; the version before it is in force again from that date).</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="code">The tariff.</param>
    /// <param name="effectiveFrom">The version's date.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success, or why not.</returns>
    Task<Result> WithdrawVersionAsync(long companyId, string code, DateOnly effectiveFrom, CancellationToken ct = default);
}
