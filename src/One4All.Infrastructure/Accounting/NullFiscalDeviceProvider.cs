using One4All.Application.Abstractions.Accounting;
using One4All.Core.Common;

namespace One4All.Infrastructure.Accounting;

/// <summary>
/// The default no-op fiscal device provider (brief §3.5). Used where no fiscal device / e-invoicing API is
/// configured: it succeeds without signing anything. A country-specific provider replaces this via DI.
/// </summary>
public sealed class NullFiscalDeviceProvider : IFiscalDeviceProvider
{
    /// <inheritdoc />
    public bool IsEnabled => false;

    /// <inheritdoc />
    public Task<Result<int>> OpenDayAsync(long companyId, CancellationToken ct = default) =>
        Task.FromResult(Result.Success(0));

    /// <inheritdoc />
    public Task<Result<FiscalReceiptResult>> FiscaliseAsync(FiscalReceiptRequest request, CancellationToken ct = default) =>
        Task.FromResult(Result.Success(new FiscalReceiptResult(null, null, null, Queued: false)));

    /// <inheritdoc />
    public Task<Result> CloseDayAsync(long companyId, CancellationToken ct = default) =>
        Task.FromResult(Result.Success());
}
