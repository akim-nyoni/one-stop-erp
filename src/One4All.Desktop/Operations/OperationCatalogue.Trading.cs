using System.Data;
using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Trading;
using One4All.Core.Accounting;
using One4All.Core.System;
using One4All.Core.Trading;
using static One4All.Desktop.Operations.Ops;

namespace One4All.Desktop.Operations;

/// <content>Customer and supplier documents: invoices and credit notes, receipts and payments, statements.</content>
public static partial class OperationCatalogue
{
    private static IEnumerable<Operation> Trading()
    {
        foreach (string module in new[] { "Customers", "Suppliers" })
        {
            TradingLedger ledger = module == "Customers" ? TradingLedger.AccountsReceivable : TradingLedger.AccountsPayable;
            yield return new Operation
            {
                Module = module, Title = "Exchange-rate type", ListTitle = module == "Customers" ? "Customers on their own rate" : "Suppliers on their own rate",
                Description = "The rate type this trader's foreign documents convert at (e.g. a contract or bank rate kept in the rate table). Daily (or blank) uses the daily rate; "
                    + "a document dated before any rate of the type falls back to the daily rate.",
                Fields = [Req("Code", module == "Customers" ? "Customer code" : "Supplier code"), Pick("RateType", "Rate type", Enum.GetNames<ExchangeRateType>())],
                Run = async (c, i) =>
                {
                    string code = i.Text("Code").Trim().ToUpperInvariant();
                    ExchangeRateType type = Enum.Parse<ExchangeRateType>(i.Text("RateType"));
                    ExchangeRateType? stored = type == ExchangeRateType.Daily ? null : type;
                    if (module == "Customers")
                    {
                        Customer customer = await c.Db.Customers.FirstOrDefaultAsync(x => x.CompanyId == c.CompanyId && x.Code == code).ConfigureAwait(true) ?? throw new OperationException($"No customer {code}.");
                        customer.RateType = stored;
                    }
                    else
                    {
                        Supplier supplier = await c.Db.Suppliers.FirstOrDefaultAsync(x => x.CompanyId == c.CompanyId && x.Code == code).ConfigureAwait(true) ?? throw new OperationException($"No supplier {code}.");
                        supplier.RateType = stored;
                    }

                    await c.Db.SaveChangesAsync().ConfigureAwait(true);
                    return $"{code} now converts at the {type} rate.";
                },
                List = async c => module == "Customers"
                    ? Table(await c.Db.Customers.AsNoTracking().Where(x => x.CompanyId == c.CompanyId && x.RateType != null).OrderBy(x => x.Code).ToListAsync().ConfigureAwait(true),
                        ("Code", x => x.Code), ("Name", x => x.Name), ("Currency", x => x.Currency), ("Rate type", x => x.RateType))
                    : Table(await c.Db.Suppliers.AsNoTracking().Where(x => x.CompanyId == c.CompanyId && x.RateType != null).OrderBy(x => x.Code).ToListAsync().ConfigureAwait(true),
                        ("Code", x => x.Code), ("Name", x => x.Name), ("Currency", x => x.Currency), ("Rate type", x => x.RateType)),
            };
            yield return new Operation
            {
                Module = module, Title = "Addresses and contacts", ListTitle = "Directory",
                Description = "Postal, physical and delivery addresses (the default postal address prints on invoices, statements, orders, remittances and letters) and the people who "
                    + "receive statements, remittance advices and purchase orders by email. Remove takes an address or contact id. The list shows the trader typed in Code.",
                Fields = [Pick("Step", "Step", ["Add address", "Add contact", "Remove address", "Remove contact"]), Req("Code", module == "Customers" ? "Customer code" : "Supplier code"),
                    Pick("Kind", "Address kind", Enum.GetNames<AddressKind>()), Opt("Line1", "Address line 1"), Opt("Line2", "Address line 2"), Opt("City", "Town / city"), Opt("Region", "Province"),
                    Opt("Postal", "Postal code"), Opt("Country", "Country"), Opt("Name", "Contact name"), Opt("Role", "Role"), Opt("Email", "Email"), Opt("Phone", "Phone"),
                    Tick("Primary", "Primary contact"), Tick("Statements", "Receives statements"), Tick("Remittances", "Receives remittances"), Tick("Orders", "Receives purchase orders"),
                    OptNum("Id", "Address / contact id (remove)")],
                Run = async (c, i) =>
                {
                    ITraderDirectoryService directory = c.Get<ITraderDirectoryService>();
                    switch (i.Text("Step"))
                    {
                        case "Add contact":
                            long contact = Check(await directory.AddContactAsync(c.CompanyId, ledger, i.Text("Code"), new NewTraderContact(i.Optional("Name") ?? string.Empty, i.Optional("Role"),
                                i.Optional("Email"), i.Optional("Phone"), i.Flag("Primary"), i.Flag("Statements"), i.Flag("Remittances"), i.Flag("Orders"))).ConfigureAwait(true));
                            return $"Contact {contact} added.";
                        case "Remove address":
                            _ = Check(await directory.RemoveAddressAsync(c.CompanyId, (long)(i.OptionalNumber("Id") ?? 0)).ConfigureAwait(true), string.Empty);
                            return "Address removed.";
                        case "Remove contact":
                            _ = Check(await directory.RemoveContactAsync(c.CompanyId, (long)(i.OptionalNumber("Id") ?? 0)).ConfigureAwait(true), string.Empty);
                            return "Contact removed.";
                        default:
                            long address = Check(await directory.AddAddressAsync(c.CompanyId, ledger, i.Text("Code"), new NewTraderAddress(Enum.Parse<AddressKind>(i.Text("Kind")),
                                i.Optional("Line1") ?? string.Empty, i.Optional("Line2"), i.Optional("City"), i.Optional("Region"), i.Optional("Postal"), i.Optional("Country"))).ConfigureAwait(true));
                            return $"Address {address} added as the default {i.Text("Kind").ToLowerInvariant()} address.";
                    }
                },
            };
            yield return new Operation
            {
                Module = module, Title = module == "Customers" ? "Email a statement" : "Email a statement, remittance or order",
                Description = "Sends the document as an attachment to the contacts who receive it (else the primary contact"
                    + (module == "Customers" ? ", else the customer's own email)." : "). Remittance: the payment's open-item id; order: the purchase order id."),
                Fields = module == "Customers"
                    ? [Req("Code", "Customer code"), Today("AsAt", "Statement date")]
                    : [Pick("Document", "Document", ["Statement", "Remittance advice", "Purchase order"]), Opt("Code", "Supplier code (statement)"), Today("AsAt", "Statement date"), OptNum("Id", "Payment item / order id")],
                Run = async (c, i) =>
                {
                    ITraderDirectoryService directory = c.Get<ITraderDirectoryService>();
                    string document = module == "Customers" ? "Statement" : i.Text("Document");
                    long batch = Check(document switch
                    {
                        "Remittance advice" => await directory.EmailRemittanceAsync(c.CompanyId, (long)(i.OptionalNumber("Id") ?? 0)).ConfigureAwait(true),
                        "Purchase order" => await directory.EmailPurchaseOrderAsync(c.CompanyId, (long)(i.OptionalNumber("Id") ?? 0)).ConfigureAwait(true),
                        _ => await directory.EmailStatementAsync(c.CompanyId, ledger, i.Optional("Code") ?? string.Empty, i.Date("AsAt")).ConfigureAwait(true),
                    });
                    return $"{document} queued for email (batch {batch}).";
                },
            };
        }

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
                if (i.Text("Document") != "Invoice")
                {
                    // A credit note goes through the company's credit-note approval workflow when there is one.
                    CreditNoteSubmission credit = Check(await c.Get<ICreditNoteApprovalService>().SubmitAsync(new SalesCreditNoteRequest
                    {
                        CompanyId = c.CompanyId, CustomerId = customer, DocumentNumber = i.Text("Number"), DocumentDate = i.Date("Date"), Currency = currency, NetAmount = i.Number("Net"),
                        TaxAmount = i.Number("Tax"), SalesAccountCode = i.Text("Account"), TaxAccountCode = i.Optional("TaxAccount"), ReasonCode = i.Optional("Reason"), Narrative = i.Optional("Narrative"),
                    }, c.UserId).ConfigureAwait(true));
                    return credit.Posted ? $"Credit note {i.Text("Number")} posted (journal {credit.JournalNumber})." : $"Credit note {i.Text("Number")} is awaiting approval.";
                }

                TradingPostingResult posted = Check(await c.Get<ISalesLedgerService>().PostInvoiceAsync(new SalesInvoiceRequest
                {
                    CompanyId = c.CompanyId, CustomerId = customer, DocumentNumber = i.Text("Number"), DocumentDate = i.Date("Date"), Currency = currency, NetAmount = i.Number("Net"),
                    TaxAmount = i.Number("Tax"), SalesAccountCode = i.Text("Account"), TaxAccountCode = i.Optional("TaxAccount"), TaxCode = i.Optional("TaxCode"), Narrative = i.Optional("Narrative"),
                }).ConfigureAwait(true));
                return $"Invoice {i.Text("Number")} posted: {M(posted.BaseAmount)} base (journal {posted.JournalNumber}).";
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
            Module = "Suppliers", Title = "Payment run approval", ListTitle = "Saved payment runs",
            Description = "Saves a proposed payment run for review: everyone due (held suppliers left out with the reason); lines can be left out or part-paid (oldest invoices "
                + "first); someone other than the proposer approves it, which pays exactly what it holds. Require approval stops direct payment runs for the company.",
            Fields = [Pick("Step", "Step", ["Save proposal", "Set line", "Approve", "Cancel", "Show", "Require approval", "Allow direct runs"]), Opt("Run", "Run number"),
                Today("Date", "Payment date"), Today("Due", "Pay invoices due on or before"), Req("Bank", "Bank account", "1100"), Opt("Currency", "Currency (blank: all)"),
                Opt("Wht", "Withholding tax code"), Opt("Layout", "Bank file layout"), Opt("Supplier", "Supplier code (set line)"), Tick("Include", "Pay this supplier", true),
                OptNum("Amount", "Amount (blank: all due)")],
            Run = async (c, i) =>
            {
                IPaymentRunService runs = c.Get<IPaymentRunService>();
                string Show(SavedPaymentRun r) => $"Run {r.Number} ({r.Status}, {D(r.PaymentDate)}): "
                    + string.Join("; ", r.TotalByCurrency.Select(t => $"{M(t.Value)} {t.Key}")) + Environment.NewLine
                    + string.Join(Environment.NewLine, r.Lines.Select(l => $"  {l.SupplierCode,-12} {l.Currency} due {M(l.Due),14}  pay {(l.Included ? M(l.Amount) : "-"),14}  {l.Note}{l.PaymentNumber}"));
                string run = i.Optional("Run") ?? string.Empty;
                switch (i.Text("Step"))
                {
                    case "Require approval":
                    case "Allow direct runs":
                        bool required = i.Text("Step") == "Require approval";
                        (await c.Db.Companies.SingleAsync(x => x.Id == c.CompanyId).ConfigureAwait(true)).PaymentRunApprovalRequired = required;
                        await c.Db.SaveChangesAsync().ConfigureAwait(true);
                        return required ? "Payment runs must now be saved and approved by a second person." : "Direct payment runs are allowed again.";
                    case "Save proposal":
                        return Show(Check(await runs.SaveProposalAsync(new PaymentRunRequest
                        {
                            CompanyId = c.CompanyId, PaymentDate = i.Date("Date"), DueOnOrBefore = i.Date("Due"), BankAccountCode = i.Text("Bank"), Currency = i.Optional("Currency"),
                            WithholdingTaxCode = i.Optional("Wht"), EftLayoutCode = i.Optional("Layout"),
                        }, c.UserId).ConfigureAwait(true)));
                    case "Set line":
                        return Show(Check(await runs.SetLineAsync(c.CompanyId, run, i.Text("Supplier"), i.Optional("Currency") ?? string.Empty, i.Flag("Include"), i.OptionalNumber("Amount"))
                            .ConfigureAwait(true)));
                    case "Approve":
                        PaymentRunResult paid = Check(await runs.ApproveAsync(c.CompanyId, run, c.UserId).ConfigureAwait(true));
                        return $"Run {run} approved: {paid.Lines.Count(l => l.PaymentNumber.Length > 0)} payment(s), {M(paid.Total)} base.";
                    case "Cancel":
                        return Check(await runs.CancelAsync(c.CompanyId, run).ConfigureAwait(true), $"Run {run} cancelled.");
                    default:
                        return Show(Check(await runs.GetAsync(c.CompanyId, run).ConfigureAwait(true)));
                }
            },
            List = async c => Table(await c.Db.Set<PaymentRunBatch>().AsNoTracking().Where(b => b.CompanyId == c.CompanyId).OrderByDescending(b => b.Id).Take(200).ToListAsync().ConfigureAwait(true),
                ("Run", b => b.Number), ("Status", b => b.Status), ("Pay date", b => D(b.PaymentDate)), ("Due by", b => D(b.DueOnOrBefore)), ("Bank", b => b.BankAccountCode),
                ("Currency", b => b.Currency), ("Proposed by", b => b.ProposedByUserId), ("Approved by", b => b.ApprovedByUserId)),
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
