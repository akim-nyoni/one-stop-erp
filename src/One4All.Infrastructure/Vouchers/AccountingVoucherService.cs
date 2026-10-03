using System.Globalization;
using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Accounting;
using One4All.Application.Abstractions.Numbering;
using One4All.Application.Abstractions.Vouchers;
using One4All.Application.Vouchers;
using One4All.Core.Accounting;
using One4All.Core.Common;
using One4All.Core.System;
using One4All.Core.Vouchers;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Vouchers;

/// <summary>Accounting vouchers through the number sequences and the posting engine.</summary>
public sealed class AccountingVoucherService : IAccountingVoucherService
{
    private readonly One4AllDbContext _db;
    private readonly IPostingEngine _engine;
    private readonly INumberSequenceService _numbers;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="engine">The posting engine.</param>
    /// <param name="numbers">The number sequences.</param>
    public AccountingVoucherService(One4AllDbContext db, IPostingEngine engine, INumberSequenceService numbers)
    {
        _db = db;
        _engine = engine;
        _numbers = numbers;
    }

    /// <inheritdoc />
    public Task<Result<PreparedVoucher>> PrepareAsync(long companyId, VoucherRequest request, CancellationToken ct = default)
        => _db.AtomicAsync(() => PrepareCoreAsync(companyId, request, ct), ct);

    // One unit: the number and the voucher are kept together.
    private async Task<Result<PreparedVoucher>> PrepareCoreAsync(long companyId, VoucherRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        bool cashVoucher = request.Type != AccountingVoucherType.Journal;
        if (request.Lines.Count == 0 || string.IsNullOrWhiteSpace(request.Party) || (cashVoucher && string.IsNullOrWhiteSpace(request.CashAccountCode))
            || (cashVoucher && request.Lines.Any(l => l.Amount <= 0m)) || request.Lines.Any(l => l.Amount == 0m))
        {
            return Error.Validation("Vou.VoucherInvalid", "A voucher needs a party and lines; payment, receipt and petty cash vouchers need a cash account and positive amounts.");
        }

        if (!cashVoucher && request.Lines.Sum(l => l.Amount) != 0m)
        {
            return Error.Rule("Vou.JournalUnbalanced", "A journal voucher's debits and credits must balance.");
        }

        Result<string> number = await _numbers.AllocateAsync(companyId, Sequence(request.Type), request.Date, ct: ct).ConfigureAwait(false);
        if (number.IsFailure)
        {
            return number.Error;
        }

        AccountingVoucher voucher = new(companyId, request.Type, number.Value, request.Date, request.Party.Trim(), request.CashAccountCode?.Trim(), request.Narrative, request.PreparedByUserId);
        foreach (VoucherLineRequest line in request.Lines)
        {
            // Payments and petty cash debit what they pay for; receipts credit what they are for; journals as signed.
            (decimal debit, decimal credit) = request.Type switch
            {
                AccountingVoucherType.Receipt => (0m, line.Amount),
                AccountingVoucherType.Journal => (Math.Max(line.Amount, 0m), Math.Max(-line.Amount, 0m)),
                _ => (line.Amount, 0m),
            };
            voucher.AddLine(line.AccountCode.Trim(), debit, credit, line.Narrative);
        }

        _db.Set<AccountingVoucher>().Add(voucher);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return new PreparedVoucher(voucher.Id, voucher.Number, request.Lines.Sum(l => Math.Abs(l.Amount)) / (cashVoucher ? 1m : 2m));
    }

    /// <inheritdoc />
    public Task<Result<string>> ApproveAsync(long companyId, long voucherId, long approverUserId, CancellationToken ct = default)
        => _db.AtomicAsync(() => ApproveCoreAsync(companyId, voucherId, approverUserId, ct), ct);

