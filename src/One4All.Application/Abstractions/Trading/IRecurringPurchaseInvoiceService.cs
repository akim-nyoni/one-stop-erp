using One4All.Core.Accounting;
using One4All.Core.Common;

namespace One4All.Application.Abstractions.Trading;

/// <summary>
/// Manages recurring purchase-invoice templates and the run that turns them into real AP invoices (rent,
/// subscriptions, service contracts). Templates hold no balance; a run posts through <see cref="IPurchaseLedgerService"/>.
/// </summary>
public interface IRecurringPurchaseInvoiceService
{
    /// <summary>Creates an active recurring purchase-invoice template.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="supplierId">The supplier billed from.</param>
    /// <param name="reference">The template reference (invoice-number stem).</param>
    /// <param name="currency">The billing currency.</param>
    /// <param name="netAmount">The net amount each period (positive).</param>
    /// <param name="taxAmount">The tax amount each period (non-negative).</param>
    /// <param name="expenseAccountCode">The expense account debited.</param>
    /// <param name="taxAccountCode">The input-tax account, if tax applies.</param>
    /// <param name="intervalMonths">The billing interval in months (positive).</param>
    /// <param name="firstRunDate">The date the first invoice falls due.</param>
    /// <param name="narrative">An optional narrative for each invoice.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The created template id.</returns>
    Task<Result<long>> CreateAsync(long companyId, long supplierId, string reference, CurrencyCode currency, decimal netAmount, decimal taxAmount, string expenseAccountCode, string? taxAccountCode, int intervalMonths, DateOnly firstRunDate, string? narrative, CancellationToken ct = default);

    /// <summary>
    /// Runs billing as at a date: for every active template whose next run has fallen due on or before
    /// <paramref name="asOf"/>, posts a supplier invoice (dated the run date, numbered reference-yyyyMM) and
    /// advances the template by its interval - catching up multiple periods if the template is behind.
    /// </summary>
    /// <param name="companyId">The company.</param>
    /// <param name="asOf">The billing cut-off date.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The number of invoices posted.</returns>
    Task<Result<int>> GenerateDueAsync(long companyId, DateOnly asOf, CancellationToken ct = default);

    /// <summary>Activates or suspends a template.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="templateId">The template.</param>
    /// <param name="active">Whether the template should be active.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<Result> SetActiveAsync(long companyId, long templateId, bool active, CancellationToken ct = default);
}
