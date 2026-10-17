using One4All.Core.Common;
using One4All.Core.Pos;

namespace One4All.Application.Abstractions.Pos;

/// <summary>A loyalty scheme's settings.</summary>
/// <param name="CompanyId">The company.</param>
/// <param name="PointsPerUnit">Points per currency unit spent.</param>
/// <param name="ValuePerPoint">What a point is worth when redeemed.</param>
/// <param name="ExpiryMonths">How long points last (null: no expiry).</param>
/// <param name="LiabilityAccountCode">The points liability account.</param>
/// <param name="ExpenseAccountCode">The loyalty cost account.</param>
/// <param name="Tiers">Tiers: name, points earned in twelve months, earn multiplier.</param>
public sealed record LoyaltySchemeRequest(long CompanyId, decimal PointsPerUnit, decimal ValuePerPoint, int? ExpiryMonths, string LiabilityAccountCode, string ExpenseAccountCode,
    IReadOnlyList<(string Name, decimal MinPoints, decimal Multiplier)> Tiers);

/// <summary>Points earned on a purchase.</summary>
/// <param name="Points">The points.</param>
/// <param name="Tier">The member's tier when earning.</param>
/// <param name="Balance">The balance after.</param>
public readonly record struct LoyaltyEarned(decimal Points, string? Tier, decimal Balance);

/// <summary>A member's statement.</summary>
/// <param name="MemberNumber">The member number.</param>
/// <param name="Name">The name.</param>
/// <param name="Tier">The current tier.</param>
/// <param name="Balance">The points balance.</param>
/// <param name="BalanceValue">What the balance is worth.</param>
/// <param name="Movements">Every movement, oldest first.</param>
public sealed record LoyaltyStatement(string MemberNumber, string Name, string? Tier, decimal Balance, decimal BalanceValue, IReadOnlyList<LoyaltyMovement> Movements);

/// <summary>
/// The loyalty programme (brief §4.1): members earn points on what they spend (times their tier's multiplier), redeem them - the value is
/// tendered at the till on the scheme's liability account - and lose them at expiry, oldest first. The value of points issued is kept as
/// a liability (Dr loyalty cost, Cr liability on earning; reversed on expiry). Member pricing is a member-only promotion.
/// </summary>
public interface ILoyaltyService
{
    /// <summary>Sets up (or changes) the company's scheme.</summary>
    /// <param name="request">The settings.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The scheme id.</returns>
    Task<Result<long>> ConfigureAsync(LoyaltySchemeRequest request, CancellationToken ct = default);

    /// <summary>Enrols a member (<c>Loyalty.MemberExists</c> for a number in use).</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="memberNumber">The card / member number.</param>
    /// <param name="name">The name.</param>
    /// <param name="customerId">The customer account, if any.</param>
    /// <param name="date">The joining date.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The member id.</returns>
    Task<Result<long>> EnrolAsync(long companyId, string memberNumber, string name, long? customerId, DateOnly date, CancellationToken ct = default);

    /// <summary>Awards points on a purchase and books their value as a liability.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="memberNumber">The member.</param>
    /// <param name="spend">The amount spent.</param>
    /// <param name="date">The date.</param>
    /// <param name="reference">The sale.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The points earned.</returns>
    Task<Result<LoyaltyEarned>> EarnAsync(long companyId, string memberNumber, decimal spend, DateOnly date, string reference, CancellationToken ct = default);

    /// <summary>
    /// Redeems points, returning their value to tender at the till on the scheme's liability account; refused beyond the balance
    /// (<c>Loyalty.InsufficientPoints</c>).
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="memberNumber">The member.</param>
    /// <param name="points">The points.</param>
    /// <param name="date">The date.</param>
    /// <param name="reference">The sale.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The value redeemed.</returns>
    Task<Result<decimal>> RedeemAsync(long companyId, string memberNumber, decimal points, DateOnly date, string reference, CancellationToken ct = default);

    /// <summary>Expires every member's points past their expiry, oldest first, releasing the liability (Dr liability, Cr loyalty cost).</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="asAt">The date.</param>
    /// <param name="documentNumber">The expiry journal's document number.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The points and value expired.</returns>
    Task<Result<(int Members, decimal Points, decimal Value)>> ExpireAsync(long companyId, DateOnly asAt, string documentNumber, CancellationToken ct = default);

    /// <summary>A member's statement.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="memberNumber">The member.</param>
    /// <param name="asAt">The date the tier is judged at.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The statement.</returns>
    Task<Result<LoyaltyStatement>> StatementAsync(long companyId, string memberNumber, DateOnly asAt, CancellationToken ct = default);
}
