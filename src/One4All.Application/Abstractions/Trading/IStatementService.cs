using One4All.Core.Common;

namespace One4All.Application.Abstractions.Trading;

/// <summary>Renders customer and supplier statements of account as printable text (brief §4.5/§4.6).</summary>
public interface IStatementService
{
    /// <summary>Renders a customer's open-item statement with an ageing footer.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="customerId">The customer.</param>
    /// <param name="asAt">The statement (and ageing) date.</param>
    /// <param name="width">The character width.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The statement text, or not-found.</returns>
    Task<Result<string>> RenderCustomerStatementTextAsync(long companyId, long customerId, DateOnly asAt, int width = 64, CancellationToken ct = default);

    /// <summary>Renders a supplier's open-item statement (what we owe them) with an ageing footer.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="supplierId">The supplier.</param>
    /// <param name="asAt">The statement (and ageing) date.</param>
    /// <param name="width">The character width.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The statement text, or not-found.</returns>
    Task<Result<string>> RenderSupplierStatementTextAsync(long companyId, long supplierId, DateOnly asAt, int width = 64, CancellationToken ct = default);
}
