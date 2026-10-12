using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Housing;
using One4All.Application.Abstractions.Trading;
using One4All.Application.Housing;
using One4All.Core.Common;
using One4All.Core.Housing;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using One4All.Infrastructure.Trading;

namespace One4All.Infrastructure.Housing;

/// <summary>Instalment default: staged notices with proof of service, and penalty interest on arrears.</summary>
public sealed class InstalmentDefaultService : IInstalmentDefaultService
{
    private readonly One4AllDbContext _db;
    private readonly ISalesLedgerService _ar;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="ar">The receivables ledger.</param>
    public InstalmentDefaultService(One4AllDbContext db, ISalesLedgerService ar)
    {
        _db = db;
        _ar = ar;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DefaultCase>> ReviewAsync(long companyId, DateOnly asAt, CancellationToken ct = default)
        => [.. (await CasesAsync(companyId, asAt, ct).ConfigureAwait(false)).Select(c => c.Case).OrderByDescending(c => c.DaysOverdue).ThenBy(c => c.AgreementNumber, StringComparer.Ordinal)];

    /// <inheritdoc />
    public async Task<Result<(string NoticeNumber, string Text)>> IssueNextAsync(long companyId, string agreementNumber, DateOnly asAt, CancellationToken ct = default)
    {
        string key = (agreementNumber ?? string.Empty).Trim();
        var found = (await CasesAsync(companyId, asAt, ct).ConfigureAwait(false)).FirstOrDefault(c => c.Case.AgreementNumber == key);
        if (found.Agreement is null)
        {
            return Error.NotFound("Hse.NoArrears", $"Agreement {key} is not an active agreement in arrears.");
        }

        if (found.Case.NextStage is not DefaultStage stage)
        {
            return Error.Rule("Hse.NoNoticeDue", found.Case.Why ?? "No notice is due.");
        }

        int issued = await _db.Set<InstalmentNotice>().CountAsync(n => n.AgreementId == found.Agreement.Id, ct).ConfigureAwait(false);
        string number = $"{found.Agreement.AgreementNumber}-N{issued + 1}";
        string stand = await _db.HousingStands.Where(s => s.Id == found.Agreement.StandId).Select(s => s.StandNumber).FirstOrDefaultAsync(ct).ConfigureAwait(false) ?? "?";
        string text = InstalmentDefault.Text(stage, number, found.Agreement.AgreementNumber, found.Case.Buyer, stand, found.Case.Arrears, found.Case.DaysOverdue, asAt);
        _db.Set<InstalmentNotice>().Add(new InstalmentNotice(companyId, found.Agreement.Id, number, stage, asAt, found.Case.Arrears, found.Case.DaysOverdue, text));
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return (number, text);
    }

    /// <inheritdoc />
    public async Task<Result> RecordServiceAsync(long companyId, string noticeNumber, DateOnly servedOn, string method, string servedBy, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(method) || string.IsNullOrWhiteSpace(servedBy))
        {
            return Result.Failure(Error.Validation("Hse.Service", "Proof of service needs how the notice was served and who served it."));
        }

        string key = (noticeNumber ?? string.Empty).Trim();
        InstalmentNotice? notice = await _db.Set<InstalmentNotice>().FirstOrDefaultAsync(n => n.CompanyId == companyId && n.NoticeNumber == key, ct).ConfigureAwait(false);
        if (notice is null)
        {
            return Result.Failure(Error.NotFound("Hse.NoticeNotFound", $"Notice {key} not found."));
        }

        if (notice.ServedOn is DateOnly served)
        {
            return Result.Failure(Error.Conflict("Hse.NoticeServed", $"Notice {key} was served on {served:yyyy-MM-dd}."));
        }

        if (servedOn < notice.IssuedOn)
        {
            return Result.Failure(Error.Validation("Hse.Service", "A notice cannot be served before it was issued."));
        }

        notice.Serve(servedOn, method.Trim(), servedBy.Trim());
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result> SetPenaltyRateAsync(long companyId, string agreementNumber, decimal annualRatePercent, CancellationToken ct = default)
    {
        if (annualRatePercent is < 0m or > 100m)
        {
            return Result.Failure(Error.Validation("Hse.PenaltyRate", "The penalty rate is an annual percentage between 0 and 100."));
        }

        string key = (agreementNumber ?? string.Empty).Trim();
        InstalmentAgreement? agreement = await _db.Set<InstalmentAgreement>().FirstOrDefaultAsync(a => a.CompanyId == companyId && a.AgreementNumber == key, ct).ConfigureAwait(false);
        if (agreement is null)
        {
            return Result.Failure(Error.NotFound("Hse.AgreementNotFound", $"Agreement {key} not found."));
        }

        agreement.PenaltyRatePercent = annualRatePercent;
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public Task<Result<PenaltyRun>> ChargePenaltyInterestAsync(long companyId, DateOnly month, CancellationToken ct = default)
        => _db.AtomicAsync(async () =>
        {
            DateOnly start = new(month.Year, month.Month, 1);
            int charged = 0;
            decimal total = 0m;
            foreach (var c in (await CasesAsync(companyId, start, ct).ConfigureAwait(false)).Where(c => c.Agreement.PenaltyRatePercent > 0m && c.Case.Arrears > 0m))
            {
                decimal penalty = InstalmentDefault.MonthlyPenalty(c.Case.Arrears, c.Agreement.PenaltyRatePercent);
                string number = $"{c.Agreement.AgreementNumber}-PEN-{start:yyyyMM}";
                if (penalty <= 0m || await _db.OpenItems.AnyAsync(i => i.CompanyId == companyId && i.Ledger == TradingLedger.AccountsReceivable && i.DocumentNumber == number, ct).ConfigureAwait(false))
                {
                    continue;
                }

                Customer buyer = await _db.Customers.AsNoTracking().FirstAsync(x => x.Id == c.Agreement.BuyerCustomerId, ct).ConfigureAwait(false);
                Result<TradingPostingResult> invoiced = await _ar.PostInvoiceAsync(new SalesInvoiceRequest
                {
                    CompanyId = companyId, CustomerId = buyer.Id, DocumentNumber = number, DocumentDate = start, Currency = buyer.Currency, NetAmount = penalty,
                    SalesAccountCode = c.Agreement.InterestAccountCode,
                    Narrative = $"Penalty interest at {c.Agreement.PenaltyRatePercent:0.##}% on arrears of {c.Case.Arrears:N2}, {c.Agreement.AgreementNumber}",
                }, ct).ConfigureAwait(false);
                if (invoiced.IsFailure)
                {
                    return invoiced.Error;
                }

                charged++;
                total += penalty;
            }

            return Result<PenaltyRun>.Success(new PenaltyRun(charged, total));
        }, ct);

    // Each active agreement in arrears: the buyer's AR balance as at the date less the principal not yet billed, and the next notice due.
    private async Task<List<(InstalmentAgreement Agreement, DefaultCase Case)>> CasesAsync(long companyId, DateOnly asAt, CancellationToken ct)
    {
        List<InstalmentAgreement> active = await _db.Set<InstalmentAgreement>().AsNoTracking().Include(a => a.Lines)
            .Where(a => a.CompanyId == companyId && a.Status == InstalmentAgreementStatus.Active && a.SignedOn <= asAt).ToListAsync(ct).ConfigureAwait(false);
        if (active.Count == 0)
        {
            return [];
        }

        Dictionary<long, decimal> owing = (await OpenItemsAsAt.LoadAsync(_db, companyId, TradingLedger.AccountsReceivable, asAt, ct).ConfigureAwait(false))
            .GroupBy(i => i.Item.TraderId).ToDictionary(g => g.Key, g => g.Sum(i => i.Signed(LedgerSign.Debit)));
        List<long> buyers = [.. active.Select(a => a.BuyerCustomerId).Distinct()];
        Dictionary<long, string> names = await _db.Customers.AsNoTracking().Where(c => buyers.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Name, ct).ConfigureAwait(false);
        List<long> ids = [.. active.Select(a => a.Id)];
        Dictionary<long, InstalmentNotice> last = (await _db.Set<InstalmentNotice>().AsNoTracking().Where(n => ids.Contains(n.AgreementId)).ToListAsync(ct).ConfigureAwait(false))
            .GroupBy(n => n.AgreementId).ToDictionary(g => g.Key, g => g.OrderByDescending(n => n.Stage).ThenByDescending(n => n.Id).First());

        List<(InstalmentAgreement, DefaultCase)> cases = [];
        foreach (IGrouping<long, InstalmentAgreement> buyer in active.GroupBy(a => a.BuyerCustomerId))
        {
            List<(DateOnly Due, decimal Amount)> raised = [.. buyer.SelectMany(a => a.Lines).Where(l => l.Raised && l.DueDate <= asAt).Select(l => (l.DueDate, l.Amount))];
            decimal notYetBilled = buyer.Sum(a => a.Lines.Where(l => l.Raised && l.DueDate <= asAt).OrderBy(l => l.Number).Select(l => (decimal?)l.BalanceAfter).LastOrDefault() ?? a.Financed);
            decimal arrears = Math.Max(0m, owing.GetValueOrDefault(buyer.Key) - notYetBilled);
            if (arrears <= 0m)
            {
                continue;
            }

            int days = InstalmentDefault.DaysOverdue(arrears, raised, asAt);
            foreach (InstalmentAgreement agreement in buyer)
            {
                InstalmentNotice? previous = last.GetValueOrDefault(agreement.Id);
                (DefaultStage? next, string? why) = InstalmentDefault.Next(days, previous is null ? null : (previous.Stage, previous.IssuedOn, previous.ServedOn), asAt);
                cases.Add((agreement, new DefaultCase(agreement.AgreementNumber, names.GetValueOrDefault(buyer.Key, "?"), arrears, days, previous?.Stage, next, why)));
            }
        }

        return cases;
    }
}
