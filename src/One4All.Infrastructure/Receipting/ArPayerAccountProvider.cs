using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Receipting;
using One4All.Core.Receipting;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Receipting;

/// <summary>
/// The AR payer-account provider (brief §RCP-2). Resolves a trade-debtor customer as a payer; this is the
/// only register receipting needs when Municipality/Housing/Schools are not licensed.
/// </summary>
public sealed class ArPayerAccountProvider : IPayerAccountProvider
{
    private readonly One4AllDbContext _db;

    /// <summary>Creates the provider.</summary>
    /// <param name="db">The database context.</param>
    public ArPayerAccountProvider(One4AllDbContext db) => _db = db;

    /// <inheritdoc />
    public PayerKind Kind => PayerKind.ArCustomer;

    /// <inheritdoc />
    public async Task<PayerAccount?> ResolveAsync(long companyId, long accountId, CancellationToken ct = default)
    {
        Customer? customer = await _db.Set<Customer>()
            .FirstOrDefaultAsync(c => c.Id == accountId && c.CompanyId == companyId, ct).ConfigureAwait(false);
        return customer is null ? null : new PayerAccount(PayerKind.ArCustomer, customer.Id, customer.Name, null);
    }
}
