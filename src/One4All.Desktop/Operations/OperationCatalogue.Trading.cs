using System.Data;
using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Trading;
using One4All.Core.Accounting;
using One4All.Core.Trading;
using static One4All.Desktop.Operations.Ops;

namespace One4All.Desktop.Operations;

/// <content>Customer and supplier documents: invoices and credit notes, receipts and payments, statements.</content>
public static partial class OperationCatalogue
{
    private static IEnumerable<Operation> Trading()
    {
        yield return new Operation
        {
            Module = "Customers", Title = "Customer invoice or credit note", ListTitle = "Open customer items",
            Description = "Posts a sales invoice or credit note to a customer's account (the customer's currency unless another is given; tax at the tax code's account).",
            Fields = [Pick("Document", "Document", ["Invoice", "Credit note"]), Req("Customer", "Customer code"), Req("Number", "Document number"), Today("Date", "Date"), Num("Net", "Net amount"),
                Num("Tax", "Tax amount", "0"), Req("Account", "Sales account"), Opt("TaxAccount", "Tax account"), Opt("TaxCode", "Tax code"), Opt("Currency", "Currency (blank: the customer's)"),
                Opt("Reason", "Reason code (credit note)"), Opt("Narrative", "Narrative")],
            Run = async (c, i) =>
            {
                long customer = await CustomerAsync(c, i.Text("Customer")).ConfigureAwait(true);
                CurrencyCode currency = i.Optional("Currency") is string code ? CurrencyCode.Parse(code)
                    : await c.Db.Customers.AsNoTracking().Where(x => x.Id == customer).Select(x => x.Currency).SingleAsync().ConfigureAwait(true);
                ISalesLedgerService sales = c.Get<ISalesLedgerService>();
                TradingPostingResult posted = Check(i.Text("Document") == "Invoice"
                    ? await sales.PostInvoiceAsync(new SalesInvoiceRequest
                    {
                        CompanyId = c.CompanyId, CustomerId = customer, DocumentNumber = i.Text("Number"), DocumentDate = i.Date("Date"), Currency = currency, NetAmount = i.Number("Net"),
                        TaxAmount = i.Number("Tax"), SalesAccountCode = i.Text("Account"), TaxAccountCode = i.Optional("TaxAccount"), TaxCode = i.Optional("TaxCode"), Narrative = i.Optional("Narrative"),
                    }).ConfigureAwait(true)
                    : await sales.PostCreditNoteAsync(new SalesCreditNoteRequest
                    {
                        CompanyId = c.CompanyId, CustomerId = customer, DocumentNumber = i.Text("Number"), DocumentDate = i.Date("Date"), Currency = currency, NetAmount = i.Number("Net"),
                        TaxAmount = i.Number("Tax"), SalesAccountCode = i.Text("Account"), TaxAccountCode = i.Optional("TaxAccount"), ReasonCode = i.Optional("Reason"), Narrative = i.Optional("Narrative"),
                    }).ConfigureAwait(true));
                return $"{i.Text("Document")} {i.Text("Number")} posted: {M(posted.BaseAmount)} base (journal {posted.JournalNumber}).";
            },
            List = c => OpenItemsAsync(c, TradingLedger.AccountsReceivable),
        };
        yield return new Operation
        {
            Module = "Customers", Title = "Customer receipt (bank)", ListTitle = "Open customer items",
            Description = "A payment received into the bank (EFT, deposit) on a customer's account; allocate it to invoices with auto-allocation or the allocation screens.",
            Fields = [Req("Customer", "Customer code"), Req("Number", "Receipt number"), Today("Date", "Date"), Num("Amount", "Amount"), Req("Bank", "Bank account", "1100"),
                Opt("Currency", "Currency (blank: the customer's)"), Opt("Narrative", "Narrative")],
            Run = async (c, i) =>
            {
                long customer = await CustomerAsync(c, i.Text("Customer")).ConfigureAwait(true);
                CurrencyCode currency = i.Optional("Currency") is string code ? CurrencyCode.Parse(code)
                    : await c.Db.Customers.AsNoTracking().Where(x => x.Id == customer).Select(x => x.Currency).SingleAsync().ConfigureAwait(true);
                TradingPostingResult posted = Check(await c.Get<ISalesLedgerService>().PostReceiptAsync(new ReceiptRequest
                {
                    CompanyId = c.CompanyId, CustomerId = customer, DocumentNumber = i.Text("Number"), ReceiptDate = i.Date("Date"), Currency = currency, Amount = i.Number("Amount"),
                    BankAccountCode = i.Text("Bank"), Narrative = i.Optional("Narrative"),
                }).ConfigureAwait(true));
                return $"Receipt {i.Text("Number")} posted: {M(posted.BaseAmount)} base (journal {posted.JournalNumber}).";
            },
            List = c => OpenItemsAsync(c, TradingLedger.AccountsReceivable),
        };
        yield return new Operation
        {
            Module = "Customers", Title = "Customer statement", ListTitle = "Open customer items", Description = "The customer's statement as at a date, ready to print or send.",
            Fields = [Req("Customer", "Customer code"), Today("AsAt", "As at")],
            Run = async (c, i) => Check(await c.Get<IStatementService>().RenderCustomerStatementTextAsync(c.CompanyId, await CustomerAsync(c, i.Text("Customer")).ConfigureAwait(true), i.Date("AsAt"), 72)
                .ConfigureAwait(true)),
            List = c => OpenItemsAsync(c, TradingLedger.AccountsReceivable),
        };
        yield return new Operation
        {
            Module = "Suppliers", Title = "Supplier invoice or credit note", ListTitle = "Open supplier items",
            Description = "Posts a supplier's invoice or credit note that has no purchase order (services, utilities); invoices for ordered goods go through Purchasing → Match a supplier invoice.",
            Fields = [Pick("Document", "Document", ["Invoice", "Credit note"]), Req("Supplier", "Supplier code"), Req("Number", "Supplier's document number"), Today("Date", "Date"), Num("Net", "Net amount"),
                Num("Tax", "Tax amount", "0"), Req("Account", "Expense account"), Opt("TaxAccount", "Tax account"), Opt("TaxCode", "Tax code"), Opt("Currency", "Currency (blank: the supplier's)"),
                Opt("Narrative", "Narrative")],
            Run = async (c, i) =>
            {
                long supplier = await SupplierAsync(c, i.Text("Supplier")).ConfigureAwait(true);
                CurrencyCode currency = i.Optional("Currency") is string code ? CurrencyCode.Parse(code)
                    : await c.Db.Suppliers.AsNoTracking().Where(x => x.Id == supplier).Select(x => x.Currency).SingleAsync().ConfigureAwait(true);
                IPurchaseLedgerService purchases = c.Get<IPurchaseLedgerService>();
                TradingPostingResult posted = Check(i.Text("Document") == "Invoice"
                    ? await purchases.PostInvoiceAsync(new PurchaseInvoiceRequest
                    {
                        CompanyId = c.CompanyId, SupplierId = supplier, DocumentNumber = i.Text("Number"), DocumentDate = i.Date("Date"), Currency = currency, NetAmount = i.Number("Net"),
                        TaxAmount = i.Number("Tax"), ExpenseAccountCode = i.Text("Account"), TaxAccountCode = i.Optional("TaxAccount"), TaxCode = i.Optional("TaxCode"), Narrative = i.Optional("Narrative"),
                    }).ConfigureAwait(true)
                    : await purchases.PostCreditNoteAsync(new PurchaseCreditNoteRequest
                    {
                        CompanyId = c.CompanyId, SupplierId = supplier, DocumentNumber = i.Text("Number"), DocumentDate = i.Date("Date"), Currency = currency, NetAmount = i.Number("Net"),
                        TaxAmount = i.Number("Tax"), ExpenseAccountCode = i.Text("Account"), TaxAccountCode = i.Optional("TaxAccount"), Narrative = i.Optional("Narrative"),
                    }).ConfigureAwait(true));
                return $"{i.Text("Document")} {i.Text("Number")} posted: {M(posted.BaseAmount)} base (journal {posted.JournalNumber})"
                    + (posted.WithheldBase == 0m ? "." : $"; {M(posted.WithheldBase)} withholding tax held back.");
            },
            List = c => OpenItemsAsync(c, TradingLedger.AccountsPayable),
        };
        yield return new Operation
        {
            Module = "Suppliers", Title = "Supplier payment", ListTitle = "Open supplier items",
            Description = "A single payment to a supplier from the bank (several at once go through the payment run).",
            Fields = [Req("Supplier", "Supplier code"), Req("Number", "Payment number"), Today("Date", "Date"), Num("Amount", "Amount"), Req("Bank", "Bank account", "1100"),
                Opt("Currency", "Currency (blank: the supplier's)"), Opt("Narrative", "Narrative")],
            Run = async (c, i) =>
            {
                long supplier = await SupplierAsync(c, i.Text("Supplier")).ConfigureAwait(true);
                CurrencyCode currency = i.Optional("Currency") is string code ? CurrencyCode.Parse(code)
                    : await c.Db.Suppliers.AsNoTracking().Where(x => x.Id == supplier).Select(x => x.Currency).SingleAsync().ConfigureAwait(true);
                TradingPostingResult posted = Check(await c.Get<IPurchaseLedgerService>().PostPaymentAsync(new PaymentRequest
                {
                    CompanyId = c.CompanyId, SupplierId = supplier, DocumentNumber = i.Text("Number"), PaymentDate = i.Date("Date"), Currency = currency, Amount = i.Number("Amount"),
                    BankAccountCode = i.Text("Bank"), Narrative = i.Optional("Narrative"),
                }).ConfigureAwait(true));
                return $"Payment {i.Text("Number")} posted: {M(posted.BaseAmount)} base (journal {posted.JournalNumber}).";
            },
            List = c => OpenItemsAsync(c, TradingLedger.AccountsPayable),
        };
        yield return new Operation
        {
            Module = "Suppliers", Title = "Supplier statement", ListTitle = "Open supplier items", Description = "The supplier account as we see it, as at a date.",
            Fields = [Req("Supplier", "Supplier code"), Today("AsAt", "As at")],
            Run = async (c, i) => Check(await c.Get<IStatementService>().RenderSupplierStatementTextAsync(c.CompanyId, await SupplierAsync(c, i.Text("Supplier")).ConfigureAwait(true), i.Date("AsAt"), 72)
                .ConfigureAwait(true)),
            List = c => OpenItemsAsync(c, TradingLedger.AccountsPayable),
        };
    }

    private static async Task<DataTable> OpenItemsAsync(OperationContext c, TradingLedger ledger)
        => Table(await c.Db.OpenItems.AsNoTracking().Where(o => o.CompanyId == c.CompanyId && o.Ledger == ledger && o.OutstandingTransaction != 0m).OrderByDescending(o => o.Id).Take(500).ToListAsync()
                .ConfigureAwait(true), ("Trader", o => o.TraderId), ("Document", o => o.DocumentNumber), ("Type", o => o.ItemType), ("Date", o => D(o.DocumentDate)), ("Due", o => D(o.DueDate)),
            ("Currency", o => o.Currency), ("Original", o => M(o.Sign == LedgerSign.Debit ? o.OriginalTransaction : -o.OriginalTransaction)),
            ("Outstanding", o => M(o.Sign == LedgerSign.Debit ? o.OutstandingTransaction : -o.OutstandingTransaction)));
}
