using One4All.Reporting.Model;
using static One4All.Reporting.SystemReports;

namespace One4All.Reporting;

/// <summary>
/// The designable documents (brief §S): DOC01–DOC12 give one business document's heading, party, reference fields, lines and totals as rows, as the data
/// source a company copies and lays out as its own quotation, delivery note, invoice or purchase order in the report designer; the company then names its
/// layout under System Manager › Document layouts and the document prints through it. DOC13–DOC46 give the text-backed documents (statements, reminders,
/// remittances, payslips, vouchers, certificates, tickets, pick lists, RFQs, the chain of title, cheques and shelf labels) one row per line of their
/// standard text, so a company frames them with its logo, header, footer and fonts.
/// </summary>
public static class DocumentReports
{
    private const string Module = "Documents";

    private static readonly Column[] BusinessColumns =
    [
        new Column("Title", "Document", 110), new Column("PartyLabel", "Party", 60), new Column("PartyName", "Name", 160), new Column("Field1Label", "Ref.", 60), new Column("Field1", "Number", 80),
        new Column("Field2Label", "Field", 60), new Column("Field2", "Value", 80), new Column("Code", "Code", 70), new Column("Description", "Description", 180), new Column("Quantity", "Qty", 60),
        new Column("UnitPrice", "Price", 70), new Column("Amount", "Amount", 85, true), new Column("TotalLabel", "Total", 120), new Column("Total", "Total amount", 90),
    ];

    private static readonly Column[] TextColumns =
    [
        new Column("Title", "Document", 100), new Column("Number", "Number", 90), new Column("Date", "Date", 70), new Column("LineNo", "Line", 40), new Column("Line", "Text", 420),
    ];

