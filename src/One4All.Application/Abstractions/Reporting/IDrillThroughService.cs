using One4All.Core.Common;

namespace One4All.Application.Abstractions.Reporting;

/// <summary>
/// Drill-through from a report cell to the record behind it (brief §9): a document number opens the invoice, credit note, receipt or
/// bill; an order number the purchase order; a GRN number the goods received note; a receipt number the till or cash-office receipt;
/// a stand number the stand's register entry and chain of title; an employee and period the payslip; a tax code the VAT lines that
/// make up the return. Each is rendered as text for the viewer's drill window.
/// </summary>
public interface IDrillThroughService
{
    /// <summary>The targets this service describes.</summary>
    static readonly string[] Targets = ["Document", "PurchaseOrder", "Grn", "Receipt", "Stand", "Payslip", "TaxCode"];

    /// <summary>Describes a target record as text.</summary>
    /// <param name="companyId">The company.</param>
    /// <param name="target">Document, PurchaseOrder, Grn, Receipt, Stand, Payslip (key "employee:period") or TaxCode.</param>
    /// <param name="key">The record's number or code.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The text, or not-found.</returns>
    Task<Result<string>> DescribeAsync(long companyId, string target, string key, CancellationToken ct = default);
}
