using One4All.Application.Pos;
using One4All.Core.Common;

namespace One4All.Application.Abstractions.Pos;

/// <summary>Produces a printable receipt for a completed POS sale.</summary>
public interface IPosReceiptService
{
    /// <summary>Renders a stored sale to fixed-width plain-text receipt (thermal-friendly).</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="saleId">The sale to print.</param>
    /// <param name="width">The receipt character width.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The receipt text, or NotFound when the sale does not exist.</returns>
    Task<Result<string>> RenderReceiptTextAsync(long companyId, long saleId, int? width = null, CancellationToken ct = default);
}
