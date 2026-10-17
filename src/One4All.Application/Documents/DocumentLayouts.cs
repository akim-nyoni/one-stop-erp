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
public sealed record DocumentKind(string Kind, string Name, string DesignableReportCode, string NumberLabel, bool NeedsDate = false);

/// <summary>The business documents with designer-editable layouts (brief §S), each with its designable data-source report.</summary>
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
/// reference fields as Field1…Field6 with their labels), the totals, notes and signature lines — what a designed layout places on the page.
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
