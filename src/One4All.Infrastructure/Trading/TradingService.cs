using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Accounting;
using One4All.Application.Abstractions.Trading;
using One4All.Core.Accounting;
using One4All.Core.Common;
using One4All.Core.System;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Trading;

/// <summary>
/// Implements the Sales Ledger, Purchase Ledger and allocation services (brief §4.5/§4.6). Every document
/// posts through the central posting engine (never a module-local journal) and creates an open item;
/// allocation matches items and realises FX from their differing base rates.
/// </summary>
public sealed class TradingService : ISalesLedgerService, IPurchaseLedgerService, IAllocationService
{
    private const int Scale = 4;
    private readonly One4AllDbContext _db;
    private readonly Application.Abstractions.Security.IPermissionService? _permissions;
    private readonly IPostingEngine _postingEngine;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="postingEngine">The posting engine.</param>
    /// <param name="webhooks">Publishes invoice and payment events.</param>
    /// <param name="permissions">Checks who may override discounts, rates and budget blocks.</param>
    /// <param name="budget">The budget gate an invoice without an order passes at posting (brief §4.18.5).</param>
    public TradingService(One4AllDbContext db, IPostingEngine postingEngine, Application.Abstractions.Integration.IWebhookService? webhooks = null,
        Application.Abstractions.Security.IPermissionService? permissions = null, Application.Abstractions.Budgeting.IBudgetGateService? budget = null, IFiscalDeviceProvider? fiscal = null)
    {
        _fiscal = fiscal;
        _budget = budget;
        _permissions = permissions;
        _db = db;
        _postingEngine = postingEngine;
        _webhooks = webhooks;
    }

    private readonly Application.Abstractions.Integration.IWebhookService? _webhooks;
    private readonly IFiscalDeviceProvider? _fiscal;
    private readonly Application.Abstractions.Budgeting.IBudgetGateService? _budget;

    /// <inheritdoc />
    public Task<Result<TradingPostingResult>> PostInvoiceAsync(SalesInvoiceRequest request, CancellationToken ct = default)
        => _db.AtomicAsync(async () =>
        {
            Result<TradingPostingResult> posted = await PostInvoiceCoreAsync(request, ct).ConfigureAwait(false);
            if (posted.IsSuccess && _webhooks is not null)
            {
                await _webhooks.PublishAsync(request.CompanyId, Application.Integration.WebhookEvents.InvoicePosted, new
                {
                    request.CustomerId, request.DocumentNumber, request.DocumentDate, Currency = request.Currency.Value, request.NetAmount, request.TaxAmount, posted.Value.JournalNumber,
                }, ct).ConfigureAwait(false);
            }

            return posted;
        }, ct);

    /// <inheritdoc />
    public Task<Result<IReadOnlyList<TradingPostingResult>>> PostInvoicesAsync(IReadOnlyList<SalesInvoiceRequest> requests, CancellationToken ct = default)
        => _db.AtomicAsync(() => PostInvoicesCoreAsync(requests, ct), ct);

    // One unit of work for a batch of invoices: the checks of the single path done once per batch where they can be, then the engine's batch path.
    private async Task<Result<IReadOnlyList<TradingPostingResult>>> PostInvoicesCoreAsync(IReadOnlyList<SalesInvoiceRequest> requests, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(requests);
        if (requests.Count == 0)
        {
            return Array.Empty<TradingPostingResult>();
        }

        long companyId = requests[0].CompanyId;
        Company company = await _db.Companies.FirstAsync(c => c.Id == companyId, ct).ConfigureAwait(false);
        if (requests.Any(r => r.CompanyId != companyId || r.Currency != company.BaseCurrency || r.DeliveryDate is not null || r.CreditLimitOverrideByUserId is not null))
        {
            return Error.Rule("Ar.BatchUnsupported", "Invoices in a foreign currency, with a delivery-date tax point or a credit-limit override post one at a time.");
        }

        foreach (SalesInvoiceRequest request in requests.Where(r => r.RevenueLines.Count > 0 && (r.RevenueLines.Sum(l => l.NetAmount) != r.NetAmount || r.RevenueLines.Any(l => string.IsNullOrWhiteSpace(l.AccountCode)))))
        {
            return Error.Validation("Ar.RevenueLinesMismatch", $"Invoice {request.DocumentNumber}: the revenue lines must each name an account and add up to the net amount.");
        }

        List<long> customerIds = [.. requests.Select(r => r.CustomerId).Distinct()];
        Dictionary<long, Customer> customers = await _db.Customers.Where(c => c.CompanyId == companyId && customerIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, ct).ConfigureAwait(false);
        foreach (SalesInvoiceRequest request in requests)
        {
            if (!customers.TryGetValue(request.CustomerId, out Customer? customer))
            {
                return Error.NotFound("Ar.CustomerNotFound", $"Customer {request.CustomerId} not found.");
            }

            if (customer.OnHold)
            {
                return Error.Rule("Ar.CustomerOnHold", $"Customer {customer.Code} is on hold; no new invoices can be posted.");
            }

            if (customer.CreditLimit > 0m && customer.CreditLimitPolicy != CreditLimitPolicy.Ignore)
            {
                return Error.Rule("Ar.BatchUnsupported", $"Customer {customer.Code} has a credit limit to check: the invoice posts on its own.");
            }
        }

        List<SalesInvoiceRequest> defaulted = [];
        foreach (SalesInvoiceRequest request in requests)
        {
            defaulted.Add(await WithTaxDefaultsAsync(request, customers[request.CustomerId], ct).ConfigureAwait(false));
        }

        requests = defaulted;

        // Exemption certificates and exempt tax codes, read once for the batch.
        DateOnly earliest = requests.Min(r => r.DocumentDate), latest = requests.Max(r => r.DocumentDate);
        List<TaxExemptionCertificate> certificates = await _db.TaxExemptionCertificates.AsNoTracking()
            .Where(c => c.CompanyId == companyId && c.Ledger == TradingLedger.AccountsReceivable && customerIds.Contains(c.TraderId) && c.ValidFrom <= latest && c.ValidTo >= earliest)
            .ToListAsync(ct).ConfigureAwait(false);
        List<string> taxCodes = [.. requests.Where(r => r.TaxCode is not null).Select(r => r.TaxCode!).Distinct()];
        HashSet<string> exemptCodes = taxCodes.Count == 0 ? [] : [.. await _db.TaxCodes.Where(t => t.CompanyId == companyId && taxCodes.Contains(t.Code) && t.Type == TaxType.Exempt).Select(t => t.Code).ToListAsync(ct).ConfigureAwait(false)];
        string? mainControl = null;
        List<JournalRequest> journals = [];
        List<(Customer Customer, decimal Gross, decimal BaseGross)> heads = [];
        foreach (SalesInvoiceRequest request in requests)
        {
            Customer customer = customers[request.CustomerId];
            bool certified = certificates.Any(c => c.TraderId == customer.Id && c.ValidFrom <= request.DocumentDate && c.ValidTo >= request.DocumentDate && (c.RevokedOn == null || c.RevokedOn > request.DocumentDate));
            if (certified && request.TaxAmount != 0m)
            {
                return Error.Rule("Tax.CustomerExempt", $"Customer {customer.Code} holds a valid tax exemption certificate: invoice without tax.");
            }

            if (!certified && request.TaxCode is string taxCode && exemptCodes.Contains(taxCode))
            {
                return Error.Rule("Tax.CertificateRequired", $"Customer {customer.Code} has no valid exemption certificate on {request.DocumentDate:yyyy-MM-dd}, so {taxCode} cannot be used.");
            }

            string control;
            if (customer.ReceivableControlAccountCode is string own)
            {
                control = own;
            }
            else
            {
                if (mainControl is null)
                {
                    Result<string> controlResult = await ControlCodeAsync(companyId, ControlAccountType.AccountsReceivable, ct).ConfigureAwait(false);
                    if (controlResult.IsFailure)
                    {
                        return controlResult.Error;
                    }

                    mainControl = controlResult.Value;
                }

                control = mainControl;
            }

            const decimal rate = 1m;
            decimal gross = request.NetAmount + request.TaxAmount;
            decimal baseNet = Round(request.NetAmount), baseTax = Round(request.TaxAmount), baseGross = baseNet + baseTax;
            List<JournalLineRequest> lines = [Line(control, JournalSide.Debit, gross, rate, baseGross)];
            if (request.RevenueLines.Count == 0)
            {
                lines.Add(Analysed(Line(request.SalesAccountCode, JournalSide.Credit, request.NetAmount, rate, baseNet), request));
            }
            else
            {
                decimal baseLeft = baseNet;
                for (int i = 0; i < request.RevenueLines.Count; i++)
                {
                    RevenueLine revenue = request.RevenueLines[i];
                    decimal baseShare = i == request.RevenueLines.Count - 1 ? baseLeft : Round(revenue.NetAmount);
                    baseLeft -= baseShare;
                    lines.Add(Analysed(Line(revenue.AccountCode, JournalSide.Credit, revenue.NetAmount, rate, baseShare), request, revenue));
                }
            }

            if (request.TaxAmount != 0m && request.TaxAccountCode is not null)
            {
                lines.Add(Line(request.TaxAccountCode, JournalSide.Credit, request.TaxAmount, rate, baseTax, request.TaxCode));
            }

            journals.Add(new JournalRequest
            {
                CompanyId = companyId, SourceModule = "AR", PostingDate = request.DocumentDate, TransactionCurrency = request.Currency, EnforceManualJournalRules = false,
                DocumentType = DocumentTypeFor(TradingLedger.AccountsReceivable, OpenItemType.Invoice), DocumentNumber = request.DocumentNumber, Narrative = request.Narrative, Lines = lines,
            });
            heads.Add((customer, gross, baseGross));
        }

        Result<IReadOnlyList<PostingReceipt>> posted = await _postingEngine.PostBatchAsync(journals, ct).ConfigureAwait(false);
        if (posted.IsFailure)
        {
            return posted.Error;
        }

        List<OpenItem> items = [];
        for (int i = 0; i < requests.Count; i++)
        {
            SalesInvoiceRequest request = requests[i];
            (Customer customer, decimal gross, decimal baseGross) = heads[i];
            items.Add(new OpenItem(companyId, TradingLedger.AccountsReceivable, customer.Id, OpenItemType.Invoice, LedgerSign.Debit, request.DocumentNumber, request.DocumentDate,
                customer.DueDateFor(request.DocumentDate), request.Currency, 1m, gross, baseGross) { JournalEntryId = posted.Value[i].JournalId, Narrative = request.Narrative });
        }

        _db.OpenItems.AddRange(items);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return items.Select((item, i) => new TradingPostingResult(item.Id, posted.Value[i].JournalNumber, heads[i].BaseGross)).ToList();
    }

    /// <inheritdoc />
    public Task<Result<TradingPostingResult>> PostDebitNoteAsync(SalesInvoiceRequest request, string originalInvoiceNumber, CancellationToken ct = default)
        => _db.AtomicAsync<TradingPostingResult>(async () =>
        {
            string original = (originalInvoiceNumber ?? string.Empty).Trim();
            if (!await _db.OpenItems.AnyAsync(o => o.CompanyId == request.CompanyId && o.Ledger == TradingLedger.AccountsReceivable && o.TraderId == request.CustomerId
                    && o.ItemType == OpenItemType.Invoice && o.DocumentNumber == original, ct).ConfigureAwait(false))
            {
                return Error.NotFound("Ar.OriginalInvoice", $"Invoice {original} is not on this customer's account.");
            }

            SalesInvoiceRequest note = new()
            {
                CompanyId = request.CompanyId, CustomerId = request.CustomerId, DocumentNumber = request.DocumentNumber, DocumentDate = request.DocumentDate, Currency = request.Currency,
                NetAmount = request.NetAmount, TaxAmount = request.TaxAmount, CreditLimitOverrideByUserId = request.CreditLimitOverrideByUserId, EnteredByUserId = request.EnteredByUserId,
                SalesAccountCode = request.SalesAccountCode, RevenueLines = request.RevenueLines, TaxAccountCode = request.TaxAccountCode, TaxCode = request.TaxCode,
                ExchangeRate = request.ExchangeRate, RateOverrideByUserId = request.RateOverrideByUserId,
                Narrative = string.IsNullOrWhiteSpace(request.Narrative) ? $"Debit note on {original}" : $"{request.Narrative.Trim()} (on {original})",
            };
            return await PostInvoiceCoreAsync(note, ct, OpenItemType.DebitNote).ConfigureAwait(false);
        }, ct);

