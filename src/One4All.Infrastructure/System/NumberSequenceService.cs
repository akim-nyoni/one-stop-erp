using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Numbering;
using One4All.Core.Common;
using One4All.Core.System;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Numbering;

/// <summary>
/// Database-backed document-number allocator. Loads the sequence for a company/document-type/branch (or
/// creates a default), allocates the next formatted number and persists the incremented counter under the
/// entity's optimistic-concurrency token so two concurrent allocations cannot take the same number.
/// </summary>
public sealed class NumberSequenceService : INumberSequenceService
{
    private readonly One4AllDbContext _db;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    public NumberSequenceService(One4AllDbContext db) => _db = db;

    /// <inheritdoc />
    public async Task<Result<string>> AllocateAsync(long companyId, string documentType, DateOnly date, long? branchId = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(documentType))
        {
            return Error.Validation("Sequence.DocumentType", "A document type is required.");
        }

        NumberSequence? sequence = await _db.Set<NumberSequence>()
            .FirstOrDefaultAsync(s => s.CompanyId == companyId && s.DocumentType == documentType && s.BranchId == branchId, ct)
            .ConfigureAwait(false);

        if (sequence is null)
        {
            sequence = new NumberSequence(companyId, documentType, $"{documentType}-", padding: 5, branchId: branchId);
            _db.Set<NumberSequence>().Add(sequence);
        }

        string number = sequence.Allocate(date.Year);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return number;
    }
}
