using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using One4All.Application.Abstractions.Accounting;
using One4All.Core.Common;

namespace One4All.Infrastructure.Accounting;

/// <summary>
/// A fiscal device simulator (brief §3.5) for training and testing tills without a revenue-authority device: it keeps a fiscal day per
/// company, numbers receipts within the day and signs each with a hash a verification QR would carry. Switched on with
/// <c>ONE4ALL_FISCAL_SIMULATE=1</c>. A real device (e.g. a FDMS integration) implements <see cref="IFiscalDeviceProvider"/> the same way.
/// </summary>
public sealed class SimulatedFiscalDeviceProvider : IFiscalDeviceProvider
{
    private readonly ConcurrentDictionary<long, (int Day, int Receipt, bool Open)> _days = new();

    /// <inheritdoc />
    public bool IsEnabled => true;

    /// <summary>When true, the device behaves as if unreachable (fiscalisation is queued), for testing the retry path.</summary>
    public bool Offline { get; set; }

    /// <inheritdoc />
    public Task<Result<int>> OpenDayAsync(long companyId, CancellationToken ct = default)
    {
        if (Offline)
        {
            return Task.FromResult<Result<int>>(Error.Failure("Fiscal.Offline", "The fiscal device cannot be reached."));
        }

        (int day, int receipt, bool open) = _days.AddOrUpdate(companyId, _ => (1, 0, true), (_, d) => d.Open ? d : (d.Day + 1, 0, true));
        return Task.FromResult(Result.Success(day));
    }

    /// <inheritdoc />
    public Task<Result<FiscalReceiptResult>> FiscaliseAsync(FiscalReceiptRequest request, CancellationToken ct = default)
    {
        if (Offline)
        {
            return Task.FromResult(Result.Success(new FiscalReceiptResult(null, null, null, Queued: true)));
        }

        (int day, int receipt, bool _) = _days.AddOrUpdate(request.CompanyId, _ => (1, 1, true), (_, d) => (d.Open ? d.Day : d.Day + 1, (d.Open ? d.Receipt : 0) + 1, true));
        string code = $"SIM-{day:D4}-{receipt:D6}";
        string payload = string.Create(CultureInfo.InvariantCulture, $"{request.CompanyId}|{request.DocumentNumber}|{request.TotalGross:0.00}|{request.TotalTax:0.00}|{request.Currency}|{code}");
        string signature = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)))[..32];
        return Task.FromResult(Result.Success(new FiscalReceiptResult(code, $"https://verify.example/fiscal?c={code}&s={signature}", day, Queued: false)));
    }

    /// <inheritdoc />
    public Task<Result> CloseDayAsync(long companyId, CancellationToken ct = default)
    {
        if (Offline)
        {
            return Task.FromResult(Result.Failure(Error.Failure("Fiscal.Offline", "The fiscal device cannot be reached.")));
        }

        _days.AddOrUpdate(companyId, _ => (0, 0, false), (_, d) => (d.Day, d.Receipt, false));
        return Task.FromResult(Result.Success());
    }
}
