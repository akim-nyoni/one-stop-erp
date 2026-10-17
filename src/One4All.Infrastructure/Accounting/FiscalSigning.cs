using One4All.Application.Abstractions.Accounting;
using One4All.Core.Common;

namespace One4All.Infrastructure.Accounting;

/// <summary>Signs a document with the fiscal device, never failing the caller: unreachable means queued for the retry task.</summary>
public static class FiscalSigning
{
    /// <summary>Signs the document.</summary>
    /// <param name="fiscal">The device (null or disabled: not required).</param>
    /// <param name="request">The document.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The code and verification when signed; null code and queued when it waits; null when no device is in use.</returns>
    public static async Task<FiscalReceiptResult?> SignAsync(IFiscalDeviceProvider? fiscal, FiscalReceiptRequest request, CancellationToken ct)
    {
        if (fiscal is not { IsEnabled: true })
        {
            return null;
        }

        try
        {
            Result<FiscalReceiptResult> result = await fiscal.FiscaliseAsync(request, ct).ConfigureAwait(false);
            return result.IsSuccess ? result.Value : new FiscalReceiptResult(null, null, null, Queued: true);
        }
        catch (Exception ex) when (ex is IOException or TimeoutException or HttpRequestException or InvalidOperationException)
        {
            return new FiscalReceiptResult(null, null, null, Queued: true);
        }
    }
}
