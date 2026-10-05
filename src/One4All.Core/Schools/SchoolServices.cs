using One4All.Core.Common;

namespace One4All.Core.Schools;

/// <summary>A school transport route billed per term (<c>sch.transport_route</c>).</summary>
public sealed class TransportRoute : Entity, ICompanyScoped
{
    private TransportRoute()
    {
        Code = string.Empty;
        Name = string.Empty;
        RevenueAccountCode = string.Empty;
    }

    /// <summary>Creates a route.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="code">The route code.</param>
    /// <param name="name">The route name.</param>
    /// <param name="capacity">Seats on the route.</param>
    /// <param name="feePerTerm">The fee per student per term.</param>
    /// <param name="revenueAccountCode">The transport revenue account.</param>
    public TransportRoute(long companyId, string code, string name, int capacity, decimal feePerTerm, string revenueAccountCode)
    {
        CompanyId = companyId;
        Code = code.Trim().ToUpperInvariant();
        Name = name;
        Capacity = capacity;
        FeePerTerm = feePerTerm;
        RevenueAccountCode = revenueAccountCode;
    }

    /// <summary>Gets the company.</summary>
    public long CompanyId { get; private set; }

    /// <summary>Gets the route code.</summary>
    public string Code { get; private set; }

    /// <summary>Gets the route name.</summary>
    public string Name { get; private set; }

    /// <summary>Gets or sets the seats.</summary>
    public int Capacity { get; set; }

    /// <summary>Gets or sets the fee per term.</summary>
    public decimal FeePerTerm { get; set; }

    /// <summary>Gets the revenue account.</summary>
    public string RevenueAccountCode { get; private set; }
}

/// <summary>A boarding house / hostel billed per term (<c>sch.hostel</c>).</summary>
public sealed class Hostel : Entity, ICompanyScoped
{
    private Hostel()
    {
        Code = string.Empty;
        Name = string.Empty;
        RevenueAccountCode = string.Empty;
    }

    /// <summary>Creates a hostel.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="code">The hostel code.</param>
    /// <param name="name">The name.</param>
    /// <param name="capacity">Beds.</param>
    /// <param name="feePerTerm">The boarding fee per term.</param>
    /// <param name="revenueAccountCode">The boarding revenue account.</param>
    public Hostel(long companyId, string code, string name, int capacity, decimal feePerTerm, string revenueAccountCode)
    {
        CompanyId = companyId;
        Code = code.Trim().ToUpperInvariant();
        Name = name;
        Capacity = capacity;
        FeePerTerm = feePerTerm;
        RevenueAccountCode = revenueAccountCode;
    }

    /// <summary>Gets the company.</summary>
    public long CompanyId { get; private set; }

    /// <summary>Gets the hostel code.</summary>
    public string Code { get; private set; }

    /// <summary>Gets the name.</summary>
    public string Name { get; private set; }

    /// <summary>Gets or sets the beds.</summary>
    public int Capacity { get; set; }

    /// <summary>Gets or sets the boarding fee per term.</summary>
    public decimal FeePerTerm { get; set; }

    /// <summary>Gets the revenue account.</summary>
    public string RevenueAccountCode { get; private set; }
}

/// <summary>A movement on a student's spending wallet (<c>sch.wallet_entry</c>): a top-up (+) or a tuck-shop spend (−).</summary>
public sealed class StudentWalletEntry : Entity, ICompanyScoped
{
    private StudentWalletEntry()
    {
        Description = string.Empty;
        JournalNumber = string.Empty;
    }

    /// <summary>Records a movement.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="studentId">The student.</param>
    /// <param name="date">The date.</param>
    /// <param name="amount">The signed amount (+ top-up, − spend).</param>
    /// <param name="description">What it was.</param>
    /// <param name="journalNumber">The journal that posted it.</param>
    public StudentWalletEntry(long companyId, long studentId, DateOnly date, decimal amount, string description, string journalNumber)
    {
        CompanyId = companyId;
        StudentId = studentId;
        Date = date;
        Amount = amount;
        Description = description;
        JournalNumber = journalNumber;
    }

    /// <summary>Gets the company.</summary>
    public long CompanyId { get; private set; }

    /// <summary>Gets the student.</summary>
    public long StudentId { get; private set; }

    /// <summary>Gets the date.</summary>
    public DateOnly Date { get; private set; }

    /// <summary>Gets the signed amount.</summary>
    public decimal Amount { get; private set; }

    /// <summary>Gets what it was.</summary>
    public string Description { get; private set; }

    /// <summary>Gets the journal.</summary>
    public string JournalNumber { get; private set; }
}
