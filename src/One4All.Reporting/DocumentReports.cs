using One4All.Reporting.Model;
using static One4All.Reporting.SystemReports;

namespace One4All.Reporting;

/// <summary>
/// The designable business documents (brief §S): DOC01–DOC12 give one document's heading, party, reference fields, lines and totals as rows, as the data
/// source a company copies and lays out as its own quotation, delivery note, invoice or purchase order in the report designer; the company then names its
/// layout under System Manager › Document layouts and the document prints through it.
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
            "A customer tax invoice or credit note by its number — company, customer and address, dates, each line, net, tax, gross and what is still outstanding — as the data source for a company's own invoice layout.",
            "DocTaxInvoice",
            [new Column("Kind", "Document", 80), new Column("CustomerCode", "Customer", 70), new Column("CustomerName", "Name", 150), new Column("Number", "Number", 80), new Column("Date", "Date", 70),
             new Column("DueDate", "Due", 70), new Column("Description", "Description", 200), new Column("Amount", "Amount", 85, true), new Column("Net", "Net", 85), new Column("Tax", "Tax", 85),
             new Column("Gross", "Gross", 85), new Column("Outstanding", "Outstanding", 85)], number);
        yield return Listing("DOC12", "Purchase Order (designable)", Module,
            "A purchase order by its number — supplier and address, date, terms, each line with quantity and price, and the order total — as the data source for a company's own purchase order layout.",
            "DocPurchaseOrder",
            [new Column("SupplierCode", "Supplier", 70), new Column("SupplierName", "Name", 150), new Column("Number", "Order", 80), new Column("Date", "Date", 70), new Column("Status", "Status", 70),
             new Column("Code", "Code", 70), new Column("Description", "Description", 180), new Column("Quantity", "Qty", 60), new Column("UnitPrice", "Price", 70), new Column("Amount", "Amount", 85, true),
             new Column("Total", "Order total", 90)], number);
    }

    // The document kind behind each code (the data-source command is "Doc" + kind, as DocumentKinds in the application layer names them).
    private static readonly Dictionary<string, string> Kinds = new(StringComparer.Ordinal)
    {
        ["DOC01"] = "Quotation", ["DOC02"] = "OrderConfirmation", ["DOC03"] = "DeliveryNote", ["DOC04"] = "PackingSlip", ["DOC05"] = "Requisition", ["DOC06"] = "GoodsReceivedNote",
        ["DOC07"] = "SupplierDebitNote", ["DOC08"] = "FeeInvoice", ["DOC09"] = "PaymentCertificate", ["DOC10"] = "MunicipalBill",
    };

    private static ReportDefinition Business(string code, string name, string what, (string Name, ParameterType Type)[] parameters)
    {
        string kind = Kinds[code];
        return Listing(code, name, Module, what + " — title, company, party, reference fields (Field1…Field6 with their labels), each line, the totals (Total = the last one), notes and signature lines "
            + "— as the data source for a company's own layout in the report designer.", "Doc" + kind, BusinessColumns, parameters);
    }
}