    // One unit of work: the journal, the open item and any allocation commit together (retried on a lost race).
    private async Task<Result<TradingPostingResult>> PostInvoiceCoreAsync(SalesInvoiceRequest request, CancellationToken ct, OpenItemType itemType = OpenItemType.Invoice)
    {
        Customer? customer = await _db.Customers.FirstOrDefaultAsync(c => c.Id == request.CustomerId && c.CompanyId == request.CompanyId, ct).ConfigureAwait(false);
        if (customer is null)
        {
            return Error.NotFound("Ar.CustomerNotFound", $"Customer {request.CustomerId} not found.");
        }

        if (customer.OnHold)
        {
            return Error.Rule("Ar.CustomerOnHold", $"Customer {customer.Code} is on hold; no new invoices can be posted.");
        }

        request = await WithTaxDefaultsAsync(request, customer, ct).ConfigureAwait(false);

        // Exemption certificates: a certified customer is not charged tax, and an exempt supply needs a valid certificate.
        bool certified = await _db.TaxExemptionCertificates.AnyAsync(c => c.CompanyId == request.CompanyId && c.Ledger == TradingLedger.AccountsReceivable && c.TraderId == customer.Id
            && c.ValidFrom <= request.DocumentDate && c.ValidTo >= request.DocumentDate && (c.RevokedOn == null || c.RevokedOn > request.DocumentDate), ct).ConfigureAwait(false);
        if (certified && request.TaxAmount != 0m)
        {
            return Error.Rule("Tax.CustomerExempt", $"Customer {customer.Code} holds a valid tax exemption certificate: invoice without tax.");
        }

        if (!certified && request.TaxCode is string taxCode
            && await _db.TaxCodes.AnyAsync(t => t.CompanyId == request.CompanyId && t.Code == taxCode && t.Type == TaxType.Exempt, ct).ConfigureAwait(false))
        {
            return Error.Rule("Tax.CertificateRequired", $"Customer {customer.Code} has no valid exemption certificate on {request.DocumentDate:yyyy-MM-dd}, so {taxCode} cannot be used.");
        }

        if (request.RevenueLines.Count > 0
            && (request.RevenueLines.Sum(r => r.NetAmount) != request.NetAmount || request.RevenueLines.Any(r => string.IsNullOrWhiteSpace(r.AccountCode))))
        {
            return Error.Validation("Ar.RevenueLinesMismatch", "The revenue lines must each name an account and add up to the net amount.");
        }

        Result<string> controlResult = await ReceivableControlAsync(request.CompanyId, customer, ct).ConfigureAwait(false);
        if (controlResult.IsFailure)
        {
            return controlResult.Error;
        }

        Result<decimal> rateResult = await ResolveRateAsync(request.CompanyId, request.Currency, request.ExchangeRate, request.DocumentDate, ct, customer.RateType, (request.RateOverrideByUserId, request.DocumentNumber), "SalesInvoice").ConfigureAwait(false);
        if (rateResult.IsFailure)
        {
            return rateResult.Error;
        }

        decimal rate = rateResult.Value;
        decimal gross = request.NetAmount + request.TaxAmount;
        decimal baseNet = Round(request.NetAmount * rate);
        decimal baseTax = Round(request.TaxAmount * rate);
        decimal baseGross = baseNet + baseTax;

        // Credit-limit control under the customer's policy: block (refuse unless a supervisor overrides), warn (post, flagged and logged) or ignore.
        bool overLimit = false;
        if (customer.CreditLimit > 0m && customer.CreditLimitPolicy != CreditLimitPolicy.Ignore)
        {
            // Lock the customer for the rest of the transaction so concurrent invoices queue here: each then sees the
            // others' committed items, and two cannot both squeeze under the limit.
            await _db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM ar.customer WHERE id = {customer.Id} FOR UPDATE", ct).ConfigureAwait(false);
            IQueryable<OpenItem> ar = _db.OpenItems.Where(i => i.CompanyId == request.CompanyId && i.Ledger == TradingLedger.AccountsReceivable && i.TraderId == customer.Id);
            decimal debits = await ar.Where(i => i.Sign == LedgerSign.Debit).SumAsync(i => (decimal?)i.OutstandingBase, ct).ConfigureAwait(false) ?? 0m;
            decimal credits = await ar.Where(i => i.Sign == LedgerSign.Credit).SumAsync(i => (decimal?)i.OutstandingBase, ct).ConfigureAwait(false) ?? 0m;
            decimal outstanding = debits - credits;
            if (outstanding + baseGross > customer.CreditLimit && customer.CreditLimitPolicy == CreditLimitPolicy.Warn)
            {
                overLimit = true;
                _db.Set<CreditLimitOverride>().Add(new CreditLimitOverride(request.CompanyId, customer.Id, request.DocumentNumber, customer.CreditLimit, outstanding + baseGross, 0,
                    request.EnteredByUserId, DateTimeOffset.UtcNow));
            }
            else if (outstanding + baseGross > customer.CreditLimit)
            {
                // Only a supervisor who may override credit limits (not whoever raised the invoice) lets it through, and the override is kept.
                if (request.CreditLimitOverrideByUserId is not long overrider)
                {
                    return Error.Rule("Ar.CreditLimitExceeded", $"Customer {customer.Code}: this invoice would take the balance to {outstanding + baseGross:0.00}, over the credit limit of {customer.CreditLimit:0.00}.");
                }

                if (overrider == request.EnteredByUserId)
                {
                    return Error.Rule("Ar.CreditOverrideSelf", "The person raising the invoice cannot override the credit limit for it.");
                }

                if (_permissions is null || !(await _permissions.ResolveAsync(overrider, ct).ConfigureAwait(false)).Has("Sales.Customer", Core.Security.OperationFlags.OverrideCreditLimit))
                {
                    return Error.Forbidden("Ar.CreditOverrideNotAllowed", "Only a user who may override credit limits can let this invoice through.");
                }

                _db.Set<CreditLimitOverride>().Add(new CreditLimitOverride(request.CompanyId, customer.Id, request.DocumentNumber, customer.CreditLimit, outstanding + baseGross, overrider,
                    request.EnteredByUserId, DateTimeOffset.UtcNow));
            }
        }

        List<JournalLineRequest> lines = [Line(controlResult.Value, JournalSide.Debit, gross, rate, baseGross)];
        if (request.RevenueLines.Count == 0)
        {
            lines.Add(Analysed(Line(request.SalesAccountCode, JournalSide.Credit, request.NetAmount, rate, baseNet), request));
        }
        else
        {
            // Each revenue account gets its share; the last absorbs base rounding so the credits equal the base net.
            decimal baseLeft = baseNet;
            for (int i = 0; i < request.RevenueLines.Count; i++)
            {
                RevenueLine revenue = request.RevenueLines[i];
                decimal baseShare = i == request.RevenueLines.Count - 1 ? baseLeft : Round(revenue.NetAmount * rate);
                baseLeft -= baseShare;
                lines.Add(Analysed(Line(revenue.AccountCode, JournalSide.Credit, revenue.NetAmount, rate, baseShare), request, revenue));
            }
        }

        if (request.TaxAmount != 0m && request.TaxAccountCode is not null)
        {
            lines.Add(Line(request.TaxAccountCode, JournalSide.Credit, request.TaxAmount, rate, baseTax, request.TaxCode));
        }

        Result<TradingPostingResult> opened = await PostAndOpenAsync(request.CompanyId, "AR", TradingLedger.AccountsReceivable, customer.Id, itemType,
            LedgerSign.Debit, request.DocumentNumber, request.DocumentDate, customer.DueDateFor(request.DocumentDate),
            request.Currency, rate, gross, baseGross, request.Narrative, lines, ct, request.TaxAmount).ConfigureAwait(false);

        // The tax point, when delivery moves it off the invoice date.
        if (opened.IsSuccess && request.DeliveryDate is DateOnly delivered && TaxPoints.Determine(request.DocumentDate, delivered) is DateOnly point && point != request.DocumentDate)
        {
            OpenItem item = await _db.OpenItems.SingleAsync(o => o.Id == opened.Value.OpenItemId, ct).ConfigureAwait(false);
            item.TaxPointDate = point;
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        return opened.IsSuccess && overLimit ? opened.Value with { OverCreditLimit = true } : opened;
    }

    /// <inheritdoc />
    public Task<Result> ContraAsync(long companyId, long customerId, long customerInvoiceItemId, long supplierId, long supplierInvoiceItemId, decimal amount, DateOnly date, string contraClearingAccountCode, string documentNumber, CancellationToken ct = default)
        => _db.AtomicAsync(() => ContraCoreAsync(companyId, customerId, customerInvoiceItemId, supplierId, supplierInvoiceItemId, amount, date, contraClearingAccountCode, documentNumber, ct), ct);

    // One unit of work: the journal, the open item and any allocation commit together (retried on a lost race).
    private async Task<Result> ContraCoreAsync(long companyId, long customerId, long customerInvoiceItemId, long supplierId, long supplierInvoiceItemId, decimal amount, DateOnly date, string contraClearingAccountCode, string documentNumber, CancellationToken ct)
    {
        if (amount <= 0m)
        {
            return Result.Failure(Error.Rule("Ar.ContraNonPositive", "The contra amount must be positive."));
        }

        if (string.IsNullOrWhiteSpace(contraClearingAccountCode))
        {
            return Result.Failure(Error.Validation("Ar.NoContraAccount", "A contra needs a clearing account."));
        }

        OpenItem? arItem = await _db.OpenItems.FirstOrDefaultAsync(i => i.Id == customerInvoiceItemId && i.CompanyId == companyId && i.Ledger == TradingLedger.AccountsReceivable && i.TraderId == customerId, ct).ConfigureAwait(false);
        OpenItem? apItem = await _db.OpenItems.FirstOrDefaultAsync(i => i.Id == supplierInvoiceItemId && i.CompanyId == companyId && i.Ledger == TradingLedger.AccountsPayable && i.TraderId == supplierId, ct).ConfigureAwait(false);
        if (arItem is null || apItem is null)
        {
            return Result.Failure(Error.NotFound("Ar.ContraItemNotFound", "The customer or supplier invoice open item was not found."));
        }

        if (amount > arItem.OutstandingTransaction || amount > apItem.OutstandingTransaction)
        {
            return Result.Failure(Error.Rule("Ar.ContraTooLarge", $"The contra {amount:0.00} exceeds an item's outstanding (AR {arItem.OutstandingTransaction:0.00}, AP {apItem.OutstandingTransaction:0.00})."));
        }

        // AR leg: a receipt to the clearing account settles the debtor invoice.
        Result<TradingPostingResult> arReceipt = await PostReceiptAsync(new ReceiptRequest
        {
            CompanyId = companyId, CustomerId = customerId, DocumentNumber = documentNumber, ReceiptDate = date,
            Currency = arItem.Currency, Amount = amount, BankAccountCode = contraClearingAccountCode, Narrative = "Contra settlement",
        }, ct).ConfigureAwait(false);
        if (arReceipt.IsFailure)
        {
            return Result.Failure(arReceipt.Error);
        }

        Result<AllocationResult> arAlloc = await AllocateAsync(companyId, TradingLedger.AccountsReceivable, customerInvoiceItemId, arReceipt.Value.OpenItemId, amount, date, ct).ConfigureAwait(false);
        if (arAlloc.IsFailure)
        {
            return Result.Failure(arAlloc.Error);
        }

        // AP leg: a payment from the clearing account settles the creditor invoice (clearing nets to zero).
        Result<TradingPostingResult> apPayment = await PostPaymentAsync(new PaymentRequest
        {
            CompanyId = companyId, SupplierId = supplierId, DocumentNumber = documentNumber, PaymentDate = date,
            Currency = apItem.Currency, Amount = amount, BankAccountCode = contraClearingAccountCode, Narrative = "Contra settlement",
        }, ct).ConfigureAwait(false);
        if (apPayment.IsFailure)
        {
            return Result.Failure(apPayment.Error);
        }

        Result<AllocationResult> apAlloc = await AllocateAsync(companyId, TradingLedger.AccountsPayable, apPayment.Value.OpenItemId, supplierInvoiceItemId, amount, date, ct).ConfigureAwait(false);
        return apAlloc.IsFailure ? Result.Failure(apAlloc.Error) : Result.Success();
    }

    /// <inheritdoc />
    public Task<Result<TradingPostingResult>> WriteOffAsync(long companyId, long openItemId, DateOnly date, string badDebtAccountCode, string reason, CancellationToken ct = default)
        => _db.AtomicAsync(() => WriteOffCoreAsync(companyId, openItemId, date, badDebtAccountCode, reason, ct), ct);

    // One unit of work: the journal, the open item and any allocation commit together (retried on a lost race).
    private async Task<Result<TradingPostingResult>> WriteOffCoreAsync(long companyId, long openItemId, DateOnly date, string badDebtAccountCode, string reason, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return Error.Validation("Ar.ReasonRequired", "A bad-debt write-off requires a reason.");
        }

        if (string.IsNullOrWhiteSpace(badDebtAccountCode))
        {
            return Error.Validation("Ar.NoBadDebtAccount", "A bad-debt write-off requires a bad-debt account.");
        }

        OpenItem? item = await _db.OpenItems.FirstOrDefaultAsync(i => i.Id == openItemId && i.CompanyId == companyId && i.Ledger == TradingLedger.AccountsReceivable, ct).ConfigureAwait(false);
        if (item is null)
        {
            return Error.NotFound("Ar.OpenItemNotFound", $"AR open item {openItemId} not found.");
        }

        if (item.Sign != LedgerSign.Debit || item.OutstandingTransaction <= 0m)
        {
            return Error.Rule("Ar.NothingToWriteOff", "Only an outstanding debtor item (invoice) can be written off.");
        }

        // Post the write-off as a "receipt" whose money side is the bad-debt expense (Dr bad debt / Cr AR control).
        Result<TradingPostingResult> writeOff = await PostReceiptAsync(new ReceiptRequest
        {
            CompanyId = companyId,
            CustomerId = item.TraderId,
            DocumentNumber = $"WO{openItemId}",
            ReceiptDate = date,
            Currency = item.Currency,
            Amount = item.OutstandingTransaction,
            BankAccountCode = badDebtAccountCode,
            ExchangeRate = item.ExchangeRate,
            Narrative = $"Bad-debt write-off of {item.DocumentNumber}: {reason}",
        }, ct).ConfigureAwait(false);

        if (writeOff.IsFailure)
        {
            return writeOff.Error;
        }

        // Allocate the write-off against the invoice so it settles and stops ageing.
        Result<AllocationResult> allocation = await AllocateAsync(companyId, TradingLedger.AccountsReceivable, openItemId, writeOff.Value.OpenItemId, item.OutstandingTransaction, date, ct).ConfigureAwait(false);
        if (allocation.IsFailure)
        {
            return allocation.Error;
        }

        return writeOff.Value;
    }

