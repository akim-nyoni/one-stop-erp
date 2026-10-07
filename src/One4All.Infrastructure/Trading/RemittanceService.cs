using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Trading;
using One4All.Application.Trading;
using One4All.Core.Common;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Trading;

/// <summary>
/// Renders a supplier remittance advice to text: it takes the remittance data from the trading reports (the
/// invoices a payment was allocated against) and formats it under the company heading.
/// </summary>
public sealed class RemittanceService : IRemittanceService
{
    private readonly One4AllDbContext _db;
    private readonly ITradingReports _reports;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="reports">The trading reports (supplies the remittance data).</param>
    public RemittanceService(One4AllDbContext db, ITradingReports reports)
    {
        _db = db;
        _reports = reports;
    }

    /// <inheritdoc />
    public async Task<Result<string>> RenderRemittanceTextAsync(long companyId, long paymentOpenItemId, int width = RemittanceFormatter.DefaultWidth, CancellationToken ct = default)
    {
        RemittanceAdvice? advice = await _reports.RemittanceAdviceAsync(companyId, paymentOpenItemId, ct).ConfigureAwait(false);
        if (advice is null)
        {
            return Error.NotFound("Remittance.PaymentNotFound", $"Payment open item {paymentOpenItemId} not found.");
        }

        string companyName = await _db.Companies.Where(c => c.Id == companyId).Select(c => c.Name).FirstOrDefaultAsync(ct).ConfigureAwait(false) ?? string.Empty;
        long supplierId = await _db.OpenItems.Where(i => i.Id == paymentOpenItemId).Select(i => i.TraderId).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        return RemittanceFormatter.ToPlainText(advice, companyName, width,
            await TraderDirectory.CorrespondenceAsync(_db, companyId, Core.Trading.TradingLedger.AccountsPayable, supplierId, ct).ConfigureAwait(false));
    }
}
