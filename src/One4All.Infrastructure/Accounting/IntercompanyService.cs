using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Accounting;
using One4All.Core.Accounting;
using One4All.Core.Common;
using One4All.Core.System;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Accounting;

/// <summary>
/// Posts intercompany transactions as a mirrored pair of journals, one per company, inside a single database
/// transaction so the two sides can never diverge.
/// </summary>
public sealed class IntercompanyService : IIntercompanyService
{
    private readonly One4AllDbContext _db;
    private readonly IPostingEngine _postingEngine;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="postingEngine">The central posting engine.</param>
    public IntercompanyService(One4AllDbContext db, IPostingEngine postingEngine)
    {
        _db = db;
        _postingEngine = postingEngine;
    }

    /// <inheritdoc />
    public async Task<Result<IntercompanyPostingResult>> PostAsync(IntercompanyJournalRequest request, CancellationToken ct = default)
    {
        // Both companies' books are posted: a request scoped to one company lifts the scope for this transaction alone.
        using IDisposable unscoped = _db.WithoutCompanyScope();
        return await _db.AtomicAsync(() => PostCoreAsync(request, ct), ct).ConfigureAwait(false);
    }

    // One unit of work: every posting it makes commits together or not at all (joining a caller's transaction).
    private async Task<Result<IntercompanyPostingResult>> PostCoreAsync(IntercompanyJournalRequest request, CancellationToken ct)
    {
        if (request.FromCompanyId == request.ToCompanyId)
        {
            return Error.Validation("Interco.SameCompany", "An intercompany transaction needs two different companies.");
        }

        if (request.Amount <= 0m)
        {
            return Error.Validation("Interco.Amount", "The intercompany amount must be positive.");
        }

        if (string.IsNullOrWhiteSpace(request.FromAccountCode) || string.IsNullOrWhiteSpace(request.FromIntercompanyAccountCode)
            || string.IsNullOrWhiteSpace(request.ToAccountCode) || string.IsNullOrWhiteSpace(request.ToIntercompanyAccountCode))
        {
            return Error.Validation("Interco.Accounts", "All four account codes (source, due-from, target, due-to) are required.");
        }

        Company? from = await _db.Companies.FirstOrDefaultAsync(c => c.Id == request.FromCompanyId, ct).ConfigureAwait(false);
        Company? to = await _db.Companies.FirstOrDefaultAsync(c => c.Id == request.ToCompanyId, ct).ConfigureAwait(false);
        if (from is null || to is null)
        {
            return Error.NotFound("Interco.CompanyNotFound", "One of the companies does not exist.");
        }

        if (from.BaseCurrency != to.BaseCurrency)
        {
            return Error.Rule("Interco.MixedCurrency", $"{from.Code} ({from.BaseCurrency}) and {to.Code} ({to.BaseCurrency}) do not share a base currency.");
        }

        string narrative = request.Narrative ?? $"Intercompany {from.Code} → {to.Code} {request.DocumentNumber}";

        // Both journals or neither: the engine only calls SaveChanges, so one transaction on the context covers both.

        Result<PostingReceipt> fromPosting = await _postingEngine.PostAsync(Journal(from, request.Date, request.DocumentNumber, narrative,
            (request.FromIntercompanyAccountCode, JournalSide.Debit),
            (request.FromAccountCode, JournalSide.Credit),
            request.Amount), ct).ConfigureAwait(false);
        if (fromPosting.IsFailure)
        {
            return fromPosting.Error;
        }

        Result<PostingReceipt> toPosting = await _postingEngine.PostAsync(Journal(to, request.Date, request.DocumentNumber, narrative,
            (request.ToAccountCode, JournalSide.Debit),
            (request.ToIntercompanyAccountCode, JournalSide.Credit),
            request.Amount), ct).ConfigureAwait(false);
        if (toPosting.IsFailure)
        {
            return toPosting.Error;
        }
        return new IntercompanyPostingResult(fromPosting.Value.JournalNumber, toPosting.Value.JournalNumber);
    }

    private static JournalRequest Journal(Company company, DateOnly date, string documentNumber, string narrative, (string Account, JournalSide Side) first, (string Account, JournalSide Side) second, decimal amount) => new()
    {
        CompanyId = company.Id,
        SourceModule = "GL",
        PostingDate = date,
        TransactionCurrency = company.BaseCurrency,
        EnforceManualJournalRules = false,
        DocumentType = "Intercompany",
        DocumentNumber = documentNumber,
        Narrative = narrative,
        Lines =
        [
            new JournalLineRequest { AccountCode = first.Account, Side = first.Side, TransactionAmount = amount },
            new JournalLineRequest { AccountCode = second.Account, Side = second.Side, TransactionAmount = amount },
        ],
    };
}
