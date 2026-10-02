using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Trading;
using One4All.Application.Trading;
using One4All.Core.Common;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Trading;

/// <summary>
/// Renders statements of account to text: takes the open-item statement from the trading reports and
/// formats it under the company heading with an ageing footer.
/// </summary>
public sealed class StatementService : IStatementService
{
    private readonly One4AllDbContext _db;
    private readonly ITradingReports _reports;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="reports">The trading reports (supply the statement data).</param>
    public StatementService(One4AllDbContext db, ITradingReports reports)
    {
        _db = db;
        _reports = reports;
    }

    /// <inheritdoc />
    public async Task<Result<string>> RenderCustomerStatementTextAsync(long companyId, long customerId, DateOnly asAt, int width = StatementFormatter.DefaultWidth, CancellationToken ct = default)
    {
        TraderStatement? statement = await _reports.CustomerStatementAsync(companyId, customerId, ct).ConfigureAwait(false);
        return statement is null
            ? Error.NotFound("Ar.CustomerNotFound", $"Customer {customerId} not found.")
            : await RenderAsync(companyId, statement, asAt, width, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<Result<string>> RenderSupplierStatementTextAsync(long companyId, long supplierId, DateOnly asAt, int width = StatementFormatter.DefaultWidth, CancellationToken ct = default)
    {
        TraderStatement? statement = await _reports.SupplierStatementAsync(companyId, supplierId, ct).ConfigureAwait(false);
        return statement is null
            ? Error.NotFound("Ap.SupplierNotFound", $"Supplier {supplierId} not found.")
            : await RenderAsync(companyId, statement, asAt, width, ct).ConfigureAwait(false);
    }

    private async Task<Result<string>> RenderAsync(long companyId, TraderStatement statement, DateOnly asAt, int width, CancellationToken ct)
    {
        string companyName = await _db.Companies.Where(c => c.Id == companyId).Select(c => c.Name).FirstOrDefaultAsync(ct).ConfigureAwait(false) ?? string.Empty;
        return StatementFormatter.ToPlainText(statement, companyName, asAt, width);
    }
}
