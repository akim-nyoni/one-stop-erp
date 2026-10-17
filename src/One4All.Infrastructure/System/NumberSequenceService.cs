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

    /// <inheritdoc />
    public async Task<Result<NumberSequenceRow>> ConfigureAsync(long companyId, string documentType, long? branchId, NumberSequenceSetup setup, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(setup);
        string type = (documentType ?? string.Empty).Trim();
        if (type.Length == 0 || type.Length > 60 || setup.Prefix is null || setup.Prefix.Length > 20 || setup.Padding is < 1 or > 12 || setup.StartAt is < 1)
        {
            return Error.Validation("Sequence.Setup", "A sequence needs a document type (up to 60 characters), a prefix up to 20 characters, padding of 1 to 12 digits and a starting number above zero.");
        }

        if (branchId is long branch && !await _db.Branches.AnyAsync(b => b.Id == branch && b.CompanyId == companyId, ct).ConfigureAwait(false))
        {
            return Error.NotFound("Sequence.BranchNotFound", $"Branch {branch} is not one of the company's.");
        }

        NumberSequence? sequence = await _db.Set<NumberSequence>().FirstOrDefaultAsync(s => s.CompanyId == companyId && s.DocumentType == type && s.BranchId == branchId, ct).ConfigureAwait(false);
        if (sequence is null)
        {
            sequence = new NumberSequence(companyId, type, setup.Prefix, setup.Padding, branchId, setup.ResetYearly, setup.Gapless);
            _db.Set<NumberSequence>().Add(sequence);
        }
        else
        {
            sequence.Configure(setup.Prefix, setup.Padding, setup.ResetYearly, setup.Gapless);
        }

        if (setup.StartAt is long startAt && !sequence.AdvanceTo(startAt))
        {
            return Error.Rule("Sequence.StartBehind", $"The sequence is already at {sequence.NextValue}; the next number cannot move back to {startAt}.");
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Row(sequence);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<NumberSequenceRow>> ListAsync(long companyId, CancellationToken ct = default)
        => [.. (await _db.Set<NumberSequence>().AsNoTracking().Where(s => s.CompanyId == companyId).OrderBy(s => s.DocumentType).ThenBy(s => s.BranchId).ToListAsync(ct).ConfigureAwait(false)).Select(Row)];

    private static NumberSequenceRow Row(NumberSequence s) => new(s.DocumentType, s.BranchId, s.Prefix, s.Padding, s.ResetYearly, s.Gapless, s.NextValue, s.CurrentYear);
}
