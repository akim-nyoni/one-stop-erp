using One4All.Core.Common;

namespace One4All.Core.Accounting;

/// <summary>
/// A VAT return as settled and filed (<c>gl.vat_return</c>, brief §3.5): the period, the basis it was worked out on, the output and input tax due,
/// the net, the VAT held back as not yet due, and the settlement journal. VAT postings dated into a filed period are refused.
/// </summary>
public sealed class VatReturnFiling : Entity, ICompanyScoped
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    private VatReturnFiling() => DocumentNumber = string.Empty;

    /// <summary>Records a filed return.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="fromDate">The period's first day.</param>
    /// <param name="toDate">The period's last day.</param>
    /// <param name="basis">The basis it was worked out on.</param>
    /// <param name="documentNumber">The settlement document number.</param>
    public VatReturnFiling(long companyId, DateOnly fromDate, DateOnly toDate, TaxPointBasis basis, string documentNumber)
    {
        CompanyId = companyId;
        FromDate = fromDate;
        ToDate = toDate;
        Basis = basis;
        DocumentNumber = documentNumber;
    }

    /// <inheritdoc />
    public long CompanyId { get; private set; }

    /// <summary>Gets the period's first day.</summary>
    public DateOnly FromDate { get; private set; }

    /// <summary>Gets the period's last day.</summary>
    public DateOnly ToDate { get; private set; }

    /// <summary>Gets the basis.</summary>
    public TaxPointBasis Basis { get; private set; }

    /// <summary>Gets the settlement document number.</summary>
    public string DocumentNumber { get; private set; }

    /// <summary>Gets or sets the output tax due.</summary>
    public decimal OutputTax { get; set; }

    /// <summary>Gets or sets the input tax claimed.</summary>
    public decimal InputTax { get; set; }

    /// <summary>Gets or sets the VAT held back as not yet due (+ output held, - input held) at this return.</summary>
    public decimal Deferred { get; set; }

    /// <summary>Gets or sets the settlement journal.</summary>
    public string? JournalNumber { get; set; }

    /// <summary>Gets or sets who filed it.</summary>
    public long? FiledByUserId { get; set; }

    /// <summary>Gets the net payable (refundable when negative).</summary>
    public decimal NetPayable => OutputTax - InputTax;
}
