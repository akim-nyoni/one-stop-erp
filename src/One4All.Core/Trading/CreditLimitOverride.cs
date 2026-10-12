using One4All.Core.Common;

namespace One4All.Core.Trading;

/// <summary>An invoice allowed over a customer's credit limit by a supervisor (<c>ar.credit_limit_override</c>, brief §5.1), kept for audit.</summary>
public sealed class CreditLimitOverride : Entity, ICompanyScoped
{
    private CreditLimitOverride() => DocumentNumber = string.Empty;

    /// <summary>Records the override.</summary>
    public CreditLimitOverride(long companyId, long customerId, string documentNumber, decimal creditLimit, decimal balanceAfter, long authorisedByUserId, long? enteredByUserId, DateTimeOffset at)
    {
        IsWarning = authorisedByUserId == 0;
        CompanyId = companyId;
        CustomerId = customerId;
        DocumentNumber = documentNumber;
        CreditLimit = creditLimit;
        BalanceAfter = balanceAfter;
        AuthorisedByUserId = authorisedByUserId;
        EnteredByUserId = enteredByUserId;
        At = at;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The customer.</summary>
    public long CustomerId { get; private set; }

    /// <summary>The invoice allowed.</summary>
    public string DocumentNumber { get; private set; }

    /// <summary>The limit at the time.</summary>
    public decimal CreditLimit { get; private set; }

    /// <summary>The balance the invoice took the customer to.</summary>
    public decimal BalanceAfter { get; private set; }

    /// <summary>Who allowed it.</summary>
    public long AuthorisedByUserId { get; private set; }

    /// <summary>Who raised the invoice.</summary>
    public long? EnteredByUserId { get; private set; }

    /// <summary>When.</summary>
    public DateTimeOffset At { get; private set; }

    /// <summary>Whether the invoice went through on a warn policy rather than a supervisor's override.</summary>
    public bool IsWarning { get; private set; }
}
