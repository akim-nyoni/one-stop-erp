using System.Text;
using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Communications;
using One4All.Application.Abstractions.Procurement;
using One4All.Application.Abstractions.Trading;
using One4All.Core.Common;
using One4All.Core.Communications;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Trading;

/// <summary>Shared lookups for documents: a trader's correspondence address and the people a document goes to.</summary>
internal static class TraderDirectory
{
    /// <summary>The address printed on a trader's documents: the default postal address, else the default physical one, else any.</summary>
    public static async Task<IReadOnlyList<string>> CorrespondenceAsync(One4AllDbContext db, long companyId, TradingLedger ledger, long traderId, CancellationToken ct)
    {
        List<TraderAddress> addresses = await db.TraderAddresses.AsNoTracking().Where(a => a.CompanyId == companyId && a.Ledger == ledger && a.TraderId == traderId)
            .ToListAsync(ct).ConfigureAwait(false);
        TraderAddress? chosen = addresses.OrderBy(a => a.Kind == AddressKind.Postal ? 0 : a.Kind == AddressKind.Physical ? 1 : 2).ThenByDescending(a => a.IsDefault).ThenBy(a => a.Id).FirstOrDefault();
        return chosen?.Lines() ?? [];
    }

    /// <summary>The emails a document goes to: the contacts flagged for it, else the primary contact, else the customer's own email.</summary>
    public static async Task<IReadOnlyList<string>> RecipientsAsync(One4AllDbContext db, long companyId, TradingLedger ledger, long traderId, TraderDocument document, CancellationToken ct)
    {
        List<TraderContact> contacts = await db.TraderContacts.AsNoTracking()
            .Where(c => c.CompanyId == companyId && c.Ledger == ledger && c.TraderId == traderId && c.IsActive && c.Email != null && c.Email != string.Empty).ToListAsync(ct).ConfigureAwait(false);
        List<string> flagged = [.. contacts.Where(c => document switch
        {
            TraderDocument.Statement => c.ReceivesStatements,
            TraderDocument.Remittance => c.ReceivesRemittances,
            _ => c.ReceivesOrders,
        }).Select(c => c.Email!.Trim())];
        if (flagged.Count > 0)
        {
            return [.. flagged.Distinct(StringComparer.OrdinalIgnoreCase)];
        }

        if (contacts.FirstOrDefault(c => c.IsPrimary) is TraderContact primary)
        {
            return [primary.Email!.Trim()];
        }

        string? own = ledger == TradingLedger.AccountsReceivable
            ? await db.Customers.AsNoTracking().Where(c => c.Id == traderId).Select(c => c.Email).FirstOrDefaultAsync(ct).ConfigureAwait(false)
            : null;
        return string.IsNullOrWhiteSpace(own) ? [] : [own.Trim()];
    }
}

/// <summary>Customer and supplier addresses and contacts, and emailing their documents.</summary>
public sealed class TraderDirectoryService : ITraderDirectoryService
{
    private readonly One4AllDbContext _db;
    private readonly INotificationService _notifications;
    private readonly IStatementService _statements;
    private readonly IRemittanceService _remittances;
    private readonly IPurchaseOrderDocumentService _orders;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    /// <param name="notifications">Queues the emails.</param>
    /// <param name="statements">Renders statements.</param>
    /// <param name="remittances">Renders remittance advices.</param>
    /// <param name="orders">Renders purchase orders.</param>
    public TraderDirectoryService(One4AllDbContext db, INotificationService notifications, IStatementService statements, IRemittanceService remittances, IPurchaseOrderDocumentService orders)
    {
        _db = db;
        _notifications = notifications;
        _statements = statements;
        _remittances = remittances;
        _orders = orders;
    }

    /// <inheritdoc />
    public async Task<Result<long>> AddAddressAsync(long companyId, TradingLedger ledger, string traderCode, NewTraderAddress address, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(address.Line1))
        {
            return Error.Validation("Trader.AddressLine", "An address needs at least its first line.");
        }

        if (await TraderIdAsync(companyId, ledger, traderCode, ct).ConfigureAwait(false) is not long traderId)
        {
            return NotFound(ledger, traderCode);
        }

        List<TraderAddress> sameKind = await _db.TraderAddresses.Where(a => a.CompanyId == companyId && a.Ledger == ledger && a.TraderId == traderId && a.Kind == address.Kind)
            .ToListAsync(ct).ConfigureAwait(false);
        bool isDefault = address.IsDefault || sameKind.Count == 0;
        if (isDefault)
        {
            sameKind.ForEach(a => a.IsDefault = false);
        }

