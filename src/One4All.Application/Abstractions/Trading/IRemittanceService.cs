using One4All.Application.Trading;
using One4All.Core.Common;

namespace One4All.Application.Abstractions.Trading;

/// <summary>Renders a supplier remittance advice (the invoices a payment settled) to a printable document.</summary>
public interface IRemittanceService
{
    /// <summary>Renders the remittance advice for a supplier payment to fixed-width plain text.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="paymentOpenItemId">The AP payment open item to remit.</param>
    /// <param name="width">The character width.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The remittance text, or NotFound when the payment does not exist.</returns>
    Task<Result<string>> RenderRemittanceTextAsync(long companyId, long paymentOpenItemId, int width = RemittanceFormatter.DefaultWidth, CancellationToken ct = default);
}
