using One4All.Core.Common;
using One4All.Core.Municipal;

namespace One4All.Application.Abstractions.Municipal;

/// <summary>A prepaid purchase.</summary>
public sealed class PrepaidVendRequest
{
    /// <summary>The council.</summary>
    public long CompanyId { get; init; }

    /// <summary>The property.</summary>
    public long PropertyId { get; init; }

    /// <summary>The prepaid service (electricity or water).</summary>
    public ServiceType ServiceType { get; init; }

    /// <summary>The amount tendered.</summary>
    public decimal Amount { get; init; }

    /// <summary>The sale date.</summary>
    public DateOnly Date { get; init; }

    /// <summary>The bank or cash account the money is banked to.</summary>
    public string BankAccountCode { get; init; } = string.Empty;

    /// <summary>The credit-off-arrears percentage (0 to 100).</summary>
    public decimal ArrearsPercent { get; init; }

    /// <summary>Where it was sold (counter, vendor, mobile).</summary>
    public string Channel { get; init; } = "Counter";
}

/// <summary>A completed prepaid sale.</summary>
/// <param name="SaleNumber">The sale number.</param>
/// <param name="Token">The token to key into the meter.</param>
/// <param name="Units">The units bought.</param>
/// <param name="ToArrears">The part taken towards arrears.</param>
/// <param name="ToUnits">The part that bought units.</param>
/// <param name="ArrearsAfter">What the account still owes.</param>
/// <param name="TestToken">True when the token came from the built-in test generator and no meter will accept it (no vendor generator is registered).</param>
public sealed record VendResult(string SaleNumber, string Token, decimal Units, decimal ToArrears, decimal ToUnits, decimal ArrearsAfter, bool TestToken = false);

/// <summary>A line of the prepaid sales report.</summary>
/// <param name="SaleNumber">The sale number.</param>
/// <param name="StandNumber">The stand.</param>
/// <param name="SoldOn">The sale date.</param>
/// <param name="Amount">The amount tendered.</param>
/// <param name="ToArrears">The part taken towards arrears.</param>
/// <param name="Units">The units bought.</param>
/// <param name="Token">The token issued.</param>
/// <param name="Channel">Where it was sold.</param>
public sealed record PrepaidSaleLine(string SaleNumber, string StandNumber, DateOnly SoldOn, decimal Amount, decimal ToArrears, decimal Units, string Token, string Channel);

/// <summary>
/// Prepaid vending (brief §4.9 prepaid hooks): sells units for a property's prepaid service, applies the
/// credit-off-arrears rule to the account, banks the money and issues a token through the token generator hook.
/// </summary>
public interface IPrepaidVendingService
{
    /// <summary>Sells units: the arrears part is receipted to the account, the rest is unit revenue; one unit.</summary>
    /// <param name="request">The purchase.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The sale, or why it was refused.</returns>
    Task<Result<VendResult>> VendAsync(PrepaidVendRequest request, CancellationToken ct = default);

    /// <summary>The prepaid sales over a period, in order.</summary>
    /// <param name="companyId">The council.</param>
    /// <param name="start">The first day.</param>
    /// <param name="end">The last day.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The sales.</returns>
    Task<IReadOnlyList<PrepaidSaleLine>> SalesAsync(long companyId, DateOnly start, DateOnly end, CancellationToken ct = default);
}