    /// <summary>The definitions.</summary>
    /// <returns>Each definition, freshly built.</returns>
    public static IEnumerable<ReportDefinition> All()
    {
        (string Name, ParameterType Type)[] number = [("Number", ParameterType.String)];
        yield return Business("DOC01", "Quotation (designable)", "A sales quotation by its number", number);
        yield return Business("DOC02", "Order Confirmation (designable)", "A sales order confirmation by its order number", number);
        yield return Business("DOC03", "Delivery Note (designable)", "A delivery note by its delivery reference", number);
        yield return Business("DOC04", "Packing Note (designable)", "A packing note by its delivery reference", number);
        yield return Business("DOC05", "Purchase Requisition (designable)", "A purchase requisition by its number", number);
        yield return Business("DOC06", "Goods Received Note (designable)", "A goods received note by its GRN number", number);
        yield return Business("DOC07", "Supplier Debit Note (designable)", "A supplier debit note by its number", number);
        yield return Business("DOC08", "Fee Invoice (designable)", "A school fee invoice by its number", number);
        yield return Business("DOC09", "Payment Certificate (designable)", "An interim payment certificate by its number", number);
        yield return Business("DOC10", "Municipal Bill (designable)", "A stand's monthly municipal bill by stand number and any day in the month", [("Number", ParameterType.String), ("Date", ParameterType.Date)]);
        yield return Listing("DOC11", "Tax Invoice / Credit Note (designable)", Module,
            "A customer tax invoice or credit note by its number - company, customer and address, dates, each line, net, tax, gross and what is still outstanding - as the data source for a company's own invoice layout.",
            "DocTaxInvoice",
            [new Column("Kind", "Document", 80), new Column("CustomerCode", "Customer", 70), new Column("CustomerName", "Name", 150), new Column("Number", "Number", 80), new Column("Date", "Date", 70),
             new Column("DueDate", "Due", 70), new Column("Description", "Description", 200), new Column("Amount", "Amount", 85, true), new Column("Net", "Net", 85), new Column("Tax", "Tax", 85),
             new Column("Gross", "Gross", 85), new Column("Outstanding", "Outstanding", 85)], number);
        yield return Listing("DOC12", "Purchase Order (designable)", Module,
            "A purchase order by its number - supplier and address, date, terms, each line with quantity and price, and the order total - as the data source for a company's own purchase order layout.",
            "DocPurchaseOrder",
            [new Column("SupplierCode", "Supplier", 70), new Column("SupplierName", "Name", 150), new Column("Number", "Order", 80), new Column("Date", "Date", 70), new Column("Status", "Status", 70),
             new Column("Code", "Code", 70), new Column("Description", "Description", 180), new Column("Quantity", "Qty", 60), new Column("UnitPrice", "Price", 70), new Column("Amount", "Amount", 85, true),
             new Column("Total", "Order total", 90)], number);
        yield return Text("DOC13", "Till Receipt (designable)", "A till receipt by its sale number");
        yield return Text("DOC14", "Customer Statement (designable)", "A customer's open-item statement by customer code as at a date", withDate: true);
        yield return Text("DOC15", "Balance-Forward Statement (designable)", "A customer's balance-forward statement by customer code for the month of a date", withDate: true);
        yield return Text("DOC16", "Supplier Statement (designable)", "A supplier's statement by supplier code as at a date", withDate: true);
        yield return Text("DOC17", "Dunning Letter (designable)", "A customer's reminder or final notice by customer code as at a date", withDate: true);
        yield return Text("DOC18", "Remittance Advice (designable)", "A supplier payment's remittance advice by its document number");
        yield return Text("DOC19", "Payslip (designable)", "An employee's payslip by employee number and pay date (the latest when none)", withDate: true);
        yield return Text("DOC20", "Payment Voucher (designable)", "A payment voucher by its number");
        yield return Text("DOC21", "Receipt Voucher (designable)", "A receipt voucher by its number");
        yield return Text("DOC22", "Journal Voucher (designable)", "A journal voucher by its number");
        yield return Text("DOC23", "Petty Cash Voucher (designable)", "A petty cash voucher by its number");
        yield return Text("DOC24", "Withholding Tax Certificate (designable)", "A withholding tax certificate by its number");
        yield return Text("DOC25", "Weighbridge Ticket (designable)", "A weighbridge ticket by its number");
        yield return Text("DOC26", "Pick List (designable)", "A sales order's pick list by order number/warehouse code (the first warehouse when none)");
        yield return Text("DOC27", "Request for Quotation (designable)", "A request for quotation by its number");
        yield return Text("DOC28", "Certificate of Service (designable)", "An employee's certificate of service by employee number");
        yield return Text("DOC29", "Budget-Holder Statement (designable)", "A budget holder's statement by department code as at a date", withDate: true);
        yield return Text("DOC30", "Chain of Title (designable)", "A stand's chain-of-title printout by stand number");
        yield return Text("DOC31", "Cheque (designable)", "A cheque by bank account code/cheque number");
        yield return Text("DOC32", "Shelf Labels (designable)", "A price list's shelf labels by price list code");
        yield return Text("DOC33", "Rates Clearance Certificate (designable)", "A property's rates clearance certificate by its number");
        yield return Text("DOC34", "Stock Transfer Note (designable)", "A stock transfer note by its transfer number");
        yield return Text("DOC35", "Asset Tag (designable)", "A fixed asset's tag by its asset number");
        yield return Text("DOC36", "Leave Application (designable)", "A leave application by employee number/start date");
        yield return Text("DOC37", "Leave Balance Certificate (designable)", "An employee's leave balances as at a date", withDate: true);
        yield return Text("DOC38", "Loan Agreement & Statement (designable)", "An employee loan's terms, balance and repayment schedule by its reference");
        yield return Text("DOC39", "Gift Voucher (designable)", "A gift voucher by its card number");
        yield return Text("DOC40", "EFT Advice (designable)", "The bank's EFT advice for a payment run by its number");
        yield return Text("DOC41", "Subcontractor Payment Certificate (designable)", "A subcontractor's payment certificate by its number");
        yield return Text("DOC42", "Licence / Permit Certificate (designable)", "A council licence or permit by its number");
        yield return Text("DOC43", "Proforma Invoice (designable)", "A proforma invoice for a sales order by its number");
        yield return Text("DOC44", "Housing Offer Letter (designable)", "The offer of a stand by its certificate number", withDate: true);
        yield return Text("DOC45", "Housing Allocation Letter (designable)", "The confirmation of an accepted stand by its certificate number", withDate: true);
        yield return Text("DOC46", "Employment Contract (designable)", "An employee's contract of employment by employee number");
    }

