using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace One4All.Application.Documents;

/// <summary>A business document a company can lay out in the report designer.</summary>
/// <param name="Kind">The kind (the key in the company's layout choices).</param>
/// <param name="Name">What it is called.</param>
/// <param name="DesignableReportCode">The shipped report that is its data source and starting layout.</param>
/// <param name="NumberLabel">What identifies one (the parameter prompt).</param>
/// <param name="NeedsDate">Whether a date goes with the number (the municipal bill's month).</param>
public sealed record DocumentKind(string Kind, string Name, string DesignableReportCode, string NumberLabel, bool NeedsDate = false, bool Text = false);

/// <summary>
/// The documents with designer-editable layouts (brief §S), each with its designable data-source report: the business documents built as headed line documents
/// (DOC01–DOC12) and the text-backed documents (Text = true, DOC13 on) whose standard text lines are the rows a company frames with its own layout.
/// </summary>
public static class DocumentKinds
{
    /// <summary>A sales quotation.</summary>
    public const string Quotation = "Quotation";

    /// <summary>A sales order confirmation.</summary>
    public const string OrderConfirmation = "OrderConfirmation";

    /// <summary>A delivery note.</summary>
    public const string DeliveryNote = "DeliveryNote";

    /// <summary>A packing note.</summary>
    public const string PackingSlip = "PackingSlip";

    /// <summary>A purchase requisition.</summary>
    public const string Requisition = "Requisition";

    /// <summary>A goods received note.</summary>
    public const string GoodsReceivedNote = "GoodsReceivedNote";

    /// <summary>A supplier debit note.</summary>
    public const string SupplierDebitNote = "SupplierDebitNote";

    /// <summary>A school fee invoice.</summary>
    public const string FeeInvoice = "FeeInvoice";

    /// <summary>An interim payment certificate.</summary>
    public const string PaymentCertificate = "PaymentCertificate";

    /// <summary>A stand's monthly municipal bill.</summary>
    public const string MunicipalBill = "MunicipalBill";

    /// <summary>A customer tax invoice or credit note.</summary>
    public const string TaxInvoice = "TaxInvoice";

    /// <summary>A purchase order.</summary>
    public const string PurchaseOrder = "PurchaseOrder";

    /// <summary>The till receipt of a point-of-sale sale.</summary>
    public const string PosReceipt = "PosReceipt";

    /// <summary>A customer's open-item statement as at a date.</summary>
    public const string CustomerStatement = "CustomerStatement";

    /// <summary>A customer's balance-forward statement for the month of a date.</summary>
    public const string BalanceForwardStatement = "BalanceForwardStatement";

    /// <summary>A supplier's statement as at a date.</summary>
    public const string SupplierStatement = "SupplierStatement";

    /// <summary>A customer's reminder or final notice as at a date.</summary>
    public const string DunningLetter = "DunningLetter";

    /// <summary>The remittance advice of a supplier payment.</summary>
    public const string RemittanceAdvice = "RemittanceAdvice";

    /// <summary>An employee's payslip for a pay date (the latest when none is given).</summary>
    public const string Payslip = "Payslip";

    /// <summary>A payment voucher.</summary>
    public const string PaymentVoucher = "PaymentVoucher";

    /// <summary>A receipt voucher.</summary>
    public const string ReceiptVoucher = "ReceiptVoucher";

    /// <summary>A journal voucher.</summary>
    public const string JournalVoucher = "JournalVoucher";

    /// <summary>A petty cash voucher.</summary>
    public const string PettyCashVoucher = "PettyCashVoucher";

    /// <summary>A withholding tax certificate.</summary>
    public const string WithholdingCertificate = "WithholdingCertificate";

    /// <summary>A weighbridge ticket.</summary>
    public const string WeighbridgeTicket = "WeighbridgeTicket";

    /// <summary>A sales order's pick list for a warehouse.</summary>
    public const string PickList = "PickList";

    /// <summary>A request for quotation.</summary>
    public const string Rfq = "Rfq";

    /// <summary>An employee's certificate of service.</summary>
    public const string CertificateOfService = "CertificateOfService";

    /// <summary>A budget holder's statement as at a date.</summary>
    public const string BudgetHolderStatement = "BudgetHolderStatement";

    /// <summary>A stand's chain-of-title printout.</summary>
    public const string ChainOfTitle = "ChainOfTitle";

    /// <summary>A cheque (bank account code/cheque number).</summary>
    public const string Cheque = "Cheque";

    /// <summary>A price list's shelf labels.</summary>
    public const string ShelfLabels = "ShelfLabels";

    /// <summary>A property's rates clearance certificate (brief §14 report 153).</summary>
    public const string ClearanceCertificate = "ClearanceCertificate";

    /// <summary>A stock transfer note.</summary>
    public const string StockTransferNote = "StockTransferNote";

    /// <summary>A fixed asset's tag.</summary>
    public const string AssetTag = "AssetTag";

    /// <summary>An employee's leave application.</summary>
    public const string LeaveApplication = "LeaveApplication";

