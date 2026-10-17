using One4All.Core.Accounting;
using One4All.Core.Common;

namespace One4All.Application.Abstractions.Trading;

/// <summary>
/// Manages recurring sales-invoice templates and the billing run that turns them into real AR invoices
/// (subscriptions, retainers, rent). Templates hold no balance; a run posts through <see cref="ISalesLedgerService"/>.
/// </summary>
public interface IRecurringSalesInvoiceService
{
    /// <summary>Creates an active recurring-invoice template.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="customerId">The customer billed.</param>
    /// <param name="reference">The template reference (invoice-number stem).</param>
    /// <param name="currency">The billing currency.</param>
    /// <param name="netAmount">The net amount billed each period (positive).</param>
    /// <param name="taxAmount">The tax amount billed each period (non-negative).</param>
    /// <param name="salesAccountCode">The revenue account credited.</param>
    /// <param name="taxAccountCode">The output-tax account, if tax applies.</param>
    /// <param name="intervalMonths">The billing interval in months (positive).</param>
    /// <param name="firstRunDate">The date the first invoice falls due.</param>
    /// <param name="narrative">An optional narrative for each invoice.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The created template id.</returns>
    Task<Result<long>> CreateAsync(long companyId, long customerId, string reference, CurrencyCode currency, decimal netAmount, decimal taxAmount, string salesAccountCode, string? taxAccountCode, int intervalMonths, DateOnly firstRunDate, string? narrative, CancellationToken ct = default);

    /// <summary>
    /// Runs billing as at a date: for every active template whose next run has fallen due on or before
    /// <paramref name="asOf"/>, posts an AR invoice (dated the run date, numbered reference-yyyyMM) and advances
    /// the template by its interval - catching up multiple periods if the template is behind.
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
