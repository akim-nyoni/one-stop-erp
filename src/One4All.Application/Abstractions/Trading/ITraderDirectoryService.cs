using One4All.Core.Common;
using One4All.Core.Trading;

namespace One4All.Application.Abstractions.Trading;

/// <summary>A new address for a trader.</summary>
public sealed record NewTraderAddress(AddressKind Kind, string Line1, string? Line2 = null, string? City = null, string? Region = null, string? PostalCode = null, string? Country = null,
    bool IsDefault = true);

/// <summary>A new contact for a trader.</summary>
public sealed record NewTraderContact(string Name, string? Role = null, string? Email = null, string? Phone = null, bool IsPrimary = false, bool ReceivesStatements = false,
    bool ReceivesRemittances = false, bool ReceivesOrders = false);

/// <summary>Which documents a contact is chosen for.</summary>
public enum TraderDocument
{
    /// <summary>Statements.</summary>
    Statement = 1,

    /// <summary>Remittance advices.</summary>
    Remittance = 2,

    /// <summary>Purchase orders.</summary>
    PurchaseOrder = 3,
}

/// <summary>A trader's addresses and contacts.</summary>
/// <param name="Addresses">Its addresses.</param>
/// <param name="Contacts">Its active contacts.</param>
public sealed record TraderDirectoryEntry(IReadOnlyList<TraderAddress> Addresses, IReadOnlyList<TraderContact> Contacts);

/// <summary>
/// Customer and supplier addresses and contacts (brief §4.5 / §4.6): kept per trader, printed on their documents, and used to email
/// statements, remittance advices and purchase orders to the right people.
/// </summary>
public interface ITraderDirectoryService
{
    /// <summary>Adds an address (a default replaces the previous default of its kind).</summary>
    Task<Result<long>> AddAddressAsync(long companyId, TradingLedger ledger, string traderCode, NewTraderAddress address, CancellationToken ct = default);

    /// <summary>Removes an address.</summary>
    Task<Result> RemoveAddressAsync(long companyId, long addressId, CancellationToken ct = default);

    /// <summary>Adds a contact (a primary contact replaces the previous one).</summary>
    Task<Result<long>> AddContactAsync(long companyId, TradingLedger ledger, string traderCode, NewTraderContact contact, CancellationToken ct = default);

    /// <summary>Stops using a contact.</summary>
    Task<Result> RemoveContactAsync(long companyId, long contactId, CancellationToken ct = default);

    /// <summary>A trader's addresses and active contacts.</summary>
    Task<Result<TraderDirectoryEntry>> DirectoryAsync(long companyId, TradingLedger ledger, string traderCode, CancellationToken ct = default);

    /// <summary>Emails a customer's or supplier's statement to its statement contacts.</summary>
    Task<Result<long>> EmailStatementAsync(long companyId, TradingLedger ledger, string traderCode, DateOnly asAt, CancellationToken ct = default);

    /// <summary>Emails a supplier payment's remittance advice to the supplier's remittance contacts.</summary>
    Task<Result<long>> EmailRemittanceAsync(long companyId, long paymentOpenItemId, CancellationToken ct = default);

    /// <summary>Emails a purchase order to the supplier's order contacts.</summary>
    Task<Result<long>> EmailPurchaseOrderAsync(long companyId, long purchaseOrderId, CancellationToken ct = default);
}
