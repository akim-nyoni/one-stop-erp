namespace One4All.Core.Accounting;

/// <summary>
/// The Sage 200-style composite posting key: <c>AccountCode(8) + CostCentre(3) + Department(3)</c>
/// (brief §3.1). Cost centre and department are analysis dimensions on the base nominal account; every
/// posting line and every period balance is keyed by this triple, so reports run by any combination.
/// A blank cost centre / department (all spaces or empty) means "unanalysed".
/// </summary>
public readonly record struct AccountKey
{
    /// <summary>The blank cost-centre / department token (unanalysed).</summary>
    public const string Blank = "";

    /// <summary>Creates a posting key. Codes are trimmed and upper-cased.</summary>
    /// <param name="accountCode">The base nominal account code (required, up to 8 chars).</param>
    /// <param name="costCentre">The cost-centre code (optional, up to 3 chars).</param>
    /// <param name="department">The department code (optional, up to 3 chars).</param>
    /// <exception cref="ArgumentException">Thrown when the account code is empty or a segment is too long.</exception>
    public AccountKey(string accountCode, string? costCentre = null, string? department = null)
    {
        if (string.IsNullOrWhiteSpace(accountCode))
        {
            throw new ArgumentException("Account code is required.", nameof(accountCode));
        }

        AccountCode = Normalize(accountCode, 8, nameof(accountCode));
        CostCentre = Normalize(costCentre ?? Blank, 3, nameof(costCentre));
        Department = Normalize(department ?? Blank, 3, nameof(department));
    }

    /// <summary>The base nominal account code.</summary>
    public string AccountCode { get; }

    /// <summary>The cost-centre code (empty when unanalysed).</summary>
    public string CostCentre { get; }

    /// <summary>The department code (empty when unanalysed).</summary>
    public string Department { get; }

    /// <summary>Renders the key as <c>ACCOUNT/CC/DEPT</c> (blank segments shown as <c>-</c>).</summary>
    /// <returns>The formatted key.</returns>
    public override string ToString() =>
        $"{AccountCode}/{(CostCentre.Length == 0 ? "-" : CostCentre)}/{(Department.Length == 0 ? "-" : Department)}";

    private static string Normalize(string value, int maxLength, string paramName)
    {
        string normalized = value.Trim().ToUpperInvariant();
        if (normalized.Length > maxLength)
        {
            throw new ArgumentException($"'{value}' exceeds {maxLength} characters.", paramName);
        }

        return normalized;
    }
}
