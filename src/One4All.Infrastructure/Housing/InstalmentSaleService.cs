using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Housing;
using One4All.Application.Abstractions.Numbering;
using One4All.Application.Abstractions.Trading;
using One4All.Application.Housing;
using One4All.Core.Common;
using One4All.Core.Housing;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Housing;

/// <summary>Instalment sales of stands: sale at the cash price, deposit, and interest raised as instalments fall due.</summary>
public sealed class InstalmentSaleService : IInstalmentSaleService
{
    private readonly One4AllDbContext _db;
    private readonly INumberSequenceService _numbers;
    private readonly IHousingService _housing;
    private readonly ISalesLedgerService _ar;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="numbers">The number sequences.</param>
    /// <param name="housing">The housing service (stand sales).</param>
    /// <param name="ar">The receivables ledger.</param>
    public InstalmentSaleService(One4AllDbContext db, INumberSequenceService numbers, IHousingService housing, ISalesLedgerService ar)
    {
        _db = db;
        _numbers = numbers;
        _housing = housing;
        _ar = ar;
    }

    /// <inheritdoc />
    public Task<Result<InstalmentSaleResult>> SignAsync(InstalmentSaleRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return _db.AtomicAsync(() => SignCoreAsync(request, ct), ct);
    }

    private async Task<Result<InstalmentSaleResult>> SignCoreAsync(InstalmentSaleRequest request, CancellationToken ct)
    {
        if (request.CashPrice <= 0m || request.Deposit < 0m || request.Deposit >= request.CashPrice || request.TermMonths < 1 || request.AnnualRatePercent < 0m)
        {
            return Error.Validation("Hse.InstalmentTerms", "An instalment sale needs a positive price, a deposit below it, a term of at least a month and a rate of zero or more.");
        }

        if (request.FirstDueDate <= request.SignedOn)
        {
            return Error.Validation("Hse.FirstDue", "The first instalment must fall due after signature.");
        }

        if (new[] { request.SalesAccountCode, request.StandsAvailableAccountCode, request.CostOfSalesAccountCode, request.InterestAccountCode }.Any(string.IsNullOrWhiteSpace)
            || (request.Deposit > 0m && string.IsNullOrWhiteSpace(request.BankAccountCode)))
        {
            return Error.Validation("Hse.InstalmentAccounts", "An instalment sale needs its sales, stands, cost-of-sales and interest accounts, and a bank account for the deposit.");
        }

        Customer? buyer = await _db.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.Id == request.BuyerCustomerId && c.CompanyId == request.CompanyId, ct).ConfigureAwait(false);
        if (buyer is null)
        {
            return Error.NotFound("Hse.BuyerNotFound", $"Customer {request.BuyerCustomerId} not found.");
        }

        Result<string> number = await _numbers.AllocateAsync(request.CompanyId, "IA", request.SignedOn, ct: ct).ConfigureAwait(false);
        if (number.IsFailure)
        {
            return number.Error;
        }

        // Control passes on signature: the sale at the cash price and the full receivable (IFRS 15).
        Result<StandSaleResult> sold = await _housing.SellStandAsync(request.CompanyId, request.StandId, buyer.Id, buyer.Name, request.CashPrice, request.SignedOn,
            request.SalesAccountCode.Trim(), request.StandsAvailableAccountCode.Trim(), request.CostOfSalesAccountCode.Trim(), number.Value, ct).ConfigureAwait(false);
        if (sold.IsFailure)
        {
            return sold.Error;
        }

        if (request.Deposit > 0m)
        {
            Result<TradingPostingResult> deposit = await _ar.PostReceiptAsync(new ReceiptRequest
            {
                CompanyId = request.CompanyId, CustomerId = buyer.Id, DocumentNumber = $"{number.Value}-DEP", ReceiptDate = request.SignedOn, Currency = buyer.Currency,
                Amount = request.Deposit, BankAccountCode = request.BankAccountCode.Trim(), Narrative = $"Deposit on instalment sale {number.Value}",
            }, ct).ConfigureAwait(false);
            if (deposit.IsFailure)
            {
                return deposit.Error;
            }
        }

        IReadOnlyList<PlannedInstalment> schedule = InstalmentPlan.Build(request.CashPrice - request.Deposit, request.AnnualRatePercent, request.TermMonths, request.Method, request.FirstDueDate);
        InstalmentAgreement agreement = new(request.CompanyId, number.Value, request.StandId, buyer.Id, request.CashPrice, request.Deposit, request.AnnualRatePercent,
            request.Method, request.TermMonths, request.SignedOn, request.InterestAccountCode.Trim());
        foreach (PlannedInstalment line in schedule)
        {
            agreement.AddLine(new InstalmentScheduleLine(line.Number, line.DueDate, line.Amount, line.Interest, line.Principal, line.BalanceAfter));
        }