    /// <inheritdoc />
    public Task<Result<VatReliefWriteOff>> WriteOffWithVatReliefAsync(long companyId, long openItemId, DateOnly date, string badDebtAccountCode, string reason, int minimumDaysOverdue, CancellationToken ct = default)
        => _db.AtomicAsync(() => WriteOffWithVatReliefCoreAsync(companyId, openItemId, date, badDebtAccountCode, reason, minimumDaysOverdue, ct), ct);

    // One unit: the net write-off, the VAT relief and both allocations commit together.
    private async Task<Result<VatReliefWriteOff>> WriteOffWithVatReliefCoreAsync(long companyId, long openItemId, DateOnly date, string badDebtAccountCode, string reason, int minimumDaysOverdue, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(reason) || string.IsNullOrWhiteSpace(badDebtAccountCode))
        {
            return Error.Validation("Ar.ReasonRequired", "A bad-debt write-off requires a reason and a bad-debt account.");
        }

        OpenItem? item = await _db.OpenItems.FirstOrDefaultAsync(i => i.Id == openItemId && i.CompanyId == companyId && i.Ledger == TradingLedger.AccountsReceivable, ct).ConfigureAwait(false);
        if (item is null)
        {
            return Error.NotFound("Ar.OpenItemNotFound", $"AR open item {openItemId} not found.");
        }

        if (item.Sign != LedgerSign.Debit || item.OutstandingTransaction <= 0m)
        {
            return Error.Rule("Ar.NothingToWriteOff", "Only an outstanding debtor item (invoice) can be written off.");
        }

        if (date.DayNumber - item.DueDate.DayNumber < minimumDaysOverdue)
        {
            return Error.Rule("Ar.VatReliefTooEarly", $"{item.DocumentNumber} has not been overdue for {minimumDaysOverdue} days, so it does not yet qualify for VAT relief.");
        }

        // The invoice's own tax lines (tax-coded, or to the output VAT account) give the VAT share of what is still owed.
        List<string> vatOutput = await _db.Accounts.Where(a => a.CompanyId == companyId && a.ControlAccountType == ControlAccountType.VatOutput)
            .Select(a => a.AccountCode).ToListAsync(ct).ConfigureAwait(false);
        List<(string Account, decimal Base)> taxLines = item.JournalEntryId is not long journalId ? [] : [.. (await _db.Set<JournalLine>()
                .Where(l => l.JournalEntryId == journalId && (l.TaxCode != null || vatOutput.Contains(l.AccountCode)) && l.BaseCredit > 0m)
                .Select(l => new { l.AccountCode, l.BaseCredit })
                .ToListAsync(ct).ConfigureAwait(false))
            .GroupBy(l => l.AccountCode).Select(g => (g.Key, g.Sum(l => l.BaseCredit)))];
        decimal outstanding = item.OutstandingTransaction;
        decimal vat = item.OriginalBase == 0m ? 0m : decimal.Round(outstanding * taxLines.Sum(t => t.Base) / item.OriginalBase, 2, MidpointRounding.AwayFromZero);

        Result<TradingPostingResult> net = await PostReceiptAsync(new ReceiptRequest
        {
            CompanyId = companyId, CustomerId = item.TraderId, DocumentNumber = $"WO{openItemId}", ReceiptDate = date, Currency = item.Currency,
            Amount = outstanding - vat, BankAccountCode = badDebtAccountCode, ExchangeRate = item.ExchangeRate,
            Narrative = $"Bad-debt write-off of {item.DocumentNumber} (net of VAT): {reason}",
        }, ct).ConfigureAwait(false);
        if (net.IsFailure)
        {
            return net.Error;
        }

        Result<AllocationResult> netAllocation = await AllocateAsync(companyId, TradingLedger.AccountsReceivable, openItemId, net.Value.OpenItemId, outstanding - vat, date, ct).ConfigureAwait(false);
        if (netAllocation.IsFailure)
        {
            return netAllocation.Error;
        }

        long? reliefItemId = null;
        if (vat > 0m)
        {
            // Reclaimed against the output VAT account the invoice credited (the largest, if it used several).
            string vatAccount = taxLines.OrderByDescending(t => t.Base).First().Account;
            Result<TradingPostingResult> relief = await PostReceiptAsync(new ReceiptRequest
            {
                CompanyId = companyId, CustomerId = item.TraderId, DocumentNumber = $"WOV{openItemId}", ReceiptDate = date, Currency = item.Currency,
                Amount = vat, BankAccountCode = vatAccount, ExchangeRate = item.ExchangeRate,
                Narrative = $"Bad-debt VAT relief on {item.DocumentNumber}",
            }, ct).ConfigureAwait(false);
            if (relief.IsFailure)
            {
                return relief.Error;
            }

            Result<AllocationResult> reliefAllocation = await AllocateAsync(companyId, TradingLedger.AccountsReceivable, openItemId, relief.Value.OpenItemId, vat, date, ct).ConfigureAwait(false);
            if (reliefAllocation.IsFailure)
            {
                return reliefAllocation.Error;
            }

            reliefItemId = relief.Value.OpenItemId;
        }

