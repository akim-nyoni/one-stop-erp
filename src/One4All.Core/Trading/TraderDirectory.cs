using One4All.Core.Common;

namespace One4All.Core.Trading;

/// <summary>What an address is used for.</summary>
public enum AddressKind
{
    /// <summary>Where correspondence, statements and invoices go.</summary>
    Postal = 1,

    /// <summary>Where the trader is.</summary>
    Physical = 2,

    /// <summary>Where goods are delivered.</summary>
    Delivery = 3,
}

/// <summary>An address of a customer or supplier (brief §4.5 / §4.6); one of each kind is the default.</summary>
public sealed class TraderAddress : AuditableEntity, ICompanyScoped
{
    private TraderAddress() => Line1 = string.Empty;

    /// <summary>Creates an address.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="ledger">Customer or supplier.</param>
    /// <param name="traderId">The trader.</param>
    /// <param name="kind">What it is for.</param>
    /// <param name="line1">The first line.</param>
    public TraderAddress(long companyId, TradingLedger ledger, long traderId, AddressKind kind, string line1)
    {
        CompanyId = companyId;
        Ledger = ledger;
        TraderId = traderId;
        Kind = kind;
        Line1 = line1;
    }

    /// <summary>The company.</summary>
    public long CompanyId { get; private set; }

    /// <summary>Customer or supplier.</summary>
    public TradingLedger Ledger { get; private set; }

    /// <summary>The trader.</summary>
    public long TraderId { get; private set; }

    /// <summary>What it is for.</summary>
    public AddressKind Kind { get; private set; }

    /// <summary>First line (street, P O Box).</summary>
    public string Line1 { get; set; }

    /// <summary>Second line.</summary>
    public string? Line2 { get; set; }

    /// <summary>Town or city.</summary>
    public string? City { get; set; }

    /// <summary>Province or region.</summary>
    public string? Region { get; set; }

    /// <summary>Postal code.</summary>
    public string? PostalCode { get; set; }

    /// <summary>Country.</summary>
    public string? Country { get; set; }

    /// <summary>Whether it is the default of its kind.</summary>
    public bool IsDefault { get; set; }

    /// <summary>The address as printed lines (blank parts left out; the city and postal code on one line).</summary>
    /// <returns>The lines.</returns>
    public IReadOnlyList<string> Lines()
    {
        string town = string.Join(' ', new[] { City, PostalCode }.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p!.Trim()));
        return [.. new[] { Line1, Line2, town, Region, Country }.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p!.Trim())];
    }
}

/// <summary>A person at a customer or supplier, and which documents they receive.</summary>
public sealed class TraderContact : AuditableEntity, ICompanyScoped
{
    private TraderContact() => Name = string.Empty;

    /// <summary>Creates a contact.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="ledger">Customer or supplier.</param>
    /// <param name="traderId">The trader.</param>
    /// <param name="name">Their name.</param>
    public TraderContact(long companyId, TradingLedger ledger, long traderId, string name)
    {
        CompanyId = companyId;
        Ledger = ledger;
        TraderId = traderId;
        Name = name;
        IsActive = true;
    }

    /// <summary>The company.</summary>
    public long CompanyId { get; private set; }

    /// <summary>Customer or supplier.</summary>
    public TradingLedger Ledger { get; private set; }

    /// <summary>The trader.</summary>
    public long TraderId { get; private set; }

    /// <summary>Their name.</summary>
    public string Name { get; set; }

    /// <summary>Their role (accounts, buyer, director...).</summary>
    public string? Role { get; set; }

    /// <summary>Email address.</summary>
    public string? Email { get; set; }

    /// <summary>Phone number.</summary>
    public string? Phone { get; set; }

    /// <summary>The main contact (used when nobody is named for a document).</summary>
    public bool IsPrimary { get; set; }

    /// <summary>Receives statements (customers and suppliers).</summary>
    public bool ReceivesStatements { get; set; }

    /// <summary>Receives remittance advices (suppliers).</summary>
    public bool ReceivesRemittances { get; set; }

    /// <summary>Receives purchase orders (suppliers).</summary>
    public bool ReceivesOrders { get; set; }

    /// <summary>Whether still in use.</summary>
    public bool IsActive { get; set; }
}
