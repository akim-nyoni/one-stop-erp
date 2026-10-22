using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Accounting;
using One4All.Core.Common;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Accounting;

/// <summary>
/// Signs a document with the fiscal device, never failing the caller: unreachable means queued for the retry task. A document posted in a
/// unit of work is signed only once that unit commits (<see cref="SignAfterCommitAsync"/>): a unit attempted again after a lost race must
/// not have it signed twice, and one that rolls back must not leave the revenue authority holding a signed document that does not exist.
/// </summary>
public static class FiscalSigning
{
    /// <summary>Signs the document.</summary>
    /// <param name="fiscal">The device (null or disabled: not required).</param>
    /// <param name="request">The document.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The code and verification when signed; null code and queued when it waits; null when no device is in use.</returns>
    public static async Task<FiscalReceiptResult?> SignAsync(IFiscalDeviceProvider? fiscal, FiscalReceiptRequest request, CancellationToken ct)
    {
        if (fiscal is not { IsEnabled: true })
        {
            return null;
        }

        try
        {
            Result<FiscalReceiptResult> result = await fiscal.FiscaliseAsync(request, ct).ConfigureAwait(false);
            return result.IsSuccess ? result.Value : new FiscalReceiptResult(null, null, null, Queued: true);
        }
        catch (Exception ex) when (ex is IOException or TimeoutException or HttpRequestException or InvalidOperationException)
        {
            return new FiscalReceiptResult(null, null, null, Queued: true);
        }
    }

    /// <summary>
    /// Signs a document once the unit of work in progress commits, then records the signature in a save of its own. The caller marks the
    /// document queued inside the unit (and saves it there), so a device that cannot be reached, or a failure between the commit and the
    /// signature, leaves it queued for the retry task.
    /// </summary>
    /// <param name="db">The database context.</param>
    /// <param name="fiscal">The device (null or disabled: nothing is signed).</param>
    /// <param name="request">The document.</param>
    /// <param name="record">Writes the signature to the document's records (and clears their queued flag).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task that completes when the signing has been registered (or, outside a unit of work, done).</returns>
    internal static Task SignAfterCommitAsync(One4AllDbContext db, IFiscalDeviceProvider? fiscal, FiscalReceiptRequest request, Action<FiscalReceiptResult> record, CancellationToken ct)
        => fiscal is not { IsEnabled: true } ? Task.CompletedTask : db.AfterCommitAsync(async () =>
        {
            if (await SignAsync(fiscal, request, ct).ConfigureAwait(false) is { Queued: false, FiscalCode: not null } signed)
            {
                record(signed);
                await db.SaveChangesAsync(ct).ConfigureAwait(false);
            }
        });

    /// <summary>Whether a sales ledger document is one the fiscal device signs: a tax invoice or a credit note.</summary>
    /// <param name="ledger">The sub-ledger.</param>
    /// <param name="itemType">The document type.</param>
    /// <returns>True when it is signed.</returns>
    internal static bool IsFiscalised(TradingLedger ledger, OpenItemType itemType)
        => ledger == TradingLedger.AccountsReceivable && itemType is OpenItemType.Invoice or OpenItemType.CreditNote;

    /// <summary>What the device is given for a sales ledger tax invoice or credit note.</summary>
    /// <param name="item">The invoice or credit note.</param>
    /// <param name="buyerTaxNumber">The customer's tax registration, or null.</param>
    /// <param name="issuedAtUtc">When it is signed.</param>
    /// <returns>The request.</returns>
    internal static FiscalReceiptRequest RequestFor(OpenItem item, string? buyerTaxNumber, DateTimeOffset issuedAtUtc)
        => new(item.CompanyId, item.DocumentNumber, item.OriginalTransaction, item.FiscalTax, item.Currency.Value, issuedAtUtc,
            item.ItemType == OpenItemType.CreditNote ? FiscalDocumentKind.CreditNote : FiscalDocumentKind.Invoice, item.ReferenceDocumentNumber, buyerTaxNumber);

    /// <summary>
    /// Signs the sales ledger invoices and credit notes still waiting for the device, oldest first, saving after each page so a long queue
    /// (a month's municipal bills) is worked through in steps.
    /// </summary>
    /// <param name="db">The database context.</param>
    /// <param name="fiscal">The device (null or disabled: nothing is signed).</param>
    /// <param name="companyId">The company.</param>
    /// <param name="issuedAtUtc">When they are signed.</param>
    /// <param name="max">The most to try in this call.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>How many were signed, and how many still wait.</returns>
    internal static async Task<(int Fiscalised, int StillQueued)> SignQueuedOpenItemsAsync(One4AllDbContext db, IFiscalDeviceProvider? fiscal, long companyId, DateTimeOffset issuedAtUtc,
        int max, CancellationToken ct)
    {
        if (fiscal is not { IsEnabled: true })
        {
            return (0, 0);
        }

        const int Page = 500;
        int signed = 0, waiting = 0;
        long after = 0;
        while (signed + waiting < max)
        {
            List<OpenItem> page = await db.OpenItems.Where(i => i.CompanyId == companyId && i.FiscalQueued && i.Id > after).OrderBy(i => i.Id)
                .Take(Math.Min(Page, max - signed - waiting)).ToListAsync(ct).ConfigureAwait(false);
            if (page.Count == 0)
            {
                break;
            }

            List<long> buyerIds = [.. page.Select(i => i.TraderId).Distinct()];
            Dictionary<long, string?> buyers = await db.Customers.AsNoTracking().Where(c => buyerIds.Contains(c.Id))
                .ToDictionaryAsync(c => c.Id, c => c.TaxRegistration, ct).ConfigureAwait(false);
            int signedBefore = signed;
            foreach (OpenItem item in page)
            {
                if (await SignAsync(fiscal, RequestFor(item, buyers.GetValueOrDefault(item.TraderId), issuedAtUtc), ct).ConfigureAwait(false) is { Queued: false, FiscalCode: not null } ok)
                {
                    (item.FiscalCode, item.FiscalVerification, item.FiscalQueued) = (ok.FiscalCode, ok.SignatureOrQr, false);
                    signed++;
                }
                else
                {
                    waiting++;
                }
            }

            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            if (signed == signedBefore)
            {
                break; // the device signed none of a page: it is down, and the rest wait for the next run
            }

            after = page[^1].Id;
        }

        // What still waits is the whole backlog, not only what was tried this time.
        return (signed, await db.OpenItems.CountAsync(i => i.CompanyId == companyId && i.FiscalQueued, ct).ConfigureAwait(false));
    }
}
