using One4All.Core.Common;

namespace One4All.Core.Banking;

/// <summary>
/// A bank's payment-file layout (<c>cb.bank_file_layout</c>, brief §4.15.7): header, detail and trailer templates the EFT files for
/// payroll and supplier payment runs are rendered from - the format is configuration, not code.
/// </summary>
public sealed class BankFileLayout : Entity, ICompanyScoped
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private BankFileLayout()
    {
        Code = string.Empty;
        Name = string.Empty;
        DetailTemplate = string.Empty;
        LineEnding = "\r\n";
    }

    /// <summary>Creates the layout.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="code">The code.</param>
    /// <param name="name">The bank / format name.</param>
    public BankFileLayout(long companyId, string code, string name)
    {
        CompanyId = companyId;
        Code = code.Trim().ToUpperInvariant();
        Name = name;
        DetailTemplate = string.Empty;
        LineEnding = "\r\n";
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>The code.</summary>
    public string Code { get; private set; }

    /// <summary>Gets or sets the bank / format name.</summary>
    public string Name { get; set; }

    /// <summary>Gets or sets the header line template (none when null).</summary>
    public string? HeaderTemplate { get; set; }

    /// <summary>Gets or sets the template for each payment.</summary>
    public string DetailTemplate { get; set; }

    /// <summary>Gets or sets the trailer line template (none when null).</summary>
    public string? TrailerTemplate { get; set; }

    /// <summary>Gets or sets the line ending (CRLF by default).</summary>
    public string LineEnding { get; set; }
}