        return new VatReliefWriteOff(outstanding - vat, vat, net.Value.OpenItemId, reliefItemId);
    }

    /// <inheritdoc />
    public Task<Result<TradingPostingResult>> WriteBackAsync(long companyId, long openItemId, DateOnly date, string writeBackAccountCode, string reason, CancellationToken ct = default)
        => _db.AtomicAsync(() => WriteBackCoreAsync(companyId, openItemId, date, writeBackAccountCode, reason, ct), ct);

    // One unit of work: the journal, the open item and any allocation commit together (retried on a lost race).
    private async Task<Result<TradingPostingResult>> WriteBackCoreAsync(long companyId, long openItemId, DateOnly date, string writeBackAccountCode, string reason, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return Error.Validation("Ap.ReasonRequired", "A creditor write-back requires a reason.");
        }

        if (string.IsNullOrWhiteSpace(writeBackAccountCode))
        {
            return Error.Validation("Ap.NoWriteBackAccount", "A creditor write-back requires a write-back income account.");
        }

        OpenItem? item = await _db.OpenItems.FirstOrDefaultAsync(i => i.Id == openItemId && i.CompanyId == companyId && i.Ledger == TradingLedger.AccountsPayable, ct).ConfigureAwait(false);
        if (item is null)
        {
            return Error.NotFound("Ap.OpenItemNotFound", $"AP open item {openItemId} not found.");
        }

        if (item.Sign != LedgerSign.Credit || item.OutstandingTransaction <= 0m)
        {
            return Error.Rule("Ap.NothingToWriteBack", "Only an outstanding creditor item (invoice) can be written back.");
        }

        // Post the write-back as a "payment" whose money side is the write-back income (Dr AP control / Cr income).
        Result<TradingPostingResult> writeBack = await PostPaymentAsync(new PaymentRequest
        {
            CompanyId = companyId,
            SupplierId = item.TraderId,
            DocumentNumber = $"WB{openItemId}",
            PaymentDate = date,
            Currency = item.Currency,
            Amount = item.OutstandingTransaction,
            BankAccountCode = writeBackAccountCode,
            ExchangeRate = item.ExchangeRate,
            Narrative = $"Creditor write-back of {item.DocumentNumber}: {reason}",
        }, ct).ConfigureAwait(false);

        if (writeBack.IsFailure)
        {
            return writeBack.Error;
        }

        // On AP the payment is the debit item, the invoice the credit item; allocate so the invoice settles.
        Result<AllocationResult> allocation = await AllocateAsync(companyId, TradingLedger.AccountsPayable, writeBack.Value.OpenItemId, openItemId, item.OutstandingTransaction, date, ct).ConfigureAwait(false);
        if (allocation.IsFailure)
        {
            return allocation.Error;
        }

        return writeBack.Value;
    }

    /// <inheritdoc />
    public Task<Result<TradingPostingResult>> SettleWithDiscountAsync(long companyId, long invoiceOpenItemId, DateOnly date, decimal cashAmount, decimal discountAmount, string bankAccountCode, string discountAccountCode, string documentNumber,
        string? taxAccountCode = null, CancellationToken ct = default)
        => _db.AtomicAsync(() => SettleWithDiscountCoreAsync(companyId, invoiceOpenItemId, date, cashAmount, discountAmount, bankAccountCode, discountAccountCode, documentNumber, taxAccountCode, ct), ct);

    /// <inheritdoc />
    public async Task<Result<decimal>> SuggestSettlementDiscountAsync(long companyId, long invoiceOpenItemId, DateOnly paidOn, CancellationToken ct = default)
    {
        OpenItem? invoice = await _db.OpenItems.AsNoTracking()
            .FirstOrDefaultAsync(i => i.Id == invoiceOpenItemId && i.CompanyId == companyId && i.Ledger == TradingLedger.AccountsReceivable, ct).ConfigureAwait(false);
        if (invoice is null)
        {
            return Error.NotFound("Ar.OpenItemNotFound", $"AR open item {invoiceOpenItemId} not found.");
        }

        var terms = await _db.Customers.AsNoTracking().Where(c => c.Id == invoice.TraderId).Select(c => new { c.SettlementDiscountPercent, c.SettlementDiscountDays })
            .FirstAsync(ct).ConfigureAwait(false);
        DateOnly lastDay = terms.SettlementDiscountDays > 0 ? invoice.DocumentDate.AddDays(terms.SettlementDiscountDays) : invoice.DueDate;
        return invoice.Sign != LedgerSign.Debit || invoice.OutstandingTransaction <= 0m || terms.SettlementDiscountPercent <= 0m || paidOn > lastDay
            ? 0m
            : Round(invoice.OutstandingTransaction * terms.SettlementDiscountPercent / 100m);
    }

    // The tax share of a settlement discount (brief §4.5/§4.6): the discount reduces the consideration, so the invoice's tax-to-gross share of it
    // comes back off output tax (a customer's discount) or input tax (a supplier's), moved from the discount account to the tax account.
    private async Task<Result<string?>> DiscountTaxAsync(OpenItem invoice, DateOnly date, decimal discount, string discountAccountCode, string? taxAccountCode, string documentNumber, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(taxAccountCode) || invoice.JournalEntryId is not long journalId || invoice.OriginalTransaction == 0m)
        {
            return Result<string?>.Success(null);
        }

        bool sales = invoice.Ledger == TradingLedger.AccountsReceivable;
        var taxLines = await _db.Set<JournalLine>().AsNoTracking().Where(l => l.JournalEntryId == journalId && l.AccountCode == taxAccountCode)
            .Select(l => new { Amount = sales ? l.TransactionCredit - l.TransactionDebit : l.TransactionDebit - l.TransactionCredit, l.TaxCode }).ToListAsync(ct).ConfigureAwait(false);
        decimal tax = taxLines.Sum(l => l.Amount);
        decimal share = Round(discount * tax / invoice.OriginalTransaction);
        if (share <= 0m)
        {
            return Result<string?>.Success(null);
        }

        string? taxCode = taxLines.Select(l => l.TaxCode).FirstOrDefault(c => !string.IsNullOrEmpty(c));
        decimal baseShare = Round(share * invoice.ExchangeRate);
        Result<PostingReceipt> posting = await _postingEngine.PostAsync(new JournalRequest
        {
            CompanyId = invoice.CompanyId,
            SourceModule = sales ? "AR" : "AP",
            PostingDate = date,
            TransactionCurrency = invoice.Currency,
            EnforceManualJournalRules = false,
            DocumentType = "SettlementDiscountTax",
            DocumentNumber = $"{documentNumber}-TAX",
            Narrative = $"Tax on settlement discount for {invoice.DocumentNumber}",
            Lines = sales
                ? [Line(taxAccountCode, JournalSide.Debit, share, invoice.ExchangeRate, baseShare, taxCode), Line(discountAccountCode, JournalSide.Credit, share, invoice.ExchangeRate, baseShare)]
                : [Line(discountAccountCode, JournalSide.Debit, share, invoice.ExchangeRate, baseShare), Line(taxAccountCode, JournalSide.Credit, share, invoice.ExchangeRate, baseShare, taxCode)],
        }, ct).ConfigureAwait(false);
        return posting.IsFailure ? posting.Error : posting.Value.JournalNumber;
    }

    // One unit of work: the journal, the open item and any allocation commit together (retried on a lost race).
    private async Task<Result<TradingPostingResult>> SettleWithDiscountCoreAsync(long companyId, long invoiceOpenItemId, DateOnly date, decimal cashAmount, decimal discountAmount, string bankAccountCode, string discountAccountCode, string documentNumber,
        string? taxAccountCode, CancellationToken ct)
    {
        if (cashAmount <= 0m || discountAmount <= 0m)
        {
            return Error.Rule("Ar.SettlementNonPositive", "The cash and discount amounts must both be positive.");
        }

        OpenItem? invoice = await _db.OpenItems.FirstOrDefaultAsync(i => i.Id == invoiceOpenItemId && i.CompanyId == companyId && i.Ledger == TradingLedger.AccountsReceivable, ct).ConfigureAwait(false);
        if (invoice is null)
        {
            return Error.NotFound("Ar.OpenItemNotFound", $"AR open item {invoiceOpenItemId} not found.");
        }

        if (invoice.Sign != LedgerSign.Debit || invoice.OutstandingTransaction <= 0m)
        {
            return Error.Rule("Ar.NothingToSettle", "Only an outstanding debtor item (invoice) can be settled.");
        }

        if (cashAmount + discountAmount > invoice.OutstandingTransaction)
        {
            return Error.Rule("Ar.SettlementExceedsOutstanding", $"The cash {cashAmount:0.00} plus discount {discountAmount:0.00} exceeds the outstanding {invoice.OutstandingTransaction:0.00}.");
        }

        // Cash receipt: Dr bank / Cr AR control.
        Result<TradingPostingResult> cash = await PostReceiptAsync(new ReceiptRequest
        {
            CompanyId = companyId, CustomerId = invoice.TraderId, DocumentNumber = documentNumber, ReceiptDate = date,
            Currency = invoice.Currency, Amount = cashAmount, BankAccountCode = bankAccountCode, ExchangeRate = invoice.ExchangeRate,
            Narrative = $"Receipt with settlement discount for {invoice.DocumentNumber}",
        }, ct).ConfigureAwait(false);
        if (cash.IsFailure)
        {
            return cash.Error;
        }

        Result<AllocationResult> allocCash = await AllocateAsync(companyId, TradingLedger.AccountsReceivable, invoiceOpenItemId, cash.Value.OpenItemId, cashAmount, date, ct).ConfigureAwait(false);
        if (allocCash.IsFailure)
        {
            return allocCash.Error;
        }

        // Discount: posted as a "receipt" whose money side is the discounts-allowed account (Dr discount / Cr AR).
        Result<TradingPostingResult> discount = await PostReceiptAsync(new ReceiptRequest
        {
            CompanyId = companyId, CustomerId = invoice.TraderId, DocumentNumber = $"{documentNumber}-DISC", ReceiptDate = date,
            Currency = invoice.Currency, Amount = discountAmount, BankAccountCode = discountAccountCode, ExchangeRate = invoice.ExchangeRate,
            Narrative = $"Settlement discount for {invoice.DocumentNumber}",
        }, ct).ConfigureAwait(false);
        if (discount.IsFailure)
        {
            return discount.Error;
        }

        Result<AllocationResult> allocDiscount = await AllocateAsync(companyId, TradingLedger.AccountsReceivable, invoiceOpenItemId, discount.Value.OpenItemId, discountAmount, date, ct).ConfigureAwait(false);
        if (allocDiscount.IsFailure)
        {
            return allocDiscount.Error;
        }

        Result<string?> discountTax = await DiscountTaxAsync(invoice, date, discountAmount, discountAccountCode, taxAccountCode, documentNumber, ct).ConfigureAwait(false);
        return discountTax.IsFailure ? discountTax.Error : cash.Value;
    }

    /// <inheritdoc />
    public Task<Result<TradingPostingResult>> PostReceiptAsync(ReceiptRequest request, CancellationToken ct = default)
        => _db.AtomicAsync(async () =>
        {
            Result<TradingPostingResult> posted = await PostReceiptCoreAsync(request, ct).ConfigureAwait(false);
            if (posted.IsSuccess && _webhooks is not null)
            {
                await _webhooks.PublishAsync(request.CompanyId, Application.Integration.WebhookEvents.PaymentReceived, new
                {
                    request.CustomerId, request.DocumentNumber, request.ReceiptDate, Currency = request.Currency.Value, request.Amount, posted.Value.JournalNumber,
                }, ct).ConfigureAwait(false);
            }

            return posted;
        }, ct);

    // One unit of work: the journal, the open item and any allocation commit together (retried on a lost race).
    private async Task<Result<TradingPostingResult>> PostReceiptCoreAsync(ReceiptRequest request, CancellationToken ct)
    {
        Customer? customer = await _db.Customers.FirstOrDefaultAsync(c => c.Id == request.CustomerId && c.CompanyId == request.CompanyId, ct).ConfigureAwait(false);
        if (customer is null)
        {
            return Error.NotFound("Ar.CustomerNotFound", $"Customer {request.CustomerId} not found.");
        }

        Result<string> controlResult = await ReceivableControlAsync(request.CompanyId, customer, ct).ConfigureAwait(false);
        if (controlResult.IsFailure)
        {
            return controlResult.Error;
        }

        Result<decimal> rateResult = await ResolveRateAsync(request.CompanyId, request.Currency, request.ExchangeRate, request.ReceiptDate, ct, customer.RateType, (request.RateOverrideByUserId, request.DocumentNumber), "Receipt").ConfigureAwait(false);
        if (rateResult.IsFailure)
        {
            return rateResult.Error;
        }

        decimal rate = rateResult.Value;
        decimal baseAmount = Round(request.Amount * rate);

        List<JournalLineRequest> lines =
        [
            Line(request.BankAccountCode, JournalSide.Debit, request.Amount, rate, baseAmount),
            Line(controlResult.Value, JournalSide.Credit, request.Amount, rate, baseAmount),
        ];

        return await PostAndOpenAsync(request.CompanyId, "AR", TradingLedger.AccountsReceivable, customer.Id, OpenItemType.Receipt,
            LedgerSign.Credit, request.DocumentNumber, request.ReceiptDate, request.ReceiptDate, request.Currency, rate,
            request.Amount, baseAmount, request.Narrative, lines, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<Result<TradingPostingResult>> PostCreditNoteAsync(SalesCreditNoteRequest request, CancellationToken ct = default)
        => _db.AtomicAsync(() => PostCreditNoteCoreAsync(request, ct), ct);

    // One unit of work: the journal, the open item and any allocation commit together (retried on a lost race).
    private async Task<Result<TradingPostingResult>> PostCreditNoteCoreAsync(SalesCreditNoteRequest request, CancellationToken ct)
    {
        Customer? customer = await _db.Customers.FirstOrDefaultAsync(c => c.Id == request.CustomerId && c.CompanyId == request.CompanyId, ct).ConfigureAwait(false);
        if (customer is null)
        {
            return Error.NotFound("Ar.CustomerNotFound", $"Customer {request.CustomerId} not found.");
        }

        Result<string> controlResult = await ReceivableControlAsync(request.CompanyId, customer, ct).ConfigureAwait(false);
        if (controlResult.IsFailure)
        {
            return controlResult.Error;
        }

        Result<decimal> rateResult = await ResolveRateAsync(request.CompanyId, request.Currency, request.ExchangeRate, request.DocumentDate, ct, customer.RateType, (request.RateOverrideByUserId, request.DocumentNumber), "SalesCreditNote").ConfigureAwait(false);
        if (rateResult.IsFailure)
        {
            return rateResult.Error;
        }

        decimal rate = rateResult.Value;
        decimal gross = request.NetAmount + request.TaxAmount;
        decimal baseNet = Round(request.NetAmount * rate);
        decimal baseTax = Round(request.TaxAmount * rate);
        decimal baseGross = baseNet + baseTax;

        // Reverse of an invoice: Dr revenue, Dr output tax, Cr AR control.
        List<JournalLineRequest> lines =
        [
            Analysed(Line(request.SalesAccountCode, JournalSide.Debit, request.NetAmount, rate, baseNet), request),
            Line(controlResult.Value, JournalSide.Credit, gross, rate, baseGross),
        ];
        if (request.TaxAmount != 0m && request.TaxAccountCode is not null)
        {
            lines.Add(Line(request.TaxAccountCode, JournalSide.Debit, request.TaxAmount, rate, baseTax));
        }

        Result<TradingPostingResult> posted = await PostAndOpenAsync(request.CompanyId, "AR", TradingLedger.AccountsReceivable, customer.Id, OpenItemType.CreditNote,
            LedgerSign.Credit, request.DocumentNumber, request.DocumentDate, request.DocumentDate, request.Currency, rate,
            gross, baseGross, request.Narrative, lines, ct, request.TaxAmount).ConfigureAwait(false);
        if (posted.IsSuccess && !string.IsNullOrWhiteSpace(request.ReasonCode))
        {
            OpenItem item = await _db.OpenItems.SingleAsync(i => i.Id == posted.Value.OpenItemId, ct).ConfigureAwait(false);
            item.ReasonCode = request.ReasonCode.Trim().ToUpperInvariant();
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        return posted;
    }

    /// <inheritdoc />
    public Task<Result<TradingPostingResult>> PostCreditNoteAsync(PurchaseCreditNoteRequest request, CancellationToken ct = default)
        => _db.AtomicAsync(() => PostCreditNoteCoreAsync(request, ct), ct);

    // One unit of work: the journal, the open item and any allocation commit together (retried on a lost race).
    private async Task<Result<TradingPostingResult>> PostCreditNoteCoreAsync(PurchaseCreditNoteRequest request, CancellationToken ct)
    {
        Supplier? supplier = await _db.Suppliers.FirstOrDefaultAsync(s => s.Id == request.SupplierId && s.CompanyId == request.CompanyId, ct).ConfigureAwait(false);
        if (supplier is null)
        {
            return Error.NotFound("Ap.SupplierNotFound", $"Supplier {request.SupplierId} not found.");
        }

        Result<string> controlResult = await ControlCodeAsync(request.CompanyId, ControlAccountType.AccountsPayable, ct).ConfigureAwait(false);
        if (controlResult.IsFailure)
        {
            return controlResult.Error;
        }

        Result<decimal> rateResult = await ResolveRateAsync(request.CompanyId, request.Currency, request.ExchangeRate, request.DocumentDate, ct, supplier.RateType, (request.RateOverrideByUserId, request.DocumentNumber), "PurchaseCreditNote").ConfigureAwait(false);
        if (rateResult.IsFailure)
        {
            return rateResult.Error;
        }

        decimal rate = rateResult.Value;
        decimal gross = request.NetAmount + request.TaxAmount;
        decimal baseNet = Round(request.NetAmount * rate);
        decimal baseTax = Round(request.TaxAmount * rate);
        decimal baseGross = baseNet + baseTax;

        // Reverse of a supplier invoice: Dr AP control, Cr expense, Cr input tax.
        List<JournalLineRequest> lines =
        [
            Line(controlResult.Value, JournalSide.Debit, gross, rate, baseGross),
            Analysed(Line(request.ExpenseAccountCode, JournalSide.Credit, request.NetAmount, rate, baseNet), request),
        ];
        if (request.TaxAmount != 0m && request.TaxAccountCode is not null)
        {
            lines.Add(Line(request.TaxAccountCode, JournalSide.Credit, request.TaxAmount, rate, baseTax));
        }

        return await PostAndOpenAsync(request.CompanyId, "AP", TradingLedger.AccountsPayable, supplier.Id, OpenItemType.CreditNote,
            LedgerSign.Debit, request.DocumentNumber, request.DocumentDate, request.DocumentDate, request.Currency, rate,
            gross, baseGross, request.Narrative, lines, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<Result<TradingPostingResult>> PostInvoiceAsync(PurchaseInvoiceRequest request, CancellationToken ct = default)
        => _db.AtomicAsync(() => PostInvoiceCoreAsync(request, ct), ct);

    // One unit of work: the journal, the open item and any allocation commit together (retried on a lost race).
    private async Task<Result<TradingPostingResult>> PostInvoiceCoreAsync(PurchaseInvoiceRequest request, CancellationToken ct)
    {
        Supplier? supplier = await _db.Suppliers.FirstOrDefaultAsync(s => s.Id == request.SupplierId && s.CompanyId == request.CompanyId, ct).ConfigureAwait(false);
        if (supplier is null)
        {
            return Error.NotFound("Ap.SupplierNotFound", $"Supplier {request.SupplierId} not found.");
        }

        request = await WithTaxDefaultsAsync(request, supplier, ct).ConfigureAwait(false);

        // Duplicate-invoice control: the same supplier invoice entered twice is the classic double payment.
        // Numbers compare on letters and digits only, so "INV-001", "inv 001" and "INV001" are one invoice.
        if (string.IsNullOrWhiteSpace(request.DocumentNumber))
        {
            return Error.Validation("Ap.InvoiceNumberRequired", "A supplier invoice needs the supplier's invoice number.");
        }

        if (request.ExpenseLines.Count > 0
            && (request.ExpenseLines.Sum(l => l.NetAmount) != request.NetAmount || request.ExpenseLines.Any(l => string.IsNullOrWhiteSpace(l.AccountCode))))
        {
            return Error.Validation("Ap.ExpenseLinesMismatch", "The expense lines must each name an account and add up to the net amount.");
        }

        List<string> existing = await _db.OpenItems
            .Where(i => i.CompanyId == request.CompanyId && i.Ledger == TradingLedger.AccountsPayable && i.TraderId == supplier.Id && i.ItemType == OpenItemType.Invoice)
            .Select(i => i.DocumentNumber)
            .ToListAsync(ct).ConfigureAwait(false);
        string? clash = existing.FirstOrDefault(n => One4All.Application.Trading.DocumentNumbers.Same(n, request.DocumentNumber));
        if (clash is not null)
        {
            return Error.Conflict("Ap.DuplicateInvoice", $"Supplier {supplier.Code} invoice {request.DocumentNumber} has already been entered (as {clash}).");
        }

        // Exemption certificates on purchases: a supply under a certificate carries no tax, and an exempt tax code needs a valid certificate.
        bool certified = await TraderCertificates.HoldsAsync(_db, request.CompanyId, TradingLedger.AccountsPayable, supplier.Id, CertificatePurpose.TaxExemption, request.DocumentDate, ct)
            .ConfigureAwait(false);
        if (certified && request.TaxAmount != 0m)
        {
            return Error.Rule("Tax.SupplierExempt", $"Purchases from {supplier.Code} are covered by a tax exemption certificate on {request.DocumentDate:yyyy-MM-dd}: enter the invoice without tax.");
        }

        if (!certified && request.TaxCode is string purchaseTaxCode
            && await _db.TaxCodes.AnyAsync(t => t.CompanyId == request.CompanyId && t.Code == purchaseTaxCode && t.Type == TaxType.Exempt, ct).ConfigureAwait(false))
        {
            return Error.Rule("Tax.CertificateRequired", $"Supplier {supplier.Code} has no valid exemption certificate on {request.DocumentDate:yyyy-MM-dd}, so {purchaseTaxCode} cannot be used.");
        }

        Result<string> controlResult = await ControlCodeAsync(request.CompanyId, ControlAccountType.AccountsPayable, ct).ConfigureAwait(false);
        if (controlResult.IsFailure)
        {
            return controlResult.Error;
        }

        Result<decimal> rateResult = await ResolveRateAsync(request.CompanyId, request.Currency, request.ExchangeRate, request.DocumentDate, ct, supplier.RateType, (request.RateOverrideByUserId, request.DocumentNumber), "PurchaseInvoice").ConfigureAwait(false);
        if (rateResult.IsFailure)
        {
            return rateResult.Error;
        }

        Core.JobCosting.JobContract? job = null;
        if (request.JobContractId is long jobId)
        {
            job = await _db.Set<Core.JobCosting.JobContract>().FirstOrDefaultAsync(c => c.Id == jobId && c.CompanyId == request.CompanyId, ct).ConfigureAwait(false);
            if (job is null || job.Status != Core.JobCosting.JobStatus.Open)
            {
                return Error.Rule("Job.ContractNotOpen", "The invoice is charged to a job that is not found or not open.");
            }
        }

        decimal rate = rateResult.Value;
        decimal gross = request.NetAmount + request.TaxAmount;
        decimal baseNet = Round(request.NetAmount * rate);
        decimal baseTax = Round(request.TaxAmount * rate);
        decimal baseGross = baseNet + baseTax;

        // An invoice with no order behind it never passed the gate at order raising, so it passes it here (brief §4.18.5): the spend against what is left of the vote.
        if (_budget is not null && request.NetAmount > 0m)
        {
            static string? Dimension(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToUpperInvariant();
            List<Application.Abstractions.Budgeting.BudgetDemand> demands = request.ExpenseLines.Count == 0
                ? [new(request.ExpenseAccountCode.Trim(), Dimension(request.Department), baseNet)]
                : [.. request.ExpenseLines.Select(l => new Application.Abstractions.Budgeting.BudgetDemand(l.AccountCode.Trim(), Dimension(l.Department ?? request.Department), Round(l.NetAmount * rate)))];
            Result<Application.Abstractions.Budgeting.BudgetGateResult> check = await _budget.CheckAsync(request.CompanyId, request.DocumentDate, demands, ct).ConfigureAwait(false);
            if (check.IsSuccess && check.Value.Outcome == Core.Budgeting.BudgetAction.Block)
            {
                return Error.Rule("Budget.Blocked", check.Value.Warning!);
            }

            if (check.IsSuccess && check.Value.Outcome == Core.Budgeting.BudgetAction.RequireApproval
                && (request.BudgetApprovedByUserId is not long approver || _permissions is null
                    || !(await _permissions.ResolveAsync(approver, ct).ConfigureAwait(false)).Has("Budget.OverrideBlock", Core.Security.OperationFlags.View)))
            {
                return Error.Rule("Budget.ApprovalNeeded", $"{check.Value.Warning} It needs the approval of someone who may override budget blocks.");
            }
        }

        List<JournalLineRequest> lines = [Line(controlResult.Value, JournalSide.Credit, gross, rate, baseGross)];
        if (request.ExpenseLines.Count == 0)
        {
            lines.Insert(0, Analysed(Line(request.ExpenseAccountCode, JournalSide.Debit, request.NetAmount, rate, baseNet), request));
        }
        else
        {
            // Several expense accounts (each with its own analysis): the last absorbs base rounding so the debits equal the base net.
            decimal baseLeft = baseNet;
            for (int i = 0; i < request.ExpenseLines.Count; i++)
            {
                RevenueLine expense = request.ExpenseLines[i];
                decimal baseShare = i == request.ExpenseLines.Count - 1 ? baseLeft : Round(expense.NetAmount * rate);
                baseLeft -= baseShare;
                lines.Insert(i, Analysed(Line(expense.AccountCode, JournalSide.Debit, expense.NetAmount, rate, baseShare), request, expense));
            }
        }
        if (request.TaxAmount != 0m && request.TaxAccountCode is not null)
        {
            lines.Add(Line(request.TaxAccountCode, JournalSide.Debit, request.TaxAmount, rate, baseTax, request.TaxCode));
        }

        Result<TradingPostingResult> posted = await PostAndOpenAsync(request.CompanyId, "AP", TradingLedger.AccountsPayable, supplier.Id, OpenItemType.Invoice,
            LedgerSign.Credit, request.DocumentNumber, request.DocumentDate, supplier.DueDateFor(request.DocumentDate),
            request.Currency, rate, gross, baseGross, request.Narrative, lines, ct).ConfigureAwait(false);
        if (posted.IsSuccess && job is not null)
        {
            // Charged to a job: its net (base) goes on the job's cost, with any overhead rate on purchases.
            await JobCosting.JobCosts.CaptureAsync(_db, job, string.IsNullOrWhiteSpace(request.JobCostHead) ? "Purchases" : request.JobCostHead.Trim(),
                Core.JobCosting.JobCostSource.PurchaseInvoice, baseNet, request.DocumentDate, $"{supplier.Code} {request.DocumentNumber}", ct).ConfigureAwait(false);
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        return posted;
    }

    /// <inheritdoc />
    public Task<Result<TradingPostingResult>> PostPaymentAsync(PaymentRequest request, CancellationToken ct = default)
        => _db.AtomicAsync(() => PostPaymentCoreAsync(request, ct), ct);

    // One unit of work: the journal, the open item and any allocation commit together (retried on a lost race).
    private async Task<Result<TradingPostingResult>> PostPaymentCoreAsync(PaymentRequest request, CancellationToken ct)
    {
        Supplier? supplier = await _db.Suppliers.FirstOrDefaultAsync(s => s.Id == request.SupplierId && s.CompanyId == request.CompanyId, ct).ConfigureAwait(false);
        if (supplier is null)
        {
            return Error.NotFound("Ap.SupplierNotFound", $"Supplier {request.SupplierId} not found.");
        }

        Result<string> controlResult = await ControlCodeAsync(request.CompanyId, ControlAccountType.AccountsPayable, ct).ConfigureAwait(false);
        if (controlResult.IsFailure)
        {
            return controlResult.Error;
        }

        Result<decimal> rateResult = await ResolveRateAsync(request.CompanyId, request.Currency, request.ExchangeRate, request.PaymentDate, ct, supplier.RateType, (request.RateOverrideByUserId, request.DocumentNumber), "Payment").ConfigureAwait(false);
        if (rateResult.IsFailure)
        {
            return rateResult.Error;
        }

        decimal rate = rateResult.Value;
        decimal baseAmount = Round(request.Amount * rate);

        // Withholding tax applies to a supplier without a valid tax clearance on the payment date: the code asked for, else the company's default.
        TaxCode? withholding = null;
        if (await TraderCertificates.WithholdingCodeAsync(_db, request.CompanyId, supplier, request.PaymentDate, request.WithholdingTaxCode, ct).ConfigureAwait(false) is string code)
        {
            withholding = await _db.TaxCodes.Include(t => t.Rates)
                .FirstOrDefaultAsync(t => t.CompanyId == request.CompanyId && t.Code == code && t.IsActive, ct).ConfigureAwait(false);
            if (withholding is null || withholding.Type != TaxType.Withholding || string.IsNullOrWhiteSpace(withholding.OutputAccountCode))
            {
                return Error.Rule("Ap.WithholdingCode", $"{code} is not an active withholding tax code with a payable account.");
            }
        }

        decimal ratePercent = withholding?.RateOn(request.PaymentDate) ?? 0m;
        decimal withheld = Round(request.Amount * ratePercent / 100m);
        decimal withheldBase = Round(withheld * rate);

        List<JournalLineRequest> lines =
        [
            Line(controlResult.Value, JournalSide.Debit, request.Amount, rate, baseAmount),
            Line(request.BankAccountCode, JournalSide.Credit, request.Amount - withheld, rate, baseAmount - withheldBase),
        ];
        if (withheld != 0m)
        {
            lines.Add(Line(withholding!.OutputAccountCode!, JournalSide.Credit, withheld, rate, withheldBase));
        }

        Result<TradingPostingResult> posted = await PostAndOpenAsync(request.CompanyId, "AP", TradingLedger.AccountsPayable, supplier.Id, OpenItemType.Payment,
            LedgerSign.Debit, request.DocumentNumber, request.PaymentDate, request.PaymentDate, request.Currency, rate,
            request.Amount, baseAmount, request.Narrative, lines, ct).ConfigureAwait(false);
        if (posted.IsFailure || withheld == 0m)
        {
            return posted;
        }

        _db.Set<WithholdingCertificate>().Add(new WithholdingCertificate(request.CompanyId, $"WHT-{request.DocumentNumber}", supplier.Id, posted.Value.OpenItemId,
            request.DocumentNumber, request.PaymentDate, withholding!.Code, ratePercent, baseAmount, withheldBase));
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return posted.Value with { WithheldBase = withheldBase };
    }

    /// <inheritdoc />
    public Task<Result<TradingPostingResult>> PayWithDiscountAsync(long companyId, long invoiceOpenItemId, DateOnly date, decimal cashAmount, decimal discountAmount, string bankAccountCode, string discountReceivedAccountCode,
        string documentNumber, string? taxAccountCode = null, CancellationToken ct = default)
        => _db.AtomicAsync(() => PayWithDiscountCoreAsync(companyId, invoiceOpenItemId, date, cashAmount, discountAmount, bankAccountCode, discountReceivedAccountCode, documentNumber, taxAccountCode, ct), ct);

    // One unit of work: the journal, the open item and any allocation commit together (retried on a lost race).
    private async Task<Result<TradingPostingResult>> PayWithDiscountCoreAsync(long companyId, long invoiceOpenItemId, DateOnly date, decimal cashAmount, decimal discountAmount, string bankAccountCode, string discountReceivedAccountCode,
        string documentNumber, string? taxAccountCode, CancellationToken ct)
    {
        if (cashAmount <= 0m || discountAmount <= 0m)
        {
            return Error.Rule("Ap.SettlementNonPositive", "The cash and discount amounts must both be positive.");
        }

        OpenItem? invoice = await _db.OpenItems.FirstOrDefaultAsync(i => i.Id == invoiceOpenItemId && i.CompanyId == companyId && i.Ledger == TradingLedger.AccountsPayable, ct).ConfigureAwait(false);
        if (invoice is null)
        {
            return Error.NotFound("Ap.OpenItemNotFound", $"AP open item {invoiceOpenItemId} not found.");
        }

        if (invoice.Sign != LedgerSign.Credit || invoice.OutstandingTransaction <= 0m)
        {
            return Error.Rule("Ap.NothingToSettle", "Only an outstanding creditor item (invoice) can be settled.");
        }

        if (cashAmount + discountAmount > invoice.OutstandingTransaction)
        {
            return Error.Rule("Ap.SettlementExceedsOutstanding", $"The cash {cashAmount:0.00} plus discount {discountAmount:0.00} exceeds the outstanding {invoice.OutstandingTransaction:0.00}.");
        }

        // Cash payment: Dr AP control / Cr bank.
        Result<TradingPostingResult> cash = await PostPaymentAsync(new PaymentRequest
        {
            CompanyId = companyId, SupplierId = invoice.TraderId, DocumentNumber = documentNumber, PaymentDate = date,
            Currency = invoice.Currency, Amount = cashAmount, BankAccountCode = bankAccountCode, ExchangeRate = invoice.ExchangeRate,
            Narrative = $"Payment with settlement discount for {invoice.DocumentNumber}",
        }, ct).ConfigureAwait(false);
        if (cash.IsFailure)
        {
            return cash.Error;
        }

        Result<AllocationResult> allocCash = await AllocateAsync(companyId, TradingLedger.AccountsPayable, cash.Value.OpenItemId, invoiceOpenItemId, cashAmount, date, ct).ConfigureAwait(false);
        if (allocCash.IsFailure)
        {
            return allocCash.Error;
        }

        // Discount: posted as a "payment" whose money side is the discounts-received income (Dr AP / Cr income).
        Result<TradingPostingResult> discount = await PostPaymentAsync(new PaymentRequest
        {
            CompanyId = companyId, SupplierId = invoice.TraderId, DocumentNumber = $"{documentNumber}-DISC", PaymentDate = date,
            Currency = invoice.Currency, Amount = discountAmount, BankAccountCode = discountReceivedAccountCode, ExchangeRate = invoice.ExchangeRate,
            Narrative = $"Settlement discount received for {invoice.DocumentNumber}",
        }, ct).ConfigureAwait(false);
        if (discount.IsFailure)
        {
            return discount.Error;
        }

        Result<AllocationResult> allocDiscount = await AllocateAsync(companyId, TradingLedger.AccountsPayable, discount.Value.OpenItemId, invoiceOpenItemId, discountAmount, date, ct).ConfigureAwait(false);
        if (allocDiscount.IsFailure)
        {
            return allocDiscount.Error;
        }

        Result<string?> discountTax = await DiscountTaxAsync(invoice, date, discountAmount, discountReceivedAccountCode, taxAccountCode, documentNumber, ct).ConfigureAwait(false);
        return discountTax.IsFailure ? discountTax.Error : cash.Value;
    }

    /// <inheritdoc />
    public Task<Result<TradingPostingResult>> PostReceiptReversalAsync(long companyId, long customerId, string documentNumber, DateOnly date, CurrencyCode currency, decimal amount, string bankAccountCode, decimal? exchangeRate = null, CancellationToken ct = default)
        => _db.AtomicAsync(() => PostReceiptReversalCoreAsync(companyId, customerId, documentNumber, date, currency, amount, bankAccountCode, exchangeRate, ct), ct);

    // One unit of work: the journal, the open item and any allocation commit together (retried on a lost race).
    private async Task<Result<TradingPostingResult>> PostReceiptReversalCoreAsync(long companyId, long customerId, string documentNumber, DateOnly date, CurrencyCode currency, decimal amount, string bankAccountCode, decimal? exchangeRate, CancellationToken ct)
    {
        Customer? customer = await _db.Customers.FirstOrDefaultAsync(c => c.Id == customerId && c.CompanyId == companyId, ct).ConfigureAwait(false);
        if (customer is null)
        {
            return Error.NotFound("Ar.CustomerNotFound", $"Customer {customerId} not found.");
        }

        Result<string> controlResult = await ReceivableControlAsync(companyId, customer, ct).ConfigureAwait(false);
        if (controlResult.IsFailure)
        {
            return controlResult.Error;
        }

        Result<decimal> rateResult = await ResolveRateAsync(companyId, currency, exchangeRate, date, ct, customer.RateType).ConfigureAwait(false);
        if (rateResult.IsFailure)
        {
            return rateResult.Error;
        }

        decimal rate = rateResult.Value;
        decimal baseAmount = Round(amount * rate);

        // The exact opposite of a receipt: Dr AR control, Cr bank/cash. Creates a debit adjustment item.
        List<JournalLineRequest> lines =
        [
            Line(controlResult.Value, JournalSide.Debit, amount, rate, baseAmount),
            Line(bankAccountCode, JournalSide.Credit, amount, rate, baseAmount),
        ];

        return await PostAndOpenAsync(companyId, "AR", TradingLedger.AccountsReceivable, customer.Id, OpenItemType.Journal,
            LedgerSign.Debit, documentNumber, date, date, currency, rate, amount, baseAmount, "Receipt reversal", lines, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<Result<AllocationResult>> AllocateAsync(long companyId, TradingLedger ledger, long debitItemId, long creditItemId, decimal transactionAmount, DateOnly allocationDate, CancellationToken ct = default)
        => _db.AtomicAsync(() => AllocateCoreAsync(companyId, ledger, debitItemId, creditItemId, transactionAmount, allocationDate, ct), ct);

    /// <inheritdoc />
    public Task<Result<AllocationResult>> AllocateAcrossCurrenciesAsync(long companyId, TradingLedger ledger, long debitItemId, long creditItemId, decimal creditTransactionAmount, DateOnly allocationDate, CancellationToken ct = default)
        => _db.AtomicAsync(() => AllocateAcrossCurrenciesCoreAsync(companyId, ledger, debitItemId, creditItemId, creditTransactionAmount, allocationDate, ct), ct);

    // The credit's base value settles the debit at the day's rate of the debit's currency; the base values' difference is realised FX.
    private async Task<Result<AllocationResult>> AllocateAcrossCurrenciesCoreAsync(long companyId, TradingLedger ledger, long debitItemId, long creditItemId, decimal creditTransactionAmount, DateOnly allocationDate, CancellationToken ct)
    {
        OpenItem? debit = await _db.OpenItems.FirstOrDefaultAsync(i => i.Id == debitItemId && i.CompanyId == companyId, ct).ConfigureAwait(false);
        OpenItem? credit = await _db.OpenItems.FirstOrDefaultAsync(i => i.Id == creditItemId && i.CompanyId == companyId, ct).ConfigureAwait(false);
        if (debit is null || credit is null)
        {
            return Error.NotFound("Alloc.ItemNotFound", "One or both open items were not found.");
        }

        if (debit.Currency == credit.Currency)
        {
            return await AllocateCoreAsync(companyId, ledger, debitItemId, creditItemId, creditTransactionAmount, allocationDate, ct).ConfigureAwait(false);
        }

        if (debit.Sign != LedgerSign.Debit || credit.Sign != LedgerSign.Credit)
        {
            return Error.Validation("Alloc.WrongSides", "Allocation requires a debit item and a credit item.");
        }

        if (debit.TraderId != credit.TraderId || debit.Ledger != credit.Ledger)
        {
            return Error.Validation("Alloc.Mismatch", "Items must be the same trader and ledger.");
        }

        decimal creditAmount = Math.Min(creditTransactionAmount, credit.OutstandingTransaction);
        if (creditAmount <= 0m || debit.OutstandingTransaction <= 0m)
        {
            return Error.Validation("Alloc.NothingToAllocate", "There is nothing outstanding to allocate.");
        }

        Company company = await _db.Companies.FirstAsync(c => c.Id == companyId, ct).ConfigureAwait(false);
        decimal debitRate = 1m;
        if (debit.Currency != company.BaseCurrency)
        {
            decimal? rate = await Accounting.ExchangeRateResolver.FindAsync(_db, debit.Currency, company.BaseCurrency, ExchangeRateType.Daily, allocationDate, ct).ConfigureAwait(false);
            if (rate is not > 0m)
            {
                return Error.NotFound("Alloc.NoRate", $"No {debit.Currency} rate on {allocationDate:yyyy-MM-dd} to value {debit.DocumentNumber} for the allocation.");
            }

            debitRate = rate.Value;
        }

        // What the credit is worth in base settles that much of the debit at today's rate; a credit worth more than the debit settles the debit whole.
        decimal creditBase = BaseDrawn(credit, creditAmount);
        decimal debitAmount = Round(creditBase / debitRate);
        if (debitAmount > debit.OutstandingTransaction)
        {
            debitAmount = debit.OutstandingTransaction;
            creditAmount = Math.Min(credit.OutstandingTransaction, Round(Round(debitAmount * debitRate) / credit.ExchangeRate));
            creditBase = BaseDrawn(credit, creditAmount);
        }

        decimal debitBase = BaseDrawn(debit, debitAmount);
        decimal realisedFx = decimal.Round(creditBase - debitBase, Scale, MidpointRounding.ToEven);
        string? fxJournalNumber = null;
        if (realisedFx != 0m)
        {
            Result<string> fxResult = await PostRealisedFxAsync(companyId, ledger, realisedFx, allocationDate, ct).ConfigureAwait(false);
            if (fxResult.IsFailure)
            {
                return fxResult.Error;
            }

            fxJournalNumber = fxResult.Value;
        }

        debit.ReduceOutstanding(debitAmount, debitBase);
        credit.ReduceOutstanding(creditAmount, creditBase);
        Allocation allocation = new(companyId, ledger, debit.TraderId, allocationDate, debit.Id, credit.Id, creditAmount, debitBase, creditBase, realisedFx) { DebitTransactionAmount = debitAmount };
        _db.Allocations.Add(allocation);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return new AllocationResult(allocation.Id, creditAmount, realisedFx, fxJournalNumber) { DebitTransactionAmount = debitAmount };
    }

    // One unit of work, retried if another user allocates against the same items at the same moment.
    private async Task<Result<AllocationResult>> AllocateCoreAsync(long companyId, TradingLedger ledger, long debitItemId, long creditItemId, decimal transactionAmount, DateOnly allocationDate, CancellationToken ct)
    {
        OpenItem? debit = await _db.OpenItems.FirstOrDefaultAsync(i => i.Id == debitItemId && i.CompanyId == companyId, ct).ConfigureAwait(false);
        OpenItem? credit = await _db.OpenItems.FirstOrDefaultAsync(i => i.Id == creditItemId && i.CompanyId == companyId, ct).ConfigureAwait(false);
        if (debit is null || credit is null)
        {
            return Error.NotFound("Alloc.ItemNotFound", "One or both open items were not found.");
        }

        if (debit.Sign != LedgerSign.Debit || credit.Sign != LedgerSign.Credit)
        {
            return Error.Validation("Alloc.WrongSides", "Allocation requires a debit item and a credit item.");
        }

        if (debit.TraderId != credit.TraderId || debit.Ledger != credit.Ledger || debit.Currency != credit.Currency)
        {
            return Error.Validation("Alloc.Mismatch", "Items must be the same trader, ledger and currency.");
        }

        decimal amount = Math.Min(transactionAmount, Math.Min(debit.OutstandingTransaction, credit.OutstandingTransaction));
        if (amount <= 0m)
        {
            return Error.Validation("Alloc.NothingToAllocate", "There is nothing outstanding to allocate.");
        }

        decimal debitBase = BaseDrawn(debit, amount);
        decimal creditBase = BaseDrawn(credit, amount);
        decimal realisedFx = decimal.Round(creditBase - debitBase, Scale, MidpointRounding.ToEven);

        // Post the realised FX before touching the items; the FX journal and the allocation commit together (the
        // public method runs this as one unit of work, retried if another user changed either item meanwhile).
        string? fxJournalNumber = null;
        if (realisedFx != 0m)
        {
            Result<string> fxResult = await PostRealisedFxAsync(companyId, ledger, realisedFx, allocationDate, ct).ConfigureAwait(false);
            if (fxResult.IsFailure)
            {
                return fxResult.Error;
            }

            fxJournalNumber = fxResult.Value;
        }

        debit.ReduceOutstanding(amount, debitBase);
        credit.ReduceOutstanding(amount, creditBase);
        Allocation allocation = new(companyId, ledger, debit.TraderId, allocationDate, debit.Id, credit.Id, amount, debitBase, creditBase, realisedFx);
        _db.Allocations.Add(allocation);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        return new AllocationResult(allocation.Id, amount, realisedFx, fxJournalNumber);
    }

    private async Task<Result<string>> PostRealisedFxAsync(long companyId, TradingLedger ledger, decimal realisedFx, DateOnly date, CancellationToken ct, bool reverse = false)
    {
        ControlAccountType controlType = ledger == TradingLedger.AccountsReceivable ? ControlAccountType.AccountsReceivable : ControlAccountType.AccountsPayable;
        Result<string> controlResult = await ControlCodeAsync(companyId, controlType, ct).ConfigureAwait(false);
        if (controlResult.IsFailure)
        {
            return controlResult.Error;
        }

        // Reversal keeps the same FX account as the original (netting it to nil), only swapping the sides.
        ControlAccountType fxType = realisedFx > 0m ? ControlAccountType.FxGain : ControlAccountType.FxLoss;
        Result<string> fxResult = await ControlCodeAsync(companyId, fxType, ct).ConfigureAwait(false);
        if (fxResult.IsFailure)
        {
            return fxResult.Error;
        }

        Company company = await _db.Companies.FirstAsync(c => c.Id == companyId, ct).ConfigureAwait(false);
        decimal magnitude = Math.Abs(realisedFx);

        // realisedFx > 0 (gain): Dr control, Cr FX gain. realisedFx < 0 (loss): Cr control, Dr FX loss.
        JournalSide controlSide = realisedFx > 0m ? JournalSide.Debit : JournalSide.Credit;
        JournalSide fxSide = realisedFx > 0m ? JournalSide.Credit : JournalSide.Debit;
        if (reverse)
        {
            (controlSide, fxSide) = (fxSide, controlSide);
        }

        Result<PostingReceipt> posting = await _postingEngine.PostAsync(new JournalRequest
        {
            CompanyId = companyId,
            SourceModule = ledger == TradingLedger.AccountsReceivable ? "AR" : "AP",
            PostingDate = date,
            TransactionCurrency = company.BaseCurrency,
            EnforceManualJournalRules = false,
            DocumentType = reverse ? "RealisedFxReversal" : "RealisedFx",
            Narrative = reverse ? "Reversal of realised exchange difference" : "Realised exchange difference on allocation",
            Lines =
            [
                new JournalLineRequest { AccountCode = controlResult.Value, Side = controlSide, TransactionAmount = magnitude },
                new JournalLineRequest { AccountCode = fxResult.Value, Side = fxSide, TransactionAmount = magnitude },
            ],
        }, ct).ConfigureAwait(false);

        return posting.IsFailure ? posting.Error : posting.Value.JournalNumber;
    }

    /// <inheritdoc />
    public Task<Result<UnallocationResult>> UnallocateAsync(long companyId, long allocationId, DateOnly reversalDate, CancellationToken ct = default)
        => _db.AtomicAsync(() => UnallocateCoreAsync(companyId, allocationId, reversalDate, ct), ct);

    // One unit of work, retried if another user allocates against the same items at the same moment.
    private async Task<Result<UnallocationResult>> UnallocateCoreAsync(long companyId, long allocationId, DateOnly reversalDate, CancellationToken ct)
    {
        Allocation? allocation = await _db.Allocations.FirstOrDefaultAsync(a => a.Id == allocationId && a.CompanyId == companyId, ct).ConfigureAwait(false);
        if (allocation is null)
        {
            return Error.NotFound("Alloc.NotFound", $"Allocation {allocationId} not found.");
        }

        if (allocation.IsReversed)
        {
            return Error.Rule("Alloc.AlreadyReversed", $"Allocation {allocationId} has already been reversed.");
        }

        OpenItem? debit = await _db.OpenItems.FirstOrDefaultAsync(i => i.Id == allocation.DebitItemId && i.CompanyId == companyId, ct).ConfigureAwait(false);
        OpenItem? credit = await _db.OpenItems.FirstOrDefaultAsync(i => i.Id == allocation.CreditItemId && i.CompanyId == companyId, ct).ConfigureAwait(false);
        if (debit is null || credit is null)
        {
            return Error.NotFound("Alloc.ItemNotFound", "One or both open items were not found.");
        }

        // Reverse any realised FX the allocation posted (netting the original gain/loss to nil) before touching the
        // items, and commit the reversal journal with the restored items.
        string? fxReversalJournal = null;
        if (allocation.RealisedFxBase != 0m)
        {
            Result<string> fxResult = await PostRealisedFxAsync(companyId, allocation.Ledger, allocation.RealisedFxBase, reversalDate, ct, reverse: true).ConfigureAwait(false);
            if (fxResult.IsFailure)
            {
                return fxResult.Error;
            }

            fxReversalJournal = fxResult.Value;
        }

        // Restore both items to their pre-allocation state using the exact base amounts drawn.
        debit.RestoreOutstanding(allocation.DebitTransactionAmount ?? allocation.TransactionAmount, allocation.DebitBase);
        credit.RestoreOutstanding(allocation.TransactionAmount, allocation.CreditBase);
        allocation.Reverse(reversalDate);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        return new UnallocationResult(allocation.Id, allocation.TransactionAmount, -allocation.RealisedFxBase, fxReversalJournal);
    }

    private async Task<Result<TradingPostingResult>> PostAndOpenAsync(
        long companyId, string module, TradingLedger ledger, long traderId, OpenItemType itemType, LedgerSign sign,
        string documentNumber, DateOnly documentDate, DateOnly dueDate, CurrencyCode currency, decimal rate,
        decimal amountTxn, decimal amountBase, string? narrative, List<JournalLineRequest> lines, CancellationToken ct, decimal tax = 0m)
    {
        Result<PostingReceipt> posting = await _postingEngine.PostAsync(new JournalRequest
        {
            CompanyId = companyId,
            SourceModule = module,
            PostingDate = documentDate,
            TransactionCurrency = currency,
            EnforceManualJournalRules = false,
            DocumentType = DocumentTypeFor(ledger, itemType),
            DocumentNumber = documentNumber,
            Narrative = narrative,
            Lines = lines,
        }, ct).ConfigureAwait(false);

        if (posting.IsFailure)
        {
            return posting.Error;
        }

        OpenItem item = new(companyId, ledger, traderId, itemType, sign, documentNumber, documentDate, dueDate, currency, rate, amountTxn, amountBase)
        {
            JournalEntryId = posting.Value.JournalId,
            Narrative = narrative,
        };

        // E-invoicing (brief §3.5): a sales ledger tax invoice or credit note is signed by the fiscal device like a till receipt; unreachable means it waits for the retry task.
        if (ledger == TradingLedger.AccountsReceivable && itemType is OpenItemType.Invoice or OpenItemType.CreditNote && _fiscal is { IsEnabled: true })
        {
            string? buyerTaxNumber = await _db.Customers.AsNoTracking().Where(c => c.Id == traderId).Select(c => c.TaxRegistration).FirstOrDefaultAsync(ct).ConfigureAwait(false);
            FiscalReceiptResult? signed = await Accounting.FiscalSigning.SignAsync(_fiscal, new FiscalReceiptRequest(companyId, documentNumber, amountTxn, tax, currency.Value, DateTimeOffset.UtcNow,
                itemType == OpenItemType.CreditNote ? FiscalDocumentKind.CreditNote : FiscalDocumentKind.Invoice, null, buyerTaxNumber), ct).ConfigureAwait(false);
            if (signed is FiscalReceiptResult result)
            {
                (item.FiscalCode, item.FiscalVerification, item.FiscalQueued) = (result.FiscalCode, result.SignatureOrQr, result.Queued || result.FiscalCode is null);
            }
        }

        _db.OpenItems.Add(item);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        return new TradingPostingResult(item.Id, posting.Value.JournalNumber, amountBase);
    }

    /// <summary>
    /// The journal document type of a trading document — what the account enquiry, the audit trail and journal
    /// listings show for it. (These journals used to carry an account code here.)
    /// </summary>
    /// <param name="ledger">The sub-ledger.</param>
    /// <param name="itemType">The open-item type the document raises.</param>
    /// <returns>The document type.</returns>
    internal static string DocumentTypeFor(TradingLedger ledger, OpenItemType itemType) => (ledger, itemType) switch
    {
        (TradingLedger.AccountsReceivable, OpenItemType.Invoice) => "SalesInvoice",
        (TradingLedger.AccountsReceivable, OpenItemType.CreditNote) => "SalesCreditNote",
        (TradingLedger.AccountsReceivable, OpenItemType.DebitNote) => "SalesDebitNote",
        (TradingLedger.AccountsReceivable, OpenItemType.Receipt) => "CustomerReceipt",
        (TradingLedger.AccountsReceivable, OpenItemType.Journal) => "ReceiptReversal",
        (TradingLedger.AccountsPayable, OpenItemType.Invoice) => "PurchaseInvoice",
        (TradingLedger.AccountsPayable, OpenItemType.CreditNote) => "PurchaseCreditNote",
        (TradingLedger.AccountsPayable, OpenItemType.Payment) => "SupplierPayment",
        _ => $"{(ledger == TradingLedger.AccountsReceivable ? "AR" : "AP")}{itemType}",
    };

    private static decimal BaseDrawn(OpenItem item, decimal transactionAmount) =>
        transactionAmount >= item.OutstandingTransaction
            ? item.OutstandingBase
            : Round(transactionAmount * item.ExchangeRate);

    // A customer's own receivables control (an instalment debtor's tenure control), else the company's main one.
    private async Task<Result<string>> ReceivableControlAsync(long companyId, Customer customer, CancellationToken ct)
        => customer.ReceivableControlAccountCode is string own ? own : await ControlCodeAsync(companyId, ControlAccountType.AccountsReceivable, ct).ConfigureAwait(false);

    private async Task<Result<string>> ControlCodeAsync(long companyId, ControlAccountType type, CancellationToken ct)
    {
        string? code = await _db.Accounts
            .Where(a => a.CompanyId == companyId && a.ControlAccountType == type)
            .OrderBy(a => a.AccountCode)
            .Select(a => a.AccountCode)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        return code is null
            ? Error.Rule("Trading.NoControlAccount", $"No {type} control account is configured for company {companyId}.")
            : code;
    }

    // The rate given on the document; else the trader's own rate type (a contract or bank rate) when one is found; else the daily rate.
    private async Task<Result<decimal>> ResolveRateAsync(long companyId, CurrencyCode currency, decimal? explicitRate, DateOnly date, CancellationToken ct, ExchangeRateType? traderRateType = null,
        (long? ByUserId, string DocumentNumber)? keyed = null, string? documentType = null)
    {
        Company company = await _db.Companies.FirstAsync(c => c.Id == companyId, ct).ConfigureAwait(false);
        if (currency == company.BaseCurrency)
        {
            return 1m;
        }

        if (explicitRate is { } r and > 0m)
        {
            // A keyed rate on a document, when the company controls overrides: only someone allowed to, and recorded against the table's rate.
            if (company.RateOverrideControl && keyed is { } document)
            {
                if (document.ByUserId is not long user || _permissions is null
                    || !(await _permissions.ResolveAsync(user, ct).ConfigureAwait(false)).Has("System.ExchangeRate", Core.Security.OperationFlags.OverridePrice))
                {
                    return Error.Forbidden("Trading.RateOverrideNotAllowed", $"Keying the rate on {document.DocumentNumber} needs the exchange-rate override permission.");
                }

                decimal? table = await Accounting.ExchangeRateResolver.FindAsync(_db, currency, company.BaseCurrency, traderRateType ?? ExchangeRateType.Daily, date, ct).ConfigureAwait(false)
                    ?? await Accounting.ExchangeRateResolver.FindAsync(_db, currency, company.BaseCurrency, ExchangeRateType.Daily, date, ct).ConfigureAwait(false);
                _db.Set<ExchangeRateOverride>().Add(new ExchangeRateOverride(companyId, document.DocumentNumber, currency.Value, date, r, table, user, DateTimeOffset.UtcNow));
            }

            return r;
        }

        if (traderRateType is null or ExchangeRateType.Daily && documentType is not null)
        {
            string ledger = documentType.StartsWith("Purchase", StringComparison.Ordinal) || documentType == "Payment" ? "AP" : "AR";
            List<ExchangeRatePolicy> policies = await _db.ExchangeRatePolicies.AsNoTracking().Where(p => p.CompanyId == companyId && (p.DocumentType == documentType || p.DocumentType == ledger))
                .ToListAsync(ct).ConfigureAwait(false);
            traderRateType = (policies.FirstOrDefault(p => p.DocumentType == documentType) ?? policies.FirstOrDefault())?.RateType ?? traderRateType;
        }

        decimal? rate = traderRateType is ExchangeRateType own && own != ExchangeRateType.Daily
            ? await Accounting.ExchangeRateResolver.FindAsync(_db, currency, company.BaseCurrency, own, date, ct).ConfigureAwait(false)
            : null;
        rate ??= await Accounting.ExchangeRateResolver.FindAsync(_db, currency, company.BaseCurrency, ExchangeRateType.Daily, date, ct).ConfigureAwait(false);
        if (rate is null)
        {
            return Error.Rule("Trading.NoRate", $"No daily rate from {currency} to {company.BaseCurrency} on or before {date:yyyy-MM-dd}.");
        }

        return rate.Value;
    }

    private static JournalLineRequest Line(string account, JournalSide side, decimal txn, decimal rate, decimal baseAmount, string? taxCode = null) =>
        new() { AccountCode = account, Side = side, TransactionAmount = txn, ExchangeRate = rate, BaseAmount = baseAmount, TaxCode = taxCode };

    // A revenue or expense line with the document's analysis (a line's own cost centre and department win over the document's).
    private static JournalLineRequest Analysed(JournalLineRequest line, ITradingAnalysis document, RevenueLine? own = null) => new()
    {
        AccountCode = line.AccountCode, Side = line.Side, TransactionAmount = line.TransactionAmount, ExchangeRate = line.ExchangeRate, BaseAmount = line.BaseAmount, TaxCode = line.TaxCode,
        Narrative = line.Narrative, CostCentre = Clean(own?.CostCentre) ?? Clean(document.CostCentre), Department = Clean(own?.Department) ?? Clean(document.Department),
        Project = Clean(document.Project),
    };

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToUpperInvariant();

    // Default tax (brief §3.5): a sales invoice entered without a tax code or amount takes the customer's default code, else the revenue account's,
    // and the tax at that code's rate on the document date, posted to the code's output account (else the VAT output control).
    private async Task<SalesInvoiceRequest> WithTaxDefaultsAsync(SalesInvoiceRequest request, Customer customer, CancellationToken ct)
    {
        if (request.TaxCode is not null || request.TaxAmount != 0m)
        {
            return request;
        }

        string? code = Clean(customer.DefaultTaxCode) ?? await AccountTaxCodeAsync(request.CompanyId, request.SalesAccountCode, ct).ConfigureAwait(false);
        if (code is null)
        {
            return request;
        }

        (decimal tax, string? account) = await DefaultTaxAsync(request.CompanyId, code, request.NetAmount, request.DocumentDate, ControlAccountType.VatOutput, ct).ConfigureAwait(false);
        return request with { TaxCode = code, TaxAmount = tax, TaxAccountCode = request.TaxAccountCode ?? (tax == 0m ? null : account) };
    }

    // As above for a purchase invoice: the supplier's default code, else the expense account's, to the code's input account (else the VAT input control).
    private async Task<PurchaseInvoiceRequest> WithTaxDefaultsAsync(PurchaseInvoiceRequest request, Supplier supplier, CancellationToken ct)
    {
        if (request.TaxCode is not null || request.TaxAmount != 0m)
        {
            return request;
        }

        string? code = Clean(supplier.DefaultTaxCode) ?? await AccountTaxCodeAsync(request.CompanyId, request.ExpenseAccountCode, ct).ConfigureAwait(false);
        if (code is null)
        {
            return request;
        }

        (decimal tax, string? account) = await DefaultTaxAsync(request.CompanyId, code, request.NetAmount, request.DocumentDate, ControlAccountType.VatInput, ct).ConfigureAwait(false);
        return request with { TaxCode = code, TaxAmount = tax, TaxAccountCode = request.TaxAccountCode ?? (tax == 0m ? null : account) };
    }

    private async Task<string?> AccountTaxCodeAsync(long companyId, string? accountCode, CancellationToken ct)
        => string.IsNullOrWhiteSpace(accountCode) ? null
            : Clean(await _db.Accounts.AsNoTracking().Where(a => a.CompanyId == companyId && a.AccountCode == accountCode).Select(a => a.DefaultTaxCode).FirstOrDefaultAsync(ct).ConfigureAwait(false));

    private async Task<(decimal Tax, string? Account)> DefaultTaxAsync(long companyId, string code, decimal net, DateOnly date, ControlAccountType control, CancellationToken ct)
    {
        TaxCode? tax = await _db.TaxCodes.AsNoTracking().Include(t => t.Rates).FirstOrDefaultAsync(t => t.CompanyId == companyId && t.Code == code && t.IsActive, ct).ConfigureAwait(false);
        if (tax is null)
        {
            return (0m, null);
        }

        decimal amount = decimal.Round(net * tax.RateOn(date) / 100m, 2, MidpointRounding.AwayFromZero);
        string? account = (control == ControlAccountType.VatOutput ? tax.OutputAccountCode : tax.InputAccountCode)
            ?? await _db.Accounts.AsNoTracking().Where(a => a.CompanyId == companyId && a.ControlAccountType == control).OrderBy(a => a.AccountCode).Select(a => a.AccountCode).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        return (amount, account);
    }

    private static decimal Round(decimal value) => decimal.Round(value, Scale, MidpointRounding.ToEven);
}
