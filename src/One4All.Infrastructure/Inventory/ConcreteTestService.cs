using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Inventory;
using One4All.Core.Common;
using One4All.Core.Inventory;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Inventory;

/// <summary>Keeps concrete slump and strength tests against their batch runs.</summary>
public sealed class ConcreteTestService : IConcreteTestService
{
    private readonly One4AllDbContext _db;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    public ConcreteTestService(One4AllDbContext db) => _db = db;

    /// <inheritdoc />
    public async Task<Result<(long Id, bool Passed)>> RecordAsync(long companyId, ConcreteTestEntry entry, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (string.IsNullOrWhiteSpace(entry.SampleReference) || entry.Result < 0m || entry.Specified <= 0m || entry.Tolerance < 0m)
        {
            return Error.Validation("Inv.ConcreteTest", "A test needs a sample reference, a result and the target (and a tolerance that is not negative).");
        }

        string run = (entry.RunNumber ?? string.Empty).Trim().ToUpperInvariant();
        AssemblyRun? batch = await _db.AssemblyRuns.AsNoTracking().FirstOrDefaultAsync(r => r.CompanyId == companyId && r.RunNumber.ToUpper() == run, ct).ConfigureAwait(false);
        if (batch is null)
        {
            return Error.NotFound("Inv.BatchRunNotFound", $"Batch run {run} not found.");
        }

        if (entry.TestedOn < batch.Date)
        {
            return Error.Validation("Inv.ConcreteTestDate", "A test cannot be before the batch was made.");
        }

        ConcreteTest test = new(companyId, batch.RunNumber, entry.SampleReference.Trim(), entry.Kind, entry.TestedOn, entry.Result, entry.Specified, entry.Tolerance)
        {
            TestedBy = string.IsNullOrWhiteSpace(entry.TestedBy) ? null : entry.TestedBy.Trim(),
        };
        _db.Set<ConcreteTest>().Add(test);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return (test.Id, test.Passed);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ConcreteTest>> ForRunAsync(long companyId, string runNumber, CancellationToken ct = default)
    {
        string run = (runNumber ?? string.Empty).Trim().ToUpperInvariant();
        return await _db.Set<ConcreteTest>().AsNoTracking().Where(t => t.CompanyId == companyId && t.RunNumber.ToUpper() == run).OrderBy(t => t.TestedOn).ThenBy(t => t.Id)
            .ToListAsync(ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ConcreteTest>> FailuresAsync(long companyId, DateOnly fromDate, DateOnly toDate, CancellationToken ct = default)
        => await _db.Set<ConcreteTest>().AsNoTracking().Where(t => t.CompanyId == companyId && !t.Passed && t.TestedOn >= fromDate && t.TestedOn <= toDate)
            .OrderBy(t => t.TestedOn).ThenBy(t => t.RunNumber).ToListAsync(ct).ConfigureAwait(false);
}
