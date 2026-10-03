using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Budgeting;
using One4All.Application.Budgeting;
using One4All.Core.Budgeting;
using One4All.Core.Common;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Budgeting;

/// <summary>Material variances and their commentary over the budget-vs-actual comparison.</summary>
public sealed class VarianceCommentaryService : IVarianceCommentaryService
{
    private readonly One4AllDbContext _db;
    private readonly IBudgetingService _budgeting;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="budgeting">The budgeting service, for budget against actual.</param>
    public VarianceCommentaryService(One4AllDbContext db, IBudgetingService budgeting)
    {
        _db = db;
        _budgeting = budgeting;
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<MaterialVarianceRow>>> MaterialVariancesAsync(long companyId, long fiscalYearId, BudgetType type, string? department, int asAtPeriod, VarianceThreshold threshold, CancellationToken ct = default)
    {
        Result<BudgetVsActualReport> report = await _budgeting.BudgetVsActualAsync(companyId, fiscalYearId, type, department, asAtPeriod, ct).ConfigureAwait(false);
        if (report.IsFailure)
        {
            return report.Error;
        }

        // Only accounts the budget covers: balance-sheet movements have no budget to vary from.
        Dictionary<string, VarianceComment> comments = await _db.Set<VarianceComment>().AsNoTracking()
            .Where(c => c.CompanyId == companyId && c.FiscalYearId == fiscalYearId && c.AsAtPeriod == asAtPeriod)
            .ToDictionaryAsync(c => c.AccountCode, ct).ConfigureAwait(false);
        List<MaterialVarianceRow> rows = [.. report.Value.Rows
            .Where(r => r.Budget != 0m && MaterialVariance.IsMaterial(r.Budget, r.Actual, threshold))
            .Select(r => new MaterialVarianceRow(r.AccountCode, r.Budget, r.Actual, comments.GetValueOrDefault(r.AccountCode)?.Comment, comments.GetValueOrDefault(r.AccountCode)?.AuthorUserId))];
        return Result<IReadOnlyList<MaterialVarianceRow>>.Success(rows);
    }

    /// <inheritdoc />
    public async Task<Result> CommentAsync(long companyId, long fiscalYearId, int asAtPeriod, string accountCode, string comment, long authorUserId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(comment) || string.IsNullOrWhiteSpace(accountCode) || asAtPeriod is < 1 or > 12)
        {
            return Result.Failure(Error.Validation("Budget.Comment.Invalid", "A variance comment needs an account, a period from 1 to 12 and some text."));
        }

        string code = accountCode.Trim();
        VarianceComment? existing = await _db.Set<VarianceComment>()
            .FirstOrDefaultAsync(c => c.CompanyId == companyId && c.FiscalYearId == fiscalYearId && c.AsAtPeriod == asAtPeriod && c.AccountCode == code, ct).ConfigureAwait(false);
        if (existing is null)
        {
            _db.Set<VarianceComment>().Add(new VarianceComment(companyId, fiscalYearId, asAtPeriod, code, comment.Trim(), authorUserId));
        }
        else
        {
            existing.Revise(comment.Trim(), authorUserId);
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }
}