    /// <summary>An employee's leave balance certificate.</summary>
    public const string LeaveBalanceCertificate = "LeaveBalanceCertificate";

    /// <summary>An employee loan's agreement and statement.</summary>
    public const string LoanStatement = "LoanStatement";

    /// <summary>A gift voucher (brief §S).</summary>
    public const string GiftVoucher = "GiftVoucher";

    /// <summary>The EFT advice for a payment run (brief §S).</summary>
    public const string EftAdvice = "EftAdvice";

    /// <summary>A subcontractor's payment certificate (brief §S).</summary>
    public const string SubcontractCertificate = "SubcontractCertificate";

    /// <summary>A council licence or permit certificate (brief §S).</summary>
    public const string LicenceCertificate = "LicenceCertificate";

    /// <summary>Every kind, in catalogue order.</summary>
    public static IReadOnlyList<DocumentKind> All { get; } =
    [
        new(Quotation, "Quotation", "DOC01", "Quote number"),
        new(OrderConfirmation, "Order confirmation", "DOC02", "Sales order number"),
        new(DeliveryNote, "Delivery note", "DOC03", "Delivery reference"),
        new(PackingSlip, "Packing slip", "DOC04", "Delivery reference"),
        new(Requisition, "Purchase requisition", "DOC05", "Requisition number"),
        new(GoodsReceivedNote, "Goods received note", "DOC06", "GRN number"),
        new(SupplierDebitNote, "Supplier debit note", "DOC07", "Debit note number"),
        new(FeeInvoice, "Fee invoice", "DOC08", "Invoice number"),
        new(PaymentCertificate, "Payment certificate", "DOC09", "Certificate number"),
        new(MunicipalBill, "Municipal bill", "DOC10", "Stand number", NeedsDate: true),
        new(TaxInvoice, "Tax invoice / credit note", "DOC11", "Invoice or credit note number"),
        new(PurchaseOrder, "Purchase order", "DOC12", "Order number"),
        new(PosReceipt, "Till receipt", "DOC13", "Sale number", Text: true),
        new(CustomerStatement, "Customer statement", "DOC14", "Customer code", NeedsDate: true, Text: true),
        new(BalanceForwardStatement, "Balance-forward statement", "DOC15", "Customer code", NeedsDate: true, Text: true),
        new(SupplierStatement, "Supplier statement", "DOC16", "Supplier code", NeedsDate: true, Text: true),
        new(DunningLetter, "Reminder / dunning letter", "DOC17", "Customer code", NeedsDate: true, Text: true),
        new(RemittanceAdvice, "Remittance advice", "DOC18", "Payment document number", Text: true),
        new(Payslip, "Payslip", "DOC19", "Employee number", NeedsDate: true, Text: true),
        new(PaymentVoucher, "Payment voucher", "DOC20", "Voucher number", Text: true),
        new(ReceiptVoucher, "Receipt voucher", "DOC21", "Voucher number", Text: true),
        new(JournalVoucher, "Journal voucher", "DOC22", "Voucher number", Text: true),
        new(PettyCashVoucher, "Petty cash voucher", "DOC23", "Voucher number", Text: true),
        new(WithholdingCertificate, "Withholding tax certificate", "DOC24", "Certificate number", Text: true),
        new(WeighbridgeTicket, "Weighbridge ticket", "DOC25", "Ticket number", Text: true),
        new(PickList, "Pick list", "DOC26", "Sales order number/warehouse code", Text: true),
        new(Rfq, "Request for quotation", "DOC27", "RFQ number", Text: true),
        new(CertificateOfService, "Certificate of service", "DOC28", "Employee number", Text: true),
        new(BudgetHolderStatement, "Budget-holder statement", "DOC29", "Department code", NeedsDate: true, Text: true),
        new(ChainOfTitle, "Chain-of-title printout", "DOC30", "Stand number", Text: true),
        new(Cheque, "Cheque", "DOC31", "Bank account code/cheque number", Text: true),
        new(ShelfLabels, "Shelf labels", "DOC32", "Price list code", Text: true),
        new(ClearanceCertificate, "Rates clearance certificate", "DOC33", "Certificate number", Text: true),
        new(StockTransferNote, "Stock transfer note", "DOC34", "Transfer number", Text: true),
        new(AssetTag, "Asset tag", "DOC35", "Asset number", Text: true),
        new(LeaveApplication, "Leave application", "DOC36", "Employee number/start date (yyyy-MM-dd)", Text: true),
        new(LeaveBalanceCertificate, "Leave balance certificate", "DOC37", "Employee number", NeedsDate: true, Text: true),
        new(LoanStatement, "Loan agreement and statement", "DOC38", "Loan reference", Text: true),
        new(GiftVoucher, "Gift voucher", "DOC39", "Card number", Text: true),
        new(EftAdvice, "EFT advice", "DOC40", "Payment run number", Text: true),
        new(SubcontractCertificate, "Subcontractor payment certificate", "DOC41", "Certificate number", Text: true),
        new(LicenceCertificate, "Licence / permit certificate", "DOC42", "Licence number", Text: true),
    ];

