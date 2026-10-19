using System.Globalization;
using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Accounting;
using One4All.Application.Abstractions.Numbering;
using One4All.Application.Abstractions.Vouchers;
using One4All.Application.Abstractions.Workflow;
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
    private readonly Lazy<IWorkflowService>? _workflow;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="engine">The posting engine.</param>
    /// <param name="numbers">The number sequences.</param>
    /// <param name="workflow">The approval workflow (null: approvals are direct only).</param>
    public AccountingVoucherService(One4AllDbContext db, IPostingEngine engine, INumberSequenceService numbers, Lazy<IWorkflowService>? workflow = null)
    {
        _db = db;
        _engine = engine;
        _numbers = numbers;
        _workflow = workflow;
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
            if (cashVoucher && !string.IsNullOrWhiteSpace(line.TaxCode))
            {
                // A VAT-inclusive amount: the net to the account and the VAT to the code's input (payment) or output (receipt) account, both tagged.
                bool purchase = request.Type != AccountingVoucherType.Receipt;
                Result<Accounting.GrossTaxSplit> split = await Accounting.GrossTaxSplit.SplitAsync(_db, companyId, line.TaxCode, line.Amount, request.Date, purchase, ct).ConfigureAwait(false);
                if (split.IsFailure)
                {
                    return split.Error;
                }

                voucher.AddLine(line.AccountCode.Trim(), purchase ? split.Value.Net : 0m, purchase ? 0m : split.Value.Net, line.Narrative, Dimension(line.CostCentre), Dimension(line.Department),
                    Dimension(line.Project), split.Value.Code);
                if (split.Value.Tax != 0m)
                {
                    voucher.AddLine(split.Value.TaxAccountCode, purchase ? split.Value.Tax : 0m, purchase ? 0m : split.Value.Tax, $"VAT ({split.Value.Code})", taxCode: split.Value.Code);
                }

                continue;
            }

            voucher.AddLine(line.AccountCode.Trim(), debit, credit, line.Narrative, Dimension(line.CostCentre), Dimension(line.Department), Dimension(line.Project));
        }

        _db.Set<AccountingVoucher>().Add(voucher);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        decimal total = request.Lines.Sum(l => Math.Abs(l.Amount)) / (cashVoucher ? 1m : 2m);

        // When the company routes vouchers through an approval workflow, it starts now; approving it in the inbox posts the voucher.
        Result<bool> governed = await Workflow.WorkflowGate.StartIfDefinedAsync(_db, _workflow, companyId, WorkflowDocumentTypes.AccountingVoucher, voucher.Number, total,
            request.PreparedByUserId, [("Type", request.Type.ToString()), ("Party", voucher.Party), ("CashAccount", voucher.CashAccountCode)], ct).ConfigureAwait(false);
        if (governed.IsFailure)
        {
            return governed.Error;
        }

        return new PreparedVoucher(voucher.Id, voucher.Number, total);
    }

    /// <inheritdoc />
    public Task<Result<string>> ApproveAsync(long companyId, long voucherId, long approverUserId, CancellationToken ct = default)
        => _db.AtomicAsync(() => ApproveCoreAsync(companyId, voucherId, approverUserId, fromWorkflow: false, ct), ct);

    /// <inheritdoc />
    public async Task<Result> CheckAsync(long companyId, long voucherId, long checkerUserId, CancellationToken ct = default)
    {
        Result<AccountingVoucher> found = await LoadDraftAsync(companyId, voucherId, ct).ConfigureAwait(false);
        if (found.IsFailure)
        {
            return Result.Failure(found.Error);
        }

        AccountingVoucher voucher = found.Value;
        if (checkerUserId == voucher.PreparedByUserId)
        {
            return Result.Failure(Error.Rule("Vou.SelfCheck", $"Voucher {voucher.Number} must be checked by someone other than the person who prepared it."));
        }

        if (voucher.CheckedByUserId is not null)
        {
            return Result.Failure(Error.Conflict("Vou.AlreadyChecked", $"Voucher {voucher.Number} has already been checked."));
        }

        voucher.MarkChecked(checkerUserId, DateTimeOffset.UtcNow);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    // One unit: the posting and the approval are kept together. From the workflow, the inbox decision is the approval.
    internal async Task<Result<string>> ApproveCoreAsync(long companyId, long voucherId, long approverUserId, bool fromWorkflow, CancellationToken ct)
    {
        Result<AccountingVoucher> found = await LoadDraftAsync(companyId, voucherId, ct).ConfigureAwait(false);
        if (found.IsFailure)
        {
            return found.Error;
        }

        AccountingVoucher voucher = found.Value;
        if (!fromWorkflow && await Workflow.WorkflowGate.InProgressAsync(_db, companyId, WorkflowDocumentTypes.AccountingVoucher, voucher.Number, ct).ConfigureAwait(false))
        {
            return Workflow.WorkflowGate.Governed(WorkflowDocumentTypes.AccountingVoucher, voucher.Number);
        }

        if (approverUserId == voucher.PreparedByUserId)
        {
            return Error.Rule("Vou.SelfApproval", $"Voucher {voucher.Number} must be approved by someone other than the person who prepared it.");
        }

        if (approverUserId == voucher.CheckedByUserId)
        {
            return Error.Rule("Vou.CheckerApproval", $"Voucher {voucher.Number} was checked by this user; someone else approves it.");
        }

        List<JournalLineRequest> lines = [.. voucher.Lines.Select(l => new JournalLineRequest
        {
            AccountCode = l.AccountCode, Side = l.Debit > 0m ? JournalSide.Debit : JournalSide.Credit, TransactionAmount = l.Debit > 0m ? l.Debit : l.Credit, Narrative = l.Narrative,
            CostCentre = l.CostCentre, Department = l.Department, Project = l.Project, TaxCode = l.TaxCode,
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
            voucher.JournalNumber)
        {
            CheckedBy = voucher.CheckedByUserId is long checker ? $"User {checker.ToString(CultureInfo.InvariantCulture)}" : null,
            Attachments = await _db.Set<Attachment>().AsNoTracking()
                .CountAsync(a => a.CompanyId == companyId && a.Target == AttachmentTarget.AccountingVoucher && a.TargetKey == voucher.Number && a.RemovedOnUtc == null, ct).ConfigureAwait(false),
        };
        return AccountingVoucherFormatter.ToPlainText(document);
    }

    private static string? Dimension(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToUpperInvariant();

    private static string Sequence(AccountingVoucherType type) => type switch
    {
        AccountingVoucherType.Payment => "PV",
        AccountingVoucherType.Receipt => "RV",
        AccountingVoucherType.Journal => "JV",
        _ => "PCV",
    };

    /// <summary>Cancels a voucher turned down in its approval workflow.</summary>
    internal async Task<Result> CancelFromWorkflowAsync(long companyId, long voucherId, CancellationToken ct)
    {
        Result<AccountingVoucher> found = await LoadDraftAsync(companyId, voucherId, ct).ConfigureAwait(false);
        if (found.IsFailure)
        {
            return Result.Success();
        }

        found.Value.Cancel();
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

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

/// <summary>Posts a voucher approved in its workflow, or cancels one rejected or returned there.</summary>
internal sealed class AccountingVoucherWorkflowHandler : IWorkflowDocumentHandler
{
    private readonly One4AllDbContext _db;
    private readonly IPostingEngine _engine;
    private readonly INumberSequenceService _numbers;

    public AccountingVoucherWorkflowHandler(One4AllDbContext db, IPostingEngine engine, INumberSequenceService numbers)
    {
        _db = db;
        _engine = engine;
        _numbers = numbers;
    }

    public string DocumentType => WorkflowDocumentTypes.AccountingVoucher;

    public async Task<Result> ConcludeAsync(long companyId, string documentReference, Core.Workflow.WorkflowStatus outcome, long decidedByUserId, string? comment, CancellationToken ct = default)
    {
        long? id = await _db.Set<AccountingVoucher>().Where(v => v.CompanyId == companyId && v.Number == documentReference).Select(v => (long?)v.Id).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (id is not long voucherId)
        {
            return Result.Failure(Error.NotFound("Vou.VoucherNotFound", $"Voucher {documentReference} not found."));
        }

        AccountingVoucherService vouchers = new(_db, _engine, _numbers);
        if (outcome != Core.Workflow.WorkflowStatus.Approved)
        {
            return await vouchers.CancelFromWorkflowAsync(companyId, voucherId, ct).ConfigureAwait(false);
        }

        Result<string> posted = await vouchers.ApproveCoreAsync(companyId, voucherId, decidedByUserId, fromWorkflow: true, ct).ConfigureAwait(false);
        return posted.IsSuccess ? Result.Success() : Result.Failure(posted.Error);
    }
}