    // The document kind behind each code (the data-source command is "Doc" + kind, as DocumentKinds in the application layer names them).
    private static readonly Dictionary<string, string> Kinds = new(StringComparer.Ordinal)
    {
        ["DOC01"] = "Quotation", ["DOC02"] = "OrderConfirmation", ["DOC03"] = "DeliveryNote", ["DOC04"] = "PackingSlip", ["DOC05"] = "Requisition", ["DOC06"] = "GoodsReceivedNote",
        ["DOC07"] = "SupplierDebitNote", ["DOC08"] = "FeeInvoice", ["DOC09"] = "PaymentCertificate", ["DOC10"] = "MunicipalBill",
        ["DOC13"] = "PosReceipt", ["DOC14"] = "CustomerStatement", ["DOC15"] = "BalanceForwardStatement", ["DOC16"] = "SupplierStatement", ["DOC17"] = "DunningLetter", ["DOC18"] = "RemittanceAdvice", ["DOC19"] = "Payslip", ["DOC20"] = "PaymentVoucher", ["DOC21"] = "ReceiptVoucher", ["DOC22"] = "JournalVoucher", ["DOC23"] = "PettyCashVoucher", ["DOC24"] = "WithholdingCertificate", ["DOC25"] = "WeighbridgeTicket", ["DOC26"] = "PickList", ["DOC27"] = "Rfq", ["DOC28"] = "CertificateOfService", ["DOC29"] = "BudgetHolderStatement", ["DOC30"] = "ChainOfTitle", ["DOC31"] = "Cheque", ["DOC32"] = "ShelfLabels", ["DOC33"] = "ClearanceCertificate", ["DOC34"] = "StockTransferNote", ["DOC35"] = "AssetTag", ["DOC36"] = "LeaveApplication", ["DOC37"] = "LeaveBalanceCertificate", ["DOC38"] = "LoanStatement", ["DOC39"] = "GiftVoucher", ["DOC40"] = "EftAdvice", ["DOC41"] = "SubcontractCertificate", ["DOC42"] = "LicenceCertificate", ["DOC43"] = "ProformaInvoice", ["DOC44"] = "HousingOfferLetter", ["DOC45"] = "HousingAllocationLetter", ["DOC46"] = "EmploymentContract",
    };

    private static ReportDefinition Business(string code, string name, string what, (string Name, ParameterType Type)[] parameters)
    {
        string kind = Kinds[code];
        return Listing(code, name, Module, what + " - title, company, party, reference fields (Field1…Field6 with their labels), each line, the totals (Total = the last one), notes and signature lines "
            + "- as the data source for a company's own layout in the report designer.", "Doc" + kind, BusinessColumns, parameters);
    }

    private static ReportDefinition Text(string code, string name, string what, bool withDate = false)
    {
        (string Name, ParameterType Type)[] parameters = withDate ? [("Number", ParameterType.String), ("Date", ParameterType.Date)] : [("Number", ParameterType.String)];
        return Listing(code, name, Module, what + " - one row per line of the standard document (Title, Number, Date, LineNo, Line), as the data source for a company's own framed layout "
            + "in the report designer: put the Line field in a monospaced font on the detail band and add the logo, header and footer.", "Doc" + Kinds[code], TextColumns, parameters);
    }
}