    /// <summary>Finds a kind by key or name (case-insensitive).</summary>
    /// <param name="kindOrName">The key or name.</param>
    /// <returns>The kind, or null.</returns>
    public static DocumentKind? Find(string? kindOrName)
        => kindOrName is null ? null : All.FirstOrDefault(k => string.Equals(k.Kind, kindOrName.Trim(), StringComparison.OrdinalIgnoreCase) || string.Equals(k.Name, kindOrName.Trim(), StringComparison.OrdinalIgnoreCase));
}

/// <summary>Which report lays out each document for a company (kept as JSON on the company): a kind without an entry prints the standard text document.</summary>
public sealed class DocumentLayoutSettings
{
    private static readonly JsonSerializerOptions Json = new() { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    /// <summary>Gets the layout report code per document kind.</summary>
    public Dictionary<string, string> Layouts { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Reads the settings (an empty set when there is none, or the JSON is unreadable).</summary>
    /// <param name="json">The JSON.</param>
    /// <returns>The settings.</returns>
    public static DocumentLayoutSettings Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new DocumentLayoutSettings();
        }

        try
        {
            Dictionary<string, string>? layouts = JsonSerializer.Deserialize<Dictionary<string, string>>(json, Json);
            return new DocumentLayoutSettings { Layouts = new Dictionary<string, string>(layouts ?? [], StringComparer.OrdinalIgnoreCase) };
        }
        catch (JsonException)
        {
            return new DocumentLayoutSettings();
        }
    }

    /// <summary>The report that lays out a kind, or null for the standard text document.</summary>
    /// <param name="kind">The kind.</param>
    /// <returns>The report code.</returns>
    public string? LayoutFor(string kind) => Layouts.TryGetValue(kind, out string? code) && !string.IsNullOrWhiteSpace(code) ? code.Trim().ToUpperInvariant() : null;

    /// <summary>Sets (or with a blank code clears) a kind's layout.</summary>
    /// <param name="kind">The kind.</param>
    /// <param name="reportCode">The report, or blank.</param>
    public void Set(string kind, string? reportCode)
    {
        if (string.IsNullOrWhiteSpace(reportCode))
        {
            Layouts.Remove(kind);
        }
        else
        {
            Layouts[kind] = reportCode.Trim().ToUpperInvariant();
        }
    }

    /// <summary>The JSON to keep, or null when nothing is set.</summary>
    /// <returns>The JSON.</returns>
    public string? ToJson() => Layouts.Count == 0 ? null : JsonSerializer.Serialize(Layouts, Json);
}

/// <summary>
/// A business document as report rows: one row per line (one row when it has no lines), every row carrying the heading (title, company, party, the
/// reference fields as Field1…Field6 with their labels), the totals, notes and signature lines - what a designed layout places on the page.
/// </summary>
public static class BusinessDocumentRows
{
    /// <summary>The rows.</summary>
    /// <param name="document">The document.</param>
    /// <returns>One row per line.</returns>
    public static IReadOnlyList<IReadOnlyDictionary<string, object?>> Of(BusinessDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        Dictionary<string, object?> head = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Title"] = document.Title, ["CompanyName"] = document.CompanyName, ["CompanyTaxNumber"] = document.CompanyTaxNumber, ["PartyLabel"] = document.PartyLabel, ["PartyName"] = document.PartyName,
            ["Fields"] = string.Join("; ", document.Fields.Select(f => $"{f.Label}: {f.Value}")), ["ShowPrices"] = document.ShowPrices,
            ["Totals"] = string.Join("; ", document.Totals.Select(t => $"{t.Label}: {t.Amount.ToString("N2", CultureInfo.InvariantCulture)}")),
            ["Total"] = document.Totals.Count == 0 ? 0m : document.Totals[^1].Amount, ["TotalLabel"] = document.Totals.Count == 0 ? string.Empty : document.Totals[^1].Label,
            ["Notes"] = string.Join(" ", document.Notes), ["Signatures"] = string.Join("; ", document.Signatures),
        };
        for (int i = 0; i < 6; i++)
        {
            head[$"Field{i + 1}Label"] = i < document.Fields.Count ? document.Fields[i].Label : string.Empty;
            head[$"Field{i + 1}"] = i < document.Fields.Count ? document.Fields[i].Value : string.Empty;
        }

        IReadOnlyList<BusinessDocumentLine> lines = document.Lines.Count == 0 ? [new BusinessDocumentLine(string.Empty, string.Empty, 0m)] : document.Lines;
        return [.. lines.Select((line, index) =>
        {
            Dictionary<string, object?> row = new(head, StringComparer.OrdinalIgnoreCase)
            {
                ["LineNo"] = document.Lines.Count == 0 ? 0 : index + 1, ["Code"] = line.Code, ["Description"] = line.Description, ["Quantity"] = line.Quantity,
                ["UnitPrice"] = line.UnitPrice ?? 0m, ["Amount"] = line.Amount ?? 0m,
            };
            return (IReadOnlyDictionary<string, object?>)row;
        })];
    }
}