        _db.Set<InstalmentAgreement>().Add(agreement);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return new InstalmentSaleResult(number.Value, schedule);
    }

    /// <inheritdoc />
    public Task<Result<InstalmentRunResult>> RaiseDueAsync(long companyId, DateOnly asAtDate, CancellationToken ct = default)
        => _db.AtomicAsync(() => RaiseCoreAsync(companyId, asAtDate, ct), ct);

    private async Task<Result<InstalmentRunResult>> RaiseCoreAsync(long companyId, DateOnly asAtDate, CancellationToken ct)
    {
        List<InstalmentAgreement> agreements = await _db.Set<InstalmentAgreement>().Include(a => a.Lines)
            .Where(a => a.CompanyId == companyId && a.Status == InstalmentAgreementStatus.Active)
            .OrderBy(a => a.AgreementNumber).ToListAsync(ct).ConfigureAwait(false);
        int raised = 0;
        decimal interest = 0m;
        foreach (InstalmentAgreement agreement in agreements)
        {
            Result<(int Count, decimal Interest)> result = await RaiseAgreementAsync(agreement, asAtDate, ct).ConfigureAwait(false);
            if (result.IsFailure)
            {
                return result.Error;
            }

            raised += result.Value.Count;
            interest += result.Value.Interest;
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return new InstalmentRunResult(raised, interest);
    }

    /// <inheritdoc />
    public async Task<Result<SettlementQuote>> QuoteSettlementAsync(long companyId, string agreementNumber, DateOnly asAtDate, CancellationToken ct = default)
    {
        Result<InstalmentAgreement> found = await FindAsync(companyId, agreementNumber, ct).ConfigureAwait(false);
        if (found.IsFailure)
        {
            return found.Error;
        }

        return InstalmentPlan.Settle(Planned(found.Value), asAtDate);
    }

    /// <inheritdoc />
    public Task<Result<SettlementQuote>> SettleEarlyAsync(long companyId, string agreementNumber, DateOnly asAtDate, CancellationToken ct = default)
        => _db.AtomicAsync(() => SettleCoreAsync(companyId, agreementNumber, asAtDate, ct), ct);

    private async Task<Result<SettlementQuote>> SettleCoreAsync(long companyId, string agreementNumber, DateOnly asAtDate, CancellationToken ct)
    {
        Result<InstalmentAgreement> found = await FindAsync(companyId, agreementNumber, ct).ConfigureAwait(false);
        if (found.IsFailure)
        {
            return found.Error;
        }

        InstalmentAgreement agreement = found.Value;
        if (agreement.Status != InstalmentAgreementStatus.Active)
        {
            return Error.Rule("Hse.AgreementClosed", $"Agreement {agreement.AgreementNumber} is {agreement.Status}.");
        }

        // Instalments due by the settlement date stand; their interest is raised before the rest is rebated.
        Result<(int Count, decimal Interest)> due = await RaiseAgreementAsync(agreement, asAtDate, ct).ConfigureAwait(false);
        if (due.IsFailure)
        {
            return due.Error;
        }

        SettlementQuote quote = InstalmentPlan.Settle(Planned(agreement), asAtDate);
        agreement.SettleEarly(asAtDate);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return quote;
    }

    // Raises one agreement's instalments due by the date: interest is invoiced (the principal is already receivable).
    private async Task<Result<(int Count, decimal Interest)>> RaiseAgreementAsync(InstalmentAgreement agreement, DateOnly asAtDate, CancellationToken ct)
    {
        int count = 0;
        decimal interest = 0m;
        Customer buyer = await _db.Customers.AsNoTracking().FirstAsync(c => c.Id == agreement.BuyerCustomerId, ct).ConfigureAwait(false);
        foreach (InstalmentScheduleLine line in agreement.Lines.Where(l => !l.Raised && !l.Rebated && l.DueDate <= asAtDate).OrderBy(l => l.Number))
        {
            if (line.Interest > 0m)
            {
                Result<TradingPostingResult> invoiced = await _ar.PostInvoiceAsync(new SalesInvoiceRequest
                {
                    CompanyId = agreement.CompanyId, CustomerId = buyer.Id, DocumentNumber = $"{agreement.AgreementNumber}-{line.Number:000}", DocumentDate = line.DueDate,
                    Currency = buyer.Currency, NetAmount = line.Interest, SalesAccountCode = agreement.InterestAccountCode,
                    Narrative = $"Interest on instalment {line.Number} of {agreement.AgreementNumber}",
                }, ct).ConfigureAwait(false);
                if (invoiced.IsFailure)
                {
                    return invoiced.Error;
                }
            }

            line.MarkRaised();
            count++;
            interest += line.Interest;
        }

        if (agreement.Lines.All(l => l.Raised))
        {
            agreement.Complete();
        }

        return Result<(int Count, decimal Interest)>.Success((count, interest));
    }

    private static List<PlannedInstalment> Planned(InstalmentAgreement agreement)
        => [.. agreement.Lines.OrderBy(l => l.Number).Select(l => new PlannedInstalment(l.Number, l.DueDate, l.Amount, l.Interest, l.Principal, l.BalanceAfter))];

    private async Task<Result<InstalmentAgreement>> FindAsync(long companyId, string agreementNumber, CancellationToken ct)
    {
        string number = (agreementNumber ?? string.Empty).Trim();
        InstalmentAgreement? agreement = await _db.Set<InstalmentAgreement>().Include(a => a.Lines)
            .FirstOrDefaultAsync(a => a.CompanyId == companyId && a.AgreementNumber == number, ct).ConfigureAwait(false);
        if (agreement is null)
        {
            return Error.NotFound("Hse.AgreementNotFound", $"Agreement {number} not found.");
        }

        return agreement;
    }
}
