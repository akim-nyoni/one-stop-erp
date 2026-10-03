using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Accounting;
using One4All.Application.Accounting;
using One4All.Core.Common;
using One4All.Core.System;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Accounting;

/// <summary>Imports manual journals from CSV through the posting engine, as one unit.</summary>
public sealed class JournalImportService : IJournalImportService
{
    private const int ErrorsShown = 5;

    private readonly One4AllDbContext _db;
    private readonly IPostingEngine _engine;
    private readonly IJournalApprovalService _approvals;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="engine">The posting engine.</param>
    /// <param name="approvals">The maker-checker service, for journals over the approval threshold.</param>
    public JournalImportService(One4AllDbContext db, IPostingEngine engine, IJournalApprovalService approvals)
    {
        _db = db;
        _engine = engine;
        _approvals = approvals;
    }

    /// <inheritdoc />
    public Task<Result<IReadOnlyList<ImportedJournalResult>>> ImportAsync(long companyId, string csv, long preparedByUserId, CancellationToken ct = default)
        => _db.AtomicAsync(() => ImportCoreAsync(companyId, csv, preparedByUserId, ct), ct);

    // One unit: every journal in the file is kept, or none is.
    private async Task<Result<IReadOnlyList<ImportedJournalResult>>> ImportCoreAsync(long companyId, string csv, long preparedByUserId, CancellationToken ct)
    {
        JournalCsvResult parsed = JournalCsvParser.Parse(csv);
        if (!parsed.IsClean)
        {
            string shown = string.Join(" ", parsed.Errors.Take(ErrorsShown).Select(e => $"Line {e.LineNumber}: {e.Message}"));
            string more = parsed.Errors.Count > ErrorsShown ? $" (and {parsed.Errors.Count - ErrorsShown} more)" : string.Empty;
            return Error.Validation("Gl.ImportParse", $"The journal file cannot be imported. {shown}{more}");
        }

        Company? company = await _db.Companies.AsNoTracking().FirstOrDefaultAsync(c => c.Id == companyId, ct).ConfigureAwait(false);
        if (company is null)
        {
            return Error.NotFound("Gl.CompanyNotFound", $"Company {companyId} not found.");
        }

        List<ImportedJournalResult> results = [];
        foreach (ImportedJournal journal in parsed.Journals)
        {
            JournalRequest request = new()
            {
                CompanyId = companyId,
                SourceModule = "GL",
                PostingDate = journal.Date,
                TransactionCurrency = company.BaseCurrency,
                DocumentType = "JournalImport",
                DocumentNumber = journal.Reference,
                Narrative = journal.Narrative,
                EnforceManualJournalRules = true,
                Lines = [.. journal.Lines.Select(l => new JournalLineRequest
                {
                    AccountCode = l.AccountCode,
                    CostCentre = l.CostCentre,
                    Department = l.Department,
                    Side = l.Debit > 0m ? JournalSide.Debit : JournalSide.Credit,
                    TransactionAmount = l.Debit > 0m ? l.Debit : l.Credit,
                    Narrative = l.Narrative,
                })],
            };

            bool needsApproval = company.ManualJournalApprovalThreshold is decimal threshold && journal.TotalDebit > threshold;
            Result<PostingReceipt> posted = needsApproval
                ? await _approvals.SubmitAsync(request, preparedByUserId, ct).ConfigureAwait(false)
                : await _engine.PostAsync(request, ct).ConfigureAwait(false);
            if (posted.IsFailure)
            {
                return Error.Rule(posted.Error.Code, $"Journal {journal.Reference}: {posted.Error.Message} Nothing was imported.");
            }

            results.Add(new ImportedJournalResult(journal.Reference, posted.Value.JournalNumber, needsApproval));
        }

        return Result<IReadOnlyList<ImportedJournalResult>>.Success(results);
    }
}
