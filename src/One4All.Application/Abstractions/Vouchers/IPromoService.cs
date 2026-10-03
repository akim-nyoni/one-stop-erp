using One4All.Core.Common;
using One4All.Core.Vouchers;

namespace One4All.Application.Abstractions.Vouchers;

/// <summary>A new promotion campaign and its codes.</summary>
/// <param name="Code">The campaign code.</param>
/// <param name="Name">The campaign name.</param>
/// <param name="DiscountType">Percentage or amount off.</param>
/// <param name="DiscountValue">The percentage or amount.</param>
/// <param name="ValidFrom">The first day codes work.</param>
/// <param name="ValidTo">The last day codes work.</param>
/// <param name="MinimumSpend">The smallest spend a code applies to.</param>
/// <param name="UsesPerCode">How many times each code may be used (1 for single-use).</param>
/// <param name="MaxRedemptions">The cap across the campaign, or null for none.</param>
/// <param name="Codes">The codes to issue.</param>
public sealed record PromoCampaignRequest(string Code, string Name, PromoDiscountType DiscountType, decimal DiscountValue, DateOnly ValidFrom, DateOnly ValidTo,
    decimal MinimumSpend, int UsesPerCode, int? MaxRedemptions, IReadOnlyList<string> Codes);

/// <summary>What a code would give on a spend.</summary>
/// <param name="Code">The code.</param>
/// <param name="CampaignCode">Its campaign.</param>
/// <param name="Discount">The discount.</param>
public sealed record PromoQuote(string Code, string CampaignCode, decimal Discount);

/// <summary>A campaign's redemption analysis.</summary>
/// <param name="CampaignCode">The campaign.</param>
/// <param name="CodesIssued">Codes issued.</param>
/// <param name="CodesUsed">Codes used at least once.</param>
/// <param name="Redemptions">Redemptions.</param>
/// <param name="Spend">Spend the codes were used on.</param>
/// <param name="Discount">Discount given.</param>
public sealed record PromoAnalysis(string CampaignCode, int CodesIssued, int CodesUsed, int Redemptions, decimal Spend, decimal Discount);

/// <summary>
/// Discount and promotion vouchers (brief §4.8): campaigns with codes, validity windows, a minimum spend, per-code and
/// campaign-wide redemption caps, redemption against a sale, and redemption analysis. Eligibility by customer group or
/// item category is not yet applied.
/// </summary>
public interface IPromoService
{
    /// <summary>Creates a campaign and issues its codes.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="request">The campaign.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The campaign id, or why it was refused.</returns>
    Task<Result<long>> CreateCampaignAsync(long companyId, PromoCampaignRequest request, CancellationToken ct = default);

    /// <summary>What a code would give on a spend at a date, without using it.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="code">The code.</param>
    /// <param name="date">The date.</param>
    /// <param name="spend">The spend.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The discount, or why the code does not apply.</returns>
    Task<Result<PromoQuote>> QuoteAsync(long companyId, string code, DateOnly date, decimal spend, CancellationToken ct = default);

    /// <summary>Uses a code on a sale, recording the redemption; checks as for a quote and the caps under a lock.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="code">The code.</param>
    /// <param name="date">The date.</param>
    /// <param name="spend">The spend.</param>
    /// <param name="reference">The sale or invoice.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The discount given, or why the code does not apply.</returns>
    Task<Result<PromoQuote>> RedeemAsync(long companyId, string code, DateOnly date, decimal spend, string reference, CancellationToken ct = default);

    /// <summary>A campaign's redemption analysis.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="campaignId">The campaign.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The analysis, or why there is none.</returns>
    Task<Result<PromoAnalysis>> AnalysisAsync(long companyId, long campaignId, CancellationToken ct = default);
}