    // One unit: the posting and the approval are kept together.
    private async Task<Result<string>> ApproveCoreAsync(long companyId, long voucherId, long approverUserId, CancellationToken ct)
    {
        Result<AccountingVoucher> found = await LoadDraftAsync(companyId, voucherId, ct).ConfigureAwait(false);
        if (found.IsFailure)
        {
            return found.Error;
        }

        AccountingVoucher voucher = found.Value;
        if (approverUserId == voucher.PreparedByUserId)
        {
            return Error.Rule("Vou.SelfApproval", $"Voucher {voucher.Number} must be approved by someone other than the person who prepared it.");
        }

        List<JournalLineRequest> lines = [.. voucher.Lines.Select(l => new JournalLineRequest
        {
            AccountCode = l.AccountCode, Side = l.Debit > 0m ? JournalSide.Debit : JournalSide.Credit, TransactionAmount = l.Debit > 0m ? l.Debit : l.Credit, Narrative = l.Narrative,
        })];
        decimal total = voucher.Lines.Sum(l => l.Debit + l.Credit);
        if (voucher.Type is AccountingVoucherType.Payment or AccountingVoucherType.PettyCash)
        {
            lines.Add(new JournalLineRequest { AccountCode = voucher.CashAccountCode!, Side = JournalSide.Credit, TransactionAmount = total });
        }
        else if (voucher.Type == AccountingVoucherType.Receipt)
        {
            lines.Add(new JournalLineRequest { AccountCode = voucher.CashAccountCode!, Side = JournalSide.Debit, TransactionAmount = total });
        }

        CurrencyCode currency = await _db.Companies.Where(c => c.Id == companyId).Select(c => c.BaseCurrency).FirstAsync(ct).ConfigureAwait(false);
        Result<PostingReceipt> posted = await _engine.PostAsync(new JournalRequest
        {
            CompanyId = companyId,
            SourceModule = "GL",
            PostingDate = voucher.Date,
            TransactionCurrency = currency,
            DocumentType = AccountingVoucherFormatter.Title(voucher.Type),
            DocumentNumber = voucher.Number,
            Narrative = voucher.Narrative ?? voucher.Party,
            EnforceManualJournalRules = true,
            Lines = lines,
        }, ct).ConfigureAwait(false);
        if (posted.IsFailure)
        {
            return posted.Error;
        }

        voucher.Approve(approverUserId, posted.Value.JournalNumber);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return posted.Value.JournalNumber;
    }

    /// <inheritdoc />
    public async Task<Result> CancelAsync(long companyId, long voucherId, CancellationToken ct = default)
    {
        Result<AccountingVoucher> found = await LoadDraftAsync(companyId, voucherId, ct).ConfigureAwait(false);
        if (found.IsFailure)
        {
            return Result.Failure(found.Error);
        }

        found.Value.Cancel();
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result<string>> RenderAsync(long companyId, long voucherId, CancellationToken ct = default)
    {
        AccountingVoucher? voucher = await _db.Set<AccountingVoucher>().AsNoTracking().Include(v => v.Lines)
            .FirstOrDefaultAsync(v => v.Id == voucherId && v.CompanyId == companyId, ct).ConfigureAwait(false);
        if (voucher is null)
        {
            return Error.NotFound("Vou.VoucherNotFound", $"Voucher {voucherId} not found.");
        }

        Company company = await _db.Companies.AsNoTracking().FirstAsync(c => c.Id == companyId, ct).ConfigureAwait(false);
        List<string> codes = [.. voucher.Lines.Select(l => l.AccountCode)];
        Dictionary<string, string> names = await _db.Accounts.AsNoTracking().Where(a => a.CompanyId == companyId && codes.Contains(a.AccountCode))
            .ToDictionaryAsync(a => a.AccountCode, a => a.Name, ct).ConfigureAwait(false);
        AccountingVoucherDocument document = new(company.Name, voucher.Type, voucher.Number, voucher.Date, voucher.Party, voucher.Narrative, company.BaseCurrency.Value,
            [.. voucher.Lines.Select(l => new VoucherDocumentLine(l.AccountCode, names.GetValueOrDefault(l.AccountCode, string.Empty), l.Narrative, l.Debit, l.Credit))],
            $"User {voucher.PreparedByUserId.ToString(CultureInfo.InvariantCulture)}",
            voucher.ApprovedByUserId is long approver ? $"User {approver.ToString(CultureInfo.InvariantCulture)}" : null,
            voucher.JournalNumber);
        return AccountingVoucherFormatter.ToPlainText(document);
    }

    private static string Sequence(AccountingVoucherType type) => type switch
    {
        AccountingVoucherType.Payment => "PV",
        AccountingVoucherType.Receipt => "RV",
        AccountingVoucherType.Journal => "JV",
        _ => "PCV",
    };

    private async Task<Result<AccountingVoucher>> LoadDraftAsync(long companyId, long voucherId, CancellationToken ct)
    {
        AccountingVoucher? voucher = await _db.Set<AccountingVoucher>().Include(v => v.Lines)
            .FirstOrDefaultAsync(v => v.Id == voucherId && v.CompanyId == companyId, ct).ConfigureAwait(false);
        if (voucher is null)
        {
            return Error.NotFound("Vou.VoucherNotFound", $"Voucher {voucherId} not found.");
        }

        if (voucher.Status != AccountingVoucherStatus.Draft)
        {
            return Error.Rule("Vou.NotDraft", $"Voucher {voucher.Number} is {voucher.Status}.");
        }

        return voucher;
    }
}
