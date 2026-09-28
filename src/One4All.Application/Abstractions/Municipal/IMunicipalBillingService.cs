using One4All.Core.Common;
using One4All.Core.Municipal;

namespace One4All.Application.Abstractions.Municipal;

/// <summary>The result of billing a property service.</summary>
/// <param name="Charge">The computed charge.</param>
/// <param name="OpenItemId">The AR open item raised on the ratepayer account.</param>
/// <param name="JournalNumber">The GL journal number.</param>
public readonly record struct BillingResult(decimal Charge, long OpenItemId, string JournalNumber);

/// <summary>
/// Municipal billing (brief §4.9): computes a service charge from the property's tariff (metered
/// consumption, valuation or flat) and raises it on the ratepayer's account through the AR ledger, so it
/// ages, reconciles and appears on the consumer statement like any receivable.
/// </summary>
public interface IMunicipalBillingService
{
    /// <summary>
    /// Records a meter reading, computing and storing the consumption since the previous reading.
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="meterId">The meter.</param>
    /// <param name="readingDate">The reading date.</param>
    /// <param name="reading">The new reading.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The consumption for the period, or an error.</returns>
    Task<Result<decimal>> RecordReadingAsync(long companyId, long meterId, DateOnly readingDate, decimal reading, CancellationToken ct = default);

    /// <summary>Bills one service of a property for a period, raising the charge on the ratepayer's AR account.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="propertyId">The property.</param>
    /// <param name="serviceType">The service to bill.</param>
    /// <param name="consumption">The metered consumption (0 for flat/valuation services).</param>
    /// <param name="invoiceNumber">The bill/invoice number.</param>
    /// <param name="date">The billing date.</param>
    /// <param name="taxAmount">Tax on the charge, if any.</param>
    /// <param name="taxAccountCode">The output-tax account, if there is tax.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The billing result, or an error.</returns>
    Task<Result<BillingResult>> BillServiceAsync(long companyId, long propertyId, ServiceType serviceType, decimal consumption, string invoiceNumber, DateOnly date, decimal taxAmount = 0m, string? taxAccountCode = null, CancellationToken ct = default);

    /// <summary>
    /// Runs a fixed-charge billing cycle for a non-metered service (e.g. rates or refuse): bills every
    /// property that has an active link to the service at its tariff's fixed charge (zero consumption),
    /// raising each on the ratepayer's account.
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="serviceType">The service to bill across all linked properties.</param>
    /// <param name="date">The billing date.</param>
    /// <param name="invoicePrefix">A prefix for the generated bill numbers.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The number of properties billed and the total charged, or an error.</returns>
    Task<Result<MunicipalBillingRunResult>> RunFixedChargeBillingAsync(long companyId, ServiceType serviceType, DateOnly date, string invoicePrefix, CancellationToken ct = default);
}

/// <summary>The result of a fixed-charge municipal billing run.</summary>
/// <param name="PropertiesBilled">The number of properties billed.</param>
/// <param name="TotalBilled">The total charge raised across all properties.</param>
public readonly record struct MunicipalBillingRunResult(int PropertiesBilled, decimal TotalBilled);
