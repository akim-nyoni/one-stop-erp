using One4All.Core.Common;
using One4All.Core.Housing;

namespace One4All.Application.Abstractions.Trading;

/// <summary>
/// Receivables control accounts beyond the company's main one (its lowest-numbered receivables control): a customer can post to a dedicated
/// control - instalment debtors per tenure type (brief §4.10.9) - and the control reconciliation then matches each control to its own customers.
/// </summary>
public interface IReceivableControlService
{
    /// <summary>
    /// Sets (or, blank, clears) a customer's own receivables control account: it must be a receivables control, the customer must trade in the
    /// base currency, and it can change only while the customer has no items.
    /// </summary>
    Task<Result> SetCustomerControlAsync(long companyId, string customerCode, string? accountCode, CancellationToken ct = default);

    /// <summary>Sets (or, blank, removes) the receivables control account a tenure type's instalment debtors post to.</summary>
    Task<Result> SetTenureControlAsync(long companyId, InstalmentTenure tenure, string? accountCode, CancellationToken ct = default);

    /// <summary>The tenure types' control accounts.</summary>
    Task<IReadOnlyList<(InstalmentTenure Tenure, string AccountCode)>> TenureControlsAsync(long companyId, CancellationToken ct = default);
}