        TraderAddress added = new(companyId, ledger, traderId, address.Kind, address.Line1.Trim())
        {
            Line2 = Clean(address.Line2), City = Clean(address.City), Region = Clean(address.Region), PostalCode = Clean(address.PostalCode), Country = Clean(address.Country), IsDefault = isDefault,
        };
        _db.TraderAddresses.Add(added);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return added.Id;
    }

    /// <inheritdoc />
    public async Task<Result> RemoveAddressAsync(long companyId, long addressId, CancellationToken ct = default)
    {
        TraderAddress? address = await _db.TraderAddresses.FirstOrDefaultAsync(a => a.Id == addressId && a.CompanyId == companyId, ct).ConfigureAwait(false);
        if (address is null)
        {
            return Result.Failure(Error.NotFound("Trader.AddressNotFound", $"Address {addressId} not found."));
        }

        _db.TraderAddresses.Remove(address);
        if (address.IsDefault && await _db.TraderAddresses.Where(a => a.CompanyId == companyId && a.Ledger == address.Ledger && a.TraderId == address.TraderId && a.Kind == address.Kind && a.Id != addressId)
                .OrderBy(a => a.Id).FirstOrDefaultAsync(ct).ConfigureAwait(false) is TraderAddress next)
        {
            next.IsDefault = true;
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result<long>> AddContactAsync(long companyId, TradingLedger ledger, string traderCode, NewTraderContact contact, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(contact.Name))
        {
            return Error.Validation("Trader.ContactName", "A contact needs a name.");
        }

        if (!string.IsNullOrWhiteSpace(contact.Email) && !contact.Email.Contains('@', StringComparison.Ordinal))
        {
            return Error.Validation("Trader.ContactEmail", $"\"{contact.Email}\" is not an email address.");
        }

        if (await TraderIdAsync(companyId, ledger, traderCode, ct).ConfigureAwait(false) is not long traderId)
        {
            return NotFound(ledger, traderCode);
        }

        if (contact.IsPrimary)
        {
            foreach (TraderContact other in await _db.TraderContacts.Where(c => c.CompanyId == companyId && c.Ledger == ledger && c.TraderId == traderId && c.IsPrimary).ToListAsync(ct).ConfigureAwait(false))
            {
                other.IsPrimary = false;
            }
        }

        TraderContact added = new(companyId, ledger, traderId, contact.Name.Trim())
        {
            Role = Clean(contact.Role), Email = Clean(contact.Email), Phone = Clean(contact.Phone), IsPrimary = contact.IsPrimary, ReceivesStatements = contact.ReceivesStatements,
            ReceivesRemittances = contact.ReceivesRemittances, ReceivesOrders = contact.ReceivesOrders,
        };
        _db.TraderContacts.Add(added);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return added.Id;
    }

    /// <inheritdoc />
    public async Task<Result> RemoveContactAsync(long companyId, long contactId, CancellationToken ct = default)
    {
        TraderContact? contact = await _db.TraderContacts.FirstOrDefaultAsync(c => c.Id == contactId && c.CompanyId == companyId, ct).ConfigureAwait(false);
        if (contact is null)
        {
            return Result.Failure(Error.NotFound("Trader.ContactNotFound", $"Contact {contactId} not found."));
        }

        contact.IsActive = false;
        contact.IsPrimary = false;
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result<TraderDirectoryEntry>> DirectoryAsync(long companyId, TradingLedger ledger, string traderCode, CancellationToken ct = default)
    {
        if (await TraderIdAsync(companyId, ledger, traderCode, ct).ConfigureAwait(false) is not long traderId)
        {
            return NotFound(ledger, traderCode);
        }

        List<TraderAddress> addresses = await _db.TraderAddresses.AsNoTracking().Where(a => a.CompanyId == companyId && a.Ledger == ledger && a.TraderId == traderId)
            .OrderBy(a => a.Kind).ThenByDescending(a => a.IsDefault).ToListAsync(ct).ConfigureAwait(false);
        List<TraderContact> contacts = await _db.TraderContacts.AsNoTracking().Where(c => c.CompanyId == companyId && c.Ledger == ledger && c.TraderId == traderId && c.IsActive)
            .OrderByDescending(c => c.IsPrimary).ThenBy(c => c.Name).ToListAsync(ct).ConfigureAwait(false);
        return new TraderDirectoryEntry(addresses, contacts);
    }

    /// <inheritdoc />
    public async Task<Result<long>> EmailStatementAsync(long companyId, TradingLedger ledger, string traderCode, DateOnly asAt, CancellationToken ct = default)
    {
        if (await TraderIdAsync(companyId, ledger, traderCode, ct).ConfigureAwait(false) is not long traderId)
        {
            return NotFound(ledger, traderCode);
        }

        Result<string> text = ledger == TradingLedger.AccountsReceivable
            ? await _statements.RenderCustomerStatementTextAsync(companyId, traderId, asAt, ct: ct).ConfigureAwait(false)
            : await _statements.RenderSupplierStatementTextAsync(companyId, traderId, asAt, ct: ct).ConfigureAwait(false);
        return text.IsFailure ? text.Error
            : await SendAsync(companyId, ledger, traderId, TraderDocument.Statement, $"Statement {traderCode.Trim().ToUpperInvariant()} {asAt:yyyy-MM-dd}", text.Value,
                $"statement-{traderCode.Trim().ToLowerInvariant()}-{asAt:yyyyMMdd}.txt", ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<Result<long>> EmailRemittanceAsync(long companyId, long paymentOpenItemId, CancellationToken ct = default)
    {
        OpenItem? payment = await _db.OpenItems.AsNoTracking().FirstOrDefaultAsync(i => i.Id == paymentOpenItemId && i.CompanyId == companyId && i.Ledger == TradingLedger.AccountsPayable, ct)
            .ConfigureAwait(false);
        if (payment is null)
        {
            return Error.NotFound("Remittance.PaymentNotFound", $"Payment open item {paymentOpenItemId} not found.");
        }

        Result<string> text = await _remittances.RenderRemittanceTextAsync(companyId, paymentOpenItemId, ct: ct).ConfigureAwait(false);
        return text.IsFailure ? text.Error
            : await SendAsync(companyId, TradingLedger.AccountsPayable, payment.TraderId, TraderDocument.Remittance, $"Remittance advice {payment.DocumentNumber}", text.Value,
                $"remittance-{payment.DocumentNumber}.txt", ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<RemittanceBatch> EmailRemittancesAsync(long companyId, IReadOnlyCollection<string> paymentNumbers, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(paymentNumbers);
        List<string> numbers = [.. paymentNumbers.Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => n.Trim()).Distinct(StringComparer.Ordinal)];
        Dictionary<string, long> payments = await _db.OpenItems.AsNoTracking()
            .Where(i => i.CompanyId == companyId && i.Ledger == TradingLedger.AccountsPayable && i.ItemType == OpenItemType.Payment && numbers.Contains(i.DocumentNumber))
            .GroupBy(i => i.DocumentNumber).Select(g => new { Number = g.Key, Id = g.Min(i => i.Id) }).ToDictionaryAsync(x => x.Number, x => x.Id, StringComparer.Ordinal, ct).ConfigureAwait(false);
        int queued = 0;
        List<string> toPost = [], failed = [];
        foreach (string number in numbers)
        {
            if (!payments.TryGetValue(number, out long id))
            {
                failed.Add($"{number}: no supplier payment with this number");
                continue;
            }

            Result<long> sent = await EmailRemittanceAsync(companyId, id, ct).ConfigureAwait(false);
            if (sent.IsSuccess)
            {
                queued++;
            }
            else if (sent.Error.Code == "Trader.NoRecipient")
            {
                toPost.Add(number);
            }
            else
            {
                failed.Add($"{number}: {sent.Error.Message}");
            }
        }

        return new RemittanceBatch(queued, toPost, failed);
    }

    /// <inheritdoc />
    public async Task<Result<long>> EmailPurchaseOrderAsync(long companyId, long purchaseOrderId, CancellationToken ct = default)
    {
        var order = await _db.PurchaseOrders.AsNoTracking().Where(o => o.Id == purchaseOrderId && o.CompanyId == companyId).Select(o => new { o.SupplierId, o.OrderNumber })
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (order is null)
        {
            return Error.NotFound("Pop.OrderNotFound", $"Purchase order {purchaseOrderId} not found.");
        }

        Result<string> text = await _orders.RenderTextAsync(companyId, purchaseOrderId, ct: ct).ConfigureAwait(false);
        return text.IsFailure ? text.Error
            : await SendAsync(companyId, TradingLedger.AccountsPayable, order.SupplierId, TraderDocument.PurchaseOrder, $"Purchase order {order.OrderNumber}", text.Value,
                $"purchase-order-{order.OrderNumber}.txt", ct).ConfigureAwait(false);
    }

    private async Task<Result<long>> SendAsync(long companyId, TradingLedger ledger, long traderId, TraderDocument document, string subject, string text, string fileName, CancellationToken ct)
    {
        IReadOnlyList<string> recipients = await TraderDirectory.RecipientsAsync(_db, companyId, ledger, traderId, document, ct).ConfigureAwait(false);
        if (recipients.Count == 0)
        {
            return Error.Rule("Trader.NoRecipient", $"Nobody at this {(ledger == TradingLedger.AccountsReceivable ? "customer" : "supplier")} has an email address for {document} documents.");
        }

        return await _notifications.QueueDirectAsync(companyId, subject, MessageChannel.Email, recipients, subject, $"Please find attached: {subject}.",
            new MessageAttachment(fileName, "text/plain", Encoding.UTF8.GetBytes(text)), ct).ConfigureAwait(false);
    }

    private async Task<long?> TraderIdAsync(long companyId, TradingLedger ledger, string traderCode, CancellationToken ct)
    {
        string code = (traderCode ?? string.Empty).Trim().ToUpperInvariant();
        return ledger == TradingLedger.AccountsReceivable
            ? await _db.Customers.Where(c => c.CompanyId == companyId && c.Code.ToUpper() == code).Select(c => (long?)c.Id).FirstOrDefaultAsync(ct).ConfigureAwait(false)
            : await _db.Suppliers.Where(s => s.CompanyId == companyId && s.Code.ToUpper() == code).Select(s => (long?)s.Id).FirstOrDefaultAsync(ct).ConfigureAwait(false);
    }

    private static Error NotFound(TradingLedger ledger, string code)
        => Error.NotFound("Trader.NotFound", $"{(ledger == TradingLedger.AccountsReceivable ? "Customer" : "Supplier")} {code} not found.");

    private static string? Clean(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}
