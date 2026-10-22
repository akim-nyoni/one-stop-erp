using One4All.Application.Abstractions.Accounting;
using One4All.Core.Common;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Trading;

/// <summary>
/// Built-in task <c>ar.fiscal-invoices</c>: signs the sales ledger tax invoices and credit notes still waiting for the fiscal device - those
/// it could not reach when they posted, and batches (a municipal billing run) that are queued rather than signed one by one while they
/// post. Schedule it often (every few minutes); each run works through up to <see cref="MaxPerRun"/> documents and stops early when the
/// device is down. A company that also licenses the point of sale has <c>pos.fiscal-retry</c> do the same for these documents.
/// </summary>
public sealed class FiscalInvoiceRetryTask : Application.Abstractions.Scheduling.IScheduledTask
{
    /// <summary>The most documents one run tries to sign.</summary>
    public const int MaxPerRun = 20_000;

    private readonly One4AllDbContext _db;
    private readonly IFiscalDeviceProvider? _fiscal;

    /// <summary>Creates the task.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="fiscal">The fiscal device (none or disabled: there is nothing to sign).</param>
    public FiscalInvoiceRetryTask(One4AllDbContext db, IFiscalDeviceProvider? fiscal = null)
    {
        _db = db;
        _fiscal = fiscal;
    }

    /// <inheritdoc />
    public string Key => "ar.fiscal-invoices";

    /// <inheritdoc />
    public async Task<Result<string>> RunAsync(long companyId, DateTimeOffset runAt, CancellationToken ct = default)
    {
        if (_fiscal is not { IsEnabled: true })
        {
            return "No fiscal device is in use; nothing to sign.";
        }

        (int signed, int waiting) = await Accounting.FiscalSigning.SignQueuedOpenItemsAsync(_db, _fiscal, companyId, DateTimeOffset.UtcNow, MaxPerRun, ct).ConfigureAwait(false);
        string message = $"{signed} invoice(s) and credit note(s) fiscalised, {waiting} still waiting for the fiscal device.";
        return waiting == 0 ? message : Error.Rule("Ar.FiscalPending", message);
    }
}
